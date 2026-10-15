using IPhoneMirror.App.Windows;

namespace IPhoneMirror.App.Services;

// Both identities must still match before restoring a preview.
internal sealed record PreviewVisibilityTarget(string Udid, NativePreviewWindow Window, ulong Session);
