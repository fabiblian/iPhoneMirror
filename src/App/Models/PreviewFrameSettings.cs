using System.Globalization;

namespace IPhoneMirror.App.Models;

internal enum PreviewHomeButtonStyle
{
    Off,
    /// <summary>A taller bottom frame band with a centred home pill.</summary>
    Bar,
    /// <summary>A small home pill inside the regular bottom frame.</summary>
    Pill,
}

internal enum PreviewHomeButtonPosition
{
    Left,
    Center,
    Right,
}

/// <summary>Physical-pixel frame thickness on each side of the iPhone image.</summary>
internal readonly record struct PreviewFrameInsets(int Left, int Top, int Right, int Bottom)
{
    internal int Horizontal => Left + Right;
    internal int Vertical => Top + Bottom;
    internal bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}

/// <summary>Physical-pixel rectangle relative to the preview window's client origin.</summary>
internal readonly record struct PreviewPixelRect(int X, int Y, int Width, int Height)
{
    internal bool IsEmpty => Width <= 0 || Height <= 0;
    internal bool Contains(int x, int y) =>
        !IsEmpty && x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>Frame insets plus the home button's clickable area and visible pill.</summary>
internal readonly record struct PreviewFrameLayout(PreviewFrameInsets Insets,
    PreviewPixelRect HomeHitArea, PreviewPixelRect HomePill)
{
    internal static PreviewFrameLayout None => default;
}

/// <summary>
/// Settings for independent preview windows: the outline drawn around the
/// iPhone image (to move and resize the borderless window, even while touch
/// control routes clicks on the image to the phone), the home button in that
/// outline, and whether touch control starts automatically.
/// </summary>
internal sealed class PreviewFrameSettings
{
    internal const double MinThicknessDips = 4;
    internal const double MaxThicknessDips = 48;
    internal const double DefaultThicknessDips = 12;
    internal const string DefaultColor = "#2B2B2B";
    internal const double DefaultOpacity = 1.0;
    internal const double HomeBarHeightDips = 28;

    public bool Enabled { get; set; } = true;
    public double ThicknessDips { get; set; } = DefaultThicknessDips;
    public string Color { get; set; } = DefaultColor;
    /// <summary>Use the app's accent colour; wins over <see cref="AutoColor"/> and <see cref="Color"/>.</summary>
    public bool UseAccentColor { get; set; } = true;
    /// <summary>Follow the colour at the edge of the phone image instead of <see cref="Color"/>.</summary>
    public bool AutoColor { get; set; }
    public double Opacity { get; set; } = DefaultOpacity;
    public PreviewHomeButtonStyle HomeButton { get; set; } = PreviewHomeButtonStyle.Bar;
    public PreviewHomeButtonPosition HomeButtonPosition { get; set; } = PreviewHomeButtonPosition.Center;
    /// <summary>Start wired (else wireless) touch control when a framed preview opens.</summary>
    public bool AutoStartControl { get; set; } = true;

    internal PreviewFrameSettings Clone() => new()
    {
        Enabled = Enabled,
        ThicknessDips = ThicknessDips,
        Color = Color,
        UseAccentColor = UseAccentColor,
        AutoColor = AutoColor,
        Opacity = Opacity,
        HomeButton = HomeButton,
        HomeButtonPosition = HomeButtonPosition,
        AutoStartControl = AutoStartControl,
    };

    /// <summary>Returns a copy with every value inside its supported range.</summary>
    internal PreviewFrameSettings Normalized() => new()
    {
        Enabled = Enabled,
        ThicknessDips = double.IsFinite(ThicknessDips)
            ? Math.Clamp(Math.Round(ThicknessDips), MinThicknessDips, MaxThicknessDips)
            : DefaultThicknessDips,
        Color = TryParseColor(Color, out var rgb) ? FormatColor(rgb) : DefaultColor,
        UseAccentColor = UseAccentColor,
        AutoColor = AutoColor,
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.0, 1.0) : DefaultOpacity,
        HomeButton = Enum.IsDefined(HomeButton) ? HomeButton : PreviewHomeButtonStyle.Bar,
        HomeButtonPosition = Enum.IsDefined(HomeButtonPosition)
            ? HomeButtonPosition : PreviewHomeButtonPosition.Center,
        AutoStartControl = AutoStartControl,
    };

    internal bool IsEquivalentTo(PreviewFrameSettings other) =>
        Enabled == other.Enabled &&
        ThicknessDips == other.ThicknessDips &&
        string.Equals(Color, other.Color, StringComparison.OrdinalIgnoreCase) &&
        UseAccentColor == other.UseAccentColor &&
        AutoColor == other.AutoColor &&
        Opacity == other.Opacity &&
        HomeButton == other.HomeButton &&
        HomeButtonPosition == other.HomeButtonPosition &&
        AutoStartControl == other.AutoStartControl;

    /// <summary>Whether the renderer samples the phone image edge for the colour.</summary>
    internal bool FollowsImageEdge => AutoColor && !UseAccentColor;

    /// <summary>
    /// Straight-alpha 0xAARRGGBB for the native renderer. <paramref name="accentRgb"/>
    /// (0xRRGGBB) is used when <see cref="UseAccentColor"/> is set.
    /// </summary>
    internal uint ToArgb(uint? accentRgb = null)
    {
        var rgb = UseAccentColor && accentRgb is { } accent ? accent & 0xFFFFFFu
            : TryParseColor(Color, out var parsed) ? parsed : 0x2B2B2Bu;
        var alpha = (uint)Math.Round(Math.Clamp(double.IsFinite(Opacity) ? Opacity : 1.0, 0, 1) * 255);
        return (alpha << 24) | rgb;
    }

    /// <summary>Thickness in physical pixels for a window DPI (96 = 100 %).</summary>
    internal int ThicknessPixels(uint dpi)
    {
        if (!Enabled) return 0;
        return Math.Max(1, (int)Math.Round(Normalized().ThicknessDips * Scale(dpi)));
    }

    /// <summary>
    /// Frame insets and home button geometry for a client area of
    /// <paramref name="clientWidth"/> x <paramref name="clientHeight"/> pixels.
    /// </summary>
    internal PreviewFrameLayout Layout(uint dpi, int clientWidth, int clientHeight)
    {
        var settings = Normalized();
        var thickness = settings.ThicknessPixels(dpi);
        if (thickness == 0) return PreviewFrameLayout.None;
        var scale = Scale(dpi);
        var bar = settings.HomeButton == PreviewHomeButtonStyle.Bar
            ? (int)Math.Round(HomeBarHeightDips * scale) : 0;
        var insets = new PreviewFrameInsets(thickness, thickness, thickness, thickness + bar);
        if (settings.HomeButton == PreviewHomeButtonStyle.Off || clientWidth <= 0 || clientHeight <= 0)
            return new(insets, default, default);

        var contentWidth = Math.Max(0, clientWidth - insets.Horizontal);
        var isBar = settings.HomeButton == PreviewHomeButtonStyle.Bar;
        var pillWidth = Math.Min(contentWidth, (int)Math.Round((isBar ? 120 : 80) * scale));
        var pillHeight = isBar
            ? Math.Max(2, (int)Math.Round(6 * scale))
            : Math.Max(2, Math.Min((int)Math.Round(4 * scale), thickness / 2));
        var hitWidth = Math.Min(contentWidth, Math.Max(pillWidth, (int)Math.Round((isBar ? 160 : 120) * scale)));
        var hitHeight = insets.Bottom;
        var centerX = settings.HomeButtonPosition switch
        {
            PreviewHomeButtonPosition.Left => insets.Left + hitWidth / 2,
            PreviewHomeButtonPosition.Right => clientWidth - insets.Right - hitWidth / 2,
            _ => clientWidth / 2,
        };
        var centerY = clientHeight - insets.Bottom / 2;
        var hit = new PreviewPixelRect(centerX - hitWidth / 2, clientHeight - hitHeight, hitWidth, hitHeight);
        var pill = new PreviewPixelRect(centerX - pillWidth / 2, centerY - pillHeight / 2,
            pillWidth, pillHeight);
        return new(insets, hit, pill);
    }

    private static double Scale(uint dpi) => dpi == 0 ? 1.0 : dpi / 96.0;

    /// <summary>Accepts "#RRGGBB" or "RRGGBB" (case-insensitive).</summary>
    internal static bool TryParseColor(string? text, out uint rgb)
    {
        rgb = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim();
        if (value.StartsWith('#')) value = value[1..];
        return value.Length == 6 &&
            uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
    }

    internal static string FormatColor(uint rgb) =>
        "#" + (rgb & 0xFFFFFFu).ToString("X6", CultureInfo.InvariantCulture);
}
