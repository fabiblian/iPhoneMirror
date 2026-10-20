namespace IPhoneMirror.App.Models;

/// <summary>
/// Everyday conveniences: what happens when an iPhone is plugged in, the
/// one-time control notices, mixed typing, wired scrolling, preview
/// sharpening and the app's mute state.
/// </summary>
internal sealed class EverydaySettings
{
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
    /// While key mapping is on, keys without a mapping still type on the
    /// iPhone instead of reaching Windows.
    /// </summary>
    public bool MixMappingAndTyping { get; set; } = true;

    /// <summary>
    /// Virtual key that switches key mapping off for typing while mapping is
    /// on (VK_RCONTROL by default, 0 = none).
    /// </summary>
    public int TypeOnlyToggleVirtualKey { get; set; } = 0xA3;

    /// <summary>
    /// Mouse wheel and touchpad scroll during wired and wireless control. The
    /// speed is the shared scroll speed of the mouse settings.
    /// </summary>
    public bool WiredWheelScroll { get; set; } = true;
    /// <summary>
    /// Off: wheel down moves the content up, as in Windows and like a swipe
    /// up on the iPhone. On: the other way round.
    /// </summary>
    public bool ReverseWheelDirection { get; set; }

    /// <summary>Sharpen text when the preview shows the phone strongly downscaled.</summary>
    public bool SharpenSmallPreview { get; set; } = true;

    /// <summary>The app's own PC audio is muted.</summary>
    public bool Muted { get; set; }

    internal EverydaySettings Clone() => (EverydaySettings)MemberwiseClone();
}
