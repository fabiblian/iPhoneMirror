using System.Windows;
using System.Windows.Controls;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    private RadioButton[] SettingsTabButtons =>
        [SettingsTabVideo, SettingsTabControl, SettingsTabWireless, SettingsTabApp];

    private void InitializeSettingsTabs()
    {
        var saved = Application.Current is App app ? app.UpdateSettings.SettingsTab : null;
        var tab = SettingsTabButtons.FirstOrDefault(button =>
            string.Equals(button.Tag as string, saved, StringComparison.Ordinal)) ?? SettingsTabVideo;
        tab.IsChecked = true;
        ShowSettingsPage(tab.Tag as string);
        StatsToggle.IsChecked = Application.Current is not App { UpdateSettings.StatsExpanded: false };
        ApplyStatsExpanded();
    }

    private void OnStatsToggleChanged(object sender, RoutedEventArgs e)
    {
        if (StatisticsViewport is null) return;
        ApplyStatsExpanded();
        var expanded = StatsToggle.IsChecked == true;
        if (Application.Current is App app && app.UpdateSettings.StatsExpanded != expanded)
        {
            app.UpdateSettings.StatsExpanded = expanded;
            app.SaveUpdateSettings();
        }
    }

    private void ApplyStatsExpanded() =>
        StatisticsViewport.Visibility = StatsToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void OnSettingsTabChecked(object sender, RoutedEventArgs e)
    {
        // Checked can fire while the window is still loading its named parts.
        if (sender is not RadioButton { Tag: string tab } || SettingsPageApp is null) return;
        ShowSettingsPage(tab);
        if (Application.Current is App app &&
            !string.Equals(app.UpdateSettings.SettingsTab, tab, StringComparison.Ordinal))
        {
            app.UpdateSettings.SettingsTab = tab;
            app.SaveUpdateSettings();
        }
    }

    private void ShowSettingsPage(string? tab)
    {
        SettingsPageVideo.Visibility = tab is "Video" or null ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageControl.Visibility = tab == "Control" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageWireless.Visibility = tab == "Wireless" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageApp.Visibility = tab == "App" ? Visibility.Visible : Visibility.Collapsed;
        SettingsScrollViewer.ScrollToTop();
    }
}
