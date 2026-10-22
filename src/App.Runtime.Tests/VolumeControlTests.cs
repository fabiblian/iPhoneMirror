using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using IPhoneMirror.App.Interop;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.ViewModels;

namespace IPhoneMirror.App.Runtime.Tests;

internal static partial class Program
{
    private static void TestToolbarVolume(MainWindow window, DeviceViewModel device,
        MemoryStream packets, string mode, string output)
    {
        Directory.CreateDirectory(output);
        var vm = (MainViewModel)KeyboardField(window, "_viewModel");
        var state = (DeviceCaptureState)KeyboardCall(vm, "GetOrCreateDeviceState", device)!;
        state.UsbVideoOnly = false;
        KeyboardCall(vm, "OnPropertyChanged", "HasIphoneVolumeTarget");
        KeyboardCall(vm, "OnPropertyChanged", "CaptureUsbAudio");
        KeyboardCall(vm, "OnPropertyChanged", "UsbControlIsInputEnabled");
        KeyboardCall(vm, "OnPropertyChanged", "BluetoothControlIsInputEnabled");
        InteractionAssert(vm.CanControlIphoneVolume, $"{mode}: connected iPhone volume controls are disabled.");
        var pcVolume = vm.PlaybackVolume;
        var pcMuted = vm.IsMuted;
        // Toolbar actions target the selected phone even if another preview was active.
        SetKeyboardField(window, "_activeControlWindow", (nint)123);
        SetKeyboardField(window, "_activeControlUdid", "other-preview");
        Task Press(ushort usage) => (Task)KeyboardCall(window, "PressIphoneConsumerButtonAsync", usage, device.Udid)!;
        packets.SetLength(0);
        if (mode == "Bluetooth")
        {
            var service = KeyboardField(vm, "_bluetoothControl");
            SetKeyboardField(service, "_disposed", 0);
            SetKeyboardField(service, "_mousePumpRunning", true);
            var queue = KeyboardField(service, "_keyboardPriorityReports");
            var reports = new List<byte[]>();
            try
            {
                var tasks = Task.WhenAll(Press(0xE9), Press(0xEA), Press(0xE9));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!tasks.IsCompleted && DateTime.UtcNow < deadline)
                {
                    while ((int)KeyboardField(queue, "Count") > 0)
                    {
                        var report = (ITuple)queue.GetType().GetMethod("Dequeue")!.Invoke(queue, null)!;
                        InteractionAssert(Convert.ToInt32(report[0]) == 4, "Wrong Bluetooth report ID.");
                        reports.Add((byte[])report[1]!);
                        ((TaskCompletionSource<bool>)report[2]!).SetResult(true);
                    }
                    AdvanceDispatcher(TimeSpan.FromMilliseconds(10));
                }
                WaitShortcutTask(tasks);
                InteractionAssert(reports.Count == 6 && reports.Select(r => r[0]).SequenceEqual(
                    new byte[] { 0xE9, 0, 0xEA, 0, 0xE9, 0 }), "Rapid Bluetooth volume clicks lost their press/release pairs.");
            }
            finally
            {
                SetKeyboardField(service, "_mousePumpRunning", false);
                SetKeyboardField(service, "_disposed", 1);
            }
        }
        else
        {
            WaitShortcutTask(Task.WhenAll(Press(0xE9), Press(0xEA), Press(0xE9)));
            var frames = ReadShortcutPackets(packets);
            InteractionAssert(frames.Length == 6, $"{mode}: expected six volume press/release packets, got {frames.Length}.");
            for (var i = 0; i < frames.Length; i++)
                InteractionAssert(frames[i].GetProperty("usagePage").GetInt32() == 12 &&
                    frames[i].GetProperty("usageCode").GetInt32() == (i / 2 == 1 ? 0xEA : 0xE9) &&
                    frames[i].GetProperty("state").GetString() == (i % 2 == 0 ? "down" : "up"),
                    $"{mode}: rapid volume click {i} was reordered or sent to the wrong HID control.");
            packets.SetLength(0);
            WaitShortcutTask((Task)KeyboardCall(window, "PressIphoneConsumerButtonAsync", (ushort)0xE9, "unrelated-phone")!);
            InteractionAssert(packets.Length == 0, "Volume input leaked to a different phone.");
        }
        InteractionAssert(vm.PlaybackVolume == pcVolume && vm.IsMuted == pcMuted,
            "Changing iPhone volume must preserve the independent PC volume and mute setting.");
        SetKeyboardField(window, "_activeControlWindow", (nint)0);
        SetKeyboardField(window, "_activeControlUdid", null);

        // Real bindings with an inert native handle: no capture or audio is opened.
        state.Handle = new NativeSessionHandle(1, ownsHandle: false);
        try
        {
            foreach (var property in new[] { "HasCaptureSession", "PreviewAndObsVisibility", "CanUseVisualPreviewTools" })
                KeyboardCall(vm, "OnPropertyChanged", property);
            window.UpdateLayout(); DrainDispatcher(); window.UpdateLayout();
            var actions = (StackPanel)window.FindName("PreviewQuickActions");
            var header = (FrameworkElement)window.FindName("HeaderCaptureActions");
            var audio = (FrameworkElement)window.FindName("PreviewVolume");
            InteractionAssert(!((Button)window.FindName("CaptureActionButton")).IsVisible &&
                actions.Children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "ToolbarStopButton").IsVisible,
                "Capture must show Stop only in the lower toolbar.");
            InteractionAssert(actions.IsAncestorOf(audio) && !header.IsAncestorOf(audio) && audio.IsVisible,
                "Audio controls must be in the lower toolbar.");
            foreach (var id in new[] { "QuickImageSettingsButton", "QuickPreviewWindowButton", "QuickScreenshotButton", "QuickFullScreenButton" })
                InteractionAssert(!((FrameworkElement)window.FindName(id)).IsVisible,
                    $"Opt-in toolbar action is shown by default: {id}.");
            var iphone = (FrameworkElement)window.FindName("IphoneVolumeControls");
            var iphoneSlider = (Slider)window.FindName("IphoneVolumeSlider");
            InteractionAssert(iphone.IsVisible && iphoneSlider.IsEnabled,
                "iPhone volume must be available while PC audio capture is enabled.");
            if (mode != "Bluetooth")
            {
                // A user move of three steps presses three times; reaching an end adds resync presses.
                // Reset the estimate without pressing, as a shortcut press would.
                KeyboardCall(vm, "SetIphoneVolumeEstimate", (double)(MainViewModel.IphoneVolumeSteps / 2));
                window.UpdateLayout(); DrainDispatcher();
                packets.SetLength(0);
                iphoneSlider.Value = iphoneSlider.Value + 3;
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (ReadShortcutPackets(packets).Length < 6 && DateTime.UtcNow < deadline)
                    AdvanceDispatcher(TimeSpan.FromMilliseconds(20));
                var frames = ReadShortcutPackets(packets);
                InteractionAssert(frames.Length == 6 && frames.All(f => f.GetProperty("usageCode").GetInt32() == 0xE9),
                    $"{mode}: moving the iPhone slider three steps up must press volume up three times, got {frames.Length / 2}.");
            }
            vm.ShowToolbarScreenshot = true;
            window.UpdateLayout(); DrainDispatcher(); window.UpdateLayout();
            InteractionAssert(((FrameworkElement)window.FindName("QuickScreenshotButton")).IsVisible,
                "Turning on Screenshot in Settings > Toolbar must show it in the toolbar.");
            vm.ShowToolbarScreenshot = false;
            vm.ShowToolbarStop = false;
            window.UpdateLayout(); DrainDispatcher(); window.UpdateLayout();
            InteractionAssert(!actions.Children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "ToolbarStopButton").IsVisible,
                "Turning off Stop in Settings > Toolbar must hide it.");
            vm.ShowToolbarStop = true;
            if (mode == "Usb") SaveWindowRender(window, Path.Combine(output, "toolbar-pc-and-iphone.png"));
            state.UsbVideoOnly = true;
            KeyboardCall(vm, "OnPropertyChanged", "CaptureUsbAudio");
            window.UpdateLayout(); DrainDispatcher(); window.UpdateLayout();
            InteractionAssert(iphone.IsVisible && !((FrameworkElement)window.FindName("PcVolumeControls")).IsVisible,
                "Audio left on the iPhone must keep its hardware volume controls.");
            if (mode == "Usb") SaveWindowRender(window, Path.Combine(output, "toolbar-iphone-audio.png"));
        }
        finally
        {
            state.Handle = null;
            state.UsbVideoOnly = false;
            foreach (var property in new[] { "HasCaptureSession", "PreviewAndObsVisibility", "CaptureUsbAudio" })
                KeyboardCall(vm, "OnPropertyChanged", property);
        }
        Console.WriteLine($"{mode}: iPhone volume packets, rapid clicks and lower toolbar controls passed.");
    }
}
