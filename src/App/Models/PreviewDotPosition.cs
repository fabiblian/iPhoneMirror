namespace IPhoneMirror.App.Models;

/// <summary>Where a dot over the preview sits: a fixed place on the edge or a custom spot.</summary>
internal enum PreviewDotPosition
{
    TopCenter = 0,
    TopLeft = 1,
    TopRight = 2,
    LeftCenter = 3,
    RightCenter = 4,
    BottomLeft = 5,
    BottomCenter = 6,
    BottomRight = 7,
    Custom = 8,
}

internal static class PreviewDotPositions
{
    /// <summary>
    /// The dot's spot as fractions of the preview (0 = left/top, 1 = right/bottom).
    /// Custom uses the stored fractions.
    /// </summary>
    internal static (double X, double Y) Fractions(PreviewDotPosition position, double customX, double customY) =>
        position switch
        {
            PreviewDotPosition.TopLeft => (0, 0),
            PreviewDotPosition.TopCenter => (0.5, 0),
            PreviewDotPosition.TopRight => (1, 0),
            PreviewDotPosition.LeftCenter => (0, 0.5),
            PreviewDotPosition.RightCenter => (1, 0.5),
            PreviewDotPosition.BottomLeft => (0, 1),
            PreviewDotPosition.BottomCenter => (0.5, 1),
            PreviewDotPosition.BottomRight => (1, 1),
            _ => (Clamp(customX), Clamp(customY)),
        };

    /// <summary>-1, 0 or 1 per axis: which side of the preview a spot is nearest to.</summary>
    internal static (int Horizontal, int Vertical) Sides(double x, double y) => (Side(x), Side(y));

    private static int Side(double fraction) => fraction < 1.0 / 3 ? -1 : fraction > 2.0 / 3 ? 1 : 0;

    internal static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
}
