using IPhoneMirror.App.Services;

internal static class ClipboardPushPolicyTests
{
    internal static void Run()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        var policy = new ClipboardPushPolicy();
        Check(policy.ShouldPush("Hallo", 10), "a new Windows copy goes to the phone");
        Check(!policy.ShouldPush("Hallo", 11), "the same text is not sent twice");
        Check(!policy.ShouldPush("", 12) && !policy.ShouldPush(null, 13),
            "empty or non-text clipboard contents are ignored");

        // iPhone -> Windows: our own write must not echo back.
        policy.RecordWindowsWrite("vom iPhone", 20);
        Check(!policy.ShouldPush("vom iPhone", 20), "our own write (same sequence) is ignored");
        Check(!policy.ShouldPush("vom iPhone", 21),
            "phone text re-announced by another app is not sent back");
        Check(policy.ShouldPush("Hallo", 22), "copying older PC text again after a phone copy is sent");

        // A different text with our write sequence is still treated as ours.
        policy.RecordWindowsWrite("A", 30);
        Check(!policy.ShouldPush("B", 30), "the write sequence alone blocks the echo");
        Check(policy.ShouldPush("B", 31), "a later external copy is sent");

        policy.PushFailed("B");
        Check(policy.ShouldPush("B", 32), "a failed push can be retried by copying again");

        var tooLarge = new string('x', ClipboardPushPolicy.MaxUtf8Bytes + 1);
        Check(!policy.ShouldPush(tooLarge, 40), "text above the bridge limit is not sent");
        Check(policy.ShouldPush("Zürich € 😀", 41), "non-ASCII text is sent");
    }
}
