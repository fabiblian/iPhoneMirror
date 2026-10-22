namespace IPhoneMirror.App.Services;

/// <summary>
/// Turns an iPhone passcode typed on the PC into keyboard usages for the lock
/// screen. Letters are translated like the keypad labels (2 = ABC … 9 = WXYZ),
/// so only digit keys are sent and no keyboard layout can change a key.
/// </summary>
internal static class PasscodeKeys
{
    internal const int MinimumLength = 4;
    internal const int MaximumLength = 16;
    internal const byte Enter = 0x28;
    internal const byte Backspace = 0x2A;

    internal static bool IsAccepted(char character) => ToDigit(character) is not null;

    internal static bool IsValid(ReadOnlySpan<char> passcode)
    {
        if (passcode.Length is < MinimumLength or > MaximumLength) return false;
        foreach (var character in passcode)
            if (!IsAccepted(character)) return false;
        return true;
    }

    // The letters printed under each key of the iPhone passcode keypad.
    // Umlauts count as their base letter.
    internal static char? ToDigit(char character) => char.ToUpperInvariant(character) switch
    {
        >= '0' and <= '9' => character,
        'A' or 'B' or 'C' or 'Ä' => '2',
        'D' or 'E' or 'F' => '3',
        'G' or 'H' or 'I' => '4',
        'J' or 'K' or 'L' => '5',
        'M' or 'N' or 'O' or 'Ö' => '6',
        'P' or 'Q' or 'R' or 'S' => '7',
        'T' or 'U' or 'V' or 'Ü' => '8',
        'W' or 'X' or 'Y' or 'Z' => '9',
        _ => null,
    };

    // HID keyboard page: '1'..'9' are 0x1E..0x26 and '0' is 0x27.
    internal static byte Usage(char character) => ToDigit(character) switch
    {
        '0' => 0x27,
        { } digit => (byte)(0x1E + digit - '1'),
        null => throw new ArgumentOutOfRangeException(nameof(character)),
    };

    // Four- and six-digit codes unlock with their last digit. Any other length
    // is a custom numeric code, which iOS confirms with OK (Enter).
    internal static bool NeedsEnter(int length) => length is not (4 or 6);
}
