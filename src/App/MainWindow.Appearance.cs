using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.Updater;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    private bool _appearanceSyncing;

    private void InitializeAppearanceControls()
    {
        AccentSwatches.Children.Clear();
        var themeDefault = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x3B, 0x82, 0xF6), 0), new(Color.FromRgb(0x8B, 0x5C, 0xF6), 0.33),
            new(Color.FromRgb(0xF9, 0x73, 0x16), 0.66), new(Color.FromRgb(0x22, 0xC5, 0x5E), 1),
        }, 45);
        themeDefault.Freeze();
        AccentSwatches.Children.Add(CreateAccentSwatch("", "AccentThemeDefault", themeDefault));
        foreach (var (key, hex) in ThemePalette.AccentChoices)
        {
            _ = ThemePalette.TryParseHex(hex, out var color);
            var fill = new SolidColorBrush(color);
            fill.Freeze();
            AccentSwatches.Children.Add(CreateAccentSwatch(hex, key, fill));
        }
        SyncAppearanceControls();
    }

    private RadioButton CreateAccentSwatch(string hex, string nameKey, Brush fill)
    {
        var swatch = new RadioButton
        {
            Style = (Style)FindResource("AccentSwatchButton"),
            GroupName = "AccentColor",
            Background = fill,
            Tag = hex,
        };
        swatch.SetResourceReference(ToolTipProperty, nameKey);
        swatch.SetResourceReference(AutomationProperties.NameProperty, nameKey);
        AutomationProperties.SetAutomationId(swatch,
            "AccentSwatch" + (hex.Length == 0 ? "Default" : hex.TrimStart('#')));
        swatch.Checked += (_, _) =>
        {
            if (!_appearanceSyncing) ChangeAppearance(settings => settings.AccentColor = hex);
        };
        return swatch;
    }

    /// <summary>Shows the stored appearance; the theme list folds presets into one choice.</summary>
    private void SyncAppearanceControls()
    {
        if (Application.Current is not App app) return;
        var settings = app.UpdateSettings;
        _appearanceSyncing = true;
        try
        {
            ThemeComboBox.SelectedValue =
                settings.Theme == AppTheme.Dark && settings.ColorTheme != AppColorTheme.Default
                    ? settings.ColorTheme.ToString()
                    : settings.Theme.ToString();
            foreach (var swatch in AccentSwatches.Children.OfType<RadioButton>())
                swatch.IsChecked = string.Equals(swatch.Tag as string, settings.AccentColor,
                    StringComparison.OrdinalIgnoreCase);
            AccentHexTextBox.Text = settings.AccentColor.Length > 0
                ? settings.AccentColor
                : ThemePalette.ToHex(ThemeService.Accent);
            UiStyleComboBox.SelectedValue = settings.UiStyle.ToString();
        }
        finally { _appearanceSyncing = false; }
    }

    private void ChangeAppearance(Action<UpdateSettings> change)
    {
        if (Application.Current is not App app) return;
        change(app.UpdateSettings);
        var settings = app.UpdateSettings;
        ThemeService.Apply(settings.Theme, settings.ColorTheme, settings.AccentColor, settings.UiStyle);
        app.SaveUpdateSettings();
        SyncAppearanceControls();
    }

    private void OnUiStyleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_appearanceSyncing || !_themeControlReady ||
            UiStyleComboBox.SelectedValue is not string value ||
            !Enum.TryParse<UiStyle>(value, out var style) ||
            Application.Current is not App { UpdateSettings.UiStyle: var current } || current == style)
            return;
        ChangeAppearance(settings => settings.UiStyle = style);
    }

    private void OnAccentHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitAccentHex();
        e.Handled = true;
    }

    private void OnAccentHexLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitAccentHex();

    private void CommitAccentHex()
    {
        if (_appearanceSyncing || Application.Current is not App app) return;
        var hex = ThemePalette.NormalizeHex(AccentHexTextBox.Text);
        var unchanged = hex.Length == 0 ||
            string.Equals(hex, app.UpdateSettings.AccentColor, StringComparison.OrdinalIgnoreCase) ||
            (app.UpdateSettings.AccentColor.Length == 0 &&
             string.Equals(hex, ThemePalette.ToHex(ThemeService.Accent), StringComparison.OrdinalIgnoreCase));
        if (unchanged) SyncAppearanceControls();
        else ChangeAppearance(settings => settings.AccentColor = hex);
    }
}
