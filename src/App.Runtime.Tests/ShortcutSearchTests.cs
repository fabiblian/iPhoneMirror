using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App.Runtime.Tests;

internal static partial class Program
{
    private static int RunShortcutSearchTests(string output)
    {
        Directory.CreateDirectory(output);
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown, IsUiPreviewMode = true };
        app.InitializeComponent();
        LocalizationService.SetLanguage("en-US");
        var bindings = Enum.GetValues<BluetoothShortcutAction>()
            .ToDictionary(action => action, KeyboardShortcut.DefaultFor);
        IReadOnlyDictionary<BluetoothShortcutAction, KeyboardShortcut>? saved = null;
        var editor = new ShortcutSettingsWindow(bindings, values => { saved = values; return null; });
        app.MainWindow = editor;
        try
        {
            editor.Show();
            AdvanceDispatcher(TimeSpan.FromMilliseconds(100));
            var search = (TextBox)editor.FindName("ShortcutSearchBox");
            var total = editor.Rows.Count;
            SaveWindowRender(editor, Path.Combine(output, "shortcut-search-all.png"));
            search.Text = "  PREVIEW  ";
            AdvanceDispatcher(TimeSpan.FromMilliseconds(70));
            InteractionAssert(editor.FilteredSections.SelectMany(s => s.Rows).Single().Action == BluetoothShortcutAction.ShowPreview,
                "Search did not find Show preview with case-insensitive, trimmed input.");
            InteractionAssert(editor.Rows.Count == total, "Filtering removed bindings from the saved model.");
            var preview = editor.Rows.Single(r => r.Action == BluetoothShortcutAction.ShowPreview);
            InteractionAssert(!editor.TryAssignShortcut(preview, KeyboardShortcut.BossKeyDefault),
                "Filtering bypassed collision detection against an invisible row.");
            InteractionAssert(editor.TryAssignShortcut(preview, new KeyboardShortcut(6, 0x59)),
                "A filtered shortcut could not be edited.");
            AdvanceDispatcher(TimeSpan.FromMilliseconds(70));
            SaveWindowRender(editor, Path.Combine(output, "shortcut-search-preview.png"));
            search.Text = "assistant";
            InteractionAssert(editor.FilteredSections.Single().Rows.Single().Action == BluetoothShortcutAction.Siri,
                "Searching by category failed.");
            search.Text = "nothing-matches-this-shortcut";
            AdvanceDispatcher(TimeSpan.FromMilliseconds(70));
            InteractionAssert(!editor.HasSearchResults &&
                ((TextBlock)editor.FindName("NoShortcutResultsText")).IsVisible,
                "Empty search results are not shown.");
            SaveWindowRender(editor, Path.Combine(output, "shortcut-search-empty.png"));
            var clear = (Button)editor.FindName("ClearShortcutSearchButton" );
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            InteractionAssert(search.Text.Length == 0 && editor.FilteredSections.Sum(s => s.Rows.Count) == total &&
                preview.Shortcut == new KeyboardShortcut(6, 0x59), "Clearing the filter lost rows or unsaved edits.");
            search.Text = "preview";
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            search.RaiseEvent(enter);
            InteractionAssert(enter.Handled && saved is null, "Enter in search submitted the dialog.");
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            search.RaiseEvent(escape);
            InteractionAssert(escape.Handled && search.Text.Length == 0, "Escape did not clear search.");
            search.Text = "preview";
            KeyboardCall(editor, "OnSaveClick", editor, new RoutedEventArgs());
            InteractionAssert(saved is not null && saved.Count == total &&
                saved[BluetoothShortcutAction.ShowPreview] == new KeyboardShortcut(6, 0x59) &&
                saved[BluetoothShortcutAction.BossKey] == KeyboardShortcut.BossKeyDefault,
                "Saving a filtered list discarded nonmatching bindings or unsaved changes.");
            Console.WriteLine("Shortcut search passed: name/category filtering, empty/clear states, hidden-row conflict detection, retained edits, Enter/Escape and saving all bindings while filtered. UI snapshots saved.");
            return 0;
        }
        finally
        {
            if (editor.IsLoaded) editor.Close();
            app.Shutdown();
        }
    }
}
