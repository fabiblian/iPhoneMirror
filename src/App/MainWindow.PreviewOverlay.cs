using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App;

// The main window supplies visibility settings for the preview move/resize dots.
public partial class MainWindow : IPreviewOverlayActions
{
    private void InitializePreviewOverlay() => _secondaryMirrors.OverlayActions = this;

    PreviewOverlayState IPreviewOverlayActions.GetOverlayState(string udid, nint window) =>
        new(ControlsOnHoverOnly: EverydaySettingsForInput.PreviewControlsOnHoverOnly);
}
