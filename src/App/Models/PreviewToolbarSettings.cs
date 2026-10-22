namespace IPhoneMirror.App.Models;

/// <summary>
/// Which actions the toolbar above the preview shows. Stop, control, media
/// output and volume are on by default; the preview tools are opt-in.
/// </summary>
internal sealed class PreviewToolbarSettings
{
    public bool Stop { get; set; } = true;
    public bool WirelessControl { get; set; } = true;
    public bool UsbControl { get; set; } = true;
    public bool BluetoothControl { get; set; } = true;
    public bool MediaOutput { get; set; } = true;
    public bool Volume { get; set; } = true;
    public bool ImageSettings { get; set; }
    public bool PreviewWindow { get; set; }
    public bool Screenshot { get; set; }
    public bool FullScreen { get; set; }

    internal PreviewToolbarSettings Clone() => (PreviewToolbarSettings)MemberwiseClone();
}
