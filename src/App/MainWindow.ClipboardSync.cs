using System.Runtime.InteropServices;
using System.Windows.Threading;
using IPhoneMirror.App.Services;

namespace IPhoneMirror.App;

public partial class MainWindow
{
    private const int WmClipboardUpdate = 0x031D;
    private DispatcherTimer? _clipboardPushTimer;
    private nint _clipboardListenerWindow;

    // Windows -> iPhone half of the automatic clipboard sync. iPhone -> Windows
    // already runs through the bridge's pasteboard polling.
    private void RegisterClipboardListener()
    {
        var hwnd = _windowSource?.Handle ?? 0;
        if (hwnd == 0 || _clipboardListenerWindow == hwnd) return;
        if (AddClipboardFormatListener(hwnd)) _clipboardListenerWindow = hwnd;
        else
            DiagnosticLogger.ReverseControlWarning("clipboard", "listener_register_failed",
                ("error", Marshal.GetLastWin32Error()));
    }

    private void UnregisterClipboardListener()
    {
        _clipboardPushTimer?.Stop();
        if (_clipboardListenerWindow == 0) return;
        RemoveClipboardFormatListener(_clipboardListenerWindow);
        _clipboardListenerWindow = 0;
    }

    private void OnWindowsClipboardUpdate()
    {
        // Apps often publish several formats in a burst; push the settled text once.
        _clipboardPushTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(150),
            DispatcherPriority.Background, (_, _) =>
            {
                _clipboardPushTimer!.Stop();
                _ = _viewModel.PushWindowsClipboardToDeviceAsync(_readClipboardText);
            }, Dispatcher);
        _clipboardPushTimer.Stop();
        _clipboardPushTimer.Start();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);
}
