using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IPhoneMirror.App.Services;

/// <summary>Image conversion for the Windows ⇄ iPhone clipboard sync.</summary>
internal static class ClipboardImageCodec
{
    // Matches the bridge's MAX_HOST_CLIPBOARD_IMAGE_BYTES (4 MiB frame, base64).
    internal const int MaxDeviceImageBytes = 2_800_000;
    private const int MinEdgePixels = 256;

    internal static BitmapSource Decode(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    internal static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// PNG when it fits the bridge frame; otherwise JPEG, scaled down until it
    /// fits. <paramref name="note"/> describes any reduction for the log.
    /// </summary>
    internal static (string Uti, byte[] Data) EncodeForDevice(BitmapSource image,
        out string? note, int maxBytes = MaxDeviceImageBytes)
    {
        note = null;
        var png = EncodePng(image);
        if (png.Length <= maxBytes) return ("public.png", png);
        var scale = 1.0;
        while (true)
        {
            var source = scale >= 1.0 ? image : Scale(image, scale);
            foreach (var quality in new[] { 90, 75 })
            {
                var jpeg = EncodeJpeg(source, quality);
                if (jpeg.Length <= maxBytes ||
                    Math.Min(source.PixelWidth, source.PixelHeight) <= MinEdgePixels)
                {
                    note = $"png={png.Length} jpeg={jpeg.Length} quality={quality} " +
                        $"size={source.PixelWidth}x{source.PixelHeight}";
                    return ("public.jpeg", jpeg);
                }
            }
            scale *= 0.75;
        }
    }

    private static byte[] EncodeJpeg(BitmapSource image, int quality)
    {
        // JPEG has no alpha; flatten onto white like a pasted screenshot.
        var opaque = new FormatConvertedBitmap(Flatten(image), PixelFormats.Bgr24, null, 0);
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(opaque));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource Flatten(BitmapSource image)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var rect = new System.Windows.Rect(0, 0, image.PixelWidth, image.PixelHeight);
            context.DrawRectangle(Brushes.White, null, rect);
            context.DrawImage(image, rect);
        }
        var target = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return target;
    }

    private static BitmapSource Scale(BitmapSource image, double scale)
    {
        var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }
}
