using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace IPhoneMirror.App.Services;

internal enum PreviewDropResult { Image, Text, Unsupported, TooLarge, Failed }

/// <summary>
/// An image or text dropped on a preview goes onto the Windows clipboard; the
/// automatic clipboard sync then hands it to the iPhone like any copy.
/// </summary>
internal static class PreviewDrop
{
    private const long MaxTextBytes = 1024 * 1024;
    private const long MaxImageBytes = 64L * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".log", ".xml", ".html", ".htm", ".yaml", ".yml", ".ini",
    };

    internal static bool IsSupportedFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path)) || TextExtensions.Contains(Path.GetExtension(path));

    // Data from another process can fail to render a format; such a drop is
    // simply not accepted.
    internal static bool CanAccept(IDataObject data)
    {
        try
        {
            return (data.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsSupportedFile)) ||
                data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.UnicodeText);
        }
        catch (COMException) { return false; }
    }

    internal static PreviewDropResult PutOnClipboard(IDataObject data)
    {
        try
        {
            if (data.GetData(DataFormats.FileDrop) is string[] files) return PutOnClipboard(files);
            if (data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
                return SetClipboard(() => Clipboard.SetImage(bitmap), PreviewDropResult.Image);
            if (data.GetData(DataFormats.UnicodeText) is string text && text.Length > 0)
                return SetClipboard(() => Clipboard.SetText(text), PreviewDropResult.Text);
            return PreviewDropResult.Unsupported;
        }
        catch (COMException error)
        {
            DiagnosticLogger.ExceptionOnce("preview-drop-data", "clipboard", "preview_drop_data_failed", error);
            return PreviewDropResult.Failed;
        }
    }

    /// <summary>Uses the first supported file; a drop carries one item to the phone.</summary>
    internal static PreviewDropResult PutOnClipboard(IReadOnlyList<string> files)
    {
        var path = files.FirstOrDefault(IsSupportedFile);
        if (path is null) return PreviewDropResult.Unsupported;
        try
        {
            var length = new FileInfo(path).Length;
            if (ImageExtensions.Contains(Path.GetExtension(path)))
            {
                if (length > MaxImageBytes) return PreviewDropResult.TooLarge;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                return SetClipboard(() => Clipboard.SetImage(image), PreviewDropResult.Image);
            }
            if (length > MaxTextBytes) return PreviewDropResult.TooLarge;
            var text = File.ReadAllText(path);
            return text.Length == 0 ? PreviewDropResult.Unsupported
                : SetClipboard(() => Clipboard.SetText(text), PreviewDropResult.Text);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            NotSupportedException or FileFormatException or ArgumentException)
        {
            DiagnosticLogger.ExceptionOnce("preview-drop-read", "clipboard", "preview_drop_read_failed", error);
            return PreviewDropResult.Failed;
        }
    }

    // Another app can hold the clipboard open for a moment; retry briefly.
    private static PreviewDropResult SetClipboard(Action set, PreviewDropResult success)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                set();
                return success;
            }
            catch (COMException) when (attempt < 4)
            {
                Thread.Sleep(40);
            }
            catch (COMException error)
            {
                DiagnosticLogger.ExceptionOnce("preview-drop-clipboard", "clipboard", "preview_drop_clipboard_failed", error);
                return PreviewDropResult.Failed;
            }
        }
    }
}
