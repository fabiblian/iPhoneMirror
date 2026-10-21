using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;

namespace IPhoneMirror.App.Windows;

/// <summary>What the preview controls show for one device.</summary>
internal sealed record PreviewOverlayState(bool ControlsOnHoverOnly);

/// <summary>Settings for the preview controls, supplied by the main window.</summary>
internal interface IPreviewOverlayActions
{
    PreviewOverlayState GetOverlayState(string udid, nint window);
}

/// <summary>
/// Transparent window over a native preview: a dot to move the window (top
/// centre) and a dot to resize it (bottom right). Everything but those
/// controls lets clicks through to the preview, so touch control keeps working.
/// </summary>
internal sealed class PreviewControlsOverlayWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const nint HtClient = 1;
    private const nint HtTransparent = -1;
    private const int HtCaption = 2;
    private const int HtBottomRight = 17;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private static readonly TimeSpan HoverLinger = TimeSpan.FromMilliseconds(900);
    private readonly nint _owner;
    private readonly string _udid;
    private readonly IPreviewOverlayActions _actions;
    private readonly Func<PreviewFrameInsets> _contentInset;
    private readonly DispatcherTimer _timer;
    private readonly List<FrameworkElement> _interactive = [];
    private HwndSource? _source;
    private DateTime _lastInside = DateTime.MinValue;

    private FrameworkElement _moveDot = null!;
    private FrameworkElement _resizeDot = null!;

    private PreviewControlsOverlayWindow(nint owner, string udid, IPreviewOverlayActions actions,
        Func<PreviewFrameInsets> contentInset)
    {
        _owner = owner;
        _udid = udid;
        _actions = actions;
        _contentInset = contentInset;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Content = BuildContent();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Refresh();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    internal static PreviewControlsOverlayWindow ShowFor(nint owner, string udid,
        IPreviewOverlayActions actions, Func<PreviewFrameInsets> contentInset)
    {
        var overlay = new PreviewControlsOverlayWindow(owner, udid, actions, contentInset);
        _ = new WindowInteropHelper(overlay) { Owner = owner };
        overlay.Show();
        overlay.Refresh();
        overlay._timer.Start();
        return overlay;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private FrameworkElement BuildContent()
    {
        var root = new Grid();

        _moveDot = Dot(HorizontalAlignment.Center, VerticalAlignment.Top, Cursors.SizeAll, HtCaption,
            "PreviewMoveDot", new Thickness(0, 2, 0, 0));
        _resizeDot = Dot(HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE, HtBottomRight,
            "PreviewResizeDot", new Thickness(0, 0, 2, 2));
        root.Children.Add(_moveDot);
        root.Children.Add(_resizeDot);

        ApplyTexts();
        return root;
    }

    private FrameworkElement Dot(HorizontalAlignment horizontal, VerticalAlignment vertical, Cursor cursor,
        int hitTest, string toolTipKey, Thickness margin)
    {
        var dot = new Border
        {
            Width = 44, Height = 32, Background = Brushes.Transparent, Cursor = cursor,
            HorizontalAlignment = horizontal, VerticalAlignment = vertical, Margin = margin, Tag = toolTipKey,
            Child = new Ellipse
            {
                Width = 12, Height = 12, Fill = Frozen(Color.FromArgb(0xEB, 0xFF, 0xFF, 0xFF)),
                Stroke = Frozen(Color.FromArgb(0x8C, 0x12, 0x12, 0x16)), StrokeThickness = 3,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        // Hand the press to the preview window as a caption or corner press,
        // so Windows runs its own move/size loop (with snapping afterwards).
        dot.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            ReleaseCapture();
            _ = SendMessageW(_owner, WmNcLeftButtonDown, hitTest, 0);
        };
        _interactive.Add(dot);
        return dot;
    }

    private void ApplyTexts()
    {
        foreach (var element in _interactive)
            if (element.Tag is string key) element.ToolTip = LocalizationService.Get(key);
    }

    /// <summary>Follows the preview window right away (called on its moves).</summary>
    internal void SyncToOwner() => Refresh();

    private void Refresh()
    {
        if (_owner == 0 || !IsWindowVisible(_owner) || IsIconic(_owner) || !GetWindowRect(_owner, out var rect))
        {
            if (IsVisible) Hide();
            return;
        }
        var inset = _contentInset();
        if (rect.Right - rect.Left > inset.Horizontal && rect.Bottom - rect.Top > inset.Vertical)
        {
            rect.Left += inset.Left;
            rect.Top += inset.Top;
            rect.Right -= inset.Right;
            rect.Bottom -= inset.Bottom;
        }
        var dpi = GetDpiForWindow(_owner);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        Left = rect.Left / scale;
        Top = rect.Top / scale;
        Width = Math.Max(1, (rect.Right - rect.Left) / scale);
        Height = Math.Max(1, (rect.Bottom - rect.Top) / scale);
        if (!IsVisible) Show();

        var state = _actions.GetOverlayState(_udid, _owner);
        if (GetCursorPos(out var cursor) && cursor.X >= rect.Left && cursor.X < rect.Right &&
            cursor.Y >= rect.Top && cursor.Y < rect.Bottom)
            _lastInside = DateTime.UtcNow;
        var hovered = DateTime.UtcNow - _lastInside < HoverLinger;
        var controlsVisible = hovered || !state.ControlsOnHoverOnly;
        var controlsOpacity = hovered ? 1.0 : 0.55;
        foreach (var element in new[] { _moveDot, _resizeDot })
        {
            element.Visibility = controlsVisible ? Visibility.Visible : Visibility.Collapsed;
            element.Opacity = controlsOpacity;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        if (_source is null) return;
        var style = GetWindowLongPtrW(_source.Handle, GwlExStyle).ToInt64();
        _ = SetWindowLongPtrW(_source.Handle, GwlExStyle, (nint)(style | WsExNoActivate | WsExToolWindow));
        _source.AddHook(WindowMessageHook);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _source?.RemoveHook(WindowMessageHook);
        _source = null;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyTexts();

    // Only the dots take the mouse; everything else passes to the
    // preview window below (same thread, so HTTRANSPARENT reaches it).
    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WmNcHitTest || Content is not UIElement root) return 0;
        handled = true;
        var screen = new Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
        Point local;
        try { local = root.PointFromScreen(screen); }
        catch (InvalidOperationException) { return HtTransparent; }
        foreach (var element in _interactive)
        {
            if (element.Visibility != Visibility.Visible) continue;
            var position = element.TranslatePoint(new Point(0, 0), root);
            if (local.X >= position.X && local.Y >= position.Y &&
                local.X < position.X + element.ActualWidth && local.Y < position.Y + element.ActualHeight)
                return HtClient;
        }
        return HtTransparent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        internal int X;
        internal int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, int message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
}
