using IPhoneMirror.App.Services;

namespace IPhoneMirror.App.ViewModels;

internal sealed partial class MainViewModel
{
    internal PinchZoomRoute? CapturePinchZoomRoute(string target, Func<bool> allowed)
    {
        var humanGuard = AutomationHumanGuard(target);
        var control = FindControl(target);
        var bridge = GetReadyUsbControlBridge(target);
        if (!humanGuard() || control is null || control.Starting || control.Stopping ||
            bridge is null || control.AppleUdid is null || IsBluetoothControlTarget(target)) return null;
        var apple = control.AppleUdid;
        var mode = control.WiredEnabled && ReferenceEquals(control.WiredBridge, bridge)
            ? ReverseControlMode.Usb : ReverseControlMode.Wireless;
        var routerGeneration = control.Router.Generation;
        var bridgeGeneration = bridge.InputGeneration;
        bool SameSession() => bridge.IsReady && bridge.InputGeneration == bridgeGeneration &&
            control.Router.Owns(apple, mode, routerGeneration);
        bool Current() => humanGuard() && !_disposed && SameSession() &&
            (ReferenceEquals(control.WiredBridge, bridge) || ReferenceEquals(control.WirelessBridge, bridge)) && allowed();
        if (!Current()) return null;
        var second = Interlocked.Add(ref _nextTouchPointerId, 2);
        var first = second - 1;
        return new(target, Current, async (action, x, y, radius, token) =>
        {
            if (!SameSession() || (action != "up" && !Current())) throw new OperationCanceledException();
            await bridge.SendTouchBatchAsync([
                new TouchPoint(first, action, x - radius, y),
                new TouchPoint(second, action, x + radius, y)],
                DateTimeOffset.UtcNow.ToUnixTimeNanoseconds(), Interlocked.Increment(ref _usbTouchSequence),
                token, () => Current() && !token.IsCancellationRequested, bridgeGeneration,
                requireAllContacts: action == "down");
        });
    }
}
