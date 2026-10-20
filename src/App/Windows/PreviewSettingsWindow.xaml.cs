using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App.Windows;

/// <summary>
/// Settings for independent preview windows (frame, home button, automatic
/// control). Changes apply live to every open preview via PreviewFrameService.
/// New preview options belong here.
/// </summary>
public partial class PreviewSettingsWindow : IPhoneMirror.UI.Controls.RoundedWindow
{
    private static readonly string[] SwatchColors =
        ["#2B2B2B", "#000000", "#FFFFFF", "#0A84FF", "#30D158", "#FF453A", "#FF9F0A", "#BF5AF2"];
    private static PreviewSettingsWindow? _current;
    private bool _syncing;

    private PreviewVisibilityTarget? _visibilityTarget;
    private MainWindow? VisibilityOwner => Application.Current?.MainWindow as MainWindow;

    internal static void ShowOrActivate(nint previewWindow = 0)
    {
        if (_current is { } existing)
        {
            existing._visibilityTarget = existing.VisibilityOwner?.ResolveSettingsPreviewTarget(previewWindow);
            existing.Sync();
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var window = new PreviewSettingsWindow();
        window._visibilityTarget = window.VisibilityOwner?.ResolveSettingsPreviewTarget(previewWindow);
        window.Sync();
        if (Application.Current?.MainWindow is { IsVisible: true } owner && !ReferenceEquals(owner, window))
            window.Owner = owner;
        else
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _current = window;
        window.Closed += (_, _) => _current = null;
        window.Show();
    }

    private PreviewSettingsWindow()
    {
        InitializeComponent();
        foreach (var color in SwatchColors)
        {
            _ = PreviewFrameSettings.TryParseColor(color, out var rgb);
            var swatch = new Button
            {
                Width = 26,
                Height = 26,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 8, 8),
                Tag = color,
                ToolTip = color,
                Cursor = Cursors.Hand,
                Template = SwatchTemplate(),
                Background = new SolidColorBrush(Color.FromRgb(
                    (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)),
            };
            swatch.SetResourceReference(BorderBrushProperty, "BorderSoftBrush");
            AutomationProperties.SetName(swatch, color);
            swatch.Click += (_, _) => PreviewFrameService.Update(s => s.Color = color);
            Swatches.Children.Add(swatch);
        }
        BuildCaseColors();
        BuildDotChoices();
        PreviewFrameService.Changed += Sync;
        NativePreviewWindow.VisibilityChanged += Sync;
        if (VisibilityOwner is { } main) main.WindowVisibilityChanged += Sync;
        Closed += (_, _) =>
        {
            PreviewFrameService.Changed -= Sync;
            NativePreviewWindow.VisibilityChanged -= Sync;
            if (VisibilityOwner is { } main) main.WindowVisibilityChanged -= Sync;
        };
        Sync();
    }

    private static ControlTemplate SwatchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background))
            { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(BorderBrush))
            { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(BorderThickness))
            { RelativeSource = RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void Sync()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Sync);
            return;
        }
        var settings = PreviewFrameService.Current;
        _syncing = true;
        try
        {
            ShowPreviewCheckBox.IsEnabled = VisibilityOwner?.CanSetPreviewVisibility(_visibilityTarget) == true;
            ShowPreviewCheckBox.IsChecked = VisibilityOwner?.IsPreviewShown(_visibilityTarget) == true;
            ShowAppCheckBox.IsChecked = VisibilityOwner?.IsAppShown != false;
            FrameEnabledCheckBox.IsChecked = settings.Enabled;
            FrameOptions.IsEnabled = settings.Enabled;
            FrameOptions.Opacity = settings.Enabled ? 1.0 : 0.5;
            ThicknessSlider.Value = settings.ThicknessDips;
            AccentColorCheckBox.IsChecked = settings.UseAccentColor;
            OtherColorOptions.IsEnabled = !settings.UseAccentColor;
            OtherColorOptions.Opacity = settings.UseAccentColor ? 0.5 : 1.0;
            AutoColorCheckBox.IsChecked = settings.AutoColor;
            FixedColorOptions.IsEnabled = !settings.AutoColor;
            FixedColorOptions.Opacity = settings.AutoColor ? 0.5 : 1.0;
            OpacitySlider.Value = Math.Round(settings.Opacity * 100);
            if (!ColorTextBox.IsKeyboardFocusWithin) ColorTextBox.Text = settings.Color;
            foreach (var child in Swatches.Children.OfType<Button>())
            {
                var selected = !settings.UseAccentColor && !settings.AutoColor && string.Equals(child.Tag as string,
                    settings.Color, StringComparison.OrdinalIgnoreCase);
                child.BorderThickness = new Thickness(selected ? 3 : 1);
                child.SetResourceReference(BorderBrushProperty,
                    selected ? "AccentBrush" : "BorderSoftBrush");
            }
            Select(HomeStyleComboBox, settings.HomeButton.ToString());
            Select(HomePositionComboBox, settings.HomeButtonPosition.ToString());
            HomePositionComboBox.IsEnabled = settings.HomeButton != PreviewHomeButtonStyle.Off;
            AutoStartControlCheckBox.IsChecked = settings.AutoStartControl;
            SyncLook(settings);
        }
        finally { _syncing = false; }
    }

    private static void Select(ComboBox comboBox, string tag) =>
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));

    private static string? SelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private void Update(Action<PreviewFrameSettings> change)
    {
        if (_syncing || !IsInitialized) return;
        PreviewFrameService.Update(change);
    }

    private void OnFrameEnabledChanged(object sender, RoutedEventArgs e)
    {
        var enabled = FrameEnabledCheckBox.IsChecked == true;
        Update(s => s.Enabled = enabled);
    }

    private void OnThicknessChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Update(s => s.ThicknessDips = e.NewValue);

    private void OnAccentColorChanged(object sender, RoutedEventArgs e)
    {
        var accent = AccentColorCheckBox.IsChecked == true;
        Update(s => s.UseAccentColor = accent);
    }

    private void OnAutoColorChanged(object sender, RoutedEventArgs e)
    {
        var automatic = AutoColorCheckBox.IsChecked == true;
        Update(s => s.AutoColor = automatic);
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        Update(s => s.Opacity = e.NewValue / 100.0);

    private void OnHomeStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Enum.TryParse<PreviewHomeButtonStyle>(SelectedTag(HomeStyleComboBox), out var style))
            Update(s => s.HomeButton = style);
    }

    private void OnHomePositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Enum.TryParse<PreviewHomeButtonPosition>(SelectedTag(HomePositionComboBox), out var position))
            Update(s => s.HomeButtonPosition = position);
    }

    private void OnAutoStartControlChanged(object sender, RoutedEventArgs e)
    {
        var enabled = AutoStartControlCheckBox.IsChecked == true;
        Update(s => s.AutoStartControl = enabled);
    }

    private void OnColorTextKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitColorText();
        e.Handled = true;
    }

    private void OnColorTextCommitted(object sender, KeyboardFocusChangedEventArgs e) =>
        CommitColorText();

    private void CommitColorText()
    {
        if (PreviewFrameSettings.TryParseColor(ColorTextBox.Text, out var rgb))
            PreviewFrameService.Update(s => s.Color = PreviewFrameSettings.FormatColor(rgb));
        // Show the normalized value, or restore the last valid one.
        ColorTextBox.Text = PreviewFrameService.Current.Color;
    }

    private async void OnShowPreviewChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsInitialized || VisibilityOwner is not { } main) return;
        await main.SetPreviewShownAsync(_visibilityTarget, ShowPreviewCheckBox.IsChecked == true);
        Sync();
    }

    private async void OnShowAppChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsInitialized || VisibilityOwner is not { } main) return;
        await main.SetAppShownAsync(ShowAppCheckBox.IsChecked == true);
        Sync();
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => PreviewFrameService.Reset();
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
