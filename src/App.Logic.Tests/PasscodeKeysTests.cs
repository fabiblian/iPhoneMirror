using IPhoneMirror.App.Services;

internal static class PasscodeKeysTests
{
    internal static void Run()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        // The digit row of the HID keyboard page, independent of any layout.
        Check(PasscodeKeys.Usage('1') == 0x1E && PasscodeKeys.Usage('9') == 0x26, "digits 1..9 send HID 0x1E..0x26");
        Check(PasscodeKeys.Usage('0') == 0x27, "digit 0 sends HID 0x27");
        Check("0123456789".Select(PasscodeKeys.Usage).Distinct().Count() == 10, "every digit has its own key");
        var rejected = false;
        try { PasscodeKeys.Usage('#'); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "a symbol is never turned into a key");

        // Letters follow the keypad labels: 2 = ABC … 9 = WXYZ, in any case.
        static string Digits(string text) => new(text.Select(c => PasscodeKeys.ToDigit(c)!.Value).ToArray());
        Check(Digits("ABCDEFGHIJKLMNOPQRSTUVWXYZ") == "22233344455566677778889999", "every letter maps to its keypad digit");
        Check(Digits("abcdefghijklmnopqrstuvwxyz") == "22233344455566677778889999", "lower-case letters map the same way");
        Check(Digits("ÄÖÜäöü") == "268268", "umlauts count as their base letter");
        Check(Digits("Love") == "5683" && Digits("12ab") == "1222", "words and mixed codes translate per character");
        Check(PasscodeKeys.Usage('S') == PasscodeKeys.Usage('7') && PasscodeKeys.Usage('z') == PasscodeKeys.Usage('9'),
            "a letter sends the same key as its digit");

        Check(PasscodeKeys.IsValid("1234") && PasscodeKeys.IsValid("123456") &&
              PasscodeKeys.IsValid("1234567890123456"), "4 to 16 digits are accepted");
        Check(PasscodeKeys.IsValid("Love") && PasscodeKeys.IsValid("Zürich"), "letter codes are accepted");
        Check(!PasscodeKeys.IsValid("123") && !PasscodeKeys.IsValid("12345678901234567"),
            "shorter or longer codes are rejected");
        Check(!PasscodeKeys.IsValid("12#4") && !PasscodeKeys.IsValid("12 34") && !PasscodeKeys.IsValid("١٢٣٤"),
            "symbols, spaces and non-ASCII digits are rejected");

        // iOS submits 4- and 6-digit codes with the last digit; custom
        // numeric codes need OK, which a keyboard sends as Enter.
        Check(!PasscodeKeys.NeedsEnter(4) && !PasscodeKeys.NeedsEnter(6), "4 and 6 digits unlock without Enter");
        Check(PasscodeKeys.NeedsEnter(5) && PasscodeKeys.NeedsEnter(8), "custom lengths are confirmed with Enter");
        Check(PasscodeKeys.Enter == 0x28, "Enter is HID 0x28");
        Check(PasscodeKeys.Backspace == 0x2A, "Backspace (clearing leftover digits) is HID 0x2A");
        Console.WriteLine("Passcode keys: digit usages, length rules and Enter passed.");
    }
}
