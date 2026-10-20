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
internal sealed record PreviewOverlayState(bool KeyboardActive, bool ControlActive, bool Muted,
    bool AudioOnIphone, double Volume, bool StatusIcons, bool ControlsOnHoverOnly);

/// <summary>Actions behind the preview controls; the main window implements them.</summary>
internal interface IPreviewOverlayActions
{
    PreviewOverlayState GetOverlayState(string udid, nint window);
    void ToggleMute(string udid);
    void SetVolume(string udid, double percent);
    void StepIphoneVolume(string udid, bool up);
    void OpenPreviewSettings();
}

/// <summary>
/// Transparent window over a native preview: status icons top left, a dot to
/// move the window (top centre), a dot to resize it (bottom right) and a
/// floating bar with sound, pin and settings. Everything but those controls
/// lets clicks through to the preview, so touch control keeps working.
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
    private static readonly Brush ActiveBrush = Frozen(Color.FromRgb(0x5F, 0xD3, 0x9A));
    private static readonly Brush InactiveBrush = Frozen(Color.FromRgb(0x8E, 0x8E, 0x96));
    private static readonly Brush MutedBrush = Frozen(Color.FromRgb(0xFF, 0x8A, 0x8A));
    private static readonly Brush PinBrush = Frozen(Color.FromRgb(0x69, 0xB1, 0xF8));
    private static readonly Brush PillBrush = Frozen(Color.FromArgb(0xD6, 0x12, 0x12, 0x16));
    private static readonly Brush StatusBrush = Frozen(Color.FromArgb(0xB8, 0x12, 0x12, 0x16));
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
    private bool _updatingSlider;

    private Border _status = null!;
    private SymbolIcon _keyboardIcon = null!;
    private SymbolIcon _controlIcon = null!;
    private SymbolIcon _mutedIcon = null!;
    private SymbolIcon _pinnedIcon = null!;
    private FrameworkElement _moveDot = null!;
    private FrameworkElement _resizeDot = null!;
    private Border _bar = null!;
    private Button _muteButton = null!;
    private SymbolIcon _muteSymbol = null!;
    private StackPanel _pcVolume = null!;
    private Slider _volumeSlider = null!;
    private TextBlock _volumeText = null!;
    private StackPanel _iphoneVolume = null!;
    private Button _pinButton = null!;
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
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    internal static PreviewControlsOverlayWindow ShowFor(nint owner, string udid,
        IPreviewOverlayActions actions, Func<PreviewFrameInsets> contentInset,
        Func<bool> isTopMost, Action togglePin)
    {
        var overlay = new PreviewControlsOverlayWindow(owner, udid, actions, contentInset, isTopMost, togglePin);
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

        _keyboardIcon = StatusIcon(SymbolRegular.Keyboard20);
        _controlIcon = StatusIcon(SymbolRegular.CursorClick20);
        _mutedIcon = StatusIcon(SymbolRegular.SpeakerMute20);
        _mutedIcon.Foreground = MutedBrush;
        _pinnedIcon = StatusIcon(SymbolRegular.Pin20);
        _pinnedIcon.Foreground = PinBrush;
        var statusIcons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var icon in new[] { _keyboardIcon, _controlIcon, _mutedIcon, _pinnedIcon })
            statusIcons.Children.Add(icon);
        _status = new Border
        {
            Background = StatusBrush, CornerRadius = new CornerRadius(12), Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10), Child = statusIcons, IsHitTestVisible = false,
        };
        root.Children.Add(_status);

        _moveDot = Dot(HorizontalAlignment.Center, VerticalAlignment.Top, Cursors.SizeAll, HtCaption,
            "PreviewMoveDot", new Thickness(0, 2, 0, 0));
        _resizeDot = Dot(HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE, HtBottomRight,
            "PreviewResizeDot", new Thickness(0, 0, 2, 2));
        root.Children.Add(_moveDot);
        root.Children.Add(_resizeDot);

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        _muteSymbol = BarIcon(SymbolRegular.Speaker220);
        _muteButton = BarButton(_muteSymbol, () => _actions.ToggleMute(_udid));
        bar.Children.Add(_muteButton);

        _volumeSlider = new Slider
        {
            Minimum = 0, Maximum = 100, Width = 96, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 6, 0), Focusable = false, IsMoveToPointEnabled = true,
        };
        _volumeSlider.ValueChanged += (_, e) =>
        {
            if (!_updatingSlider) _actions.SetVolume(_udid, e.NewValue);
        };
        _volumeText = new TextBlock
        {
            Width = 38, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.WhiteSmoke, FontSize = 12, Margin = new Thickness(0, 0, 6, 0),
        };
        _pcVolume = new StackPanel { Orientation = Orientation.Horizontal };
        _pcVolume.Children.Add(_volumeSlider);
        _pcVolume.Children.Add(_volumeText);
        bar.Children.Add(_pcVolume);

        _iphoneVolume = new StackPanel { Orientation = Orientation.Horizontal };
        _iphoneVolume.Children.Add(BarButton(BarIcon(SymbolRegular.Subtract20),
            () => _actions.StepIphoneVolume(_udid, false), "PreviewIphoneVolumeDown"));
        _iphoneVolume.Children.Add(new TextBlock
        {
            Text = "iPhone", Foreground = Brushes.Gainsboro, FontSize = 12, Width = 48,
            TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        _iphoneVolume.Children.Add(BarButton(BarIcon(SymbolRegular.Add20),
            () => _actions.StepIphoneVolume(_udid, true), "PreviewIphoneVolumeUp"));
        bar.Children.Add(_iphoneVolume);

        bar.Children.Add(new Rectangle
        {
            Width = 1, Height = 20, Fill = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        _pinSymbol = BarIcon(SymbolRegular.Pin20);
        _pinButton = BarButton(_pinSymbol, () => { _togglePin(); Refresh(); }, "PreviewPinButton");
        bar.Children.Add(_pinButton);
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

    private static SymbolIcon StatusIcon(SymbolRegular symbol) => new()
    {
        Symbol = symbol, FontSize = 15, Margin = new Thickness(3, 0, 3, 0), Foreground = InactiveBrush,
    };

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
        var hovered = DateTime.UtcNow - _lastInside < HoverLinger || _volumeSlider.IsMouseCaptureWithin;
        var controlsVisible = hovered || !state.ControlsOnHoverOnly;
        var controlsOpacity = hovered ? 1.0 : 0.55;
        foreach (var element in new[] { _moveDot, _resizeDot, _bar })
        {
            element.Visibility = controlsVisible ? Visibility.Visible : Visibility.Collapsed;
            element.Opacity = controlsOpacity;
        }
        // A very small preview keeps only the dots; the bar would cover the phone.
        if (Width < 230 || Height < 220) _bar.Visibility = Visibility.Collapsed;

        var topMost = _isTopMost();
        _status.Visibility = state.StatusIcons ? Visibility.Visible : Visibility.Collapsed;
        _keyboardIcon.Foreground = state.KeyboardActive ? ActiveBrush : InactiveBrush;
        _keyboardIcon.ToolTip = LocalizationService.Get(state.KeyboardActive ? "PreviewKeyboardActive" : "PreviewKeyboardInactive");
        _controlIcon.Foreground = state.ControlActive ? ActiveBrush : InactiveBrush;
        _controlIcon.ToolTip = LocalizationService.Get(state.ControlActive ? "PreviewControlActive" : "PreviewControlInactive");
        _mutedIcon.Visibility = state.Muted ? Visibility.Visible : Visibility.Collapsed;
        _pinnedIcon.Visibility = topMost ? Visibility.Visible : Visibility.Collapsed;

        _muteSymbol.Symbol = state.Muted ? SymbolRegular.SpeakerMute20 : SymbolRegular.Speaker220;
        _muteSymbol.Foreground = state.Muted ? MutedBrush : Brushes.White;
        _muteButton.ToolTip = LocalizationService.Get(state.Muted ? "PreviewUnmute" : "PreviewMute");
        _pcVolume.Visibility = state.AudioOnIphone ? Visibility.Collapsed : Visibility.Visible;
        _iphoneVolume.Visibility = state.AudioOnIphone ? Visibility.Visible : Visibility.Collapsed;
        if (!_volumeSlider.IsMouseCaptureWithin)
        {
            _updatingSlider = true;
            _volumeSlider.Value = state.Volume;
            _updatingSlider = false;
        }
        _volumeText.Text = state.Muted ? "0 %" : $"{state.Volume:F0} %";
        _pinSymbol.Foreground = topMost ? PinBrush : Brushes.White;
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
