using IPhoneMirror.App.Services;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    private readonly KeyboardInputRouter _keyboardRouter = new();
    private Task _keyboardHandoff = Task.CompletedTask;
    private readonly HashSet<Task> _keyboardSends = [];
    private DirectKeyboardRoute? _directKeyboardRoute;
    private DirectKeyboardRoute? _unreleasedKeyboardRoute;
    private KeyboardInputMode CurrentKeyboardInputMode => _keyboardRouter.Mode;
    private long KeyboardInputModeGeneration => _keyboardRouter.Generation;
    private bool IsKeyboardMappingInputModeActive =>
        _keyboardRouter.RequestedMode == KeyboardInputMode.Mapping;
    private bool IsDirectKeyboardInputModeActive => _keyboardRouter.Mode == KeyboardInputMode.Direct;

    private bool TryEnterDirectKeyboardInputMode()
    {
        if (IsKeyboardMappingInputModeActive || _mappingClosing) return false;
        if (_keyboardRouter.RequestedMode != KeyboardInputMode.Direct)
            ChangeKeyboardOwner(KeyboardInputMode.Direct);
        return IsDirectKeyboardInputModeActive;
    }

    private bool TryEnterKeyboardMappingInputMode()
    {
        if (_mappingClosing) return false;
        if (!IsKeyboardMappingInputModeActive)
            ChangeKeyboardOwner(KeyboardInputMode.Mapping, sampleHeldKeys: true);
        return true;
    }

    private void LeaveKeyboardMappingInputMode()
    {
        _typeOnly = false;
        if (IsKeyboardMappingInputModeActive)
            ChangeKeyboardOwner(_mappingClosing ? KeyboardInputMode.None : KeyboardInputMode.Direct,
                sampleHeldKeys: true);
    }

    private void LeaveDirectKeyboardInputMode()
    {
        if (_keyboardRouter.RequestedMode == KeyboardInputMode.Direct)
            ChangeKeyboardOwner(KeyboardInputMode.None);
    }

    private void ResetKeyboardOwnership() => ChangeKeyboardOwner(_keyboardRouter.RequestedMode);

    private void ChangeKeyboardOwner(KeyboardInputMode next, bool sampleHeldKeys = false)
    {
        Dispatcher.VerifyAccess();
        var previous = _keyboardRouter.Mode;
        var generation = _keyboardRouter.BeginHandoff(next, sampleHeldKeys
            ? Enumerable.Range(8, 248).Where(k => k is not (0x10 or 0x11 or 0x12) && _isPhysicalKeyboardKeyDown(k))
            : null);
        PublishKeyboardInputMode(KeyboardInputMode.None);
        if (next == KeyboardInputMode.Mapping) RegisterRawInput(_rawMouseInputEnabled, false);
        Interlocked.Increment(ref _keyboardInputGeneration);
        CancelMappedGesture();
        _controlKeyboardUsages.Clear();
        _controlModifierKeys.Clear();
        _controlKeyboardModifiers = 0;
        _pasteVPending = false;
        _arrowSwipeKeys.Clear();
        _mixedHeldKeys.Clear();
        var oldRoute = _directKeyboardRoute;
        _directKeyboardRoute = null;
        var pending = Task.WhenAll(_keyboardSends.ToArray());
        _keyboardHandoff = FinishKeyboardHandoffAsync(_keyboardHandoff, pending,
            _mappingExecutor.Completion, oldRoute, generation);
        DiagnosticLogger.ReverseControl("keyboard_input", "owner_changing",
            ("from", previous), ("to", next), ("generation", generation),
            ("device", AppLog.Device(oldRoute?.Target)), ("transport", oldRoute?.Transport));
        RefreshDeviceHotkeys();
        ReconcileKeyboardHook();
    }

    private async Task FinishKeyboardHandoffAsync(Task previous, Task pending, Task mapping,
        DirectKeyboardRoute? oldRoute, long generation)
    {
        try
        {
            await previous;
            // A press which already entered a writer finishes before its empty
            // release. The next owner stays closed for the entire drain.
            try { await Task.WhenAll(pending, mapping); }
            catch (Exception error)
            {
                DiagnosticLogger.ReverseControlWarning("keyboard_input", "drain_failed", ("error", error.Message));
            }
            await _systemShortcutGate.WaitAsync();
            try
            {
                oldRoute ??= _unreleasedKeyboardRoute;
                if (oldRoute?.IsCurrent() == true)
                    await oldRoute.SendAsync(0, [], null);
                _unreleasedKeyboardRoute = null;
            }
            finally { _systemShortcutGate.Release(); }
            if (!_keyboardRouter.CompleteHandoff(generation)) return;
            if (!_mappingClosing)
                // Mouse INPUTSINK belongs to the active Bluetooth route,
                // including independent previews, regardless of keyboard owner.
                RegisterRawInput(IsBluetoothControlActive,
                    _keyboardRouter.Mode == KeyboardInputMode.Direct &&
                    (IsBluetoothControlActive || IsUsbControlActive) && _activeControlWindow == 0);
            PublishKeyboardInputMode(_keyboardRouter.Mode);
            RefreshDeviceHotkeys();
            ReconcileKeyboardHook();
            DiagnosticLogger.ReverseControl("keyboard_input", "owner_ready",
                ("owner", _keyboardRouter.Mode), ("generation", generation));
        }
        catch (Exception error)
        {
            _unreleasedKeyboardRoute = oldRoute;
            // A failed release must not open another owner over a stuck key.
            DiagnosticLogger.ReverseControlWarning("keyboard_input", "release_failed",
                ("device", AppLog.Device(oldRoute?.Target)), ("error", error.Message));
        }
    }

    private async Task TrackKeyboardSendAsync(Task send)
    {
        _keyboardSends.Add(send);
        try { await send; }
        finally { _keyboardSends.Remove(send); }
    }

    // Mixed typing: unmapped keys type on the iPhone while the Mapping owner
    // stays in charge. Its own press set drops auto-repeats (HID reports are
    // stateful) and is cleared with every owner change.
    private readonly HashSet<int> _mixedHeldKeys = [];
    private bool _typeOnly;

    private static Models.EverydaySettings EverydaySettingsForInput =>
        (System.Windows.Application.Current as App)?.UpdateSettings.Everyday ?? new();

    private bool RouteMixedKey(int key, bool down) =>
        down ? _mixedHeldKeys.Add(key) : _mixedHeldKeys.Remove(key);

    // The type-only key (Right Ctrl by default) flips key mapping off and on
    // while mapping is the owner; it never reaches the iPhone or Windows.
    private bool TryToggleTypeOnly(MappedKey key, bool down)
    {
        var toggle = EverydaySettingsForInput.TypeOnlyToggleVirtualKey;
        if (toggle == 0 || key.VirtualKey != toggle) return false;
        if (down && _mixedHeldKeys.Add(-toggle))
        {
            _typeOnly = !_typeOnly;
            CancelMappedGesture();
            PublishKeyboardInputMode(_keyboardRouter.Mode);
            DiagnosticLogger.ReverseControl("keyboard_input", "type_only_toggled", ("type_only", _typeOnly));
        }
        else if (!down) _mixedHeldKeys.Remove(-toggle);
        return true;
    }

    private void PublishKeyboardInputMode(KeyboardInputMode mode) =>
        _viewModel.SetKeyboardInputModeStatus(mode switch
        {
            KeyboardInputMode.Mapping when _typeOnly => "KeyboardInputModeTypeOnly",
            KeyboardInputMode.Mapping when EverydaySettingsForInput.MixMappingAndTyping => "KeyboardInputModeMixed",
            KeyboardInputMode.Mapping => "KeyboardInputModeMapping",
            KeyboardInputMode.Direct => "KeyboardInputModeDirect",
            _ => "KeyboardInputModeNone",
        });
}
