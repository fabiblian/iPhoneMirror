using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using TouchPoint = IPhoneMirror.App.Services.TouchPoint;
using System.Windows.Interop;
using IPhoneMirror.App.Controls;
using IPhoneMirror.App.Interop;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.ViewModels;

namespace IPhoneMirror.App.Runtime.Tests;

internal static partial class Program
{
    private static int RunDesktopInputTests()
    {
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        var app = new App { IsUiPreviewMode = true, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var main = CreateWorkspaceTestWindow(app, includeNativePreview: false);
        var activated = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), main, "OnMainKeyboardActivated");
        var deactivated = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), main, "OnMainKeyboardDeactivated");
        var focused = (KeyboardFocusChangedEventHandler)Delegate.CreateDelegate(typeof(KeyboardFocusChangedEventHandler), main, "OnMainKeyboardFocusChanged");
        main.Activated -= activated; main.Deactivated -= deactivated; main.PreviewGotKeyboardFocus -= focused;
        var vm = (MainViewModel)KeyboardField(main, "_viewModel");
        var hwnd = new WindowInteropHelper(main).Handle;
        nint foreground = hwnd;
        SetKeyboardField(main, "_keyboardForegroundWindow", (Func<nint>)(() => foreground));
        SetKeyboardField(main, "_keyboardFocusedWindow", (Func<nint>)(() => (nint)123));
        var preview = (NativePreviewHost)main.FindName("MainPreviewHost");
        SetKeyboardField(preview, "_window", (nint)123);
        var phone = TrayTestDevice("desktop-input-phone", "Desktop input fixture");
        SetKeyboardField(vm, "_selectedDevice", phone);
        SetKeyboardField(vm, "_isCapturing", true);
        SetKeyboardField(vm, "_sourceVideoWidth", 390u); SetKeyboardField(vm, "_sourceVideoHeight", 844u);
        var sessions = (DeviceSessionManager)KeyboardField(vm, "_sessions");
        sessions.Set(new() { Udid = phone.Udid, Handle = new NativeSessionHandle(456, ownsHandle: false) });
        var host = new UsbTouchBridgeHost();
        var bridge = (DirectUsbInputBridge)KeyboardField(host, "_bridge");
        using var packets = new MemoryStream();
        using var writer = new StreamWriter(packets, leaveOpen: true);
        SetKeyboardField(bridge, "_stdin", writer); SetKeyboardField(bridge, "<IsReady>k__BackingField", true);
        SetKeyboardField(host, "<State>k__BackingField", ReverseControlState.Ready);
        var controls = (IDictionary)KeyboardField(vm, "_deviceControls");
        var control = new DeviceControlSession(phone.Udid) { AppleUdid = phone.Udid, WiredBridge = host,
            WirelessBridge = host, WiredConnected = true, WirelessConnected = true };
        controls[phone.Udid] = control;
        var replayed = new List<(int Key, bool Down)>();
        SetKeyboardField(main, "_keyboardReplay", (Action<MappedKey, bool>)((key, down) => replayed.Add((key.VirtualKey, down))));
        bool Key(int vk, bool down) => (bool)KeyboardCall(main, "RouteKeyboardEvent", new MappedKey(vk, 0, false), down, phone.Udid, hwnd)!;
        void Focus()
        {
            // Emulate the full owner reset published by real transport changes.
            KeyboardCall(main, "ResetKeyboardOwnership");
            AwaitMapping((Task)KeyboardField(main, "_keyboardHandoff"));
            foreground = hwnd;
            KeyboardCall(main, "FocusControlDevice", phone.Udid, (nint)0);
            AwaitMapping((Task)KeyboardField(main, "_keyboardHandoff"));
            ReleaseTestPhysicalKeys(main);
        }
        void FinishKeys()
        {
            AdvanceDispatcher(TimeSpan.FromMilliseconds(20));
            WaitShortcutTask(Task.WhenAll((HashSet<Task>)KeyboardField(main, "_keyboardSends")));
            AdvanceDispatcher(TimeSpan.FromMilliseconds(20));
        }
        WheelPinchZoom Zoom(int delta)
        {
            WaitShortcutTask((Task)KeyboardCall(main, "HandleUsbPointerInputAsync",
                new PreviewPointerEventArgs(PreviewPointerKind.Wheel, (short)195, (short)422, 0, delta, 390, 844, 390, 844), phone.Udid)!);
            return ((Dictionary<string, WheelPinchZoom>)KeyboardField(main, "_pinchZooms"))[phone.Udid];
        }
        List<JsonElement> TouchFrames() => MappingFrames(packets).Where(f => f.TryGetProperty("points", out _)).ToList();
        try
        {
            foreach (var mode in new[] { ReverseControlMode.Usb, ReverseControlMode.Wireless })
            {
                control.WiredEnabled = mode == ReverseControlMode.Usb; control.WirelessEnabled = mode == ReverseControlMode.Wireless;
                control.Router.Begin(phone.Udid, mode);
                Focus();
                foreach (var win in new[] { 0x5B, 0x5C })
                {
                    packets.SetLength(0);
                    MappingAssert(Key(win, true) && packets.Length == 0, "Search must wait for standalone Win release.");
                    for (var repeat = 0; repeat < 5; repeat++) Key(win, true);
                    Key(win, false); FinishKeys();
                    var frames = MappingFrames(packets);
                    var keyReports = frames.Where(f => f.TryGetProperty("usages", out _)).Select(f =>
                        string.Join(",", f.GetProperty("usages").EnumerateArray().Select(v => v.GetInt32()).Order())).ToArray();
                    MappingAssert(keyReports.SequenceEqual(new[] { "227", "44,227", "227", "" }),
                        $"{mode}: Win must press Command before Space once and release both. Frames: {string.Join(";", frames)}");
                }
                packets.SetLength(0); replayed.Clear();
                Key(0x5B, true); Key(0x45, true); Key(0x45, false); Key(0x5B, false); FinishKeys();
                MappingAssert(packets.Length == 0 && replayed.SequenceEqual(new[] { (0x5B, true), (0x45, true) }),
                    "Win+E must remain a Windows chord without iPhone input.");
                packets.SetLength(0);
                Key(0x5B, true); foreground = 0; Key(0x5B, false); FinishKeys();
                MappingAssert(MappingFrames(packets).All(f => !f.TryGetProperty("usages", out var usages) || usages.GetArrayLength() == 0),
                    "Focus loss while holding Win must cancel search; neutral cleanup reports remain allowed.");
                Focus();
                Key(0xA2, true);
                foreach (var delta in new[] { 120, -120 })
                {
                    packets.SetLength(0);
                    var zoom = Zoom(delta); WaitShortcutTask(zoom.Completion);
                    var frames = TouchFrames();
                    MappingAssert(frames.Count == 8 && frames.All(f => f.GetProperty("points").GetArrayLength() == 2),
                        $"{mode}: zoom must keep both contacts together in each frame.");
                    var initial = frames[0].GetProperty("points").EnumerateArray().ToArray();
                    var final = frames[^2].GetProperty("points").EnumerateArray().ToArray();
                    var startDistance = initial[1].GetProperty("normalizedX").GetDouble() - initial[0].GetProperty("normalizedX").GetDouble();
                    var endDistance = final[1].GetProperty("normalizedX").GetDouble() - final[0].GetProperty("normalizedX").GetDouble();
                    MappingAssert((delta > 0 ? endDistance > startDistance : endDistance < startDistance) &&
                        initial.Select(p => p.GetProperty("pointerId").GetInt32()).Distinct().Count() == 2 &&
                        frames[^1].GetProperty("points").EnumerateArray().All(p => p.GetProperty("action").GetString() == "up"),
                        "Wheel direction or pinch cleanup is wrong.");
                }
                packets.SetLength(0);
                var cancelled = Zoom(120); Key(0xA2, false); WaitShortcutTask(cancelled.Completion);
                MappingAssert(TouchFrames().Count == 2 && TouchFrames()[^1].GetProperty("points").EnumerateArray()
                    .All(p => p.GetProperty("action").GetString() == "up"), "Ctrl release must lift both fingers before any delayed movement.");
                Key(0xA2, true); packets.SetLength(0);
                var bounded = Zoom(120);
                for (var i = 0; i < 100; i++) bounded.AddDelta(120);
                WaitShortcutTask(bounded.Completion);
                MappingAssert(TouchFrames().Count(f => f.GetProperty("points")[0].GetProperty("action").GetString() == "down") <= 2,
                    "Fast wheel input must not build a delayed gesture queue.");
                packets.SetLength(0);
                var away = Zoom(120); foreground = 0; WaitShortcutTask(away.Completion);
                MappingAssert(TouchFrames().Count == 2, "Focus loss must release without delayed pinch moves.");
                Focus(); Key(0xA2, true);
                WaitShortcutTask(bridge.SendTouchBatchAsync(Enumerable.Range(100, 4).Select(id => new TouchPoint(id, "down", .4, .4)).ToArray(), 0, 0));
                packets.SetLength(0);
                WaitShortcutTask((Task)KeyboardCall(main, "HandleUsbPointerInputAsync",
                    new PreviewPointerEventArgs(PreviewPointerKind.Wheel, (short)195, (short)422, 0, 120, 390, 844, 390, 844), phone.Udid)!);
                if (((Dictionary<string, WheelPinchZoom>)KeyboardField(main, "_pinchZooms")).TryGetValue(phone.Udid, out var full))
                    WaitShortcutTask(full.Completion);
                MappingAssert(!TouchFrames().Any(f => f.GetProperty("points").EnumerateArray().Any(p => p.GetProperty("action").GetString() is "down" or "move")),
                    "A two-finger pinch must not degrade to a one-finger swipe when only one slot is free.");
                WaitShortcutTask(bridge.SendTouchBatchAsync(Enumerable.Range(100, 4).Select(id => new TouchPoint(id, "up", .4, .4)).ToArray(), 0, 0));
                Key(0xA2, false); FinishKeys();
                packets.SetLength(0);
                WaitShortcutTask((Task)KeyboardCall(main, "HandleUsbPointerInputAsync",
                    new PreviewPointerEventArgs(PreviewPointerKind.Wheel, (short)195, (short)422, 0, 120, 390, 844, 390, 844), phone.Udid)!);
                var pointer = KeyboardCall(main, "GetUsbTouchPointerState", phone.Udid)!;
                WaitShortcutTask((Task)KeyboardField(pointer, "WheelCompletion"));
                MappingAssert(TouchFrames().Count > 2 && TouchFrames().All(f => f.GetProperty("points").GetArrayLength() == 1),
                    "Ordinary wheel scrolling must remain a single-finger gesture.");
                packets.SetLength(0);
                WaitShortcutTask((Task)KeyboardCall(main, "HandleUsbPointerInputAsync",
                    new PreviewPointerEventArgs(PreviewPointerKind.Wheel, (short)195, (short)422, 0, 120, 390, 844, 390, 844), phone.Udid)!);
                Key(0xA2, true); var switched = Zoom(120); WaitShortcutTask(switched.Completion); Key(0xA2, false); FinishKeys();
                var transition = TouchFrames();
                MappingAssert(transition[0].GetProperty("points").GetArrayLength() == 1 &&
                    transition[1].GetProperty("points")[0].GetProperty("action").GetString() == "up" &&
                    transition.Skip(2).All(f => f.GetProperty("points").GetArrayLength() == 2),
                    "Switching from scroll to zoom must lift the scrolling finger first.");
                packets.SetLength(0); Key(0xA2, true);
                var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                SetKeyboardField(pointer, "WheelCompletion", wait.Task);
                var delayed = (Task)KeyboardCall(main, "HandleUsbPointerInputAsync",
                    new PreviewPointerEventArgs(PreviewPointerKind.Wheel, (short)195, (short)422, 0, 120, 390, 844, 390, 844), phone.Udid)!;
                control.Router.Begin(phone.Udid, mode); wait.SetResult(); WaitShortcutTask(delayed);
                MappingAssert(TouchFrames().Count == 0, "A wheel event waiting on scroll must not target a replacement transport generation.");
                SetKeyboardField(pointer, "WheelCompletion", Task.CompletedTask); Key(0xA2, false); FinishKeys();
                Console.WriteLine($"{mode}: both Win keys, repeats, Windows chords, focus cancellation, zoom directions, paired releases, bounded input and atomic capacity passed.");
            }
            KeyboardCall(main, "ResetKeyboardOwnership");
            AwaitMapping((Task)KeyboardField(main, "_keyboardHandoff"));
            control.WiredEnabled = control.WirelessEnabled = false;
            SetKeyboardField(vm, "_bluetoothControlEnabled", true); SetKeyboardField(vm, "_bluetoothControlConnected", true);
            SetKeyboardField(vm, "_bluetoothControlInputEnabled", true); SetKeyboardField(vm, "_bluetoothControlDeviceUdid", phone.Udid);
            var service = KeyboardField(vm, "_bluetoothControl");
            SetKeyboardField(service, "_targetDeviceUdid", phone.Udid); SetKeyboardField(service, "_mousePumpRunning", true);
            var reports = KeyboardField(service, "_keyboardPriorityReports");
            var bytes = new List<byte[]>();
            var bluetoothRoute = vm.CaptureDirectKeyboardRoute(phone.Udid) ?? throw new InvalidOperationException("Bluetooth test route missing.");
            var search = (Task)KeyboardCall(main, "SendIphoneSearchAsync", bluetoothRoute, (Func<bool>)(() => true))!;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && (bytes.Count < 4 || !search.IsCompleted))
            {
                while ((int)KeyboardField(reports, "Count") != 0)
                {
                    var report = (ITuple)reports.GetType().GetMethod("Dequeue")!.Invoke(reports, null)!;
                    MappingAssert(Convert.ToInt32(report[0]) == 1, "Search needs a keyboard report.");
                    bytes.Add((byte[])report[1]!); ((TaskCompletionSource<bool>)report[2]!).SetResult(true);
                }
                AdvanceDispatcher(TimeSpan.FromMilliseconds(10));
            }
            WaitShortcutTask(search);
            MappingAssert(bytes.Count == 4 && bytes[0][0] == 8 && bytes[0].Skip(1).All(b => b == 0) &&
                bytes[1][0] == 8 && bytes[1][2] == 0x2C && bytes[2].SequenceEqual(bytes[0]) && bytes[3].All(b => b == 0),
                "Bluetooth Search must press Command before Space and release both.");
            SetKeyboardField(service, "_mousePumpRunning", false);
            Console.WriteLine("Bluetooth: standalone Win Search keyboard report and release passed. No real device or Windows input sent.");
            return 0;
        }
        finally
        {
            SetKeyboardField(vm, "_bluetoothControlEnabled", false); SetKeyboardField(vm, "_bluetoothControlConnected", false);
            SetKeyboardField(vm, "_bluetoothControlInputEnabled", false);
            var bluetooth = KeyboardField(vm, "_bluetoothControl");
            SetKeyboardField(bluetooth, "_disposed", 1);
            SetKeyboardField(bluetooth, "_mousePumpRunning", false);
            var pendingReports = KeyboardField(bluetooth, "_keyboardPriorityReports");
            while ((int)KeyboardField(pendingReports, "Count") != 0)
            {
                var pending = (ITuple)pendingReports.GetType().GetMethod("Dequeue")!.Invoke(pendingReports, null)!;
                ((TaskCompletionSource<bool>)pending[2]!).TrySetCanceled();
            }
            KeyboardCall(main, "ResetKeyboardOwnership"); AwaitMapping((Task)KeyboardField(main, "_keyboardHandoff"));
            SetKeyboardField(vm, "_isCapturing", false); sessions.Remove(phone.Udid); controls.Clear();
            SetKeyboardField(preview, "_window", (nint)0); SetKeyboardField(bridge, "_stdin", null);
            SetKeyboardField(bridge, "<IsReady>k__BackingField", false);
            main.Activated += activated; main.Deactivated += deactivated; main.PreviewGotKeyboardFocus += focused;
            CloseWorkspaceTestWindow(main); app.Shutdown();
        }
    }
}
