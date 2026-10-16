using IPhoneMirror.App.Services;

internal static class SwissKeyboardLayoutTests
{
    internal static void Run()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static byte Physical(int scanCode, bool extended = false) =>
            SwissKeyboardLayout.TryMapPhysicalKey(scanCode, extended, out var usage) ? usage : (byte)0;

        // "Zürich yz äöü": the physical position decides, not the Windows key.
        Check(Physical(0x15) == 0x1C, "Swiss Z key (US Y position) sends HID Y usage");
        Check(Physical(0x2C) == 0x1D, "Swiss Y key (US Z position) sends HID Z usage");
        Check(Physical(0x1A) == 0x2F, "ü key sends HID [ usage");
        Check(Physical(0x27) == 0x33, "ö key sends HID ; usage");
        Check(Physical(0x28) == 0x34, "ä key sends HID ' usage");
        Check(Physical(0x02) == 0x1E && Physical(0x0B) == 0x27, "digit row 1..0");
        Check(Physical(0x56) == 0x64, "ISO <> key sends HID non-US backslash");
        Check(Physical(0x35) == 0x38 && Physical(0x35, extended: true) == 0,
            "numpad divide (extended) keeps the virtual-key path");
        Check(Physical(0x1D) == 0 && Physical(0x2A) == 0 && Physical(0x48) == 0,
            "modifiers and navigation keep the virtual-key path");

        Check(DeviceKeyMap.TryMapKey(0x5A, 0x15, out var usage, out var modifier) &&
              usage == 0x1C && modifier == 0, "scan code wins over the virtual key");
        Check(DeviceKeyMap.TryMapKey(0x5A, 0, out usage, out _) && usage == 0x1D,
            "injected keys without scan code fall back to the virtual key");
        Check(DeviceKeyMap.TryMapKey(0x5B, 0x15B, out usage, out modifier) &&
              usage == 0 && modifier == 0x08, "left Win becomes Command");
        Check(DeviceKeyMap.TryMapKey(0x70, 0x3B, out usage, out _) && usage == 0x3A, "F1");
        Check(DeviceKeyMap.TryMapKey(0x71, 0x3C, out usage, out _) && usage == 0x3B, "F2");

        static (byte Usage, byte Modifiers) AltGr(char character) =>
            SwissKeyboardLayout.TryMapAltGrCharacter(character, out var usage, out var modifiers)
                ? (usage, modifiers) : ((byte)0, (byte)0);
        const byte option = SwissKeyboardLayout.LeftOption;
        Check(AltGr('@') == (0x0A, option), "@ = Option+G");
        Check(AltGr('€') == (0x08, option), "€ = Option+E");
        Check(AltGr('#') == (0x20, option), "# = Option+3");
        Check(AltGr('|') == (0x24, option), "| = Option+7");
        Check(AltGr('[') == (0x22, option) && AltGr(']') == (0x23, option), "[ ] = Option+5/6");
        Check(AltGr('{') == (0x25, option) && AltGr('}') == (0x26, option), "{ } = Option+8/9");
        Check(AltGr('\\') == (0x24, (byte)(option | SwissKeyboardLayout.LeftShift)),
            "\\ = Option+Shift+7");
        Check(AltGr('~') == (0x11, option), "~ = Option+N");
        Check(AltGr('¦') == (0, 0), "unlisted AltGr characters use text insertion");

        static (byte Usage, byte Modifiers) Ctrl(int virtualKey, bool shift = false) =>
            SwissKeyboardLayout.TryMapControlShortcut(virtualKey, shift, out var usage, out var modifiers)
                ? (usage, modifiers) : ((byte)0, (byte)0);
        const byte command = SwissKeyboardLayout.LeftCommand;
        const byte commandShift = command | SwissKeyboardLayout.LeftShift;
        Check(Ctrl(0x41) == (0x04, command), "Ctrl+A = Cmd+A");
        Check(Ctrl(0x58) == (0x1B, command), "Ctrl+X = Cmd+X");
        Check(Ctrl(0x5A) == (0x1C, command), "Ctrl+Z = Cmd+Z on the Swiss Z position");
        Check(Ctrl(0x5A, shift: true) == (0x1C, commandShift), "Ctrl+Shift+Z = Cmd+Shift+Z");
        Check(Ctrl(0x59) == (0x1C, commandShift), "Ctrl+Y = Cmd+Shift+Z");
        Check(Ctrl(0x56) == (0, 0) && Ctrl(0x43) == (0, 0),
            "Ctrl+V and Ctrl+C keep their existing clipboard handling");

        Check(SwissKeyboardLayout.ModifierUsages(commandShift).SequenceEqual(new byte[] { 0xE1, 0xE3 }),
            "modifier mask expands to HID modifier usages");

        // Arrow keys: one swipe through the middle, 35 % long.
        static ((double X, double Y) Start, (double X, double Y) End) Swipe(byte usage) =>
            ArrowKeySwipe.TryGetSwipe(usage, out var start, out var end) ? (start, end) : default;
        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
        var down = Swipe(0x51);
        Check(Near(down.Start.X, 0.5) && down.Start.Y > down.End.Y &&
              Near(down.Start.Y - down.End.Y, ArrowKeySwipe.Distance), "↓ moves the finger up");
        var up = Swipe(0x52);
        Check(up.Start.Y < up.End.Y && Near(up.Start.X, 0.5), "↑ moves the finger down");
        var right = Swipe(0x4F);
        Check(right.Start.X > right.End.X && Near(right.Start.Y, 0.5) &&
              Near(right.Start.X - right.End.X, ArrowKeySwipe.Distance), "→ moves the finger left");
        var left = Swipe(0x50);
        Check(left.Start.X < left.End.X, "← moves the finger right");
        Check(!ArrowKeySwipe.TryGetSwipe(0x28, out _, out _) && !ArrowKeySwipe.IsArrow(0x4E),
            "only the four arrow keys swipe");
        Check(ArrowKeySwipe.DurationMs == 250, "swipe takes about 250 ms");
    }
}
