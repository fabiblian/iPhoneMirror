using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App.Runtime.Tests;

// Exercise the production status service, XAML window and timers on a real STA
// dispatcher. Only the transport's status reports are simulated.
//
// The windows are real top-level windows on the interactive desktop, so the
// test swallows desktop input to them. The app's countdown runs on real time,
// so a loaded machine can delay the test's own steps past its five-second
// deadline. Scenarios measure that and retry instead of asserting on a
// precondition that never held; every attempt within the budget is checked in
// full.
internal static class ReverseControlCountdownTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Assembly Assembly = typeof(App).Assembly;
    private static readonly Type ServiceType = Type("ControlStatusService");
    private static readonly Type ModeType = Type("ControlStatusMode");
    private static readonly Type StageType = Type("ControlStage");
    private static Type Type(string name) => Assembly.GetType($"IPhoneMirror.App.Services.{name}", true)!;
    // The app arms its absolute five-second deadline no earlier than the
    // stopwatch a scenario starts just before Ready, so the deadline cannot
    // have passed while that stopwatch is below this budget.
    private static readonly TimeSpan SetupBudget = TimeSpan.FromSeconds(4.5);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);
    private const int MaxAttempts = 3;

    internal static int Run()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        typeof(App).GetProperty("IsUiPreviewMode", Instance)!.SetValue(app, true);
        app.InitializeComponent();
        var owner = new Wpf.Ui.Controls.FluentWindow { Width = 320, Height = 240, ShowInTaskbar = false };
        owner.Show();
        try
        {
            Run(owner);
            return 0;
        }
        finally
        {
            owner.Close();
            app.Shutdown();
        }
    }

    internal static void Run(Window owner)
    {
        foreach (var mode in new[] { "Usb", "Wireless", "Bluetooth" })
            Retry($"{mode} success", () => TestQueuedSuccess(owner, mode));
        Retry("worker success", () => TestQueuedSuccess(owner, "Usb", workerReports: true));
        Retry("display timer", () => TestDisplayTimerCompletesCountdown(owner));
        Retry("failure", () => TestFailure(owner));
        Retry("prompt and recovery", () => TestPromptAndRecovery(owner));
        Retry("manual close", () => TestManualCloseAndReplacement(owner));
        Console.WriteLine("Reverse-control countdown runtime tests passed.");
    }

    private static void TestQueuedSuccess(Window owner, string modeName, bool workerReports = false)
    {
        var service = Activator.CreateInstance(ServiceType, nonPublic: true)!;
        var mode = Enum.Parse(ModeType, modeName);
        Call(service, "Begin", mode, "Runtime test iPhone");
        var callbackCount = 0;
        var window = Show(owner, service, () => callbackCount++);
        var closed = false;
        var closedOnDispatcher = false;
        var elapsed = Stopwatch.StartNew();
        var closedAt = TimeSpan.Zero;
        window.Closed += (_, _) =>
        {
            closed = true;
            closedOnDispatcher = window.Dispatcher.CheckAccess();
            closedAt = elapsed.Elapsed;
        };
        var labels = new List<string>();
        ((INotifyPropertyChanged)window.DataContext).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "CountdownText")
                labels.Add((string)window.DataContext.GetType().GetProperty("CountdownText")!
                    .GetValue(window.DataContext)!);
        };
        try
        {
            // Matches EnableUsbControlCoreAsync: these reports occur in one
            // continuation, before the dispatcher's queued Apply calls run.
            void Connect()
            {
                Report(service, mode, "Connecting");
                Report(service, mode, "InitializingServices");
                Report(service, mode, "StartingInputRouter");
                Call(service, "Ready", mode, "Runtime test iPhone", "Connected");
            }
            if (workerReports) Task.Run(Connect).GetAwaiter().GetResult();
            else Connect();
            Pump(TimeSpan.FromSeconds(2));
            RequireBeforeDeadline(elapsed, "The first countdown check");
            Require(window.IsVisible && !closed, "Success must remain visible during its countdown." + State(window, elapsed));
            var deadline = Deadline(window);
            // Duplicate ready notifications must not restart the countdown or
            // replace the displayed instance.
            Call(service, "Ready", mode, "Runtime test iPhone", "Still connected");
            var duplicateAt = elapsed.Elapsed;
            Require(ReferenceEquals(window, Show(owner, service)), "Show must reuse the visible instance.");
            Pump(TimeSpan.FromMilliseconds(100));
            RequireBeforeDeadline(elapsed, "Applying the duplicate ready report");
            Require(window.IsVisible && !closed, "Success must remain visible during its countdown." + State(window, elapsed));
            Require(Deadline(window) == deadline,
                "Repeated ready reports must preserve the original five-second deadline.");
            PumpUntil(() => closed, CloseTimeout);
            Console.WriteLine($"{modeName} (worker={workerReports}): closed_at={closedAt.TotalSeconds:F2}s, " +
                $"closed={closed}, visible={window.IsVisible}, callback={callbackCount}, " +
                $"labels={string.Join(" | ", labels.Distinct())}");
            if (!closed)
            {
                var model = window.DataContext;
                Console.WriteLine($"Unclosed countdown: remaining={model.GetType().GetField("_remaining", Instance)!.GetValue(model)}, " +
                    $"armed={model.GetType().GetField("_countdownArmed", Instance)!.GetValue(model)}, " +
                    $"elapsed={model.GetType().GetField("_countdownElapsed", Instance)!.GetValue(model)}, " +
                    $"display_timer_enabled={((DispatcherTimer)model.GetType().GetField("_timer", Instance)!.GetValue(model)!).IsEnabled}");
            }
            Require(closed && closedOnDispatcher && !window.IsVisible,
                $"{modeName}: the visible status window must reach Closed after the five-second countdown.");
            Require(callbackCount == 1, "The completion callback must run exactly once.");
            // A restarted countdown could not close before duplicateAt + 5 s.
            Require(closedAt.TotalSeconds >= 4.95 && closedAt < duplicateAt + TimeSpan.FromSeconds(5),
                "Repeated ready reports must preserve the original five-second deadline.");
            Require(labels.Contains(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                (string)Application.Current.FindResource("ControlCloseCountdown"), 1)),
                "The visible countdown must advance to its final second.");
            Require(!Application.Current.Windows.Cast<Window>().Contains(window),
                "Close must remove the actual displayed instance from Application.Windows.");
        }
        finally { if (!closed) window.Close(); }
    }

    private static void TestDisplayTimerCompletesCountdown(Window owner)
    {
        var service = Activator.CreateInstance(ServiceType, nonPublic: true)!;
        var mode = Enum.Parse(ModeType, "Usb");
        Call(service, "Begin", mode, "Runtime test iPhone");
        var completed = 0;
        var window = Show(owner, service, () => completed++);
        var closed = false;
        var startUtc = DateTime.UtcNow;
        var elapsed = Stopwatch.StartNew();
        var closedAt = TimeSpan.Zero;
        var closedUtc = default(DateTime);
        window.Closed += (_, _) => { closed = true; closedAt = elapsed.Elapsed; closedUtc = DateTime.UtcNow; };
        try
        {
            Call(service, "Ready", mode, "Runtime test iPhone", "Connected");
            Pump(TimeSpan.FromMilliseconds(100));
            // Reproduce a missing one-shot completion (for example, its
            // callback arrived just before the absolute deadline). Reaching
            // zero in the display timer must still close the real window.
            var timer = (System.Threading.Timer)window.DataContext.GetType()
                .GetField("_deadlineTimer", Instance)!.GetValue(window.DataContext)!;
            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Call(window.DataContext, "OnCountdownDeadline");
            RequireBeforeDeadline(elapsed, "The early pool-timer callback");
            Require(!closed, "An early pool-timer callback must not close the window before five seconds.");
            PumpUntil(() => closed, CloseTimeout);
            Require(closed && !window.IsVisible && completed == 1,
                "The display timer reaching zero must complete auto-close without the pool timer.");
            Require(closedAt.TotalSeconds >= 4.95,
                $"The display timer must not complete auto-close before five seconds (closed_at={closedAt.TotalSeconds:F3}s, " +
                $"utc_elapsed={(closedUtc - startUtc).TotalSeconds:F3}s, deadline_after_start={(Deadline(window) - startUtc).TotalSeconds:F3}s).");
            Require(typeof(ReverseControlStatusWindow).GetField("_active",
                BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) is null,
                "Closed must clear the popup manager's active instance.");
            Console.WriteLine("PASS: reaching zero closes the real window even without a pool-timer callback.");
        }
        finally { if (!closed) window.Close(); }
    }

    private static void TestFailure(Window owner)
    {
        var service = Activator.CreateInstance(ServiceType, nonPublic: true)!;
        var mode = Enum.Parse(ModeType, "Usb");
        Call(service, "Begin", mode, "Runtime test iPhone");
        var completed = 0;
        var window = Show(owner, service, () => completed++);
        try
        {
            var elapsed = Stopwatch.StartNew();
            Call(service, "Ready", mode, "Runtime test iPhone", "Connected");
            Pump(TimeSpan.FromMilliseconds(100));
            Call(service, "Failed", mode, "Runtime test iPhone", "Connection timed out", "Simulated USB error");
            RequireBeforeDeadline(elapsed, "The failure report");
            Pump(TimeSpan.FromSeconds(5.5));
            Require(window.IsVisible && completed == 0, "A failure must cancel success auto-close." + State(window, elapsed));
            Require((string)window.DataContext.GetType().GetProperty("StageDescription")!
                .GetValue(window.DataContext)! == "Connection timed out", "The error must remain displayed.");
            Call(window, "OnCloseClick", window, new RoutedEventArgs());
            Require(!window.IsVisible && completed == 0, "Manual error close must not enable input.");
            Console.WriteLine("PASS: failure/timeout remains visible beyond five seconds; manual close works.");
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    private static void TestPromptAndRecovery(Window owner)
    {
        var service = Activator.CreateInstance(ServiceType, nonPublic: true)!;
        var mode = Enum.Parse(ModeType, "Usb");
        Call(service, "Begin", mode, "Runtime test iPhone");
        var completed = 0;
        var window = Show(owner, service, () => completed++);
        try
        {
            var elapsed = Stopwatch.StartNew();
            Call(service, "Ready", mode, "Runtime test iPhone", "Connected");
            Pump(TimeSpan.FromMilliseconds(100));
            var prompt = Activator.CreateInstance(Type("ControlPrompt"),
                [Enum.Parse(Type("ControlPromptType"), "UserActionRequired"),
                    "Developer Mode / trust required", "Keep this prompt visible", "Continue", "Cancel", true, null, null])!;
            var promptTask = (Task)Call(service, "RequestPromptAsync", prompt, CancellationToken.None)!;
            RequireBeforeDeadline(elapsed, "The user-action prompt");
            Pump(TimeSpan.FromSeconds(5.5));
            Require(window.IsVisible && completed == 0 && !promptTask.IsCompleted,
                "An unresolved user-action prompt must not auto-close even at Ready." + State(window, elapsed));
            Call(window, "OnPromptPrimaryClick", window, new RoutedEventArgs());
            Pump(TimeSpan.FromMilliseconds(100));
            Require(promptTask.IsCompleted, "The prompt's existing primary action must still resolve it.");
            Report(service, mode, "Recovering");
            Pump(TimeSpan.FromMilliseconds(100));
            var recovered = Stopwatch.StartNew();
            var recoveredAtUtc = DateTime.UtcNow;
            var closedAt = TimeSpan.Zero;
            window.Closed += (_, _) => closedAt = recovered.Elapsed;
            Call(service, "Ready", mode, "Runtime test iPhone", "Recovered");
            Pump(TimeSpan.FromSeconds(4));
            RequireBeforeDeadline(recovered, "The recovered countdown check");
            Require(window.IsVisible && completed == 0 && Deadline(window) >= recoveredAtUtc.AddSeconds(5),
                "Recovery needs a fresh full five-second countdown." + State(window, recovered));
            PumpUntil(() => !window.IsVisible, CloseTimeout);
            Require(!window.IsVisible && completed == 1, "Resolved prompt and recovery must rearm auto-close.");
            Require(closedAt.TotalSeconds >= 4.95, "Recovery needs a fresh full five-second countdown.");
            Console.WriteLine("PASS: action prompt stays open; resolving it and recovery rearm the countdown.");
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    private static void TestManualCloseAndReplacement(Window owner)
    {
        var service = Activator.CreateInstance(ServiceType, nonPublic: true)!;
        var mode = Enum.Parse(ModeType, "Bluetooth");
        Call(service, "Begin", mode, "Runtime test iPhone");
        var completed = 0;
        var window = Show(owner, service, () => completed++);
        try
        {
            var elapsed = Stopwatch.StartNew();
            Call(service, "Ready", mode, "Runtime test iPhone", "Connected");
            Pump(TimeSpan.FromMilliseconds(100));
            // Queue a CloseActive from another thread, then close/reopen before it
            // executes. That queued close must keep targeting the old instance.
            Task.Run(() => typeof(ReverseControlStatusWindow)
                .GetMethod("CloseActive", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, null)).GetAwaiter().GetResult();
            RequireBeforeDeadline(elapsed, "The manual close");
            Require(window.IsVisible && completed == 0, "Bluetooth ready must wait for its countdown or a manual close.");
            Call(window, "OnCloseClick", window, new RoutedEventArgs());
            Require(!window.IsVisible && completed == 1, "Manual Bluetooth ready close must release input once.");
        }
        finally { if (window.IsVisible) window.Close(); }
        Call(service, "Begin", mode, "Runtime test iPhone");
        var replacement = Show(owner, service);
        try
        {
            Pump(TimeSpan.FromSeconds(5.5));
            Require(replacement.IsVisible && completed == 1,
                "An old countdown/queued CloseActive must not close a replacement or repeat its callback.");
            Report(service, mode, "CheckingBluetooth");
            replacement.Close();
            Pump(TimeSpan.FromMilliseconds(100));
            // Drains Apply after Dispose: no disposed-timer access or reopen.
            Require(!Application.Current.Windows.Cast<Window>().Any(w => w is ReverseControlStatusWindow),
                "Queued updates after manual close must not recreate the window.");
            Console.WriteLine("PASS: Bluetooth manual close, instance replacement and queued updates after disposal.");
        }
        finally { if (replacement.IsVisible) replacement.Close(); }
    }

    // Thrown when the scheduler delayed the test's own steps past the app's
    // real-time deadline, so the scenario's precondition never held.
    private sealed class SetupOverrunException(string message) : Exception(message);

    private static void Retry(string name, Action scenario)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                scenario();
                return;
            }
            catch (SetupOverrunException overrun) when (attempt < MaxAttempts)
            {
                Console.WriteLine($"RETRY {name} (attempt {attempt}): {overrun.Message}");
            }
        }
    }

    // Call after the deadline-sensitive step, so the step itself happened
    // within the budget. Assertions after this point apply in full.
    private static void RequireBeforeDeadline(Stopwatch sinceReady, string step)
    {
        if (sinceReady.Elapsed >= SetupBudget)
            throw new SetupOverrunException(
                $"{step} ran {sinceReady.Elapsed.TotalSeconds:F2}s after Ready, too close to the five-second deadline.");
    }

    private static string State(Window window, Stopwatch elapsed)
    {
        var model = window.DataContext;
        object? Field(string name) => model.GetType().GetField(name, Instance)!.GetValue(model);
        return $" [t={elapsed.Elapsed.TotalSeconds:F2}s, visible={window.IsVisible}, " +
            $"open={Application.Current.Windows.Cast<Window>().Contains(window)}, " +
            $"owner={window.Owner?.WindowState}/{window.Owner?.IsVisible}, armed={Field("_countdownArmed")}, " +
            $"completed={Field("_countdownElapsed")}, disposed={Field("_disposed")}, " +
            $"deadline_in={(Deadline(window) - DateTime.UtcNow).TotalSeconds:F2}s]";
    }

    private static DateTime Deadline(Window window) =>
        (DateTime)window.DataContext.GetType().GetField("_autoCloseAtUtc", Instance)!.GetValue(window.DataContext)!;

    private static Window Show(Window owner, object service, Action? completed = null)
    {
        typeof(ReverseControlStatusWindow).GetMethod("Show", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [owner, service, null, null, completed]);
        var window = Application.Current.Windows.Cast<Window>().Single(w => w is ReverseControlStatusWindow);
        Require(window.IsVisible, "The production status window must be visible before testing.");
        IgnoreDesktopInput(window);
        return window;
    }

    private static readonly HashSet<Window> InputIsolated = [];

    // Show activates the window on the interactive desktop, so a click or Enter
    // meant for another app can press Close or a prompt button mid-scenario.
    // Every action here goes through the handlers directly; swallow real input.
    private static void IgnoreDesktopInput(Window window)
    {
        if (!InputIsolated.Add(window)) return;
        var reported = false;
        void Swallow(RoutedEventArgs e)
        {
            e.Handled = true;
            if (reported) return;
            reported = true;
            Console.WriteLine($"Ignored desktop input on the status window: {e.RoutedEvent.Name}.");
        }
        window.PreviewMouseDown += (_, e) => Swallow(e);
        window.PreviewMouseUp += (_, e) => Swallow(e);
        window.PreviewKeyDown += (_, e) => Swallow(e);
        window.PreviewKeyUp += (_, e) => Swallow(e);
        window.PreviewTextInput += (_, e) => Swallow(e);
        window.PreviewTouchDown += (_, e) => Swallow(e);
        window.PreviewStylusDown += (_, e) => Swallow(e);
        window.Closed += (_, _) => InputIsolated.Remove(window);
    }

    private static void Report(object service, object mode, string stage) =>
        Call(service, "Report", mode, Enum.Parse(StageType, stage), "Runtime test iPhone", stage, true, null, 0, 0);

    private static object? Call(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, Instance)!.Invoke(target, args);

    // Pumps for at least the measured duration; under load it may return
    // late, never early.
    private static void Pump(TimeSpan duration)
    {
        var elapsed = Stopwatch.StartNew();
        PumpUntil(() => elapsed.Elapsed >= duration, TimeSpan.MaxValue);
    }

    private static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        // Background items only run while no input is pending, which lets a
        // busy desktop stretch the test's own clock. Normal still runs after
        // Apply calls that were queued before the pump started.
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (!condition() && elapsed.Elapsed < timeout) return;
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return condition();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
