using System.Windows;
using System.Windows.Interop;
using IPhoneMirror.App.Controls;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    private PreviewVisibilityTarget? _hiddenVisibilityPreview;
    private PreviewVisibilityTarget? _lastVisibilityPreview;
    private TrayIconService? _visibilityRestoreTrayIcon;
    private bool _previewVisibilityChanging;
    private int _visibilityInputReleases;
    internal event Action? WindowVisibilityChanged;
    internal bool IsAppShown => !_bossKeyHidden;
    private static bool IsVisibilityShortcut(BluetoothShortcutAction action) =>
        action is BluetoothShortcutAction.BossKey or BluetoothShortcutAction.ShowPreview;

    // Hiding the app hides only its own windows; independent previews stay on
    // screen and keep their input. Blocks input aimed at anything else.
    private bool AppHiddenBlocks(nint window) =>
        _bossKeyHidden && !_secondaryMirrors.ContainsWindow(window);

    private bool KeepVisibleWhileAppHidden(Window window) =>
        window is PreviewSettingsWindow ||
        _secondaryMirrors.ContainsWindow(new WindowInteropHelper(window).Owner);

    private PreviewVisibilityTarget? CurrentVisibilityTarget(PreviewVisibilityTarget? target) =>
        target is not null && _secondaryMirrors.IsCurrent(target) ? target : null;

    private PreviewVisibilityTarget? HiddenVisibilityPreview =>
        _hiddenVisibilityPreview = CurrentVisibilityTarget(_hiddenVisibilityPreview);

    internal PreviewVisibilityTarget? ResolveSettingsPreviewTarget(nint source = 0) =>
        source != 0 ? CurrentVisibilityTarget(_secondaryMirrors.GetVisibilityTarget(source)) :
        CurrentVisibilityTarget(_secondaryMirrors.GetVisibilityTarget(
            _secondaryMirrors.GetWindowHandle(_viewModel.SelectedDevice?.Udid))) ??
        CurrentVisibilityTarget(_lastVisibilityPreview);

    private PreviewVisibilityTarget? ResolvePreviewVisibilityTarget() =>
        HiddenVisibilityPreview ??
        CurrentVisibilityTarget(_secondaryMirrors.GetVisibilityTarget(_keyboardForegroundWindow())) ??
        ResolveSettingsPreviewTarget();

    private bool CanTogglePreviewVisibility => !_previewVisibilityChanging && ResolvePreviewVisibilityTarget() is not null;

    internal bool CanSetPreviewVisibility(PreviewVisibilityTarget? target) =>
        !_previewVisibilityChanging && CurrentVisibilityTarget(target) is not null &&
        (HiddenVisibilityPreview is null || HiddenVisibilityPreview == target);

    internal bool IsPreviewShown(PreviewVisibilityTarget? target) =>
        CurrentVisibilityTarget(target) is not null && !target!.Window.IsPreviewHidden;

    internal async Task SetPreviewShownAsync(PreviewVisibilityTarget? target, bool shown)
    {
        if (!CanSetPreviewVisibility(target) || IsPreviewShown(target) == shown) return;
        _previewVisibilityChanging = true;
        try
        {
            if (!shown)
            {
                _hiddenVisibilityPreview = target;
                ResetKeyboardOwnership();
                target!.Window.SetPreviewHidden(true);
                ApplyBluetoothControlInputState(activateIndependentWindow: false);
            }
            else
            {
                _hiddenVisibilityPreview = null;
                target!.Window.SetPreviewHidden(false);
                target.Window.Activate();
            }
        }
        finally
        {
            _previewVisibilityChanging = false;
            WindowVisibilityChanged?.Invoke();
        }
        // Recovery remains available while transport releases are draining.
        if (!shown) await ReleaseVisibilityInputAsync([target!.Udid],
            _viewModel.IsBluetoothControlTarget(target.Udid));
    }

    private Task TogglePreviewVisibilityAsync(PreviewVisibilityTarget? target = null)
    {
        target = HiddenVisibilityPreview ?? target ?? ResolvePreviewVisibilityTarget();
        return SetPreviewShownAsync(target, !IsPreviewShown(target));
    }

    internal Task SetAppShownAsync(bool shown) =>
        shown == IsAppShown ? Task.CompletedTask : ToggleBossKeyWindowsAsync();

    private async Task ToggleBossKeyWindowsAsync()
    {
        if (_shutdownStarted || Interlocked.Exchange(ref _bossKeyChanging, 1) != 0) return;
        var restoring = _bossKeyHidden;
        string[] releaseTargets = [];
        var releaseBluetooth = false;
        try
        {
            if (restoring)
            {
                _bossKeyHidden = false;
                BossKeyWindowVisibility.RestoreAll();
                _visibilityRestoreTrayIcon?.Dispose();
                _visibilityRestoreTrayIcon = null;
                ApplyBluetoothControlInputState(activateIndependentWindow: false);
            }
            else
            {
                // Establish the recovery path before any window disappears.
                if (_trayIcon is null)
                    _visibilityRestoreTrayIcon ??= new TrayIconService(() => _ = SetAppShownAsync(true));
                _bossKeyHidden = true;
                ResetKeyboardOwnership();
                ApplyBluetoothControlInputState(activateIndependentWindow: false);
                // Only the main window's preview disappears; visible previews keep their touches.
                releaseTargets = _usbTouchStates.Keys.Concat(_previewTouches.Keys).Distinct()
                    .Where(udid => AppHiddenBlocks(_secondaryMirrors.GetWindowHandle(udid))).ToArray();
                releaseBluetooth = _viewModel.IsBluetoothControlEnabled && AppHiddenBlocks(_activeControlWindow);
                BossKeyWindowVisibility.KeepVisible ??= KeepVisibleWhileAppHidden;
                BossKeyWindowVisibility.HideAll();
            }
            _viewModel.AddDiagnosticLog(AppLog.Event("boss_key_toggled",
                ("hidden", _bossKeyHidden), ("wpf_windows", BossKeyWindowVisibility.HiddenWindowCount)));
        }
        catch (Exception error)
        {
            _viewModel.AddDiagnosticLog(AppLog.Event("boss_key_toggle_failed",
                ("hidden", _bossKeyHidden), ("error", AppLog.Error(error))));
        }
        finally
        {
            Volatile.Write(ref _bossKeyChanging, 0);
            WindowVisibilityChanged?.Invoke();
        }
        if (!restoring && _bossKeyHidden)
            await ReleaseVisibilityInputAsync(releaseTargets, releaseBluetooth);
    }

    private async Task ReleaseVisibilityInputAsync(string[] targets, bool bluetooth)
    {
        ++_visibilityInputReleases;
        try
        {
            // New pointer/mapping input stays gated until all releases complete;
            // global visibility shortcuts never depend on this transport gate.
            foreach (var udid in targets)
                await HandleUsbPointerInputAsync(new PreviewPointerEventArgs(
                    PreviewPointerKind.Reset, 0, 0, 0, 0), udid);
            await _bluetoothRouteGate.WaitAsync();
            try
            {
                if (bluetooth) await _viewModel.ReleaseBluetoothControlInputAsync();
                await _keyboardHandoff;
            }
            finally { _bluetoothRouteGate.Release(); }
        }
        catch (Exception error)
        {
            _viewModel.AddDiagnosticLog(AppLog.Event("visibility_input_release_failed", ("error", AppLog.Error(error))));
        }
        finally
        {
            --_visibilityInputReleases;
            if (!_shutdownStarted) ApplyBluetoothControlInputState(activateIndependentWindow: false);
        }
    }
}
