using System.Globalization;
using System.Windows;
using System.Windows.Media;
using IPhoneMirror.App.Updater;

namespace IPhoneMirror.App.Services;

/// <summary>
/// Colour presets, accent colours and UI styles layered over the base
/// Light/Dark theme dictionaries. Everything here is pure: it maps the chosen
/// appearance to resource values; <see cref="ThemeService"/> installs them.
/// </summary>
internal static class ThemePalette
{
    internal sealed record Preset(Color Background, Color Surface, Color Raised, Color Sidebar,
        Color Border, Color SoftBorder, Color Text, Color MutedText, Color Accent,
        Color Success, Color Warning, Color Error);

    internal static readonly Color DarkAccent = Rgb(0x69B1F8);
    internal static readonly Color LightAccent = Rgb(0x0F6CBD);
    private static readonly Color DarkBackground = Rgb(0x1F1F1F);
    private static readonly Color LightBackground = Rgb(0xF3F3F5);

    internal static IReadOnlyDictionary<AppColorTheme, Preset> Presets { get; } =
        new Dictionary<AppColorTheme, Preset>
        {
            [AppColorTheme.Midnight] = new(Rgb(0x0B1020), Rgb(0x121A2E), Rgb(0x19233C), Rgb(0x0D1426),
                Argb(0x2E8CAAFF), Argb(0x178CAAFF), Rgb(0xE8EDF9), Rgb(0x9AA6C4), Rgb(0x5B8CFF),
                Rgb(0x5FD39A), Rgb(0xFFC66B), Rgb(0xFF7A8A)),
            [AppColorTheme.Nord] = new(Rgb(0x2E3440), Rgb(0x3B4252), Rgb(0x434C5E), Rgb(0x2A303B),
                Argb(0x29D8DEE9), Argb(0x14D8DEE9), Rgb(0xECEFF4), Rgb(0xB0B8C7), Rgb(0x88C0D0),
                Rgb(0xA3BE8C), Rgb(0xEBCB8B), Rgb(0xD08770)),
            [AppColorTheme.Sunset] = new(Rgb(0x1D1418), Rgb(0x281B20), Rgb(0x33232A), Rgb(0x181014),
                Argb(0x29FFBEAA), Argb(0x14FFBEAA), Rgb(0xFBEEE8), Rgb(0xC9AAA3), Rgb(0xFF8A5C),
                Rgb(0x7FD1A1), Rgb(0xFFD166), Rgb(0xFF6B81)),
            [AppColorTheme.Forest] = new(Rgb(0x111A15), Rgb(0x18241E), Rgb(0x1F2D26), Rgb(0x0E1612),
                Argb(0x26AAE6C8), Argb(0x14AAE6C8), Rgb(0xE6F2EA), Rgb(0x9FBAA9), Rgb(0x5FD3A5),
                Rgb(0x8BDC7A), Rgb(0xE9C46A), Rgb(0xF28482)),
        };

    /// <summary>Accent presets offered beside the theme's own accent.</summary>
    internal static IReadOnlyList<(string Key, string Hex)> AccentChoices { get; } =
    [
        ("AccentBlue", "#3B82F6"), ("AccentViolet", "#7C3AED"), ("AccentGreen", "#22C55E"),
        ("AccentTeal", "#14B8A6"), ("AccentOrange", "#F97316"), ("AccentPink", "#EC4899"),
        ("AccentRed", "#EF4444"), ("AccentGraphite", "#8A8F98"),
    ];

    /// <summary>Upper-case #RRGGBB, or empty for anything that is not a colour.</summary>
    internal static string NormalizeHex(string? value) =>
        TryParseHex(value, out var color) ? ToHex(color) : "";

    internal static bool TryParseHex(string? value, out Color color)
    {
        color = default;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (text[0] == '#') text = text[1..];
        if (text.Length == 3) text = string.Concat(text.Select(c => new string(c, 2)));
        if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Rgb(rgb);
        return true;
    }

    internal static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Whether a preset applies: presets are dark palettes.</summary>
    internal static AppColorTheme EffectiveColorTheme(AppColorTheme colorTheme, bool isDark) =>
        isDark && Presets.ContainsKey(colorTheme) ? colorTheme : AppColorTheme.Default;

    /// <summary>
    /// The accent actually shown: the chosen one (or the theme's own), lifted
    /// or deepened until it stays visible against the background.
    /// </summary>
    internal static Color EffectiveAccent(AppColorTheme colorTheme, string? accentHex, bool isDark)
    {
        var preset = Presets.GetValueOrDefault(EffectiveColorTheme(colorTheme, isDark));
        var background = preset?.Background ?? (isDark ? DarkBackground : LightBackground);
        var accent = TryParseHex(accentHex, out var chosen) ? chosen
            : preset?.Accent ?? (isDark ? DarkAccent : LightAccent);
        var toward = isDark ? Colors.White : Colors.Black;
        for (var step = 0; step < 8 && Contrast(accent, background) < 3.0; ++step)
            accent = Mix(accent, toward, 0.18);
        // Text on the accent (buttons, selected swatches) must stay readable too.
        var onAccent = OnColor(accent);
        var away = onAccent == Colors.White ? Colors.Black : Colors.White;
        for (var step = 0; step < 8 && Contrast(onAccent, accent) < 4.5; ++step)
            accent = Mix(accent, away, 0.06);
        return accent;
    }

    /// <summary>
    /// Resource values that replace the base theme's entries. Empty when the
    /// default palette, the theme accent and the modern style are selected,
    /// so the app keeps its original look exactly.
    /// </summary>
    internal static Dictionary<string, object> BuildOverrides(bool isDark,
        AppColorTheme colorTheme, string? accentHex, UiStyle style)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        var effectiveTheme = EffectiveColorTheme(colorTheme, isDark);
        var customAccent = TryParseHex(accentHex, out _);
        var accent = EffectiveAccent(colorTheme, accentHex, isDark);
        if (Presets.TryGetValue(effectiveTheme, out var preset)) AddPreset(values, preset);
        if (customAccent || effectiveTheme != AppColorTheme.Default) AddAccent(values, accent, isDark);
        AddStyle(values, style, accent, preset?.Text ?? (isDark ? Colors.White : Colors.Black));
        return values;
    }

    private static void AddPreset(Dictionary<string, object> values, Preset p)
    {
        values["ThemeBackgroundColor"] = p.Background;
        values["ThemeSurfaceColor"] = WithAlpha(p.Surface, 0xB3);
        values["ThemeRaisedColor"] = WithAlpha(p.Raised, 0xD1);
        values["ThemeSidebarColor"] = WithAlpha(p.Sidebar, 0xC4);
        values["ThemeBorderColor"] = p.Border;
        values["ThemeSoftBorderColor"] = p.SoftBorder;
        values["ThemeTextColor"] = p.Text;
        values["ThemeMutedTextColor"] = p.MutedText;
        foreach (var key in new[] { "BackgroundBrush", "AppBackgroundBrush", "AppBackground", "WindowBackgroundBrush" })
            values[key] = Brush(p.Background);
        values["SidebarBrush"] = Brush(WithAlpha(p.Sidebar, 0xC4));
        values["PanelBrush"] = values["PanelBackground"] = Brush(WithAlpha(p.Surface, 0xB3));
        values["PanelAltBrush"] = values["PanelRaisedBrush"] = values["RaisedBackground"] =
            Brush(WithAlpha(p.Raised, 0xD1));
        values["CardBrush"] = Brush(WithAlpha(p.Raised, 0xA6));
        values["CardHoverBrush"] = Brush(WithAlpha(Mix(p.Raised, Colors.White, 0.04), 0xC4));
        values["DialogSurfaceBrush"] = Brush(WithAlpha(p.Surface, 0xF2));
        values["ComboPopupBrush"] = Brush(WithAlpha(p.Raised, 0xF2));
        values["BorderBrush"] = Brush(p.Border);
        values["BorderSoftBrush"] = Brush(p.SoftBorder);
        values["TextBrush"] = values["SelectedTextBrush"] = Brush(p.Text);
        values["MutedTextBrush"] = values["MutedBrush"] = values["DisabledTextBrush"] =
            values["PrimaryActionDisabledTextBrush"] = Brush(p.MutedText);
        values["PrimaryActionDisabledBrush"] = Brush(Mix(p.Raised, Colors.White, 0.08));
        values["OverlayBrush"] = Brush(WithAlpha(p.Background, 0xD9));
        values["MediaOverlayBrush"] = Brush(WithAlpha(p.Surface, 0xE6));
        values["PreviewChromeBrush"] = Brush(WithAlpha(Mix(p.Background, Colors.Black, 0.55), 0xEE));
        values["PreviewPanelAltBrush"] = Brush(Mix(p.Background, Colors.Black, 0.4));
        values["PreviewBorderBrush"] = Brush(Mix(p.Raised, Colors.White, 0.1));
        values["SuccessBrush"] = values["StatusAppliedBrush"] = Brush(p.Success);
        values["WarningBrush"] = values["StatusPendingBrush"] = Brush(p.Warning);
        values["ErrorBrush"] = values["StatusFailedBrush"] = Brush(p.Error);
        values["SuccessSurfaceBrush"] = Brush(WithAlpha(p.Success, 0x22));
        values["WarningSurfaceBrush"] = Brush(WithAlpha(p.Warning, 0x22));
        values["ErrorSurfaceBrush"] = Brush(WithAlpha(p.Error, 0x22));
    }

    private static void AddAccent(Dictionary<string, object> values, Color accent, bool isDark)
    {
        var lighter = isDark ? Colors.White : Colors.Black;
        var onAccent = OnColor(accent);
        values["ThemeAccentColor"] = accent;
        values["ThemeAccentHoverColor"] = Mix(accent, lighter, 0.14);
        values["ThemeOnAccentColor"] = onAccent;
        values["AccentBrush"] = values["FocusStrokeBrush"] = values["InfoBrush"] =
            values["MediaPlayerFocusBrush"] = values["WorkspaceItemSelectedBorderBrush"] = Brush(accent);
        values["AccentHoverBrush"] = Brush(Mix(accent, lighter, 0.14));
        values["OnAccentBrush"] = values["PrimaryActionTextBrush"] = values["AboutCheckUpdatesTextBrush"] =
            values["PrimaryActionFocusBrush"] = Brush(onAccent);
        values["InfoSurfaceBrush"] = Brush(WithAlpha(accent, 0x22));
        values["WorkspaceItemSelectedBrush"] = Brush(WithAlpha(accent, 0x28));
        values["TextSelectionBrush"] = Brush(WithAlpha(accent, 0x80));
        values["PrimaryActionBrush"] = values["SliderThumbBorderBrush"] = Brush(accent);
        values["PrimaryActionHoverBrush"] = Brush(Mix(accent, lighter, 0.12));
        values["PrimaryActionPressedBrush"] = Brush(Mix(accent, Colors.Black, 0.15));
        values["PrimaryActionBorderBrush"] = Brush(isDark ? Mix(accent, Colors.White, 0.35) : Mix(accent, Colors.Black, 0.2));
        values["PrimaryActionHoverBorderBrush"] = Brush(isDark ? Mix(accent, Colors.White, 0.55) : Mix(accent, Colors.Black, 0.3));
        values["PrimaryActionPressedBorderBrush"] = Brush(isDark ? Mix(accent, Colors.White, 0.2) : Mix(accent, Colors.Black, 0.1));
        // The start button keeps white text, so its fill must stay dark enough.
        var start = accent;
        for (var step = 0; step < 10 && Contrast(start, Colors.White) < 4.5; ++step)
            start = Mix(start, Colors.Black, 0.12);
        values["CaptureStartBrush"] = Brush(start);
        values["CaptureStartHoverBrush"] = Brush(Mix(start, Colors.Black, 0.1));
        values["CaptureStartBorderBrush"] = Brush(Mix(accent, Colors.White, 0.4));
    }

    private static void AddStyle(Dictionary<string, object> values, UiStyle style, Color accent, Color text)
    {
        switch (style)
        {
            case UiStyle.Playful:
                values["ControlCornerRadius"] = new CornerRadius(10);
                values["ButtonCornerRadius"] = new CornerRadius(16);
                values["CardCornerRadius"] = new CornerRadius(22);
                values["PopupCornerRadius"] = new CornerRadius(16);
                values["DialogCornerRadius"] = new CornerRadius(24);
                values["WorkspaceCardBrush"] = Freeze(new LinearGradientBrush(
                    WithAlpha(accent, 0x1C), WithAlpha(accent, 0x06), 90));
                values["BorderSoftBrush"] = Brush(WithAlpha(accent, 0x38));
                values["WorkspaceItemSelectedBrush"] = Brush(WithAlpha(accent, 0x3A));
                values["CardShadowColor"] = accent;
                break;
            case UiStyle.Minimal:
                values["ControlCornerRadius"] = new CornerRadius(4);
                values["ButtonCornerRadius"] = new CornerRadius(6);
                values["CardCornerRadius"] = new CornerRadius(6);
                values["PopupCornerRadius"] = new CornerRadius(6);
                values["DialogCornerRadius"] = new CornerRadius(8);
                values["CardBrush"] = Brush(Colors.Transparent);
                values["BorderSoftBrush"] = Brush(WithAlpha(text, 0x10));
                values["CardShadowColor"] = Colors.Transparent;
                break;
        }
    }

    internal static Color OnColor(Color fill) =>
        Contrast(fill, Colors.White) >= Contrast(fill, Rgb(0x0F1419)) ? Colors.White : Rgb(0x0F1419);

    internal static Color Mix(Color from, Color to, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * Math.Clamp(amount, 0, 1));
        return Color.FromArgb(from.A, Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    internal static double Contrast(Color a, Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
    private static Color Rgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    private static Color Argb(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    private static SolidColorBrush Brush(Color color) => Freeze(new SolidColorBrush(color));
    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
