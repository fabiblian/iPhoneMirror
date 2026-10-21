using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App;

// The controls drawn over each preview window act through the main window,
// which knows the control routes and the sound settings.
public partial class MainWindow : IPreviewOverlayActions
{
    private void InitializePreviewOverlay() => _secondaryMirrors.OverlayActions = this;

    PreviewOverlayState IPreviewOverlayActions.GetOverlayState(string udid, nint window) =>
        new(Muted: _viewModel.IsMuted,
            ControlsOnHoverOnly: EverydaySettingsForInput.PreviewControlsOnHoverOnly);

    void IPreviewOverlayActions.ToggleMute(string udid) => ToggleAppMute(udid);

    void IPreviewOverlayActions.OpenPreviewSettings() => PreviewSettingsWindow.ShowOrActivate();
}
