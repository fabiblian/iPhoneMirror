namespace IPhoneMirror.App.Services;

/// <summary>
/// Key translation for an iPhone whose hardware keyboard layout is set to
/// German (Switzerland). Typing keys are sent by physical position so the
/// phone's own layout produces y/z, umlauts and shifted characters; AltGr
/// characters and PC editing shortcuts become their Mac/iOS keystrokes.
/// </summary>
internal static class SwissKeyboardLayout
{
    internal const byte LeftShift = 0x02;
    internal const byte LeftOption = 0x04;
    internal const byte LeftCommand = 0x08;

    /// <summary>
    /// Maps a PC scan code (set 1) of the typing block to its HID usage. Keys
    /// outside the block (navigation, function keys, numpad) return false and
    /// keep the virtual-key mapping, which is layout independent for them.
    /// </summary>
    internal static bool TryMapPhysicalKey(int scanCode, bool extended, out byte usage)
    {
        usage = 0;
        if (extended) return false;
        usage = scanCode switch
        {
            >= 0x02 and <= 0x0B => (byte)(scanCode - 0x02 + 0x1E), // 1..9, 0
            0x0C => 0x2D, 0x0D => 0x2E, // ' and ^ on Swiss keyboards
            0x10 => 0x14, 0x11 => 0x1A, 0x12 => 0x08, 0x13 => 0x15, 0x14 => 0x17,
            0x15 => 0x1C, 0x16 => 0x18, 0x17 => 0x0C, 0x18 => 0x12, 0x19 => 0x13,
            0x1A => 0x2F, 0x1B => 0x30, // ü, ¨
            0x1E => 0x04, 0x1F => 0x16, 0x20 => 0x07, 0x21 => 0x09, 0x22 => 0x0A,
            0x23 => 0x0B, 0x24 => 0x0D, 0x25 => 0x0E, 0x26 => 0x0F,
            0x27 => 0x33, 0x28 => 0x34, 0x29 => 0x35, 0x2B => 0x31, // ö, ä, §, $
            0x2C => 0x1D, 0x2D => 0x1B, 0x2E => 0x06, 0x2F => 0x19, 0x30 => 0x05,
            0x31 => 0x11, 0x32 => 0x10, 0x33 => 0x36, 0x34 => 0x37, 0x35 => 0x38,
            0x56 => 0x64, // ISO <> between left Shift and Y
            _ => (byte)0,
        };
        return usage != 0;
    }

    /// <summary>
    /// iOS German (Switzerland) keystroke for a character Windows produces with
    /// AltGr. Characters without an entry go through text insertion instead.
    /// </summary>
    internal static bool TryMapAltGrCharacter(char character, out byte usage, out byte modifiers)
    {
        (usage, modifiers) = character switch
        {
            '@' => ((byte)0x0A, LeftOption),               // ⌥G
            '€' => ((byte)0x08, LeftOption),               // ⌥E
            '#' => ((byte)0x20, LeftOption),               // ⌥3
            '|' => ((byte)0x24, LeftOption),               // ⌥7
            '[' => ((byte)0x22, LeftOption),               // ⌥5
            ']' => ((byte)0x23, LeftOption),               // ⌥6
            '{' => ((byte)0x25, LeftOption),               // ⌥8
            '}' => ((byte)0x26, LeftOption),               // ⌥9
            '\\' => ((byte)0x24, (byte)(LeftOption | LeftShift)), // ⌥⇧7
            '~' => ((byte)0x11, LeftOption),               // ⌥N
            _ => ((byte)0, (byte)0),
        };
        return usage != 0;
    }

    /// <summary>
    /// Translates a Ctrl editing shortcut (by Windows virtual key, so the
    /// active PC layout decides which letter was meant) to its Command form:
    /// Ctrl+A/X/Z → Cmd+A/X/Z, Ctrl+Y → Cmd+Shift+Z.
    /// </summary>
    internal static bool TryMapControlShortcut(int virtualKey, bool shift,
        out byte usage, out byte modifiers)
    {
        var extraShift = false;
        usage = virtualKey switch
        {
            0x41 => 0x04, // A
            0x58 => 0x1B, // X
            0x5A => 0x1C, // Z sits on the US-Y position in the Swiss layout
            0x59 => 0x1C, // Y = redo = Cmd+Shift+Z
            _ => (byte)0,
        };
        if (virtualKey == 0x59) extraShift = true;
        modifiers = (byte)(LeftCommand | (shift || extraShift ? LeftShift : 0));
        if (usage == 0) modifiers = 0;
        return usage != 0;
    }

    /// <summary>HID usages for a modifier mask, for transports that also
    /// expect modifiers inside the usage list.</summary>
    internal static IEnumerable<byte> ModifierUsages(byte modifiers)
    {
        for (var bit = 0; bit < 8; bit++)
        {
            if ((modifiers & (1 << bit)) != 0) yield return (byte)(0xE0 + bit);
        }
    }
}
