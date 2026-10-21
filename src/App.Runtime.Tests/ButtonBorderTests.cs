using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IPhoneMirror.App.Services;
using IPhoneMirror.App.Updater;
using Button = System.Windows.Controls.Button;

namespace IPhoneMirror.App.Runtime.Tests;

internal static partial class Program
{
    // Render the real toolbar button with keyboard focus in an unconstrained
    // adorner layer: any focus pixels outside its bounds would be clipped by
    // the toolbar's ScrollViewer, leaving the fragments from the regression.
    private static int RunButtonBorderTests(string output)
    {
        Directory.CreateDirectory(output);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new App { IsUiPreviewMode = true, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var source = CreateWorkspaceTestWindow(app, includeNativePreview: false);
        var button = (Button)source.FindName("QuickMediaOutputButton");
        var parent = (Panel)button.Parent;
        var index = parent.Children.IndexOf(button);
        var originalStyle = button.Style;
        parent.Children.Remove(button);
        button.Width = 200;
        button.Height = 36;
        button.Margin = new Thickness(24);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.VerticalAlignment = VerticalAlignment.Top;
        button.IsEnabled = true;
        var panel = new Grid { Width = 260, Height = 96, Focusable = true };
        panel.SetResourceReference(Panel.BackgroundProperty, "AppBackgroundBrush");
        panel.Children.Add(button);
        var decorator = new AdornerDecorator { Child = panel };
        var host = new Window { Width = 280, Height = 140, Content = decorator,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, UseLayoutRounding = true, SnapsToDevicePixels = true };
        host.Resources.MergedDictionaries.Add(source.Resources);
        var alwaysFocus = typeof(KeyboardNavigation).GetProperty("AlwaysShowFocusVisual",
            BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Focus test override unavailable.");
        var originalFocus = alwaysFocus.GetValue(null);
        alwaysFocus.SetValue(null, true);
        var count = 0;
        try
        {
            DisplayLanguage("en-US");
            host.Show();
            host.Activate();
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            foreach (var shape in new[] { UiStyle.Playful, UiStyle.Modern, UiStyle.Minimal })
            foreach (var style in new[] { "PreviewToolButton", "SecondaryButton", "PrimaryButton" })
            {
                ThemeService.Apply(theme, AppColorTheme.Midnight, null, shape);
                button.Style = style == "PreviewToolButton" ? originalStyle : (Style)source.FindResource(style);
                Keyboard.ClearFocus();
                AdvanceDispatcher(TimeSpan.FromMilliseconds(20));
                host.UpdateLayout();
                var before = button.RenderSize;
                var unfocused = new Dictionary<double, byte[]>();
                foreach (var scale in new[] { 1.25, 1.0, 1.5 })
                {
                    var normal = new RenderTargetBitmap((int)Math.Ceiling(decorator.ActualWidth * scale),
                        (int)Math.Ceiling(decorator.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    normal.Render(decorator);
                    var normalPixels = new byte[normal.PixelWidth * normal.PixelHeight * 4];
                    normal.CopyPixels(normalPixels, normal.PixelWidth * 4, 0);
                    unfocused.Add(scale, normalPixels);
                }
                Keyboard.Focus(button);
                AdvanceDispatcher(TimeSpan.FromMilliseconds(40));
                host.UpdateLayout();
                InteractionAssert(button.IsKeyboardFocused, "The keyboard-focus fixture is not focused.");
                InteractionAssert(button.RenderSize == before, "Focus must not move or resize a button.");
                var bounds = button.TransformToAncestor(decorator).TransformBounds(new Rect(button.RenderSize));
                foreach (var scale in new[] { 1.25, 1.0, 1.5 })
                {
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(decorator.ActualWidth * scale),
                        (int)Math.Ceiling(decorator.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bitmap.Render(decorator);
                    var name = $"{theme}-{shape}-{style}-{scale * 100:0}";
                    var encoder = new PngBitmapEncoder();
                    var crop = new Int32Rect((int)Math.Floor((bounds.Left - 8) * scale),
                        (int)Math.Floor((bounds.Top - 8) * scale),
                        (int)Math.Ceiling((bounds.Width + 16) * scale),
                        (int)Math.Ceiling((bounds.Height + 16) * scale));
                    encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bitmap, crop)));
                    using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
                    var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    var normalPixels = unfocused[scale];
                    var outside = 0;
                    var inside = 0;
                    for (var y = 0; y < bitmap.PixelHeight; y++)
                    for (var x = 0; x < bitmap.PixelWidth; x++)
                    {
                        var offset = (y * bitmap.PixelWidth + x) * 4;
                        // Compare actual focus changes, not a particular color:
                        // a primary button's focus color can match the canvas.
                        if (Math.Abs(pixels[offset] - normalPixels[offset]) < 12 &&
                            Math.Abs(pixels[offset + 1] - normalPixels[offset + 1]) < 12 &&
                            Math.Abs(pixels[offset + 2] - normalPixels[offset + 2]) < 12 &&
                            Math.Abs(pixels[offset + 3] - normalPixels[offset + 3]) < 12) continue;
                        var point = new Point((x + 0.5) / scale, (y + 0.5) / scale);
                        var allowed = bounds;
                        allowed.Inflate(1 / scale, 1 / scale); // antialiasing tolerance: one physical pixel
                        if (allowed.Contains(point)) inside++;
                        else outside++;
                    }
                    Console.WriteLine($"{name}: focus pixels inside={inside}, outside={outside}");
                    InteractionAssert(inside > 20, name + ": keyboard focus must remain clearly visible.");
                    InteractionAssert(outside == 0, name + ": focus spills beyond the button and is clipped by toolbars.");
                    count++;
                }
            }
            Console.WriteLine($"Button borders: {count} theme, shape, style and scaling combinations passed.");
        }
        finally
        {
            alwaysFocus.SetValue(null, originalFocus);
            host.Close();
            panel.Children.Remove(button);
            parent.Children.Insert(index, button);
            CloseWorkspaceTestWindow(source);
            app.Shutdown();
        }
        return 0;
    }
}
