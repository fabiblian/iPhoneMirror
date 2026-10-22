using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Updater;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    // Shows "v1.8.5-pre2 · redesign · 94902cb" in the window title (taskbar,
    // Alt+Tab) and in the heading's tooltip, so a test run always shows which
    // build is running without cluttering the heading itself.
    private void InitializeBuildLabel()
    {
        ApplyBuildLabelTitle();
        LocalizationService.RefreshWhenLanguageChanges(this, ApplyBuildLabelTitle);
    }

    private void ApplyBuildLabelTitle()
    {
        Title = $"{LocalizationService.Get("WindowTitleConnectivity")} · {VersionManager.BuildLabel}";
        WorkspaceHeading.ToolTip =
            $"{LocalizationService.Get("AppSubtitleConnectivity")}\n{VersionManager.BuildLabel}";
    }
}
