namespace IPhoneMirror.App.Services;

/// <summary>
/// Arrow keys as one swipe through the middle of the screen, in normalized
/// screen coordinates (y grows downwards). The arrow names the content
/// direction: ↓ moves the finger up (next video), → moves it left (next page).
/// </summary>
internal static class ArrowKeySwipe
{
    internal const double Distance = 0.35;
    internal const int DurationMs = 250;

    internal static bool IsArrow(byte usage) => usage is >= 0x4F and <= 0x52;

    internal static bool TryGetSwipe(byte usage, out (double X, double Y) start, out (double X, double Y) end)
    {
        const double half = Distance / 2;
        (start, end) = usage switch
        {
            0x51 => ((0.5, 0.5 + half), (0.5, 0.5 - half)), // ↓ finger up
            0x52 => ((0.5, 0.5 - half), (0.5, 0.5 + half)), // ↑ finger down
            0x4F => ((0.5 + half, 0.5), (0.5 - half, 0.5)), // → finger left
            0x50 => ((0.5 - half, 0.5), (0.5 + half, 0.5)), // ← finger right
            _ => ((0d, 0d), (0d, 0d)),
        };
        return IsArrow(usage);
    }
}
