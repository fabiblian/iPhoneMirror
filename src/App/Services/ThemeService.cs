using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IPhoneMirror.App.Updater;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace IPhoneMirror.App.Services;

internal static class ThemeService
{
    private const string LightThemePath = "Themes/LightTheme.xaml";
    private const string DarkThemePath = "Themes/DarkTheme.xaml";
    private sealed class WindowBackdropState
    {
        internal bool EdgeToEdge { get; set; }
    }

    private static readonly ConditionalWeakTable<Window, WindowBackdropState> AttachedWindows = new();
    private static bool _systemEventsAttached;

    internal static event EventHandler? ThemeChanged;

    internal static bool IsDark { get; private set; } = true;
    internal static AppTheme Preference { get; private set; } = AppTheme.System;
    internal static AppColorTheme ColorTheme { get; private set; } = AppColorTheme.Default;
    internal static string AccentHex { get; private set; } = "";
    /// <summary>The accent follows the Windows accent colour and updates with it.</summary>
    internal static bool FollowsWindowsAccent { get; private set; }
    internal static UiStyle Style { get; private set; } = UiStyle.Modern;
    /// <summary>The accent currently shown, after contrast adjustment.</summary>
    internal static Color Accent { get; private set; } = ThemePalette.DarkAccent;
    /// <summary>A colour preset paints its own background instead of Mica.</summary>
    internal static bool UsesCustomBackground => !SystemParameters.HighContrast &&
        ThemePalette.EffectiveColorTheme(ColorTheme, IsDark) != AppColorTheme.Default;
    private static ResourceDictionary? _appearanceOverrides;
    private static Color? _wpfUiDefaultAccent;

    internal static void Apply(AppTheme theme, AppColorTheme colorTheme, string? accentHex, UiStyle style)
    {
        ColorTheme = Enum.IsDefined(colorTheme) ? colorTheme : AppColorTheme.Default;
        FollowsWindowsAccent = ThemePalette.IsWindowsAccent(accentHex);
        AccentHex = FollowsWindowsAccent ? "" : ThemePalette.NormalizeHex(accentHex);
        Style = Enum.IsDefined(style) ? style : UiStyle.Modern;
        Apply(theme);
    }

    internal static void Apply(AppTheme theme)
    {
        // Re-read on every apply: Windows reports accent changes as a user
        // preference change, which re-applies the theme.
        if (FollowsWindowsAccent) AccentHex = ThemePalette.ToHex(ThemePalette.ReadWindowsAccent());
        Preference = theme;
        IsDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDark(),
        };
        EnsureSystemEvents();

        var application = Application.Current;
        if (application is null) return;
        SwapThemeDictionary(application.Resources, IsDark ? DarkThemePath : LightThemePath);
        var applicationTheme = CurrentApplicationTheme;
        ApplicationThemeManager.Apply(
            applicationTheme, Wpf.Ui.Controls.WindowBackdropType.None,
            updateAccent: false);
        ApplyAppearance(application.Resources, applicationTheme);
        // Added after the appearance layer so high-contrast colours still win.
        IPhoneMirror.UI.Services.AccessibilityAppearance.Apply(application);
        foreach (Window window in application.Windows)
        {
            RefreshWindowTheme(window, applicationTheme);
            AnimateThemeTransition(window);
        }
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static void Attach(Window window)
    {
        if (AttachedWindows.TryGetValue(window, out _)) return;
        AttachedWindows.Add(window, new WindowBackdropState());
        if (!window.AllowsTransparency)
            window.SetResourceReference(Window.BackgroundProperty, "AppBackgroundBrush");
        window.SourceInitialized += (_, _) =>
            RefreshWindowTheme(window, CurrentApplicationTheme);
        window.StateChanged += (_, _) => ApplyBackdrop(window);
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            RefreshWindowTheme(window, CurrentApplicationTheme);
    }

    internal static void SetEdgeToEdge(Window window, bool enabled)
    {
        Attach(window);
        if (!AttachedWindows.TryGetValue(window, out var state)) return;
        state.EdgeToEdge = enabled;
        RefreshWindowTheme(window, CurrentApplicationTheme);
    }

    internal static void Shutdown()
    {
        if (!_systemEventsAttached) return;
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        _systemEventsAttached = false;
    }

    private static void ApplyAppearance(ResourceDictionary resources, ApplicationTheme applicationTheme)
    {
        Accent = ThemePalette.EffectiveAccent(ColorTheme, AccentHex, IsDark);
        if (_appearanceOverrides is not null) resources.MergedDictionaries.Remove(_appearanceOverrides);
        _appearanceOverrides = null;
        var values = SystemParameters.HighContrast
            ? []
            : ThemePalette.BuildOverrides(IsDark, ColorTheme, AccentHex, Style);
        if (values.Count > 0)
        {
            var overrides = new ResourceDictionary();
            foreach (var (key, value) in values) overrides[key] = value;
            resources.MergedDictionaries.Add(overrides);
            _appearanceOverrides = overrides;
        }
        // WPF-UI controls (toggle switches, navigation) read their own accent
        // keys. Leave them untouched unless a custom accent is (or was) active.
        if (values.ContainsKey("ThemeAccentColor"))
        {
            _wpfUiDefaultAccent ??= ApplicationAccentColorManager.SystemAccent;
            ApplicationAccentColorManager.Apply(Accent, applicationTheme);
        }
        else if (_wpfUiDefaultAccent is { } original)
        {
            ApplicationAccentColorManager.Apply(original, applicationTheme);
            _wpfUiDefaultAccent = null;
        }
    }

    private static void SwapThemeDictionary(ResourceDictionary resources, string path)
    {
        var existing = resources.MergedDictionaries.FirstOrDefault(dictionary =>
            IsThemeDictionary(dictionary.Source));
        if (existing?.Source?.OriginalString.EndsWith(path,
                StringComparison.OrdinalIgnoreCase) == true)
            return;

        var replacement = new ResourceDictionary
        {
            Source = new Uri(
                $"/{typeof(ThemeService).Assembly.GetName().Name};component/{path}",
                UriKind.Relative),
        };
        if (existing is null)
            resources.MergedDictionaries.Insert(0, replacement);
        else
        {
            var index = resources.MergedDictionaries.IndexOf(existing);
            resources.MergedDictionaries[index] = replacement;
        }
    }

    private static bool IsThemeDictionary(Uri? source)
    {
        var value = source?.OriginalString;
        return value?.EndsWith(LightThemePath, StringComparison.OrdinalIgnoreCase) == true ||
               value?.EndsWith(DarkThemePath, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void AnimateThemeTransition(Window window)
    {
        if (!window.IsLoaded || !SystemParameters.ClientAreaAnimation) return;
        var opacity = (double)window.GetAnimationBaseValue(UIElement.OpacityProperty);
        window.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(opacity * 0.88, opacity, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
    }

    private static ApplicationTheme CurrentApplicationTheme =>
        SystemParameters.HighContrast ? ApplicationTheme.HighContrast :
        IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light;

    private static void RefreshWindowTheme(Window window,
        ApplicationTheme applicationTheme)
    {
        if (!window.AllowsTransparency)
        {
            var backdrop = window is Wpf.Ui.Controls.FluentWindow fluentWindow
                ? fluentWindow.WindowBackdropType
                : Wpf.Ui.Controls.WindowBackdropType.None;
            if (SystemParameters.HighContrast || UsesCustomBackground)
                window.SetResourceReference(Window.BackgroundProperty, "AppBackgroundBrush");
            else WindowBackgroundManager.UpdateBackground(window, applicationTheme, backdrop);
        }
        ApplyBackdrop(window);
    }

    private static void EnsureSystemEvents()
    {
        if (_systemEventsAttached) return;
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        _systemEventsAttached = true;
    }

    private static void OnSystemPreferenceChanged(object sender,
        UserPreferenceChangedEventArgs args)
    {
        if (Application.Current is null) return;
        _ = Application.Current.Dispatcher.BeginInvoke(() => Apply(Preference));
    }

    internal static void ApplyBackdrop(Window window)
    {
        if (window.AllowsTransparency) return;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        _ = AttachedWindows.TryGetValue(window, out var state);
        var edgeToEdge = state?.EdgeToEdge == true;
        var flushToDisplayEdge = edgeToEdge || window.WindowState == WindowState.Maximized;
        var dark = IsDark ? 1 : 0;
        var corner = flushToDisplayEdge ? DwmDoNotRound : DwmRound;
        var acrylic = window is Wpf.Ui.Controls.FluentWindow fluentWindow &&
                      fluentWindow.WindowBackdropType ==
                          Wpf.Ui.Controls.WindowBackdropType.Acrylic;
        var backdrop = SystemParameters.HighContrast || edgeToEdge || UsesCustomBackground
            ? DwmBackdropNone
            : acrylic ? DwmBackdropAcrylic : DwmBackdropMica;
        var border = flushToDisplayEdge ? DwmColorNone : DwmColorDefault;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref corner, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref backdrop, sizeof(int));
        _ = DwmSetWindowAttributeColor(handle, DwmBorderColor, ref border, sizeof(uint));
    }

    private static bool IsSystemDark()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0);
            return value is not int integer || integer == 0;
        }
        catch (Exception error)
        {
            DiagnosticLogger.ExceptionOnce("theme-registry", "theme",
                "system_theme_read_failed", error);
            return true;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute,
        ref int value, int valueSize);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttributeColor(IntPtr window, int attribute,
        ref uint value, int valueSize);

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmSystemBackdropType = 38;
    private const int DwmDoNotRound = 1;
    private const int DwmRound = 2;
    private const int DwmBackdropNone = 1;
    private const int DwmBackdropMica = 2;
    private const int DwmBackdropAcrylic = 3;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const uint DwmColorDefault = 0xFFFFFFFF;
}
