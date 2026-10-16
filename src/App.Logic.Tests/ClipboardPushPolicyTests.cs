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

        // Images: our own write is blocked by sequence; the re-encoded PNG differs.
        var images = new ClipboardPushPolicy();
        byte[] phoneJpeg = [1, 2, 3], windowsPng = [9, 9, 9], screenshot = [4, 5, 6];
        images.RecordWindowsImageWrite(phoneJpeg, 50);
        Check(!images.ShouldPushImage(windowsPng, 50), "our own image write does not echo");
        Check(images.ShouldPushImage(screenshot, 51), "a new Windows screenshot is sent");
        Check(!images.ShouldPushImage(screenshot, 52), "the same image is not sent twice");
        images.PushImageFailed(screenshot);
        Check(images.ShouldPushImage(screenshot, 53), "a failed image push can be retried");
        Check(!images.ShouldPushImage([], 54), "empty image data is ignored");
        Check(images.ShouldPush("text after image", 55), "text and image hashes do not collide");

        Exception? codecError = null;
        var thread = new Thread(() =>
        {
            try { RunCodec(Check); }
            catch (Exception error) { codecError = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (codecError is not null) throw codecError;
    }

    private static void RunCodec(Action<bool, string> check)
    {
        static System.Windows.Media.Imaging.BitmapSource Noise(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            new Random(7).NextBytes(pixels); // incompressible: forces the JPEG path
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            return bitmap;
        }

        var small = Noise(64, 48);
        var (uti, data) = ClipboardImageCodec.EncodeForDevice(small, out var note);
        check(uti == "public.png" && note is null, "small images stay PNG");
        var decoded = ClipboardImageCodec.Decode(data);
        check(decoded.PixelWidth == 64 && decoded.PixelHeight == 48, "PNG round trip keeps the size");

        var large = Noise(1200, 900);
        var limit = 200_000;
        (uti, data) = ClipboardImageCodec.EncodeForDevice(large, out note, limit);
        check(uti == "public.jpeg" && data.Length <= limit && note is not null,
            "large images become a JPEG within the bridge limit, with a log note");
        decoded = ClipboardImageCodec.Decode(data);
        check(decoded.PixelWidth <= 1200 && decoded.PixelWidth >= 256, "reduced image keeps a usable size");
    }
}
