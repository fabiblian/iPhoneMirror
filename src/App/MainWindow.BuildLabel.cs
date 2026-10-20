using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Updater;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    // Shows "v1.8.5-pre2 · redesign · 94902cb" in the window title (taskbar,
    // Alt+Tab) and beside the heading, so a test run always shows which build
    // is running.
    private void InitializeBuildLabel()
    {
        WorkspaceBuildLabel.Text = "  " + VersionManager.BuildLabel;
        ApplyBuildLabelTitle();
        LocalizationService.RefreshWhenLanguageChanges(this, ApplyBuildLabelTitle);
    }

    private void ApplyBuildLabelTitle() =>
        Title = $"{LocalizationService.Get("WindowTitleConnectivity")} · {VersionManager.BuildLabel}";
}
