using System.Runtime.InteropServices;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    /// <summary>
    /// Decides whether a pressed typing key needs a layout translation instead
    /// of its physical HID usage: AltGr characters become the iOS German
    /// (Switzerland) Option keystroke (or text insertion when there is none),
    /// and Ctrl editing shortcuts become Command shortcuts.
    /// </summary>
    private bool TryTranslateLayoutKey(int virtualKey, int scanCode, bool bluetooth,
        out byte usage, out byte modifiers, out string? insertText)
    {
        usage = 0;
        modifiers = 0;
        insertText = null;
        var shift = _controlModifierKeys.Contains(0xA0) || _controlModifierKeys.Contains(0xA1);
        // Windows reports AltGr as left Ctrl + right Alt.
        if (_controlModifierKeys.Contains(0xA5) && _controlModifierKeys.Contains(0xA2))
        {
            if (AltGrCharacter(virtualKey, scanCode, shift) is not { } character) return false;
            if (SwissKeyboardLayout.TryMapAltGrCharacter(character, out usage, out modifiers)) return true;
            // Text insertion needs the USB/Wi-Fi control bridge.
            if (bluetooth || char.IsControl(character)) return false;
            insertText = character.ToString();
            return true;
        }
        var control = _controlModifierKeys.Contains(0xA2) || _controlModifierKeys.Contains(0xA3);
        var alt = _controlModifierKeys.Contains(0xA4) || _controlModifierKeys.Contains(0xA5);
        return control && !alt &&
            SwissKeyboardLayout.TryMapControlShortcut(virtualKey, shift, out usage, out modifiers);
    }

    private static char? AltGrCharacter(int virtualKey, int scanCode, bool shift)
    {
        var state = new byte[256];
        state[0x11] = state[0xA2] = 0x80; // Ctrl, left Ctrl
        state[0x12] = state[0xA5] = 0x80; // Alt, right Alt
        if (shift) state[0x10] = state[0xA0] = 0x80;
        var buffer = new char[4];
        // Flag 4 leaves the thread's dead-key state untouched.
        var count = ToUnicodeEx((uint)virtualKey, (uint)(scanCode & 0xFF), state,
            buffer, buffer.Length, 4, GetKeyboardLayout(0));
        // -1 is a dead key (for example AltGr+^ = ~); its character is still valid.
        return count is 1 or -1 ? buffer[0] : null;
    }

    private async Task SendKeyTapAsync(DirectKeyboardRoute route, byte modifiers, byte usage,
        bool bluetooth, Func<bool> canSend)
    {
        var sessionGate = KeyboardSessionGate(route);
        await sessionGate.WaitAsync();
        var pressed = false;
        try
        {
            if (!canSend() || !route.IsCurrent()) return;
            byte[] modifierUsages = bluetooth ? [] : SwissKeyboardLayout.ModifierUsages(modifiers).ToArray();
            // Modifiers first, so the phone never sees the bare key before them.
            pressed = true;
            await route.SendAsync(modifiers, modifierUsages, canSend);
            await route.SendAsync(modifiers, [.. modifierUsages, usage], canSend);
            await Task.Delay(20);
            await route.SendAsync(modifiers, modifierUsages, canSend);
        }
        catch (Exception error)
        {
            _viewModel.AddDiagnosticLog(AppLog.Event("keyboard_layout_tap_failed",
                ("device", AppLog.Device(route.Target)), ("error", AppLog.Error(error))));
        }
        finally
        {
            try
            {
                if (pressed) await route.SendAsync(0, [], null);
                await RestoreDirectKeyboardStateAsync(route, canSend);
            }
            catch (Exception error)
            {
                DiagnosticLogger.ReverseControlWarning("keyboard_input", "layout_tap_release_failed",
                    ("error", error.Message));
                ResetKeyboardOwnership();
            }
            finally { sessionGate.Release(); }
        }
    }

    private (uint Width, uint Height, int Rotation) ControlGeometry(string? targetUdid) =>
        _secondaryMirrors.TryGetControlGeometry(targetUdid, out var width, out var height, out var rotation)
            ? (width, height, rotation)
            : (_viewModel.SourceVideoWidth, _viewModel.SourceVideoHeight, 0);

    private async Task SendArrowSwipeAsync(byte usage, string? targetUdid, Func<bool> canSend)
    {
        if (!ArrowKeySwipe.TryGetSwipe(usage, out var start, out var end)) return;
        var geometry = ControlGeometry(targetUdid);
        if (geometry.Width == 0 || geometry.Height == 0) return;
        // Same screen-to-device mapping as keyboard-mapping gestures, so the
        // swipe follows what the preview shows in portrait and landscape.
        var portrait = _viewModel.AppliedBluetoothPortraitMouseDirection;
        var landscape = _viewModel.AppliedBluetoothLandscapeMouseDirection;
        var reverseX = _viewModel.AppliedBluetoothMouseReverseHorizontal;
        var reverseY = _viewModel.AppliedBluetoothMouseReverseVertical;
        var route = _viewModel.CaptureTouchRoute(targetUdid,
            () => canSend() && geometry == ControlGeometry(targetUdid),
            (x, y) => BluetoothMouseOrientationMapper.MapNormalized(x, y, geometry.Width, geometry.Height,
                geometry.Rotation, portrait, landscape, reverseX, reverseY));
        if (route is null) return;
        try
        {
            await _mappingExecutor.ExecuteGestureAsync(new TouchGesture(Guid.NewGuid(),
                MappedTouchAction.Swipe, start.X, start.Y, end.X, end.Y, ArrowKeySwipe.DurationMs), route);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            _viewModel.AddDiagnosticLog(AppLog.Event("arrow_key_swipe_failed",
                ("device", AppLog.Device(targetUdid)), ("error", AppLog.Error(error))));
        }
    }

    [DllImport("user32.dll")]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState,
        [Out] char[] buffer, int bufferSize, uint flags, nint layout);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);
}
