using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IPhoneMirror.App.Controls;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.Updater;

namespace IPhoneMirror.App.Runtime.Tests;

internal static partial class Program
{
    // The info tip beside a settings label: a long label wraps and keeps the
    // tip in view, a click pins the text as a popover that Esc or a second
    // click closes, and screen readers get a name plus the text as help.
    private static int RunInfoTipTests(string output)
    {
        Directory.CreateDirectory(output);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new App { IsUiPreviewMode = true, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        DisplayLanguage("en-US");
        const string text = "Scrolls like a swipe on the iPhone. Speed is the Scroll speed in the mouse settings.";
        var tip = new InfoTip { Text = text };
        var label = new CheckBox { Content = "Scroll with the wheel during wired and wireless control" };
        var row = new InfoRow();
        row.Children.Add(label);
        row.Children.Add(tip);
        var root = new Border { Width = 240, Padding = new Thickness(12), Child = row };
        root.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        var host = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = root,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        var click = typeof(InfoTip).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            host.Show();
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                ThemeService.Apply(theme, AppColorTheme.Default, null, UiStyle.Modern);
                AdvanceDispatcher(TimeSpan.FromMilliseconds(20));
                host.UpdateLayout();
                var content = root.ActualWidth - root.Padding.Left - root.Padding.Right;
                var tipBounds = tip.TransformToAncestor(row).TransformBounds(new Rect(tip.RenderSize));
                InteractionAssert(tipBounds.Right <= content + 0.5, $"{theme}: the info tip is pushed out of its row.");
                InteractionAssert(label.ActualWidth + tip.ActualWidth + tip.Margin.Left <= content + 0.5,
                    $"{theme}: a long label must wrap beside its info tip.");
                InteractionAssert(AutomationProperties.GetName(tip) == "More info" &&
                    AutomationProperties.GetHelpText(tip) == text, $"{theme}: the info tip lacks its accessible name or help.");
                SaveWindowRender(root, Path.Combine(output, $"{theme}-row.png"));

                click.Invoke(tip, null);
                AdvanceDispatcher(TimeSpan.FromMilliseconds(60));
                var popover = (Popup)tip.Template.FindName("PART_Popover", tip);
                InteractionAssert(tip.IsPopoverOpen && popover.IsOpen, $"{theme}: a click must pin the popover.");
                InteractionAssert(!ToolTipService.GetIsEnabled(tip), $"{theme}: the tooltip must stay hidden under the popover.");
                var surface = (FrameworkElement)popover.Child;
                surface.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth + 20),
                    (int)Math.Ceiling(surface.ActualHeight + 20), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(surface);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(output, $"{theme}-popover.png"))) encoder.Save(stream);

                tip.Focus();
                tip.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(tip)!, 0, Key.Escape)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                AdvanceDispatcher(TimeSpan.FromMilliseconds(20));
                InteractionAssert(!tip.IsPopoverOpen && !popover.IsOpen, $"{theme}: Esc must close the popover.");
                InteractionAssert(ToolTipService.GetIsEnabled(tip), $"{theme}: the tooltip must return after the popover closes.");

                click.Invoke(tip, null);
                click.Invoke(tip, null);
                AdvanceDispatcher(TimeSpan.FromMilliseconds(20));
                InteractionAssert(!tip.IsPopoverOpen, $"{theme}: a second click must close the popover.");
            }
            Console.WriteLine("Info tip: wrapping, accessible text, popover, Esc and toggle passed in light and dark themes.");
        }
        finally
        {
            host.Close();
            app.Shutdown();
        }
        return 0;
    }
}
