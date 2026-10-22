namespace IPhoneMirror.App.Services;

internal sealed record PinchZoomRoute(string Target, Func<bool> IsCurrent,
    Func<string, double, double, double, CancellationToken, Task> SendAsync);

// Short two-finger pinches can repeat without a finite zoom range. Coalesce
// wheel input while the transport is busy instead of building a delayed queue.
internal sealed class WheelPinchZoom(PinchZoomRoute route, double x, double y)
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly double _x = Math.Clamp(x, .28, .72), _y = Math.Clamp(y, .08, .92);
    private int _pending;
    private long _lastInput;
    private bool _started, _finished;
    private double _radius = .12;
    internal Task Completion { get; private set; } = Task.CompletedTask;

    internal void AddDelta(int delta)
    {
        if (delta == 0 || _finished || _cancel.IsCancellationRequested) return;
        _pending = Math.Clamp(_pending + delta, -360, 360);
        _lastInput = Environment.TickCount64;
        if (_started) return;
        _started = true;
        Completion = DrainAsync();
    }

    internal void Cancel() { if (!_finished) _cancel.Cancel(); }

    private async Task DrainAsync()
    {
        try
        {
            while (_pending != 0 && route.IsCurrent() && !_cancel.IsCancellationRequested)
            {
                if (Environment.TickCount64 - _lastInput > 200) break;
                var delta = _pending;
                _pending = 0;
                _radius = .12;
                var end = .12 * Math.Exp(delta / 120.0 * .16);
                try
                {
                    await route.SendAsync("down", _x, _y, _radius, _cancel.Token);
                    for (var frame = 1; frame <= 6; frame++)
                    {
                        await Task.Delay(12, _cancel.Token);
                        if (!route.IsCurrent()) throw new OperationCanceledException();
                        var progress = frame / 6.0;
                        _radius = .12 + (end - .12) * (progress * progress * (3 - 2 * progress));
                        await route.SendAsync("move", _x, _y, _radius, _cancel.Token);
                    }
                }
                finally { await ReleaseAsync(); }
            }
        }
        catch (OperationCanceledException) { }
        finally { _finished = true; _cancel.Dispose(); }
    }

    private async Task ReleaseAsync()
    {
        // Cleanup is addressed to the original transport, even after focus or
        // Ctrl is released. The route still rejects a replacement generation.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await route.SendAsync("up", _x, _y, _radius, timeout.Token);
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
        catch (System.IO.IOException) { }
    }
}
