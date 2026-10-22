using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App.Windows;

public partial class ShortcutSettingsWindow : IPhoneMirror.UI.Controls.RoundedWindow,
    INotifyPropertyChanged
{
    private readonly Func<IReadOnlyDictionary<BluetoothShortcutAction, KeyboardShortcut>, string?> _apply;
    private string _statusText = string.Empty;
    public ObservableCollection<ShortcutBindingRow> Rows { get; } = [];
    public ObservableCollection<ShortcutBindingSection> Sections { get; } = [];
    public ObservableCollection<ShortcutBindingSection> FilteredSections { get; } = [];
    public bool HasSearchResults => FilteredSections.Count != 0;

    internal ShortcutSettingsWindow(
        IReadOnlyDictionary<BluetoothShortcutAction, KeyboardShortcut> shortcuts,
        Func<IReadOnlyDictionary<BluetoothShortcutAction, KeyboardShortcut>, string?> apply)
    {
        _apply = apply;
        foreach (var action in Enum.GetValues<BluetoothShortcutAction>().Where(
            action => action != BluetoothShortcutAction.ReverseControl)
            .OrderBy(action => action == BluetoothShortcutAction.ShowPreview
                ? (int)BluetoothShortcutAction.BossKey + 0.5 : (int)action))
        {
            var row = new ShortcutBindingRow(action,
                shortcuts.TryGetValue(action, out var shortcut)
                    ? shortcut : KeyboardShortcut.DefaultFor(action));
            Rows.Add(row);
            var category = GetCategory(action);
            var section = Sections.FirstOrDefault(candidate =>
                candidate.Category == category);
            if (section is null)
            {
                section = new ShortcutBindingSection(category);
                Sections.Add(section);
            }
            section.Rows.Add(row);
        }
        InitializeComponent();
        DataContext = this;
        RefreshSearch();
        Loaded += (_, _) => ShortcutSearchBox.Focus();
        LocalizationService.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => LocalizationService.LanguageChanged -= OnLanguageChanged;
    }

    internal static void ShowDeveloperPreview(Window owner) =>
        new ShortcutSettingsWindow(
            new Dictionary<BluetoothShortcutAction, KeyboardShortcut>(),
            _ => LocalizationService.Get("DeveloperReadOnlyPreview"))
        { Owner = owner }.Show();

    public string StatusText
    {
        get => LocalizationService.RefreshText(_statusText);
        private set
        {
            if (string.Equals(_statusText, value, StringComparison.Ordinal)) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized) RefreshSearch();
    }

    private void RefreshSearch()
    {
        var terms = ShortcutSearchBox.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        FilteredSections.Clear();
        foreach (var section in Sections)
        {
            var matches = section.Rows.Where(row => terms.All(term =>
                row.Label.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                section.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
            if (matches.Length == 0) continue;
            var filtered = new ShortcutBindingSection(section.Category);
            foreach (var row in matches) filtered.Rows.Add(row);
            FilteredSections.Add(filtered);
        }
        OnPropertyChanged(nameof(HasSearchResults));
        ShortcutListScrollViewer.ScrollToTop();
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        ShortcutSearchBox.Clear();
        ShortcutSearchBox.Focus();
    }

    private void OnSearchPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter in the search field must not submit the dialog's default Save.
        if (e.Key == Key.Enter) e.Handled = true;
        if (e.Key != Key.Escape || ShortcutSearchBox.Text.Length == 0) return;
        ShortcutSearchBox.Clear();
        e.Handled = true;
    }

    private void OnShortcutPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ShortcutBindingRow row) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Navigation and dismissal remain local to the settings window.
        if (key is Key.Tab or Key.Escape) return;
        if (Keyboard.Modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            TryAssignShortcut(row, KeyboardShortcut.Unbound);
            e.Handled = true;
            return;
        }
        if (!KeyboardShortcut.TryCreate(key, Keyboard.Modifiers, out var shortcut))
        {
            StatusText = LocalizationService.Get("ShortcutSettingsInvalid");
            e.Handled = true;
            return;
        }

        TryAssignShortcut(row, shortcut);
        e.Handled = true;
    }

    private void OnShortcutPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ShortcutBindingRow row) return;
        var button = e.ChangedButton switch
        {
            MouseButton.Right => ShortcutMouseButton.Right,
            MouseButton.Middle => ShortcutMouseButton.Middle,
            _ => ShortcutMouseButton.None,
        };
        if (!KeyboardShortcut.TryCreateMouse(button, Keyboard.Modifiers,
                out var shortcut))
            return;
        TryAssignShortcut(row, shortcut);
        e.Handled = true;
    }

    private void OnResetRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ShortcutBindingRow row) return;
        TryAssignShortcut(row, row.DefaultShortcut);
    }

    internal bool TryAssignShortcut(ShortcutBindingRow row, KeyboardShortcut shortcut)
    {
        if (row.Action == BluetoothShortcutAction.BossKey && shortcut.IsMouse)
        {
            StatusText = LocalizationService.Get("ShortcutSettingsBossKeyKeyboardOnly");
            return false;
        }
        if (!shortcut.IsValidFor(row.Action))
        {
            StatusText = LocalizationService.Get("ShortcutSettingsInvalid");
            return false;
        }
        var conflict = Rows.FirstOrDefault(other => other != row &&
            shortcut.IsBound && other.Shortcut == shortcut);
        if (conflict is not null)
        {
            StatusText = LocalizationService.Format("ShortcutSettingsDuplicateBindingFormat",
                shortcut.DisplayText, conflict.Label);
            return false;
        }
        row.Shortcut = shortcut;
        StatusText = string.Empty;
        return true;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var shortcuts = Rows.ToDictionary(row => row.Action, row => row.Shortcut);
        if (!KeyboardShortcut.HaveUniqueBoundValues(shortcuts.Values))
        {
            StatusText = LocalizationService.Get("ShortcutSettingsDuplicate");
            return;
        }
        var error = _apply(shortcuts);
        if (!string.IsNullOrWhiteSpace(error))
        {
            StatusText = error;
            return;
        }

        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var section in Sections) section.RefreshTitle();
        foreach (var row in Rows) row.RefreshLabel();
        RefreshSearch();
        OnPropertyChanged(nameof(StatusText));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static ShortcutBindingCategory GetCategory(BluetoothShortcutAction action) =>
        action switch
        {
            BluetoothShortcutAction.BluetoothControl or
            BluetoothShortcutAction.WirelessControl or
            BluetoothShortcutAction.StopMirroring or
            BluetoothShortcutAction.WiredControl or
            BluetoothShortcutAction.VolumeUp or
            BluetoothShortcutAction.VolumeDown or
            BluetoothShortcutAction.LockScreen or
            BluetoothShortcutAction.EnterPasscode => ShortcutBindingCategory.Control,
            BluetoothShortcutAction.BossKey or BluetoothShortcutAction.ShowPreview => ShortcutBindingCategory.Control,
            BluetoothShortcutAction.Siri => ShortcutBindingCategory.Assistant,
            _ => ShortcutBindingCategory.Navigation,
        };
}

public enum ShortcutBindingCategory
{
    Control,
    Navigation,
    Assistant,
}

public sealed class ShortcutBindingSection : INotifyPropertyChanged
{
    internal ShortcutBindingSection(ShortcutBindingCategory category)
    {
        Category = category;
        RefreshTitle();
    }

    internal ShortcutBindingCategory Category { get; }
    public ObservableCollection<ShortcutBindingRow> Rows { get; } = [];
    public string Title { get; private set; } = string.Empty;

    internal void RefreshTitle()
    {
        Title = LocalizationService.Get(Category switch
        {
            ShortcutBindingCategory.Control => "ShortcutSettingsCategoryControl",
            ShortcutBindingCategory.Navigation => "ShortcutSettingsCategoryNavigation",
            ShortcutBindingCategory.Assistant => "ShortcutSettingsCategoryAssistant",
            _ => "ShortcutSettingsCategoryControl",
        });
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ShortcutBindingRow : INotifyPropertyChanged
{
    private KeyboardShortcut _shortcut;
    internal ShortcutBindingRow(BluetoothShortcutAction action, KeyboardShortcut shortcut)
    {
        Action = action;
        DefaultShortcut = KeyboardShortcut.DefaultFor(action);
        _shortcut = shortcut;
        RefreshLabel();
    }
    internal BluetoothShortcutAction Action { get; }
    internal KeyboardShortcut DefaultShortcut { get; }
    public string Label { get; private set; } = string.Empty;
    public string ShortcutText => _shortcut.DisplayText;
    public bool IsBound => _shortcut.IsBound;
    internal KeyboardShortcut Shortcut
    {
        get => _shortcut;
        set
        {
            if (_shortcut == value) return;
            _shortcut = value;
            OnPropertyChanged(nameof(ShortcutText));
            OnPropertyChanged(nameof(IsBound));
        }
    }
    internal void RefreshLabel()
    {
        Label = LocalizationService.Get(Action switch
        {
            BluetoothShortcutAction.BluetoothControl => "ShortcutSettingsBluetoothControl",
            BluetoothShortcutAction.WirelessControl => "ShortcutSettingsWirelessControl",
            BluetoothShortcutAction.ShowPreview => "ShowPreview",
            BluetoothShortcutAction.StopMirroring => "ShortcutSettingsStopMirroring",
            BluetoothShortcutAction.WiredControl => "ShortcutSettingsWiredControl",
            BluetoothShortcutAction.ControlCenter => "ShortcutSettingsControlCenter",
            BluetoothShortcutAction.NotificationCenter => "ShortcutSettingsNotificationCenter",
            BluetoothShortcutAction.AppSwitcher => "ShortcutSettingsAppSwitcher",
            BluetoothShortcutAction.Home => "ShortcutSettingsHome",
            BluetoothShortcutAction.BossKey => "ShowApp",
            BluetoothShortcutAction.Dock => "ShortcutSettingsDock",
            BluetoothShortcutAction.Siri => "ShortcutSettingsSiri",
            BluetoothShortcutAction.VolumeUp => "ShortcutSettingsVolumeUp",
            BluetoothShortcutAction.VolumeDown => "ShortcutSettingsVolumeDown",
            BluetoothShortcutAction.LockScreen => "ShortcutSettingsLockScreen",
            BluetoothShortcutAction.EnterPasscode => "ShortcutSettingsEnterPasscode",
            _ => "ShortcutSettingsBluetoothControl",
        });
        OnPropertyChanged(nameof(Label));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new(name));
}
