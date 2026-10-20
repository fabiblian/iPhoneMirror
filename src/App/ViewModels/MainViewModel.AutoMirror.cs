using System.Windows;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App.ViewModels;

// Plug-in detection for "start mirroring when an iPhone is plugged in". A
// wired phone is announced once when it shows up ready; it is announced again
// only after it was gone for a few enumerations, so the short usbmux gaps of a
// QuickTime switch or a stopped capture never restart mirroring on their own.
internal sealed partial class MainViewModel
{
    private const int WiredDepartureEnumerations = 2;
    private readonly HashSet<string> _announcedWiredUdids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _wiredMissingEnumerations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A ready wired iPhone was plugged in (or was present at launch).</summary>
    internal event Action<string>? WiredDeviceConnected;

    private static EverydaySettings Everyday =>
        (Application.Current as App)?.UpdateSettings.Everyday ?? new();

    public bool AutoMirrorOnConnect
    {
        get => Everyday.AutoMirrorOnConnect;
        set => SetEveryday(value, s => s.AutoMirrorOnConnect, (s, v) => s.AutoMirrorOnConnect = v,
            "auto_mirror_on_connect");
    }

    public bool AutoOpenPreviewOnConnect
    {
        get => Everyday.AutoOpenPreviewOnConnect;
        set => SetEveryday(value, s => s.AutoOpenPreviewOnConnect, (s, v) => s.AutoOpenPreviewOnConnect = v,
            "auto_open_preview_on_connect");
    }

    public bool AutoStartControlOnConnect
    {
        get => Everyday.AutoStartControlOnConnect;
        set => SetEveryday(value, s => s.AutoStartControlOnConnect, (s, v) => s.AutoStartControlOnConnect = v,
            "auto_start_control_on_connect");
    }

    /// <summary>Starts mirroring the selected device and completes once the start finished.</summary>
    internal Task StartSelectedCaptureAsync() => StartAsync();

    private bool SetEveryday<T>(T value, Func<EverydaySettings, T> get,
        Action<EverydaySettings, T> set, string logName,
        [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (Application.Current is not App app) return false;
        var settings = app.UpdateSettings.Everyday;
        var previous = get(settings);
        if (EqualityComparer<T>.Default.Equals(previous, value)) return false;
        set(settings, value);
        if (!app.SaveUpdateSettings()) set(settings, previous);
        OnPropertyChanged(property);
        AddDiagnosticLog(AppLog.Event(logName + "_changed", ("value", get(settings))));
        return true;
    }

    private void ObserveWiredArrivals(bool enumerated)
    {
        if (!enumerated) return;
        var wired = Devices.Where(device => !device.IsWireless && !device.IsMediaCast).ToArray();
        foreach (var udid in _announcedWiredUdids.ToArray())
        {
            if (wired.Any(device => DeviceViewModel.UdidEquals(device.Udid, udid))) continue;
            var misses = _wiredMissingEnumerations.GetValueOrDefault(udid) + 1;
            if (misses < WiredDepartureEnumerations)
            {
                _wiredMissingEnumerations[udid] = misses;
                continue;
            }
            _wiredMissingEnumerations.Remove(udid);
            _announcedWiredUdids.Remove(udid);
            AddDiagnosticLog(AppLog.Event("wired_device_departed", ("device", AppLog.Device(udid))));
        }
        foreach (var device in wired)
        {
            _wiredMissingEnumerations.Remove(device.Udid);
            // A phone that is not trusted or not unlocked yet is announced
            // once it becomes ready, so the start does not fail on arrival.
            if (!device.Ready || !_announcedWiredUdids.Add(device.Udid)) continue;
            AddDiagnosticLog(AppLog.Event("wired_device_arrived", ("device", AppLog.Device(device.Udid))));
            WiredDeviceConnected?.Invoke(device.Udid);
        }
    }
}
