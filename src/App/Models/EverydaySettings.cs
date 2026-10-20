namespace IPhoneMirror.App.Models;

/// <summary>
/// Everyday conveniences: what happens when an iPhone is plugged in, the
/// one-time control notices, the type-only key, wired scrolling, preview
/// sharpening and the app's mute state.
/// </summary>
internal sealed class EverydaySettings
{
    internal const double MinWheelSensitivity = 10;
    internal const double MaxWheelSensitivity = 1000;
    internal const double DefaultWheelSensitivity = 400;

    /// <summary>Start mirroring when an iPhone is plugged in (or present at launch).</summary>
    public bool AutoMirrorOnConnect { get; set; } = true;
    /// <summary>Open the preview window once that automatic mirroring runs.</summary>
    public bool AutoOpenPreviewOnConnect { get; set; } = true;
    /// <summary>Start wired control together with that automatic mirroring.</summary>
    public bool AutoStartControlOnConnect { get; set; } = true;

    /// <summary>The "Before enabling wired control" notice was confirmed once.</summary>
    public bool WiredControlNoticeAcknowledged { get; set; }
    /// <summary>The "Before enabling wireless control" notice was confirmed once.</summary>
    public bool WirelessControlNoticeAcknowledged { get; set; }

    /// <summary>
    /// Virtual key that switches key mapping off for typing while mapping is
    /// on (VK_RCONTROL by default, 0 = none).
    /// </summary>
    public int TypeOnlyToggleVirtualKey { get; set; } = 0xA3;

    /// <summary>Mouse wheel and touchpad scroll during wired and wireless control.</summary>
    public bool WiredWheelScroll { get; set; } = true;
    /// <summary>Wheel travel per notch, same scale as the Bluetooth wheel setting.</summary>
    public double WiredWheelSensitivity { get; set; } = DefaultWheelSensitivity;
    /// <summary>Wheel down moves the content up, like a touchpad on the iPhone.</summary>
    public bool WheelNaturalDirection { get; set; } = true;

    /// <summary>Sharpen text when the preview shows the phone strongly downscaled.</summary>
    public bool SharpenSmallPreview { get; set; } = true;

    /// <summary>The app's own PC audio is muted.</summary>
    public bool Muted { get; set; }

    internal EverydaySettings Clone() => (EverydaySettings)MemberwiseClone();

    internal EverydaySettings Normalized()
    {
        var copy = Clone();
        copy.WiredWheelSensitivity = double.IsFinite(WiredWheelSensitivity)
            ? Math.Clamp(WiredWheelSensitivity, MinWheelSensitivity, MaxWheelSensitivity)
            : DefaultWheelSensitivity;
        copy.TypeOnlyToggleVirtualKey = TypeOnlyToggleVirtualKey is >= 0 and <= 0xFE
            ? TypeOnlyToggleVirtualKey : 0xA3;
        return copy;
    }
}
