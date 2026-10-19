using System.Windows;
using System.Windows.Controls.Primitives;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    private void OnMoreMenuClick(object sender, RoutedEventArgs e)
    {
        MoreMenu.PlacementTarget = MoreMenuButton;
        MoreMenu.Placement = PlacementMode.Bottom;
        MoreMenu.IsOpen = true;
    }

    private void OnMoreMenuDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.ShowAboutWindow(this, _viewModel, showDiagnostics: true);
    }
}
