using IPhoneMirror.App.Models;

namespace IPhoneMirror.App.Services;

/// <summary>
/// WM_NCHITTEST classification for the borderless device preview. The outer
/// part of the frame resizes, the home button area is a separate target, the
/// rest of the frame moves the window, and the iPhone image is either a client
/// area (touch control) or a move handle. Coordinates are physical screen
/// pixels; the home button rectangle is relative to the window origin.
/// </summary>
internal static class PreviewFrameHitTest
{
    internal const int HtClient = 1;
    internal const int HtCaption = 2;
    internal const int HtLeft = 10;
    internal const int HtRight = 11;
    internal const int HtTop = 12;
    internal const int HtTopLeft = 13;
    internal const int HtTopRight = 14;
    internal const int HtBottom = 15;
    internal const int HtBottomLeft = 16;
    internal const int HtBottomRight = 17;
    // HTOBJECT: DefWindowProc gives it no behaviour, so the window handles it.
    internal const int HtHomeButton = 19;

    /// <param name="defaultResizeBorder">Resize band without a frame (DPI scaled).</param>
    /// <param name="insets">Frame thickness per side in pixels; empty when hidden.</param>
    /// <param name="homeButton">Clickable home area relative to the window; empty when none.</param>
    /// <param name="contentIsClient">True while clicks on the image control the phone.</param>
    internal static int Classify(int x, int y, int left, int top, int right, int bottom,
        int defaultResizeBorder, PreviewFrameInsets insets, PreviewPixelRect homeButton,
        bool contentIsClient)
    {
        // With a frame, the outer half of its regular thickness resizes and
        // the rest moves; the corners get a longer grab span so diagonal
        // resizing stays easy. The bottom may be taller (home bar).
        var thickness = Math.Min(insets.Left, Math.Min(insets.Top, insets.Right));
        var border = thickness > 0
            ? Math.Max(4, thickness / 2)
            : Math.Max(1, defaultResizeBorder);
        var cornerSpan = Math.Max(border, thickness * 2);

        var nearLeft = x < left + border;
        var nearRight = x >= right - border;
        var nearTop = y < top + border;
        var nearBottom = y >= bottom - border;
        var inLeftSpan = x < left + cornerSpan;
        var inRightSpan = x >= right - cornerSpan;
        var inTopSpan = y < top + cornerSpan;
        var inBottomSpan = y >= bottom - cornerSpan;

        if ((nearTop && inLeftSpan) || (nearLeft && inTopSpan)) return HtTopLeft;
        if ((nearTop && inRightSpan) || (nearRight && inTopSpan)) return HtTopRight;
        if ((nearBottom && inLeftSpan) || (nearLeft && inBottomSpan)) return HtBottomLeft;
        if ((nearBottom && inRightSpan) || (nearRight && inBottomSpan)) return HtBottomRight;
        if (nearLeft) return HtLeft;
        if (nearRight) return HtRight;
        if (nearTop) return HtTop;
        if (nearBottom) return HtBottom;

        if (homeButton.Contains(x - left, y - top)) return HtHomeButton;

        var inContent = x >= left + insets.Left && x < right - insets.Right &&
            y >= top + insets.Top && y < bottom - insets.Bottom;
        if (inContent && contentIsClient) return HtClient;
        return HtCaption;
    }
}
