using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using IPhoneMirror.App.Controls;
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
    private static int RunWindowVisibilityTests(string output)
    {
        Directory.CreateDirectory(output);
        SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext());
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown, IsUiPreviewMode = true };
        app.InitializeComponent();
        LocalizationService.SetLanguage("en-US");
        // Use the same class handler as production, including newly opened dialogs.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => BossKeyWindowVisibility.Apply((Window)sender)));
        var main = CreateWorkspaceTestWindow(app, includeNativePreview: false);
        app.MainWindow = main;
        var vm = (MainViewModel)main.DataContext;
        var originalSessions = (DeviceSessionManager)KeyboardField(vm, "_sessions");
        var stops = 0;
        var destroys = 0;
        var sessions = new DeviceSessionManager(_ => stops++, _ => destroys++);
        SetKeyboardField(vm, "_sessions", sessions);
        var phone = TrayTestDevice("visibility-first", "First preview");
        var otherPhone = TrayTestDevice("visibility-second", "Second preview");
        using var firstHandle = new NativeSessionHandle(111, ownsHandle: false);
        using var secondHandle = new NativeSessionHandle(222, ownsHandle: false);
        using var replacementHandle = new NativeSessionHandle(333, ownsHandle: false);
        var firstState = new DeviceCaptureState { Udid = phone.Udid, Handle = firstHandle, UsbVideoOnly = true };
        var secondState = new DeviceCaptureState { Udid = otherPhone.Udid, Handle = secondHandle };
        sessions.Set(firstState);
        sessions.Set(secondState);
        SetKeyboardField(vm, "_selectedDevice", phone);
        var manager = (MultiDevicePreviewManager)KeyboardField(main, "_secondaryMirrors");
        var windows = (Dictionary<string, NativePreviewWindow>)KeyboardField(manager, "_windows");
        var pointerEvents = new List<PreviewPointerEventArgs>();
        var keyEvents = new List<PreviewKeyboardEventArgs>();
        var attachCount = 0;
        var detachCount = 0;
        var previews = new List<NativePreviewWindow>();
        NativePreviewWindow CreatePreview(ulong session)
        {
            var ctor = typeof(NativePreviewWindow).GetConstructors(KeyboardTestMembers).Single();
            var parameters = ctor.GetParameters();
            var args = parameters.Select(p => p.DefaultValue).ToArray();
            object[] required = [390u, 844u, "Visibility fixture",
                (Func<nint, bool>)(_ => { attachCount++; return true; }),
                (Action<nint>)(_ => detachCount++), (Func<nint, bool>)(_ => true), session, 0d, 1d];
            Array.Copy(required, args, required.Length);
            args[Array.FindIndex(parameters, p => p.Name == "isReverseControlEnabled")] = (Func<nint, bool>)(_ => true);
            args[Array.FindIndex(parameters, p => p.Name == "pointerInput")] = (Action<PreviewPointerEventArgs>)(e => pointerEvents.Add(e));
            args[Array.FindIndex(parameters, p => p.Name == "keyboardInput")] = (Action<PreviewKeyboardEventArgs>)(e => keyEvents.Add(e));
            args[Array.FindIndex(parameters, p => p.Name == "isUsbControlEnabled")] = (Func<bool>)(() => true);
            var preview = (NativePreviewWindow)ctor.Invoke(args);
            KeyboardCall(preview, "ShowInitially");
            previews.Add(preview);
            return preview;
        }
        var first = CreatePreview(111);
        var second = CreatePreview(222);
        windows[phone.Udid] = first;
        windows[otherPhone.Udid] = second;
        var mainHandle = new WindowInteropHelper(main).Handle;
        nint foreground = first.Handle;
        SetKeyboardField(main, "_keyboardForegroundWindow", (Func<nint>)(() => foreground));
        SetKeyboardField(first, "_inputForegroundWindow", (Func<nint>)(() => foreground));
        SetKeyboardField(second, "_inputForegroundWindow", (Func<nint>)(() => foreground));
        var bindings = Enum.GetValues<BluetoothShortcutAction>().ToDictionary(
            action => action, action => KeyboardShortcut.DefaultFor(action));
        KeyboardCall(main, "ConfigureKeyboardShortcuts", bindings);
        var router = (KeyboardInputRouter)KeyboardField(main, "_keyboardRouter");
        void TogglePreview()
        {
            var shortcut = router.Shortcuts.Match(0x50, 3);
            InteractionAssert(shortcut?.Name == "ShowPreview", "Global preview shortcut unavailable without reverse control.");
            shortcut!.Execute();
            AdvanceDispatcher(TimeSpan.FromMilliseconds(120));
        }
        PreviewSettingsWindow? settings = null;
        Window? dialog = null;
        Window? lateDialog = null;
        Window? alreadyHidden = null;
        try
        {
            var beforeBounds = VisibilityBounds(first.Handle);
            var audio = vm.PlayAudio;
            TogglePreview();
            InteractionAssert(!VisibilityIsWindowVisible(first.Handle) && VisibilityIsWindowVisible(second.Handle) &&
                VisibilityIsWindowVisible(mainHandle), "Preview toggle hid the wrong windows.");
            InteractionAssert(pointerEvents.Any(e => e.Kind == PreviewPointerKind.Reset) &&
                keyEvents.Any(e => e.Kind == PreviewKeyboardKind.Reset), "Hiding did not release held input.");
            pointerEvents.Clear(); keyEvents.Clear();
            PreviewStyleSendMessage(first.Handle, 0x201, 1, (nint)0x00200020); // mouse down
            PreviewStyleSendMessage(first.Handle, 0x100, 0x41, 0); // key down
            InteractionAssert(pointerEvents.Count == 0 && keyEvents.Count == 0 &&
                !(bool)KeyboardCall(main, "CanForwardControlPointer", phone.Udid, first.Handle)!,
                "An invisible preview still forwarded input.");
            first.Activate();
            InteractionAssert(!VisibilityIsWindowVisible(first.Handle), "An activation callback bypassed preview hiding.");
            foreground = second.Handle;
            TogglePreview();
            InteractionAssert(VisibilityIsWindowVisible(first.Handle) && VisibilityIsWindowVisible(second.Handle),
                "A different active preview displaced the pending restore target.");
            InteractionAssert(beforeBounds == VisibilityBounds(first.Handle), "Preview placement changed after hiding.");
            foreground = first.Handle;
            TogglePreview();
            foreground = (nint)0x12345;
            TogglePreview();
            InteractionAssert(VisibilityIsWindowVisible(first.Handle), "Foreign focus prevented preview restoration.");

            // Main entry: selected device, then last active. Context entry: the exact source window.
            KeyboardCall(main, "OnIndependentKeyboardFocusChanged", otherPhone.Udid, second.Handle, true);
            InteractionAssert(main.ResolveSettingsPreviewTarget()?.Window == first, "Selected-device targeting failed.");
            SetKeyboardField(vm, "_selectedDevice", null);
            InteractionAssert(main.ResolveSettingsPreviewTarget()?.Window == second, "Last-active fallback failed.");
            SetKeyboardField(vm, "_selectedDevice", phone);
            PreviewSettingsWindow.ShowOrActivate(second.Handle);
            settings = app.Windows.OfType<PreviewSettingsWindow>().Single();
            AdvanceDispatcher(TimeSpan.FromMilliseconds(120));
            InteractionAssert(((PreviewVisibilityTarget)KeyboardField(settings, "_visibilityTarget")).Window == second,
                "Context-menu settings did not retain their preview identity.");
            var showPreview = (CheckBox)settings.FindName("ShowPreviewCheckBox");
            var showApp = (CheckBox)settings.FindName("ShowAppCheckBox");
            InteractionAssert(showPreview.IsEnabled && showPreview.IsChecked == true && showApp.IsChecked == true,
                "Visibility switches are missing or incorrectly synchronized.");
            SaveWindowRender(settings, Path.Combine(output, "preview-visibility-settings.png"));
            showPreview.IsChecked = false;
            AdvanceDispatcher(TimeSpan.FromMilliseconds(100));
            InteractionAssert(!VisibilityIsWindowVisible(second.Handle) && VisibilityIsWindowVisible(first.Handle),
                "Settings switch hid the selected device instead of its context preview.");
            showPreview.IsChecked = true;
            AdvanceDispatcher(TimeSpan.FromMilliseconds(100));
            PreviewSettingsWindow.ShowOrActivate();
            InteractionAssert(((PreviewVisibilityTarget)KeyboardField(settings, "_visibilityTarget")).Window == first,
                "Reopening existing settings did not update the target.");

            // Individually hidden A survives app hide/restore; new dialogs join the app hide.
            foreground = first.Handle;
            TogglePreview();
            dialog = new Window { Style = null, Width = 280, Height = 160, Content = new TextBlock { Text = "Open dialog" } };
            dialog.Show();
            alreadyHidden = new Window { Style = null, Width = 180, Height = 120 };
            alreadyHidden.Show();
            VisibilityShowWindow(new WindowInteropHelper(alreadyHidden).Handle, 0);
            WaitShortcutTask(main.SetAppShownAsync(false));
            InteractionAssert(!main.IsAppShown && !VisibilityIsWindowVisible(mainHandle) &&
                !VisibilityIsWindowVisible(second.Handle) && !VisibilityIsWindowVisible(new WindowInteropHelper(dialog).Handle),
                "App toggle did not hide all window types.");
            InteractionAssert(KeyboardField(main, "_visibilityRestoreTrayIcon") is TrayIconService,
                "Normal mode has no temporary tray restoration icon.");
            lateDialog = new Window { Style = null, Width = 240, Height = 140 };
            lateDialog.Show();
            AdvanceDispatcher(TimeSpan.FromMilliseconds(100));
            InteractionAssert(!VisibilityIsWindowVisible(new WindowInteropHelper(lateDialog).Handle),
                "A newly created dialog escaped app hiding.");
            dialog.Hide(); dialog.Show();
            InteractionAssert(!VisibilityIsWindowVisible(new WindowInteropHelper(dialog).Handle),
                "Re-showing a tracked dialog escaped app hiding.");
            var newPreviewWhileHidden = CreatePreview(444);
            InteractionAssert(!VisibilityIsWindowVisible(newPreviewWhileHidden.Handle),
                "A new native preview flashed while the app was hidden.");
            foreground = (nint)0x12345;
            var boss = router.Shortcuts.Match(0x42, 3);
            InteractionAssert(boss?.Name == "BossKey", "Global app restore was disabled without control/foreground.");
            boss!.Execute(); AdvanceDispatcher(TimeSpan.FromMilliseconds(150));
            InteractionAssert(main.IsAppShown && VisibilityIsWindowVisible(mainHandle) &&
                VisibilityIsWindowVisible(second.Handle) && !VisibilityIsWindowVisible(first.Handle) &&
                VisibilityIsWindowVisible(new WindowInteropHelper(dialog).Handle) &&
                VisibilityIsWindowVisible(new WindowInteropHelper(lateDialog).Handle) && !VisibilityIsWindowVisible(new WindowInteropHelper(alreadyHidden).Handle),
                $"Restore flags: app={main.IsAppShown} main={VisibilityIsWindowVisible(mainHandle)} second={VisibilityIsWindowVisible(second.Handle)} first={VisibilityIsWindowVisible(first.Handle)} dialog={VisibilityIsWindowVisible(new WindowInteropHelper(dialog).Handle)} late={VisibilityIsWindowVisible(new WindowInteropHelper(lateDialog).Handle)} alreadyHidden={VisibilityIsWindowVisible(new WindowInteropHelper(alreadyHidden).Handle)}");
            InteractionAssert(VisibilityIsWindowVisible(newPreviewWhileHidden.Handle), "A new preview was lost on app restoration.");
            newPreviewWhileHidden.Dispose();
            InteractionAssert(KeyboardField(main, "_visibilityRestoreTrayIcon") is null,
                "Temporary tray icon outlived hidden mode.");
            TogglePreview();
            InteractionAssert(VisibilityIsWindowVisible(first.Handle), "Individually hidden preview could not be restored after app recovery.");

            // Actual shell callback and existing Tray mode callback both restore.
            WaitShortcutTask(main.SetAppShownAsync(false));
            var tray = (TrayIconService)KeyboardField(main, "_visibilityRestoreTrayIcon");
            ((Action)KeyboardField(tray, "_open"))();
            AdvanceDispatcher(TimeSpan.FromMilliseconds(150));
            InteractionAssert(main.IsAppShown && VisibilityIsWindowVisible(mainHandle), "Tray callback failed to restore app.");
            WaitShortcutTask(main.SetAppShownAsync(false));
            KeyboardCall(main, "ShowTrayPanel");
            AdvanceDispatcher(TimeSpan.FromMilliseconds(120));
            InteractionAssert(main.IsAppShown, "Existing tray callback did not restore hidden app.");

            // A blocked transport release must not block either recovery shortcut.
            var routeGate = (SemaphoreSlim)KeyboardField(main, "_bluetoothRouteGate");
            routeGate.Wait();
            Task pendingHide;
            try
            {
                pendingHide = main.SetAppShownAsync(false);
                InteractionAssert(!pendingHide.IsCompleted && !main.IsAppShown, "Transport fixture did not block release.");
                WaitShortcutTask(main.SetAppShownAsync(true));
                InteractionAssert(main.IsAppShown && VisibilityIsWindowVisible(mainHandle), "Pending release blocked app recovery.");
            }
            finally { routeGate.Release(); }
            WaitShortcutTask(pendingHide);
            routeGate.Wait();
            try
            {
                var releaseTarget = manager.GetVisibilityTarget(first.Handle)!;
                pendingHide = main.SetPreviewShownAsync(releaseTarget, false);
                InteractionAssert(!pendingHide.IsCompleted, "Preview fixture did not block release.");
                foreground = (nint)0x12345;
                TogglePreview();
                InteractionAssert(VisibilityIsWindowVisible(first.Handle), "Pending release blocked preview recovery.");
            }
            finally { routeGate.Release(); }
            WaitShortcutTask(pendingHide);

            // Hiding must not exit a nested WPF modal loop or lose its modal state.
            var modal = new Window { Style = null, Width = 260, Height = 140, Owner = main };
            var modalReturned = false;
            main.Dispatcher.BeginInvoke(() =>
            {
                WaitShortcutTask(main.SetAppShownAsync(false));
                AdvanceDispatcher(TimeSpan.FromMilliseconds(50));
                InteractionAssert(!modalReturned && !VisibilityIsWindowVisible(new WindowInteropHelper(modal).Handle),
                    "Hiding closed the modal dialog or left it visible.");
                WaitShortcutTask(main.SetAppShownAsync(true));
                InteractionAssert(!modalReturned && VisibilityIsWindowVisible(new WindowInteropHelper(modal).Handle),
                    "Restoring lost the modal dialog.");
                modal.Close();
            });
            modal.ShowDialog(); modalReturned = true;

            // Native placement/fullscreen are retained without detaching the capture.
            first.ToggleFullScreen();
            var fullBounds = VisibilityBounds(first.Handle);
            var target = manager.GetVisibilityTarget(first.Handle)!;
            WaitShortcutTask(main.SetPreviewShownAsync(target, false));
            WaitShortcutTask(main.SetAppShownAsync(false));
            WaitShortcutTask(main.SetAppShownAsync(true));
            WaitShortcutTask(main.SetPreviewShownAsync(target, true));
            InteractionAssert(first.IsFullScreen && fullBounds == VisibilityBounds(first.Handle), "Fullscreen placement was lost.");
            first.ToggleFullScreen();
            InteractionAssert(firstState.HasSession && secondState.HasSession && stops == 0 && destroys == 0 &&
                detachCount == 0 && vm.PlayAudio == audio && firstState.UsbVideoOnly,
                "Visibility changed capture lifetime or the chosen audio mode.");

            // A replacement session/window cannot be resurrected through the pending token.
            target = manager.GetVisibilityTarget(first.Handle)!;
            WaitShortcutTask(main.SetPreviewShownAsync(target, false));
            windows.Remove(phone.Udid); first.Dispose();
            firstState.Handle = replacementHandle;
            var replacement = CreatePreview(333);
            windows[phone.Udid] = replacement;
            foreground = (nint)0x12345;
            WaitShortcutTask(main.SetPreviewShownAsync(target, true));
            InteractionAssert(!first.IsLive && VisibilityIsWindowVisible(replacement.Handle), "A stale token affected the replacement preview.");
            windows.Remove(phone.Udid); replacement.Dispose();
            windows.Remove(otherPhone.Udid); second.Dispose();
            PreviewSettingsWindow.ShowOrActivate();
            InteractionAssert(!showPreview.IsEnabled && !(bool)KeyboardCall(main, "CanForwardControlPointer", phone.Udid, first.Handle)!,
                "Closed previews remained a usable visibility/input target.");
            TestVisibilityShortcutPersistence(output);
            var editor = new ShortcutSettingsWindow(bindings, _ => null);
            try
            {
                editor.Show();
                var previewRow = editor.Rows.Single(r => r.Action == BluetoothShortcutAction.ShowPreview);
                var appRow = editor.Rows.Single(r => r.Action == BluetoothShortcutAction.BossKey);
                InteractionAssert(previewRow.Label == "Show preview" && appRow.Label == "Show app" &&
                    !editor.TryAssignShortcut(previewRow, KeyboardShortcut.HomeDefault) &&
                    editor.TryAssignShortcut(previewRow, new KeyboardShortcut(6, 0x59)),
                    "Visibility shortcut rows are missing, incorrectly named or accept mouse-only restore.");
                InteractionAssert(editor.TryAssignShortcut(previewRow, KeyboardShortcut.ShowPreviewDefault),
                    "The preview shortcut default could not be restored in the editor.");
                FindVisualDescendant<TextBox>(editor, box => ReferenceEquals(box.DataContext, previewRow))?.BringIntoView();
                AdvanceDispatcher(TimeSpan.FromMilliseconds(120));
                SaveWindowRender(editor, Path.Combine(output, "visibility-shortcuts.png"));
            }
            finally { editor.Close(); }
            Console.WriteLine("Window visibility passed: uncontrolled/global restoration; two preview targets; settings scope; held-input release and hidden-input rejection; app + preview hide reasons; native/WPF/new/modal dialogs; tray restoration; fullscreen bounds; capture/audio preservation; closed/replaced session guards; persisted/custom/unbound/conflicting shortcuts; UI snapshots.");
            return 0;
        }
        finally
        {
            WaitShortcutTask(main.SetAppShownAsync(true));
            settings?.Close(); dialog?.Close(); lateDialog?.Close(); alreadyHidden?.Close();
            windows.Clear();
            foreach (var preview in previews) preview.Dispose();
            SetKeyboardField(vm, "_selectedDevice", null);
            SetKeyboardField(vm, "_sessions", originalSessions);
            CloseWorkspaceTestWindow(main);
            app.Shutdown();
        }
    }

    private static void TestVisibilityShortcutPersistence(string output)
    {
        var store = new UpdateSettingsStore(Path.Combine(output, "visibility-settings.json"));
        var stored = new UpdateSettings { BluetoothShortcutSchema = 6, BluetoothModeShortcutSchema = 1,
            BluetoothControlShortcutSchema = 1, BluetoothBossShortcutVirtualKey = 0x44,
            BluetoothBossShortcutModifiers = 6, ShowPreviewShortcutVirtualKey = 0x59, ShowPreviewShortcutModifiers = 3 };
        store.Save(stored.Clone());
        var loaded = store.Load();
        InteractionAssert(KeyboardShortcut.FromSettings(loaded, BluetoothShortcutAction.ShowPreview) == new KeyboardShortcut(3, 0x59) &&
            KeyboardShortcut.FromSettings(loaded, BluetoothShortcutAction.BossKey) == new KeyboardShortcut(6, 0x44),
            "Custom preview and legacy boss bindings did not survive save/load.");
        stored.ShowPreviewShortcutVirtualKey = 0; stored.ShowPreviewShortcutModifiers = 0;
        store.Save(stored);
        InteractionAssert(!KeyboardShortcut.FromSettings(store.Load(), BluetoothShortcutAction.ShowPreview).IsBound,
            "An explicitly unbound preview shortcut reverted to the default.");
        stored.ShowPreviewShortcutVirtualKey = 0x50; stored.ShowPreviewShortcutModifiers = 3;
        stored.BluetoothHomeShortcutVirtualKey = 0x50; stored.BluetoothHomeShortcutModifiers = 3;
        store.Save(stored);
        loaded = store.Load();
        InteractionAssert(loaded.ShowPreviewShortcutVirtualKey == 0 &&
            KeyboardShortcut.FromSettings(loaded, BluetoothShortcutAction.Home) == KeyboardShortcut.ShowPreviewDefault,
            "Introducing Ctrl+Alt+P overwrote an existing user binding.");
        InteractionAssert(!new KeyboardShortcut(0, KeyboardShortcut.MouseRight).IsValidFor(BluetoothShortcutAction.ShowPreview),
            "Preview restore must be keyboard-only.");
        InteractionAssert(!File.ReadAllText(Path.Combine(output, "visibility-settings.json")).Contains("VisibilityHidden"), "Runtime visibility leaked into persisted settings.");
    }

    private static (int Left, int Top, int Right, int Bottom) VisibilityBounds(nint hwnd)
    {
        PreviewStyleGetWindowRect(hwnd, out var rect);
        return (rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    private static extern bool VisibilityShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    private static extern bool VisibilityIsWindowVisible(nint hwnd);
}
