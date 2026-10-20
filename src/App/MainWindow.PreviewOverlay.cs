using IPhoneMirror.App.Services;
using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App;

// The controls drawn over each preview window act through the main window,
// which knows the control routes and the sound settings.
public partial class MainWindow : IPreviewOverlayActions
{
    private void InitializePreviewOverlay() => _secondaryMirrors.OverlayActions = this;

    PreviewOverlayState IPreviewOverlayActions.GetOverlayState(string udid, nint window)
    {
        var everyday = EverydaySettingsForInput;
        return new(
            KeyboardActive: CanForwardControlKeyboard(udid, window),
            ControlActive: _viewModel.IsDeviceControlEnabled(udid) || IsBluetoothControlActiveFor(udid),
            Muted: _viewModel.IsMuted,
            AudioOnIphone: _viewModel.IsAudioOnIphone(udid),
            Volume: _viewModel.GetDeviceVolume(udid),
            StatusIcons: everyday.PreviewStatusIcons,
            ControlsOnHoverOnly: everyday.PreviewControlsOnHoverOnly);
    }

    void IPreviewOverlayActions.ToggleMute(string udid) => ToggleAppMute(udid);

    void IPreviewOverlayActions.SetVolume(string udid, double percent) =>
        _viewModel.SetDeviceVolume(udid, percent);

    void IPreviewOverlayActions.StepIphoneVolume(string udid, bool up)
    {
        if (up && _viewModel.IsMuted) _viewModel.IsMuted = false;
        _ = PressIphoneConsumerButtonAsync(up
            ? CoreDeviceTouchProtocol.IndigoVolumeUp
            : CoreDeviceTouchProtocol.IndigoVolumeDown, udid);
    }

    void IPreviewOverlayActions.OpenPreviewSettings() => PreviewSettingsWindow.ShowOrActivate();
}
