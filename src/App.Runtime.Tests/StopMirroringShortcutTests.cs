using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using IPhoneMirror.App.Interop;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.Updater;
using IPhoneMirror.App.ViewModels;
using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App.Runtime.Tests;

internal static partial class Program
{
    private static int RunStopMirroringShortcutTests(string output)
    {
        Directory.CreateDirectory(output);
        SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext());
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown, IsUiPreviewMode = true };
        app.InitializeComponent();
        LocalizationService.SetLanguage("en-US");
        var main = CreateWorkspaceTestWindow(app, includeNativePreview: false);
        var vm = (MainViewModel)main.DataContext;
        var originalSessions = (DeviceSessionManager)KeyboardField(vm, "_sessions");
        var stops = new List<ulong>();
        var sessions = new DeviceSessionManager(h => stops.Add(h.RawHandle), _ => { });
        SetKeyboardField(vm, "_sessions", sessions);
        var phone = TrayTestDevice("shortcut-selected", "Selected phone");
        var second = TrayTestDevice("shortcut-preview", "Preview phone");
        using var firstHandle = new NativeSessionHandle(111, ownsHandle: false);
        using var previewHandle = new NativeSessionHandle(222, ownsHandle: false);
        using var replacementHandle = new NativeSessionHandle(333, ownsHandle: false);
        var first = new DeviceCaptureState { Udid = phone.Udid, Handle = firstHandle };
        var secondState = new DeviceCaptureState { Udid = second.Udid, Handle = previewHandle };
        sessions.Set(first);
        sessions.Set(secondState);
        SetKeyboardField(vm, "_selectedDevice", phone);
        var mainHandle = new WindowInteropHelper(main).Handle;
        nint foreground = mainHandle;
        SetKeyboardField(main, "_keyboardForegroundWindow", (Func<nint>)(() => foreground));
        var originalStop = vm.StopCommand;
        var mainStops = 0;
        SetKeyboardField(vm, "<StopCommand>k__BackingField", new RelayCommand(() => mainStops++));
        var bindings = Enum.GetValues<BluetoothShortcutAction>().ToDictionary(
            action => action, _ => KeyboardShortcut.Unbound);
        bindings[BluetoothShortcutAction.StopMirroring] = KeyboardShortcut.StopMirroringDefault;
        KeyboardCall(main, "ConfigureKeyboardShortcuts", bindings);
        var router = (KeyboardInputRouter)KeyboardField(main, "_keyboardRouter");
        NativePreviewWindow? preview = null;
        var windows = (Dictionary<string, NativePreviewWindow>)KeyboardField(
            KeyboardField(main, "_secondaryMirrors"), "_windows");
        try
        {
            var shortcut = router.Shortcuts.Match(0x58, KeyboardShortcut.Control | KeyboardShortcut.Shift);
            InteractionAssert(shortcut?.Name == "StopMirroring",
                "Stop shortcut must be available in the main window without reverse control.");
            shortcut!.Execute();
            AdvanceDispatcher(TimeSpan.FromMilliseconds(100));
            InteractionAssert(mainStops == 1, "Stop shortcut did not dispatch the ordinary stop command.");

            var ctor = typeof(NativePreviewWindow).GetConstructors(KeyboardTestMembers).Single();
            var args = ctor.GetParameters().Select(p => p.DefaultValue).ToArray();
            object[] required = [390u, 844u, "Stop shortcut preview fixture",
                (Func<nint, bool>)(_ => true), (Action<nint>)(_ => { }),
                (Func<nint, bool>)(_ => true), 222UL, 0d, 1d];
            Array.Copy(required, args, required.Length);
            preview = (NativePreviewWindow)ctor.Invoke(args);
            KeyboardCall(preview, "ShowInitially");
            windows[second.Udid] = preview;
            foreground = preview.Handle;
            shortcut = router.Shortcuts.Match(0x58, 6);
            InteractionAssert(shortcut is not null, "Stop shortcut missing in an uncontrolled preview.");
            shortcut!.Execute();
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (secondState.HasSession && timeout.Elapsed < TimeSpan.FromSeconds(3))
                AdvanceDispatcher(TimeSpan.FromMilliseconds(15));
            InteractionAssert(stops.SequenceEqual(new ulong[] { 222 }) &&
                first.HasSession && !secondState.HasSession,
                "Stopping a preview must stop that session and preserve the selected other device.");

            secondState.Handle = replacementHandle;
            WaitShortcutTask((Task)KeyboardCall(main, "StopMirroringFromShortcutAsync")!);
            InteractionAssert(secondState.HasSession && stops.Count == 1,
                "A stale preview shortcut stopped a replacement capture session.");
            foreground = (nint)0x12345;
            InteractionAssert(router.Shortcuts.Match(0x58, 6) is null,
                "Stop shortcut must not intercept another application's keyboard input.");

            var store = new UpdateSettingsStore(Path.Combine(output, "settings.json"));
            var stored = new UpdateSettings { StopMirroringShortcutVirtualKey = 0x59,
                StopMirroringShortcutModifiers = 6 };
            store.Save(stored.Clone());
            InteractionAssert(KeyboardShortcut.FromSettings(store.Load(), BluetoothShortcutAction.StopMirroring)
                == new KeyboardShortcut(6, 0x59), "Custom stop shortcut did not survive save/load.");
            stored.StopMirroringShortcutVirtualKey = 0;
            stored.StopMirroringShortcutModifiers = 0;
            store.Save(stored);
            InteractionAssert(!KeyboardShortcut.FromSettings(store.Load(), BluetoothShortcutAction.StopMirroring).IsBound,
                "Explicitly clearing Stop mirroring must survive a restart.");
            stored = new UpdateSettings { BluetoothHomeShortcutVirtualKey = 0x58,
                BluetoothHomeShortcutModifiers = 6, BluetoothShortcutSchema = 6,
                BluetoothModeShortcutSchema = 1, BluetoothControlShortcutSchema = 1 };
            store.Save(stored);
            InteractionAssert(store.Load().StopMirroringShortcutVirtualKey == 0,
                "Adding the default stop shortcut must not replace an existing user chord.");

            var editor = new ShortcutSettingsWindow(bindings, _ => null);
            try
            {
                editor.Show();
                AdvanceDispatcher(TimeSpan.FromMilliseconds(100));
                var row = editor.Rows.Single(r => r.Action == BluetoothShortcutAction.StopMirroring);
                InteractionAssert(row.Label == "Stop mirroring" &&
                    !editor.TryAssignShortcut(row, KeyboardShortcut.HomeDefault) &&
                    editor.TryAssignShortcut(row, new KeyboardShortcut(6, 0x59)),
                    "Stop mirroring must be named and editable in Configure shortcuts.");
                var field = FindVisualDescendant<TextBox>(editor, box => ReferenceEquals(box.DataContext, row));
                field?.BringIntoView();
                AdvanceDispatcher(TimeSpan.FromMilliseconds(100));
                SaveWindowRender(editor, Path.Combine(output, "stop-mirroring-shortcut.png"));
            }
            finally { editor.Close(); }
            Console.WriteLine("Stop shortcut passed: main window, uncontrolled preview, correct device, stale-session guard, foreign focus, settings persistence, cleared binding, collision migration and visible editor row.");
            return 0;
        }
        finally
        {
            windows.Clear();
            preview?.Dispose();
            SetKeyboardField(vm, "<StopCommand>k__BackingField", originalStop);
            SetKeyboardField(vm, "_selectedDevice", null);
            SetKeyboardField(vm, "_sessions", originalSessions);
            CloseWorkspaceTestWindow(main);
            app.Shutdown();
        }
    }
}
