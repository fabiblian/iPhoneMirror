using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using IPhoneMirror.App.Localization;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    // Fabian's personal branch launcher installs itself here (see
    // tools/version-launcher). Without it the menu entry stays hidden.
    private static string VersionLauncherScript => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "iPhoneMirrorVersions", "launcher", "iPhoneMirrorLauncher.ps1");

    private void OnMoreMenuClick(object sender, RoutedEventArgs e)
    {
        VersionLauncherMenuItem.Visibility = File.Exists(VersionLauncherScript)
            ? Visibility.Visible : Visibility.Collapsed;
        MoreMenu.PlacementTarget = MoreMenuButton;
        MoreMenu.Placement = PlacementMode.Bottom;
        MoreMenu.IsOpen = true;
    }

    private void OnMoreMenuDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.ShowAboutWindow(this, _viewModel, showDiagnostics: true);
    }

    private void OnOpenVersionLauncherClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass",
                         "-WindowStyle", "Hidden", "-File", VersionLauncherScript })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
        }
        catch (Exception error)
        {
            _viewModel.AddUiLog(LocalizationService.Format("VersionLauncherFailedFormat", error.Message));
        }
    }
}
