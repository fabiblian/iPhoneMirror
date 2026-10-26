using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using IPhoneMirror.App.Models;

namespace IPhoneMirror.App.Windows;

// Where the preview's move and resize dots sit: a fixed place from the list,
// or a custom spot set by clicking or dragging on the small phone sketch,
// which always shows where the dot currently is.
public partial class PreviewSettingsWindow
{
    // Same edge distance as the dots on the preview, scaled to the sketch.
    private const double SketchEdgeInset = 7;
    private static readonly (PreviewDotPosition Position, string Key)[] DotChoices =
    [
        (PreviewDotPosition.TopCenter, "PreviewDotTopCenter"),
        (PreviewDotPosition.TopLeft, "PreviewDotTopLeft"),
        (PreviewDotPosition.TopRight, "PreviewDotTopRight"),
        (PreviewDotPosition.LeftCenter, "PreviewDotLeftCenter"),
        (PreviewDotPosition.RightCenter, "PreviewDotRightCenter"),
        (PreviewDotPosition.BottomLeft, "PreviewDotBottomLeft"),
        (PreviewDotPosition.BottomCenter, "PreviewDotBottomCenter"),
        (PreviewDotPosition.BottomRight, "PreviewDotBottomRight"),
        (PreviewDotPosition.Custom, "PreviewDotCustom"),
    ];

    private void BuildDotChoices()
    {
        foreach (var comboBox in new[] { MoveDotComboBox, ResizeDotComboBox })
            foreach (var (position, key) in DotChoices)
            {
                var item = new ComboBoxItem { Tag = position };
                item.SetResourceReference(ContentProperty, key);
                comboBox.Items.Add(item);
            }
        HookSketch(MoveDotCanvas, resize: false);
        HookSketch(ResizeDotCanvas, resize: true);
    }

    private void SyncDots(EverydaySettings everyday)
    {
        SelectDot(MoveDotComboBox, everyday.PreviewMoveDot);
        SelectDot(ResizeDotComboBox, everyday.PreviewResizeDot);
        PlaceMarker(MoveDotCanvas, MoveDotMarker,
            PreviewDotPositions.Fractions(everyday.PreviewMoveDot, everyday.PreviewMoveDotX, everyday.PreviewMoveDotY));
        PlaceMarker(ResizeDotCanvas, ResizeDotMarker,
            PreviewDotPositions.Fractions(everyday.PreviewResizeDot, everyday.PreviewResizeDotX, everyday.PreviewResizeDotY));
    }

    private static void SelectDot(ComboBox comboBox, PreviewDotPosition position) =>
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is PreviewDotPosition tag && tag == position);

    private static void PlaceMarker(Canvas canvas, Ellipse marker, (double X, double Y) spot)
    {
        var width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 62;
        var height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 126;
        Canvas.SetLeft(marker, SketchEdgeInset + spot.X * (width - 2 * SketchEdgeInset) - marker.Width / 2);
        Canvas.SetTop(marker, SketchEdgeInset + spot.Y * (height - 2 * SketchEdgeInset) - marker.Height / 2);
    }

    private void OnDotPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || !IsInitialized || Application.Current is not App app ||
            (sender as ComboBox)?.SelectedItem is not ComboBoxItem { Tag: PreviewDotPosition position }) return;
        var everyday = app.UpdateSettings.Everyday;
        if (ReferenceEquals(sender, MoveDotComboBox)) everyday.PreviewMoveDot = position;
        else everyday.PreviewResizeDot = position;
        app.SaveUpdateSettings();
        Resync(everyday);
    }

    // Clicking or dragging on the sketch puts the dot there (custom spot).
    // The previews follow while dragging; the setting is saved on release.
    private void HookSketch(Canvas canvas, bool resize)
    {
        canvas.SizeChanged += (_, _) =>
        {
            if (Application.Current is App app) Resync(app.UpdateSettings.Everyday);
        };
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            canvas.CaptureMouse();
            SetCustomSpot(canvas, e.GetPosition(canvas), resize, save: false);
            e.Handled = true;
        };
        canvas.MouseMove += (_, e) =>
        {
            if (canvas.IsMouseCaptured) SetCustomSpot(canvas, e.GetPosition(canvas), resize, save: false);
        };
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (!canvas.IsMouseCaptured) return;
            canvas.ReleaseMouseCapture();
            SetCustomSpot(canvas, e.GetPosition(canvas), resize, save: true);
        };
    }

    private void SetCustomSpot(Canvas canvas, Point point, bool resize, bool save)
    {
        if (Application.Current is not App app) return;
        var x = PreviewDotPositions.Clamp((point.X - SketchEdgeInset) / Math.Max(1, canvas.ActualWidth - 2 * SketchEdgeInset));
        var y = PreviewDotPositions.Clamp((point.Y - SketchEdgeInset) / Math.Max(1, canvas.ActualHeight - 2 * SketchEdgeInset));
        var everyday = app.UpdateSettings.Everyday;
        if (resize)
        {
            everyday.PreviewResizeDot = PreviewDotPosition.Custom;
            (everyday.PreviewResizeDotX, everyday.PreviewResizeDotY) = (x, y);
        }
        else
        {
            everyday.PreviewMoveDot = PreviewDotPosition.Custom;
            (everyday.PreviewMoveDotX, everyday.PreviewMoveDotY) = (x, y);
        }
        if (save) app.SaveUpdateSettings();
        Resync(everyday);
    }

    private void Resync(EverydaySettings everyday)
    {
        _syncing = true;
        try { SyncDots(everyday); }
        finally { _syncing = false; }
    }
}
