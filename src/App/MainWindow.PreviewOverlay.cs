using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App;

// The controls drawn over each preview window act through the main window,
// which knows the sound settings.
public partial class MainWindow : IPreviewOverlayActions
{
    private void InitializePreviewOverlay() => _secondaryMirrors.OverlayActions = this;

    PreviewOverlayState IPreviewOverlayActions.GetOverlayState(string udid, nint window)
    {
        var everyday = EverydaySettingsForInput;
        return new(
            Muted: _viewModel.IsMuted,
            ControlsOnHoverOnly: everyday.PreviewControlsOnHoverOnly,
            MoveDot: everyday.PreviewMoveDot, MoveDotX: everyday.PreviewMoveDotX, MoveDotY: everyday.PreviewMoveDotY,
            ResizeDot: everyday.PreviewResizeDot, ResizeDotX: everyday.PreviewResizeDotX,
            ResizeDotY: everyday.PreviewResizeDotY);
    }

    void IPreviewOverlayActions.ToggleMute(string udid) => ToggleAppMute(udid);

    void IPreviewOverlayActions.OpenPreviewSettings() => PreviewSettingsWindow.ShowOrActivate();
}
