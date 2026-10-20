using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace IPhoneMirror.App.Windows;

/// <summary>What the preview controls show for one device.</summary>
internal sealed record PreviewOverlayState(bool Muted, bool ControlsOnHoverOnly,
    PreviewDotPosition MoveDot, double MoveDotX, double MoveDotY,
    PreviewDotPosition ResizeDot, double ResizeDotX, double ResizeDotY);

/// <summary>Actions behind the preview controls; the main window implements them.</summary>
internal interface IPreviewOverlayActions
{
    PreviewOverlayState GetOverlayState(string udid, nint window);
    void ToggleMute(string udid);
    void OpenPreviewSettings();
}

/// <summary>
/// Transparent window over a native preview: a dot on the edge to move the
/// window (borderless previews only; a frame moves the window itself), a dot
/// to resize it and a floating bar with mute, pin and settings. Both dots sit
/// where the preview settings put them. Everything but those controls lets
/// clicks through to the preview, so touch control keeps working.
/// </summary>
internal sealed class PreviewControlsOverlayWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const nint HtClient = 1;
    private const nint HtTransparent = -1;
    private const int HtCaption = 2;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    // The dot's centre sits this far from the preview's edge, so the dot
    // touches the edge; its press area is a square around it.
    private const double DotEdgeInset = 7;
    private const double DotHitSize = 32;
    private const double DotSize = 12;
    private static readonly TimeSpan HoverLinger = TimeSpan.FromMilliseconds(900);
    private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0xFF, 0x8A, 0x8A));
    private static readonly Brush PinBrush = Frozen(Color.FromRgb(0x69, 0xB1, 0xF8));
    private static readonly Brush PillBrush = Frozen(Color.FromArgb(0xD6, 0x12, 0x12, 0x16));
    private static readonly ControlTemplate RoundButtonTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
            <Border x:Name="Root" Width="36" Height="36" CornerRadius="18" Background="{TemplateBinding Background}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Root" Property="Background" Value="#33FFFFFF"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Root" Property="Background" Value="#55FFFFFF"/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    private readonly nint _owner;
    private readonly string _udid;
    private readonly IPreviewOverlayActions _actions;
    private readonly Func<PreviewFrameInsets> _contentInset;
    private readonly Func<bool> _isTopMost;
    private readonly Action _togglePin;
    private readonly DispatcherTimer _timer;
    private readonly List<FrameworkElement> _interactive = [];
    private HwndSource? _source;
    private DateTime _lastInside = DateTime.MinValue;
    // Physical bounds last given to this window, to skip unchanged moves.
    private WindowRect _applied;
    private bool _sizeMoving;
    private bool _hiddenForResize;
    private PreviewOverlayState? _state;

    private Dot _moveDot = null!;
    private Dot _resizeDot = null!;
    private Border _bar = null!;
    private Button _muteButton = null!;
    private SymbolIcon _muteSymbol = null!;
    private SymbolIcon _pinSymbol = null!;

    private PreviewControlsOverlayWindow(nint owner, string udid, IPreviewOverlayActions actions,
        Func<PreviewFrameInsets> contentInset, Func<bool> isTopMost, Action togglePin)
    {
        _owner = owner;
        _udid = udid;
        _actions = actions;
        _contentInset = contentInset;
        _isTopMost = isTopMost;
        _togglePin = togglePin;
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
        // The dots follow a new size once layout has it.
        SizeChanged += (_, _) => PlaceDots();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    internal static PreviewControlsOverlayWindow ShowFor(nint owner, string udid,
        IPreviewOverlayActions actions, Func<PreviewFrameInsets> contentInset,
        Func<bool> isTopMost, Action togglePin)
    {
        var overlay = new PreviewControlsOverlayWindow(owner, udid, actions, contentInset, isTopMost, togglePin);
        var helper = new WindowInteropHelper(overlay) { Owner = owner };
        // Size the window over the preview before its first frame is drawn.
        helper.EnsureHandle();
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

        _moveDot = new Dot(Cursors.SizeAll, "PreviewMoveDot");
        _resizeDot = new Dot(Cursors.SizeNWSE, "PreviewResizeDot");
        foreach (var dot in new[] { _moveDot, _resizeDot })
        {
            // Hand the press to the preview window as a caption or edge press,
            // so Windows runs its own move/size loop (with snapping afterwards).
            dot.Root.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                ReleaseCapture();
                _ = SendMessageW(_owner, WmNcLeftButtonDown, dot.HitTest, 0);
            };
            _interactive.Add(dot.Root);
            root.Children.Add(dot.Root);
        }

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        _muteSymbol = BarIcon(SymbolRegular.Speaker220);
        _muteButton = BarButton(_muteSymbol, () => _actions.ToggleMute(_udid));
        bar.Children.Add(_muteButton);
        _pinSymbol = BarIcon(SymbolRegular.Pin20);
        bar.Children.Add(BarButton(_pinSymbol, () => { _togglePin(); Refresh(); }, "PreviewPinButton"));
        bar.Children.Add(BarButton(BarIcon(SymbolRegular.Settings20), _actions.OpenPreviewSettings,
            "PreviewSettingsTitle"));

        _bar = new Border
        {
            Background = PillBrush, CornerRadius = new CornerRadius(22), Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 46), Child = bar,
        };
        _interactive.Add(_bar);
        root.Children.Add(_bar);
        ApplyTexts();
        return root;
    }

    private static SymbolIcon BarIcon(SymbolRegular symbol) => new()
    {
        Symbol = symbol, FontSize = 18, Foreground = Brushes.White,
    };

    private Button BarButton(SymbolIcon icon, Action click, string? toolTipKey = null)
    {
        var button = new Button
        {
            Template = RoundButtonTemplate, Content = icon, Focusable = false,
            Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(1, 0, 1, 0),
        };
        if (toolTipKey is not null) button.Tag = toolTipKey;
        button.Click += (_, _) => click();
        return button;
    }

    private void ApplyTexts()
    {
        foreach (var element in _interactive.Concat(LogicalChildrenOf(_bar)))
            if (element.Tag is string key) element.ToolTip = LocalizationService.Get(key);
    }

    private static IEnumerable<FrameworkElement> LogicalChildrenOf(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<FrameworkElement>())
        {
            yield return child;
            foreach (var nested in LogicalChildrenOf(child)) yield return nested;
        }
    }

    /// <summary>Follows the preview window right away (called on its moves).</summary>
    internal void SyncToOwner() => UpdateBounds();

    /// <summary>
    /// The preview window entered or left its move/size loop. While it runs,
    /// the overlay only follows moves; a resize hides it until the end, since
    /// redrawing a transparent window at every step stalls that loop.
    /// </summary>
    internal void SizeMoveChanged(bool active)
    {
        _sizeMoving = active;
        if (active) return;
        _hiddenForResize = false;
        Refresh();
    }

    // Puts this window over the preview's content with one native call; a
    // plain move keeps the size, so nothing is redrawn.
    private bool UpdateBounds()
    {
        if (_source is null || _owner == 0 || !IsWindowVisible(_owner) || IsIconic(_owner) ||
            !GetWindowRect(_owner, out var rect))
        {
            if (IsVisible) Hide();
            return false;
        }
        var inset = _contentInset();
        if (rect.Right - rect.Left > inset.Horizontal && rect.Bottom - rect.Top > inset.Vertical)
        {
            rect.Left += inset.Left;
            rect.Top += inset.Top;
            rect.Right -= inset.Right;
            rect.Bottom -= inset.Bottom;
        }
        var sameSize = rect.Right - rect.Left == _applied.Right - _applied.Left &&
            rect.Bottom - rect.Top == _applied.Bottom - _applied.Top;
        if (_sizeMoving && (!sameSize || _hiddenForResize))
        {
            _hiddenForResize = true;
            if (IsVisible) Hide();
            return false;
        }
        if (!sameSize || rect.Left != _applied.Left || rect.Top != _applied.Top)
        {
            var flags = SwpNoZOrder | SwpNoOwnerZOrder | SwpNoActivate | (sameSize ? SwpNoSize : 0);
            if (SetWindowPos(_source.Handle, 0, rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top, flags))
                _applied = rect;
        }
        if (!IsVisible) Show();
        return true;
    }

    private void Refresh()
    {
        if (_sizeMoving || !UpdateBounds()) return;
        var state = _actions.GetOverlayState(_udid, _owner);
        if (GetCursorPos(out var cursor) && cursor.X >= _applied.Left && cursor.X < _applied.Right &&
            cursor.Y >= _applied.Top && cursor.Y < _applied.Bottom)
            _lastInside = DateTime.UtcNow;
        var hovered = DateTime.UtcNow - _lastInside < HoverLinger;
        var controlsVisible = hovered || !state.ControlsOnHoverOnly;
        var controlsOpacity = hovered ? 1.0 : 0.55;

        _state = state;
        PlaceDots();
        // A frame moves the window itself; only a borderless preview needs the dot.
        SetShown(_moveDot.Root, controlsVisible && _contentInset().IsEmpty, controlsOpacity);
        SetShown(_resizeDot.Root, controlsVisible, controlsOpacity);
        // A very small preview keeps only the dots; the bar would cover the phone.
        SetShown(_bar, controlsVisible && ActualWidth >= 160 && ActualHeight >= 180, controlsOpacity);

        var topMost = _isTopMost();
        _muteSymbol.Symbol = state.Muted ? SymbolRegular.SpeakerMute20 : SymbolRegular.Speaker220;
        _muteSymbol.Foreground = state.Muted ? MutedBrush : Brushes.White;
        _muteButton.ToolTip = LocalizationService.Get(state.Muted ? "PreviewUnmute" : "PreviewMute");
        _pinSymbol.Foreground = topMost ? PinBrush : Brushes.White;
    }

    private void PlaceDots()
    {
        if (_state is not { } state) return;
        _moveDot.Place(PreviewDotPositions.Fractions(state.MoveDot, state.MoveDotX, state.MoveDotY),
            ActualWidth, ActualHeight, resize: false);
        _resizeDot.Place(PreviewDotPositions.Fractions(state.ResizeDot, state.ResizeDotX, state.ResizeDotY),
            ActualWidth, ActualHeight, resize: true);
    }

    private static void SetShown(FrameworkElement element, bool shown, double opacity)
    {
        element.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        element.Opacity = opacity;
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

    // Only the dots and the bar take the mouse; everything else passes to the
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

    /// <summary>One dot: a small circle in a larger, invisible press area.</summary>
    private sealed class Dot
    {
        private readonly Ellipse _circle;
        private (double X, double Y, double Width, double Height) _placed = (double.NaN, 0, 0, 0);

        internal Dot(Cursor cursor, string toolTipKey)
        {
            _circle = new Ellipse
            {
                Width = DotSize, Height = DotSize, Fill = Frozen(Color.FromArgb(0xEB, 0xFF, 0xFF, 0xFF)),
                Stroke = Frozen(Color.FromArgb(0x8C, 0x12, 0x12, 0x16)), StrokeThickness = 3,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            };
            Root = new Grid
            {
                Width = DotHitSize, Height = DotHitSize, Background = Brushes.Transparent, Cursor = cursor,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Tag = toolTipKey, Children = { _circle },
            };
        }

        internal Grid Root { get; }

        /// <summary>The non-client code the press is handed to the preview window as.</summary>
        internal int HitTest { get; private set; } = HtCaption;

        // Places the circle at the spot (fractions of the preview, its centre
        // DotEdgeInset in from the edges) and keeps the press area inside.
        internal void Place((double X, double Y) spot, double width, double height, bool resize)
        {
            if (_placed == (spot.X, spot.Y, width, height)) return;
            _placed = (spot.X, spot.Y, width, height);
            var centreX = DotEdgeInset + spot.X * Math.Max(0, width - 2 * DotEdgeInset);
            var centreY = DotEdgeInset + spot.Y * Math.Max(0, height - 2 * DotEdgeInset);
            var left = Math.Clamp(centreX - DotHitSize / 2, 0, Math.Max(0, width - DotHitSize));
            var top = Math.Clamp(centreY - DotHitSize / 2, 0, Math.Max(0, height - DotHitSize));
            Root.Margin = new Thickness(left, top, 0, 0);
            _circle.Margin = new Thickness(centreX - left - DotSize / 2, centreY - top - DotSize / 2, 0, 0);
            if (!resize) return;
            // The resize dot pulls the nearest edge or corner; one in the
            // middle pulls the bottom right corner.
            var (horizontal, vertical) = PreviewDotPositions.Sides(spot.X, spot.Y);
            if (horizontal == 0 && vertical == 0) (horizontal, vertical) = (1, 1);
            (HitTest, Root.Cursor) = (horizontal, vertical) switch
            {
                (-1, -1) => (13, Cursors.SizeNWSE),
                (0, -1) => (12, Cursors.SizeNS),
                (1, -1) => (14, Cursors.SizeNESW),
                (-1, 0) => (10, Cursors.SizeWE),
                (1, 0) => (11, Cursors.SizeWE),
                (-1, 1) => (16, Cursors.SizeNESW),
                (0, 1) => (15, Cursors.SizeNS),
                _ => (17, Cursors.SizeNWSE),
            };
        }
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height,
        uint flags);

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
