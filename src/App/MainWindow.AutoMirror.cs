using System.Windows.Threading;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App;

// Plugging in an iPhone starts mirroring, opens the preview window and starts
// wired control, each step switchable in the settings.
public partial class MainWindow
{
    private bool _autoMirrorRunning;

    private void InitializeAutoMirror() =>
        _viewModel.WiredDeviceConnected += udid =>
            // The arrival is reported while the device refresh still holds
            // the core gate; start after it returned.
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => _ = AutoMirrorConnectedDeviceAsync(udid)));

    private async Task AutoMirrorConnectedDeviceAsync(string udid)
    {
        if (!_viewModel.AutoMirrorOnConnect || _autoMirrorRunning) return;
        // A phone that is already mirrored keeps the main preview; plugging
        // in a second one only adds its card.
        if (_viewModel.HasCaptureSession || _viewModel.IsCapturing || _viewModel.IsMediaCasting) return;
        var device = _viewModel.Devices.FirstOrDefault(d => DeviceViewModel.UdidEquals(d.Udid, udid));
        if (device is null || device.IsMediaCast || device.IsWireless) return;
        _autoMirrorRunning = true;
        try
        {
            _viewModel.AddDiagnosticLog(AppLog.Event("auto_mirror_begin",
                ("device", AppLog.Device(udid)),
                ("preview", _viewModel.AutoOpenPreviewOnConnect),
                ("control", _viewModel.AutoStartControlOnConnect)));
            _viewModel.SelectedDevice = device;
            if (!_viewModel.StartCommand.CanExecute(null)) return;
            await _viewModel.StartSelectedCaptureAsync();
            if (!_viewModel.HasCaptureSession ||
                !DeviceViewModel.UdidEquals(_viewModel.SelectedDevice?.Udid, udid)) return;
            _viewModel.AddUiLog(LocalizationService.Format("AutoMirrorStartedFormat", device.DisplayName));

            if (_viewModel.AutoOpenPreviewOnConnect) await OpenPreviewWindowAsync();
            if (_viewModel.AutoStartControlOnConnect && !_viewModel.IsDeviceControlEnabled(udid) &&
                _viewModel.CanAutoStartDeviceControl(udid))
            {
                ShowReverseControlStatus(ControlStatusMode.Usb, udid);
                await _viewModel.StartUsbControlAsync(udid);
            }
        }
        catch (Exception error)
        {
            _viewModel.AddDiagnosticLog(AppLog.Event("auto_mirror_failed",
                ("device", AppLog.Device(udid)), ("error", AppLog.Error(error))));
        }
        finally
        {
            _autoMirrorRunning = false;
        }
    }
}
