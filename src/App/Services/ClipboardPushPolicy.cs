using System.Security.Cryptography;
using System.Text;

namespace IPhoneMirror.App.Services;

/// <summary>
/// Decides whether a Windows clipboard change is pushed to the iPhone. Text
/// the app itself wrote from the phone must not bounce back (sequence number
/// of our write), and content already exchanged in either direction is not
/// sent again (content hash). Dispatcher-owned.
/// </summary>
internal sealed class ClipboardPushPolicy
{
    // The bridge's write_clipboard frame accepts at most 64 KiB of UTF-8.
    internal const int MaxUtf8Bytes = 65536;

    private uint? _ownWriteSequence;
    private string? _lastHash;

    /// <summary>The app wrote phone text into the Windows clipboard.</summary>
    internal void RecordWindowsWrite(string text, uint sequenceAfterWrite)
    {
        _ownWriteSequence = sequenceAfterWrite;
        _lastHash = Hash(text);
    }

    internal bool ShouldPush(string? text, uint sequence)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (sequence == _ownWriteSequence) return false;
        if (Encoding.UTF8.GetByteCount(text) > MaxUtf8Bytes) return false;
        var hash = Hash(text);
        if (hash == _lastHash) return false;
        _lastHash = hash;
        return true;
    }

    /// <summary>A failed push may be retried by copying the same text again.</summary>
    internal void PushFailed(string text)
    {
        if (_lastHash == Hash(text)) _lastHash = null;
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
