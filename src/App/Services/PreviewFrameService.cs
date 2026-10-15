using System.IO;
using System.Windows.Threading;
using IPhoneMirror.App.Models;
using IPhoneMirror.App.Updater;

namespace IPhoneMirror.App.Services;

/// <summary>
/// Process-wide preview-window frame preference. Open preview windows follow
/// <see cref="Changed"/> live. The value lives in the application's in-memory
/// <see cref="UpdateSettings"/> (which App saves in full on exit); writes to
/// disk are coalesced so dragging a slider does not rewrite settings.json for
/// every intermediate value.
/// </summary>
internal static class PreviewFrameService
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);
    private static PreviewFrameSettings? _fallback;
    private static DispatcherTimer? _saveTimer;

    internal static event Action? Changed;

    private static App? AppInstance => System.Windows.Application.Current as App;

    internal static PreviewFrameSettings Current
    {
        get
        {
            if (AppInstance is { } app)
                return (app.UpdateSettings.PreviewFrame ?? new()).Normalized();
            _fallback ??= LoadFallback();
            return _fallback.Clone();
        }
    }

    internal static void Update(Action<PreviewFrameSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var next = Current;
        change(next);
        Replace(next);
    }

    internal static void Reset() => Replace(new PreviewFrameSettings());

    private static void Replace(PreviewFrameSettings value)
    {
        var previous = Current;
        var next = value.Normalized();
        if (previous.IsEquivalentTo(next)) return;
        if (AppInstance is { } app) app.UpdateSettings.PreviewFrame = next;
        else _fallback = next;
        Changed?.Invoke();
        QueueSave();
    }

    private static PreviewFrameSettings LoadFallback()
    {
        try { return new UpdateSettingsStore().Load().PreviewFrame.Normalized(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DiagnosticLogger.Exception("settings", "preview_frame_load_failed", error);
            return new PreviewFrameSettings();
        }
    }

    private static void QueueSave()
    {
        var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        if (dispatcher is null)
        {
            Save();
            return;
        }
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = SaveDelay,
            };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                Save();
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static void Save()
    {
        if (AppInstance is { } app)
        {
            _ = app.SaveUpdateSettings();
            return;
        }
        var snapshot = _fallback?.Clone();
        if (snapshot is null) return;
        try { new UpdateSettingsStore().Update(settings => settings.PreviewFrame = snapshot); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            DiagnosticLogger.Exception("settings", "preview_frame_save_failed", error);
        }
    }
}
