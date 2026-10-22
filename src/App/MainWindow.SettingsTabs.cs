using System.Windows;
using System.Windows.Controls;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    private RadioButton[] SettingsTabButtons =>
        [SettingsTabVideo, SettingsTabControl, SettingsTabWireless, SettingsTabApp];

    private Expander[] SettingsSections =>
    [
        SettingsSectionUsbMode, SettingsSectionPicture, SettingsSectionSound, SettingsSectionToolbar,
        SettingsSectionKeyboard, SettingsSectionWheel, SettingsSectionMapping, SettingsSectionShortcuts,
        SettingsSectionBluetoothMouse, SettingsSectionWirelessReceiver, SettingsSectionMediaCast,
        SettingsSectionAppearance, SettingsSectionAutoConnect, AdvancedSettingsCard,
    ];

    private void InitializeSettingsTabs()
    {
        var saved = Application.Current is App app ? app.UpdateSettings.SettingsTab : null;
        var tab = SettingsTabButtons.FirstOrDefault(button =>
            string.Equals(button.Tag as string, saved, StringComparison.Ordinal)) ?? SettingsTabVideo;
        tab.IsChecked = true;
        ShowSettingsPage(tab.Tag as string);
        StatsToggle.IsChecked = Application.Current is not App { UpdateSettings.StatsExpanded: false };
        ApplyStatsExpanded();
        InitializeSettingsSections();
    }

    // Sections keep the open or closed state the user last chose; untouched
    // sections keep their XAML default.
    private void InitializeSettingsSections()
    {
        var saved = Application.Current is App app ? app.UpdateSettings.SettingsSectionsExpanded : null;
        foreach (var section in SettingsSections)
        {
            if (saved?.TryGetValue(section.Name, out var expanded) == true) section.IsExpanded = expanded;
            section.Expanded += OnSettingsSectionToggled;
            section.Collapsed += OnSettingsSectionToggled;
        }
    }

    private void OnSettingsSectionToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander section || !ReferenceEquals(e.OriginalSource, section) ||
            Application.Current is not App app) return;
        var sections = app.UpdateSettings.SettingsSectionsExpanded;
        if (sections.TryGetValue(section.Name, out var saved) && saved == section.IsExpanded) return;
        sections[section.Name] = section.IsExpanded;
        app.SaveUpdateSettings();
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
