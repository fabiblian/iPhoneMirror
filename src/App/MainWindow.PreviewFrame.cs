using System.Windows;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App;

// Independent preview frame: Preview settings window, the frame's home pill
// and starting touch control automatically when a framed preview opens.
public partial class MainWindow
{
    private const byte BluetoothHomeKeyUsage = 0x0B; // Globe+H

    private void OnPreviewSettingsClick(object sender, RoutedEventArgs e) =>
        PreviewSettingsWindow.ShowOrActivate();

    private void OnIndependentPreviewOpened(string udid, nint window)
    {
        var settings = PreviewFrameService.Current;
        if (!settings.Enabled || !settings.AutoStartControl) return;
        if (IsBluetoothControlActiveFor(udid) || !_viewModel.CanAutoStartDeviceControl(udid)) return;
        _viewModel.AddDiagnosticLog(AppLog.Event("independent_preview_auto_control",
            ("device", AppLog.Device(udid)),
            ("wired", _viewModel.PrefersWiredControl(udid))));
        StartIndependentPreviewControl(udid, window);
    }

    private void StartIndependentPreviewControl(string udid, nint window)
    {
        if (_viewModel.PrefersWiredControl(udid)) OnIndependentUsbControlRequested(udid, window);
        else OnIndependentWirelessControlRequested(udid, window);
    }

    // The frame's home pill does what swiping up from the bottom edge does:
    // the hardware Home press over wired/wireless control, Globe+H over
    // Bluetooth. Without control it starts control first.
    private async void OnIndependentHomeRequested(string udid, nint window)
    {
        try
        {
            if (_viewModel.IsUsbControlTarget(udid))
            {
                const ushort page = CoreDeviceTouchProtocol.IndigoConsumerUsagePage;
                const ushort home = CoreDeviceTouchProtocol.IndigoHome;
                try
                {
                    await _viewModel.SendUsbButtonAsync(page, home, "down", udid);
                    await Task.Delay(50);
                }
                finally
                {
                    await _viewModel.SendUsbButtonAsync(page, home, "up", udid);
                }
                return;
            }
            if (IsBluetoothControlActiveFor(udid))
            {
                await _viewModel.SendBluetoothSystemShortcutAsync(BluetoothHomeKeyUsage, udid);
                return;
            }
            _viewModel.AddUiLog(LocalizationService.Get("PreviewHomeNeedsControl"));
            if (_viewModel.CanAutoStartDeviceControl(udid))
                StartIndependentPreviewControl(udid, window);
        }
        catch (Exception error)
        {
            _viewModel.AddUiLog(LocalizationService.Format("PreviewHomeFailedFormat", error.Message));
            _viewModel.AddDiagnosticLog(AppLog.Event("independent_home_failed",
                ("device", AppLog.Device(udid)), ("error", AppLog.Error(error))));
        }
    }
}
