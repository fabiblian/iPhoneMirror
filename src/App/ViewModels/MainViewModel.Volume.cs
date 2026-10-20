using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App.ViewModels;

// The app's own sound: muting keeps every session's volume but plays it at 0,
// and the state survives a restart. When the iPhone keeps its audio (USB audio
// off), volume changes go to the iPhone's own buttons instead.
internal sealed partial class MainViewModel
{
    public bool IsMuted
    {
        get => Everyday.Muted;
        set
        {
            if (!SetEveryday(value, s => s.Muted, (s, v) => s.Muted = v, "audio_muted")) return;
            ApplyMuteToSessions();
        }
    }

    /// <summary>The selected wired iPhone keeps its audio instead of sending it to the PC.</summary>
    public bool AudioOnIphone =>
        SelectedDevice is { IsWireless: false, IsMediaCast: false } && !CaptureUsbAudio;

    /// <summary>Changes the app's PC volume by <paramref name="delta"/> percent; this also unmutes.</summary>
    internal void StepPlaybackVolume(double delta) => PlaybackVolume = PlaybackVolume + delta;

    /// <summary>The device's wired audio stays on the iPhone (USB audio off).</summary>
    internal bool IsAudioOnIphone(string? udid)
    {
        if (DeviceViewModel.UdidEquals(udid, SelectedDevice?.Udid)) return AudioOnIphone;
        return udid is not null && !DeviceViewModel.IsWirelessUdid(udid) &&
            _sessions.TryGet(udid, out var state) && state.UsbVideoOnly;
    }

    /// <summary>The device's own app volume in percent.</summary>
    internal double GetDeviceVolume(string? udid) =>
        DeviceViewModel.UdidEquals(udid, SelectedDevice?.Udid) ? PlaybackVolume
        : udid is not null && _sessions.TryGet(udid, out var state) ? state.Volume : 100;

    /// <summary>Sets the device's app volume; this also unmutes.</summary>
    internal void SetDeviceVolume(string? udid, double percent)
    {
        if (!double.IsFinite(percent) || udid is null) return;
        if (DeviceViewModel.UdidEquals(udid, SelectedDevice?.Udid))
        {
            PlaybackVolume = percent;
            return;
        }
        if (!_sessions.TryGet(udid, out var state) || state.Handle is not { IsInvalid: false } handle) return;
        if (IsMuted) IsMuted = false;
        var clamped = Math.Clamp(percent, 0, 100);
        var result = InvokeDeviceSetting(() => _core.SetDeviceAudioVolume(handle, clamped / 100.0));
        if (result.Success) state.Volume = clamped;
    }

    private double MutedVolume(double volume) => IsMuted ? 0 : volume;

    private void ApplyMuteToSessions()
    {
        foreach (var state in _sessions.Values.ToArray())
        {
            if (!state.HasSession || state.IsStarting || state.IsStopping || state.Handle is not { } handle)
                continue;
            var volume = MutedVolume(state.Volume) / 100.0;
            var result = InvokeDeviceSetting(() => _core.SetDeviceAudioVolume(handle, volume));
            if (!result.Success)
                AddDiagnosticLog(AppLog.Event("audio_mute_apply_failed",
                    ("device", AppLog.Device(state.Udid)), ("message", result.Message)));
        }
    }
}
