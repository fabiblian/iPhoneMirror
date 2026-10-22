using IPhoneMirror.App.Models;

namespace IPhoneMirror.App.ViewModels;

// Which actions the toolbar above the preview shows (Settings > Toolbar), and
// the estimated iPhone volume behind its slider.
internal sealed partial class MainViewModel
{
    /// <summary>The iPhone has 16 volume steps; the app cannot read the current one.</summary>
    internal const int IphoneVolumeSteps = 16;
    private double _iphoneVolumeLevel = IphoneVolumeSteps / 2;

    /// <summary>The slider moved by this many steps to the new level; the window presses the buttons.</summary>
    internal event Action<int, int>? IphoneVolumeStepsRequested;

    /// <summary>
    /// Estimated iPhone volume step behind the toolbar slider. Moving it presses
    /// the iPhone's volume buttons; dragging to either end resynchronizes it.
    /// </summary>
    public double IphoneVolumeLevel
    {
        get => _iphoneVolumeLevel;
        set
        {
            var previous = _iphoneVolumeLevel;
            if (!SetIphoneVolumeEstimate(value)) return;
            IphoneVolumeStepsRequested?.Invoke((int)(_iphoneVolumeLevel - previous), (int)_iphoneVolumeLevel);
        }
    }

    public double IphoneVolumePercent => _iphoneVolumeLevel * 100 / IphoneVolumeSteps;

    /// <summary>Follows a volume button press made elsewhere (shortcut) without pressing again.</summary>
    internal void StepIphoneVolumeEstimate(int delta) => SetIphoneVolumeEstimate(_iphoneVolumeLevel + delta);

    private bool SetIphoneVolumeEstimate(double value)
    {
        var level = Math.Clamp(Math.Round(value), 0, IphoneVolumeSteps);
        if (_iphoneVolumeLevel == level) return false;
        _iphoneVolumeLevel = level;
        OnPropertyChanged(nameof(IphoneVolumeLevel));
        OnPropertyChanged(nameof(IphoneVolumePercent));
        return true;
    }

    private static PreviewToolbarSettings Toolbar => Everyday.PreviewToolbar ??= new();

    public bool ShowToolbarStop
    {
        get => Toolbar.Stop;
        set => SetEveryday(value, s => s.PreviewToolbar.Stop, (s, v) => s.PreviewToolbar.Stop = v,
            "toolbar_stop");
    }

    public bool ShowToolbarWirelessControl
    {
        get => Toolbar.WirelessControl;
        set => SetEveryday(value, s => s.PreviewToolbar.WirelessControl,
            (s, v) => s.PreviewToolbar.WirelessControl = v, "toolbar_wireless_control");
    }

    public bool ShowToolbarUsbControl
    {
        get => Toolbar.UsbControl;
        set => SetEveryday(value, s => s.PreviewToolbar.UsbControl,
            (s, v) => s.PreviewToolbar.UsbControl = v, "toolbar_usb_control");
    }

    public bool ShowToolbarBluetoothControl
    {
        get => Toolbar.BluetoothControl;
        set => SetEveryday(value, s => s.PreviewToolbar.BluetoothControl,
            (s, v) => s.PreviewToolbar.BluetoothControl = v, "toolbar_bluetooth_control");
    }

    public bool ShowToolbarMediaOutput
    {
        get => Toolbar.MediaOutput;
        set => SetEveryday(value, s => s.PreviewToolbar.MediaOutput,
            (s, v) => s.PreviewToolbar.MediaOutput = v, "toolbar_media_output");
    }

    public bool ShowToolbarVolume
    {
        get => Toolbar.Volume;
        set => SetEveryday(value, s => s.PreviewToolbar.Volume, (s, v) => s.PreviewToolbar.Volume = v,
            "toolbar_volume");
    }

    public bool ShowToolbarImageSettings
    {
        get => Toolbar.ImageSettings;
        set => SetEveryday(value, s => s.PreviewToolbar.ImageSettings,
            (s, v) => s.PreviewToolbar.ImageSettings = v, "toolbar_image_settings");
    }

    public bool ShowToolbarPreviewWindow
    {
        get => Toolbar.PreviewWindow;
        set => SetEveryday(value, s => s.PreviewToolbar.PreviewWindow,
            (s, v) => s.PreviewToolbar.PreviewWindow = v, "toolbar_preview_window");
    }

    public bool ShowToolbarScreenshot
    {
        get => Toolbar.Screenshot;
        set => SetEveryday(value, s => s.PreviewToolbar.Screenshot,
            (s, v) => s.PreviewToolbar.Screenshot = v, "toolbar_screenshot");
    }

    public bool ShowToolbarFullScreen
    {
        get => Toolbar.FullScreen;
        set => SetEveryday(value, s => s.PreviewToolbar.FullScreen,
            (s, v) => s.PreviewToolbar.FullScreen = v, "toolbar_full_screen");
    }
}
