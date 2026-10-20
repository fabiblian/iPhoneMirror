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
        var windowsFill = new SolidColorBrush(ThemePalette.ReadWindowsAccent());
        AccentSwatches.Children.Add(CreateAccentSwatch(ThemePalette.WindowsAccent, "AccentWindows", windowsFill));
        BuildAppearanceTiles();
        SyncAppearanceControls();
    }

    // Theme tiles show a small sketch of the app in that palette; style
    // tiles show a card and a button with that style's corners.
    private void BuildAppearanceTiles()
    {
        ThemeTiles.Children.Clear();
        foreach (var (theme, nameKey) in new[]
        {
            (AppColorTheme.Default, "ThemeStandard"), (AppColorTheme.Midnight, "ThemeMidnight"),
            (AppColorTheme.Nord, "ThemeNord"), (AppColorTheme.Sunset, "ThemeSunset"),
            (AppColorTheme.Forest, "ThemeForest"),
        })
        {
            var tile = CreateAppearanceTile("ThemeTile", theme.ToString(), nameKey, ThemeSketch(theme));
            tile.Checked += (_, _) =>
            {
                if (_appearanceSyncing) return;
                // Presets are dark palettes: choosing one switches to dark.
                ChangeAppearance(settings =>
                {
                    settings.ColorTheme = theme;
                    if (theme != AppColorTheme.Default) settings.Theme = AppTheme.Dark;
                });
            };
            ThemeTiles.Children.Add(tile);
        }
        StyleTiles.Children.Clear();
        foreach (var (style, nameKey, radius, buttonRadius) in new[]
        {
            (UiStyle.Modern, "UiStyleModern", 6.0, 5.0), (UiStyle.Minimal, "UiStyleMinimal", 2.0, 2.0),
            (UiStyle.Playful, "UiStylePlayful", 12.0, 9.0),
        })
        {
            var tile = CreateAppearanceTile("StyleTile", style.ToString(), nameKey, StyleSketch(radius, buttonRadius));
            tile.Checked += (_, _) =>
            {
                if (!_appearanceSyncing) ChangeAppearance(settings => settings.UiStyle = style);
            };
            StyleTiles.Children.Add(tile);
        }
    }

    private RadioButton CreateAppearanceTile(string group, string tag, string nameKey, FrameworkElement sketch)
    {
        var name = new TextBlock
        {
            Margin = new Thickness(2, 6, 0, 0), FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        name.SetResourceReference(TextBlock.TextProperty, nameKey);
        name.SetResourceReference(TextBlock.FontSizeProperty, "CaptionFontSize");
        var content = new StackPanel();
        content.Children.Add(sketch);
        content.Children.Add(name);
        var tile = new RadioButton
        {
            Style = (Style)FindResource("AppearanceTileButton"),
            GroupName = group,
            Tag = tag,
            Content = content,
        };
        tile.SetResourceReference(AutomationProperties.NameProperty, nameKey);
        AutomationProperties.SetAutomationId(tile, group + tag);
        return tile;
    }

    private static Brush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static FrameworkElement ThemeSketch(AppColorTheme theme)
    {
        var preset = ThemePalette.Presets.GetValueOrDefault(theme);
        var background = Solid(preset?.Background ?? Color.FromRgb(0x1F, 0x1F, 0x1F));
        var sidebar = Solid(preset?.Sidebar ?? Color.FromRgb(0x1A, 0x1A, 0x1A));
        var surface = Solid(preset?.Surface ?? Color.FromRgb(0x2A, 0x2A, 0x2A));
        var text = Solid(preset?.Text ?? Colors.White);
        var muted = Solid(preset?.MutedText ?? Color.FromRgb(0xA8, 0xA8, 0xA8));
        var accent = Solid(preset?.Accent ?? ThemePalette.DarkAccent);
        Border Bar(Brush fill, double width, double height = 3, double left = 0) => new()
        {
            Background = fill, Width = width, Height = height, CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(left, 0, 0, 3),
        };
        var side = new StackPanel { Width = 14, Background = sidebar };
        side.Children.Add(new Border { Height = 5 });
        foreach (var fill in new[] { accent, muted, muted })
            side.Children.Add(Bar(fill, 8, left: 3));
        var body = new DockPanel { Margin = new Thickness(5) };
        var phone = new Border
        {
            Width = 13, Height = 26, CornerRadius = new CornerRadius(3), Background = surface,
            BorderBrush = muted, BorderThickness = new Thickness(0.5), Margin = new Thickness(0, 0, 5, 0),
        };
        DockPanel.SetDock(phone, Dock.Left);
        body.Children.Add(phone);
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Bar(text, 26));
        lines.Children.Add(Bar(muted, 18));
        lines.Children.Add(Bar(accent, 20, 6));
        body.Children.Add(lines);
        var sketch = new DockPanel();
        DockPanel.SetDock(side, Dock.Left);
        sketch.Children.Add(side);
        sketch.Children.Add(body);
        return new Border
        {
            Height = 44, CornerRadius = new CornerRadius(5), Background = background,
            ClipToBounds = true, Child = sketch,
        };
    }

    private static FrameworkElement StyleSketch(double radius, double buttonRadius)
    {
        var card = new Border
        {
            Height = 44, CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        var button = new Border
        {
            Width = 36, Height = 13, CornerRadius = new CornerRadius(buttonRadius),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
        };
        button.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var line = new Border
        {
            Width = 30, Height = 3, CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        line.SetResourceReference(Border.BackgroundProperty, "MutedTextBrush");
        var grid = new Grid();
        grid.Children.Add(line);
        grid.Children.Add(button);
        card.Child = grid;
        return card;
    }

    private void OnThemeModeChecked(object sender, RoutedEventArgs e)
    {
        if (_appearanceSyncing || !_themeControlReady || sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse<AppTheme>(tag, out var mode)) return;
        ChangeAppearance(settings => settings.Theme = mode);
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
            foreach (var mode in ThemeModeButtons.Children.OfType<RadioButton>())
                mode.IsChecked = string.Equals(mode.Tag as string, settings.Theme.ToString(), StringComparison.Ordinal);
            foreach (var tile in ThemeTiles.Children.OfType<RadioButton>())
                tile.IsChecked = string.Equals(tile.Tag as string, settings.ColorTheme.ToString(), StringComparison.Ordinal);
            foreach (var tile in StyleTiles.Children.OfType<RadioButton>())
                tile.IsChecked = string.Equals(tile.Tag as string, settings.UiStyle.ToString(), StringComparison.Ordinal);
            // A preset only paints the dark theme; say so while it waits for dark mode.
            ThemeDarkOnlyHint.Visibility = settings.ColorTheme != AppColorTheme.Default && !ThemeService.IsDark
                ? Visibility.Visible : Visibility.Collapsed;
            foreach (var swatch in AccentSwatches.Children.OfType<RadioButton>())
                swatch.IsChecked = string.Equals(swatch.Tag as string, settings.AccentColor,
                    StringComparison.OrdinalIgnoreCase);
            AccentHexTextBox.Text = settings.AccentColor.Length > 0 && !ThemePalette.IsWindowsAccent(settings.AccentColor)
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
