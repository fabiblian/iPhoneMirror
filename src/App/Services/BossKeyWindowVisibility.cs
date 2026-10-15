using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace IPhoneMirror.App.Services;

internal static class BossKeyWindowVisibility
{
    private static readonly Dictionary<Window, HwndSource> TrackedWindows = [];
    private static readonly HashSet<Window> HiddenWindows = [];
    private static readonly HashSet<Window> InitialWindows = [];
    private static bool _hidden;
    private static bool _scanning;
    internal static int HiddenWindowCount => HiddenWindows.Count;

    internal static void Apply(Window window)
    {
        if (!TrackedWindows.ContainsKey(window) &&
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle) is HwndSource source)
        {
            TrackedWindows.Add(window, source);
            source.AddHook((nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
            {
                // Prevent a tracked dialog or activation callback from showing
                // a window while the app is hidden, including during ShowDialog.
                if (_hidden && message == 0x46 && lParam != 0) // WM_WINDOWPOSCHANGING
                {
                    var position = Marshal.PtrToStructure<WindowPosition>(lParam);
                    if ((position.Flags & 0x40) != 0) // SWP_SHOWWINDOW
                    {
                        HiddenWindows.Add(window);
                        position.Flags = (position.Flags & ~0x40u) | 0x80u;
                        Marshal.StructureToPtr(position, lParam, false);
                    }
                }
                if (_hidden && message == 0x47 && IsWindowVisible(hwnd)) // WM_WINDOWPOSCHANGED
                {
                    HiddenWindows.Add(window);
                    ShowWindow(hwnd, 0);
                }
                return 0;
            });
            window.Closed += OnWindowClosed;
        }
        if (_hidden && window.IsVisible)
        {
            if (TrackedWindows.TryGetValue(window, out var trackedSource))
            {
                if (!InitialWindows.Contains(window) || IsWindowVisible(trackedSource.Handle))
                    HiddenWindows.Add(window);
                ShowWindow(trackedSource.Handle, 0);
            }
        }
    }

    internal static void HideAll()
    {
        if (!_hidden && Application.Current is { } app)
            app.Dispatcher.Hooks.OperationStarted += OnDispatcherOperation;
        // Snapshot before hiding owners can implicitly hide their dialogs.
        InitialWindows.Clear();
        foreach (var window in Application.Current?.Windows.Cast<Window>().ToArray() ?? []) InitialWindows.Add(window);
        var visible = InitialWindows
            .Where(window => window.IsVisible && IsWindowVisible(new WindowInteropHelper(window).Handle))
            .ToArray();
        _hidden = true;
        foreach (var window in visible)
        {
            HiddenWindows.Add(window);
            Apply(window);
        }
    }

    internal static void RestoreAll()
    {
        _hidden = false;
        if (Application.Current is { } app) app.Dispatcher.Hooks.OperationStarted -= OnDispatcherOperation;
        foreach (var window in HiddenWindows
            .OrderBy(window => ReferenceEquals(window, Application.Current?.MainWindow) ? 0 : 1).ToArray())
        {
            if (window.IsVisible && TrackedWindows.TryGetValue(window, out var source) && !source.IsDisposed)
                ShowWindow(source.Handle, 8); // SW_SHOWNA preserves placement and modal state.
        }
        HiddenWindows.Clear();
        InitialWindows.Clear();
    }

    private static void OnDispatcherOperation(object? sender, DispatcherHookEventArgs e)
    {
        // Empty HWND-backed WPF windows may have no Loaded visual tree. Scan
        // before dispatcher work so new dialogs are covered before input/render.
        if (!_hidden || _scanning) return;
        _scanning = true;
        try
        {
            foreach (var window in Application.Current?.Windows.Cast<Window>().ToArray() ?? [])
                if (window.IsVisible && (!TrackedWindows.TryGetValue(window, out var source) ||
                    IsWindowVisible(source.Handle))) Apply(window);
        }
        finally { _scanning = false; }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Closed -= OnWindowClosed;
        TrackedWindows.Remove(window);
        HiddenWindows.Remove(window);
        InitialWindows.Remove(window);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        internal nint Window, InsertAfter;
        internal int X, Y, Width, Height;
        internal uint Flags;
    }
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);
}
