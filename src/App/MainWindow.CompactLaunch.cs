using System.Diagnostics;
using System.Windows;
using IPhoneMirror.App.Localization;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    private bool _compactPreviewActive;
    private readonly CancellationTokenSource _compactLaunchCancellation = new();
    private readonly SemaphoreSlim _compactLaunchGate = new(1, 1);

    private async Task InitializeDeviceStartupAsync()
    {
        var options = (Application.Current as App)?.LaunchOptions;
        if (options is not { Enabled: true })
        {
            await _viewModel.RefreshAsync();
            return;
        }
        await OpenCompactRequestAsync(options);
    }

    internal async Task OpenCompactRequestAsync(CompactLaunchOptions options)
    {
        if (_compactLaunchCancellation.IsCancellationRequested) return;
        var acquired = false;
        try
        {
            await _compactLaunchGate.WaitAsync(_compactLaunchCancellation.Token);
            acquired = true;
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(20))
            {
                _compactLaunchCancellation.Token.ThrowIfCancellationRequested();
                var remaining = TimeSpan.FromSeconds(20) - deadline.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                await _viewModel.RefreshAsync(forceDeviceEnumeration: true)
                    .WaitAsync(remaining, _compactLaunchCancellation.Token);
                _compactLaunchCancellation.Token.ThrowIfCancellationRequested();
                var devices = _viewModel.Devices.Where(device => !device.IsMediaCast).ToArray();
                var index = options.SelectDevice(devices.Select(device => device.Udid).ToArray());
                if (index >= 0)
                {
                    var device = devices[index];
                    if ((Application.Current as App)?.LaunchOptions.Enabled == true)
                        _viewModel.SelectedDevice = device;
                    var result = await _secondaryMirrors.ShowAsync(device);
                    _compactLaunchCancellation.Token.ThrowIfCancellationRequested();
                    if (!result.Success) throw new InvalidOperationException(result.Message);
                    if ((Application.Current as App)?.LaunchOptions.Enabled == true)
                    {
                        if (!_compactPreviewActive)
                            _secondaryMirrors.PreviewClosed += OnCompactPreviewClosed;
                        _compactPreviewActive = true;
                        Hide();
                    }
                    _secondaryMirrors.Activate(device.Udid);
                    return;
                }
                if (options.DeviceId is null && devices.Length > 1)
                    throw new InvalidOperationException(LocalizationService.Get("CompactLaunchMultipleDevices"));
                await Task.Delay(500, _compactLaunchCancellation.Token);
            }
            throw new InvalidOperationException(LocalizationService.Get("CompactLaunchDeviceUnavailable"));
        }
        catch (Exception) when (_compactLaunchCancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (IsTrayMode)
            {
                _trayPanel?.ReportError(LocalizationService.Format("CompactLaunchFailed", error.Message));
                ShowTrayPanel();
                return;
            }
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _viewModel.AddUiLog(LocalizationService.Format("CompactLaunchFailed", error.Message));
            IPhoneMirror.App.Windows.AppPromptWindow.Inform(LocalizationService.Get("CompactLaunchTitle"),
                LocalizationService.Format("CompactLaunchFailed", error.Message), this);
        }
        finally { if (acquired) _compactLaunchGate.Release(); }
    }

    private bool _coldStartPreviewRunning;

    private bool CanColdStartPreview => !_coldStartPreviewRunning && !_shutdownStarted &&
        !_previewVisibilityChanging && !_secondaryMirrors.HasAnyOpen &&
        !_compactLaunchCancellation.IsCancellationRequested;

    // ShowPreview shortcut with no preview yet: open an independent preview
    // without bringing up the main window.
    private async Task ColdStartPreviewFromShortcutAsync()
    {
        if (!CanColdStartPreview) return;
        _coldStartPreviewRunning = true;
        var token = _compactLaunchCancellation.Token;
        try
        {
            DeviceViewModel? PickDevice() =>
                _viewModel.SelectedDevice is { IsMediaCast: false } selected ? selected
                    : _viewModel.Devices.FirstOrDefault(device => !device.IsMediaCast);
            var device = PickDevice();
            var deadline = Stopwatch.StartNew();
            while (device is null && deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                var remaining = TimeSpan.FromSeconds(10) - deadline.Elapsed;
                if (remaining <= TimeSpan.Zero) break;
                await _viewModel.RefreshAsync(forceDeviceEnumeration: true).WaitAsync(remaining, token);
                device = PickDevice();
                if (device is null) await Task.Delay(500, token);
            }
            if (device is null)
                throw new InvalidOperationException(LocalizationService.Get("CompactLaunchDeviceUnavailable"));
            var result = await _secondaryMirrors.ShowAsync(device);
            token.ThrowIfCancellationRequested();
            if (!result.Success) throw new InvalidOperationException(result.Message);
            _secondaryMirrors.Activate(device.Udid);
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            var message = LocalizationService.Format("CompactLaunchFailed", error.Message);
            _viewModel.AddUiLog(message);
            if (IsTrayMode)
            {
                _trayPanel?.ReportError(message);
                ShowTrayPanel();
                return;
            }
            if (IsVisible)
                IPhoneMirror.App.Windows.AppPromptWindow.Inform(LocalizationService.Get("CompactLaunchTitle"), message, this);
            else
                IPhoneMirror.App.Windows.AppPromptWindow.InformStandalone(LocalizationService.Get("CompactLaunchTitle"), message);
        }
        finally { _coldStartPreviewRunning = false; }
    }

    private void OnCompactPreviewClosed(string deviceId)
    {
        if (IsTrayMode) return;
        if (!_compactPreviewActive || _secondaryMirrors.HasAnyOpen) return;
        _compactPreviewActive = false;
        // Let the native close handler finish before the ordinary async shutdown.
        Dispatcher.BeginInvoke(() => Close());
    }
}
