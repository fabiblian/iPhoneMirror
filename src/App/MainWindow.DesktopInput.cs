using System.Windows.Input;
using IPhoneMirror.App.Controls;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    private readonly Dictionary<string, WheelPinchZoom> _pinchZooms = new(StringComparer.OrdinalIgnoreCase);
    private Action<MappedKey, bool> _keyboardReplay = ReplayKeyboardEvent;
    private bool IsZoomModifierPressed => (_keyboardRouter.PressedModifiers & KeyboardShortcut.Control) != 0 ||
        Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

    private async Task QueuePinchZoomAsync(PreviewPointerEventArgs input, string target, UsbTouchPointerState state)
    {
        // Capture the original window and transport before waiting for an
        // existing scroll to finish. A reconnect must not retarget this tick.
        var sourceWindow = GetControlKeyboardWindow(target);
        var focusGuard = CapturePointerSendGuard(sourceWindow);
        var route = _viewModel.CapturePinchZoomRoute(target, () => focusGuard() && IsZoomModifierPressed &&
            CanForwardControlPointer(target, sourceWindow));
        var point = MapPointerToNormalized(input,
            input.SourceWidth != 0 ? input.SourceWidth : _viewModel.SourceVideoWidth,
            input.SourceHeight != 0 ? input.SourceHeight : _viewModel.SourceVideoHeight);
        CancelWheelScroll(state);
        await state.WheelCompletion;
        if (route is null || point is null || !route.IsCurrent() || state.Pressed ||
            _previewTouches.TryGetValue(target, out var touches) && touches.Count != 0) return;
        state.ZoomWheelRemainder += input.Wheel;
        if (Math.Abs(state.ZoomWheelRemainder) < 20) return;
        var delta = state.ZoomWheelRemainder;
        state.ZoomWheelRemainder = 0;
        if (_pinchZooms.TryGetValue(target, out var existing) && !existing.Completion.IsCompleted)
        {
            existing.AddDelta(delta);
            return;
        }
        var zoom = new WheelPinchZoom(route, point.Value.X, point.Value.Y);
        _pinchZooms[target] = zoom;
        zoom.AddDelta(delta);
        _ = ObservePinchZoomAsync(target, zoom);
    }

    private async Task ObservePinchZoomAsync(string target, WheelPinchZoom zoom)
    {
        try { await zoom.Completion; }
        catch (Exception error)
        {
            _viewModel.AddDiagnosticLog(AppLog.Event("pinch_zoom_failed", ("device", AppLog.Device(target)),
                ("error", AppLog.Error(error))));
        }
        finally
        {
            if (_pinchZooms.TryGetValue(target, out var current) && ReferenceEquals(current, zoom)) _pinchZooms.Remove(target);
        }
    }

    private async Task CancelPinchZoomAsync(string target)
    {
        if (!_pinchZooms.TryGetValue(target, out var zoom)) return;
        zoom.Cancel();
        try { await zoom.Completion; } catch (Exception) { /* Observed by ObservePinchZoomAsync. */ }
    }

    private async Task SendIphoneSearchAsync(DirectKeyboardRoute route, Func<bool> canSend)
    {
        var gate = KeyboardSessionGate(route);
        await _systemShortcutGate.WaitAsync();
        var gateHeld = false;
        var pressed = false;
        try
        {
            await gate.WaitAsync();
            gateHeld = true;
            if (!canSend() || !route.IsCurrent()) return;
            _directKeyboardRoute = route;
            pressed = true;
            // Apple's external-keyboard Search shortcut is Command-Space.
            // USB carries modifiers as usages; Bluetooth has a modifier byte.
            var commandOnly = route.Transport == "BluetoothDirect" ? Array.Empty<byte>() : [(byte)0xE3];
            await route.SendAsync(0x08, commandOnly, canSend);
            await Task.Delay(20);
            if (!canSend() || !route.IsCurrent()) return;
            await route.SendAsync(0x08, route.Transport == "BluetoothDirect" ? [(byte)0x2C] : [(byte)0xE3, (byte)0x2C], canSend);
            await Task.Delay(20);
            if (!canSend() || !route.IsCurrent()) return;
            await route.SendAsync(0x08, commandOnly, canSend);
        }
        finally
        {
            try
            {
                if (pressed) await route.SendAsync(0, [], null);
                if (gateHeld) await RestoreDirectKeyboardStateAsync(route, canSend);
            }
            finally { if (gateHeld) gate.Release(); _systemShortcutGate.Release(); }
        }
    }
}
