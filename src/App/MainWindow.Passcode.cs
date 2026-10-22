using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    // The mirror hides the lock screen keypad, so the passcode is typed on the
    // PC and sent as keyboard keys. Waking the phone and showing the keypad
    // takes a moment; digits sent sooner would be lost.
    private static readonly TimeSpan PasscodeKeypadDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan PasscodeKeyHold = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan PasscodeKeyGap = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan PasscodeClearGap = TimeSpan.FromMilliseconds(30);
    private PasscodeEntryWindow? _passcodeWindow;

    private void ShowPasscodeEntry()
    {
        if (_passcodeWindow is not null)
        {
            _passcodeWindow.Activate();
            return;
        }
        var target = ActiveInputDeviceUdid;
        if (_viewModel.CaptureDirectKeyboardRoute(target) is null)
        {
            AppPromptWindow.Inform(LocalizationService.Get("PasscodeEntryTitle"),
                LocalizationService.Get("PasscodeEntryNeedsControl"), IsVisible ? this : null);
            return;
        }
        var name = _viewModel.Devices.FirstOrDefault(device =>
            DeviceViewModel.UdidEquals(device.Udid, target))?.Name ?? "iPhone";
        _passcodeWindow = PasscodeEntryWindow.Show(GetControlKeyboardWindow(target), name,
            digits => _ = EnterPasscodeAsync(target, digits));
        _passcodeWindow.Closed += (_, _) => _passcodeWindow = null;
    }

    private async Task EnterPasscodeAsync(string? target, char[] digits)
    {
        DirectKeyboardRoute? route = null;
        SemaphoreSlim? sessionGate = null;
        var shortcutGateHeld = false;
        try
        {
            await _systemShortcutGate.WaitAsync();
            shortcutGateHeld = true;
            route = _viewModel.CaptureDirectKeyboardRoute(target);
            if (route is null)
            {
                AppPromptWindow.Inform(LocalizationService.Get("PasscodeEntryTitle"),
                    LocalizationService.Get("PasscodeEntryNeedsControl"), IsVisible ? this : null);
                return;
            }
            sessionGate = KeyboardSessionGate(route);
            await sessionGate.WaitAsync();
            var current = route.IsCurrent;
            // Home asks a locked iPhone for its passcode keypad, the same way
            // the Home shortcut does.
            var home = GetIndigoButton(BluetoothShortcutAction.Home)!.Value;
            if (route.Transport == "BluetoothDirect")
                await _viewModel.SendBluetoothConsumerShortcutAsync(home.Code, home.HoldMs, target, current);
            else
                await SendUsbButtonPulseAsync(home, route, current);
            await Task.Delay(PasscodeKeypadDelay);
            // Digits left from an earlier, interrupted try would join this
            // code and turn it into a failed attempt. Clear the field first.
            for (var index = 0; index < PasscodeKeys.MaximumLength; index++)
                await PressPasscodeKeyAsync(route, PasscodeKeys.Backspace, current, PasscodeClearGap);
            foreach (var digit in digits)
                await PressPasscodeKeyAsync(route, PasscodeKeys.Usage(digit), current);
            if (PasscodeKeys.NeedsEnter(digits.Length))
                await PressPasscodeKeyAsync(route, PasscodeKeys.Enter, current);
            // Never log the passcode or its length.
            _viewModel.AddDiagnosticLog(AppLog.Event("passcode_entry_sent",
                ("device", AppLog.Device(target)), ("transport", route.Transport)));
        }
        catch (Exception error)
        {
            _viewModel.AddDiagnosticLog(AppLog.Event("passcode_entry_failed",
                ("device", AppLog.Device(target)), ("error", error.GetType().Name)));
        }
        finally
        {
            Array.Clear(digits);
            try
            {
                if (route is not null) await RestoreDirectKeyboardStateAsync(route, route.IsCurrent);
            }
            catch (Exception error)
            {
                DiagnosticLogger.ReverseControlWarning("keyboard_input", "passcode_restore_failed", ("error", error.Message));
                ResetKeyboardOwnership();
            }
            finally
            {
                sessionGate?.Release();
                if (shortcutGateHeld) _systemShortcutGate.Release();
            }
        }
    }

    private static async Task PressPasscodeKeyAsync(DirectKeyboardRoute route, byte usage, Func<bool> current,
        TimeSpan? gap = null)
    {
        if (!current()) throw new OperationCanceledException();
        try
        {
            await route.SendAsync(0, [usage], current);
            await Task.Delay(PasscodeKeyHold);
        }
        finally
        {
            // Always release, so a dropped route cannot leave a digit held.
            await route.SendAsync(0, [], null);
        }
        await Task.Delay(gap ?? PasscodeKeyGap);
    }
}
