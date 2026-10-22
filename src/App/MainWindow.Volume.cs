using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.ViewModels;

namespace IPhoneMirror.App;

// Volume and mute for the toolbar and the shortcuts
// Ctrl+Alt+M and Ctrl+Alt+Plus/Minus. Sound on the PC changes only this app's
// volume; sound that stays on the iPhone uses the iPhone's volume buttons
// (the app cannot read the iPhone's level, so its slider is an estimate).
public partial class MainWindow
{
    private const double PlaybackVolumeStep = 10;
    private readonly SemaphoreSlim _iphoneConsumerButtonGate = new(1, 1);

    private void ToggleAppMute() => ToggleAppMute(ActiveInputDeviceUdid ?? _viewModel.SelectedDevice?.Udid);

    private void ToggleAppMute(string? udid)
    {
        _viewModel.IsMuted = !_viewModel.IsMuted;
        if (_viewModel.IsAudioOnIphone(udid))
            _ = PressIphoneConsumerButtonAsync(CoreDeviceTouchProtocol.IndigoMute, udid);
        _viewModel.AddUiLog(LocalizationService.Get(_viewModel.IsMuted ? "AudioMuted" : "AudioUnmuted"));
    }

    private void OnToolbarMuteClick(object sender, System.Windows.RoutedEventArgs e) =>
        ToggleAppMute(_viewModel.SelectedDevice?.Udid);

    private void InitializeIphoneVolumeSlider() =>
        _viewModel.IphoneVolumeStepsRequested += (delta, level) =>
            _ = PressIphoneVolumeStepsAsync(delta, level, _viewModel.SelectedDevice?.Udid);

    // The slider is an estimate. Reaching either end presses a few extra times,
    // so the iPhone really is at its minimum or maximum and the estimate is exact again.
    private async Task PressIphoneVolumeStepsAsync(int delta, int level, string? udid)
    {
        const int resyncPresses = 2;
        var presses = Math.Abs(delta);
        if (level is 0 or MainViewModel.IphoneVolumeSteps) presses += resyncPresses;
        var usage = delta > 0 ? CoreDeviceTouchProtocol.IndigoVolumeUp : CoreDeviceTouchProtocol.IndigoVolumeDown;
        for (var i = 0; i < presses; i++)
            await PressIphoneConsumerButtonAsync(usage, udid);
    }

    private async Task StepAppVolumeAsync(bool up)
    {
        var target = ActiveInputDeviceUdid ?? _viewModel.SelectedDevice?.Udid;
        if (!_viewModel.IsAudioOnIphone(target))
        {
            _viewModel.StepPlaybackVolume(up ? PlaybackVolumeStep : -PlaybackVolumeStep);
            return;
        }
        if (DeviceViewModel.UdidEquals(target, _viewModel.SelectedDevice?.Udid))
            _viewModel.StepIphoneVolumeEstimate(up ? 1 : -1);
        await PressIphoneConsumerButtonAsync(up
            ? CoreDeviceTouchProtocol.IndigoVolumeUp
            : CoreDeviceTouchProtocol.IndigoVolumeDown, target);
    }

    private async Task PressIphoneConsumerButtonAsync(ushort usage, string? udid)
    {
        udid ??= _viewModel.SelectedDevice?.Udid;
        // Keep each press/release pair together when the user clicks quickly.
        await _iphoneConsumerButtonGate.WaitAsync();
        try
        {
            if (_viewModel.IsUsbControlTarget(udid))
            {
                const ushort page = CoreDeviceTouchProtocol.IndigoConsumerUsagePage;
                try
                {
                    await _viewModel.SendUsbButtonAsync(page, usage, "down", udid);
                    await Task.Delay(50);
                }
                finally
                {
                    await _viewModel.SendUsbButtonAsync(page, usage, "up", udid);
                }
                return;
            }
            if (_viewModel.BluetoothControlIsInputEnabled && _viewModel.IsBluetoothControlTarget(udid))
            {
                await _viewModel.SendBluetoothConsumerShortcutAsync(usage, 50, udid);
                return;
            }
            _viewModel.AddUiLog(LocalizationService.Get("IphoneVolumeNeedsControl"));
        }
        catch (Exception error)
        {
            _viewModel.AddDiagnosticLog(AppLog.Event("iphone_volume_button_failed",
                ("device", AppLog.Device(udid)), ("usage", usage), ("error", AppLog.Error(error))));
        }
        finally { _iphoneConsumerButtonGate.Release(); }
    }
}
