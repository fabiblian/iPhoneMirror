namespace IPhoneMirror.App.Models;

// Persisted subset of the existing DeviceCaptureState. Both setup and normal
// projection settings continue to edit the same runtime state.
internal sealed record DeviceVideoPreferences(uint Width, uint Height, int FrameRate, DecoderPreference Decoder,
    UsbProjectionMode? UsbMode = null)
{
    internal bool IsValid => Width <= 8192 && Height <= 8192 && (Width == 0) == (Height == 0) &&
        FrameRate is 24 or 30 or 60 or 120 && Enum.IsDefined(Decoder);
    internal static DeviceVideoPreferences From(DeviceCaptureState state) =>
        new(state.RenderWidth, state.RenderHeight, state.FrameRate, state.DecoderPreference, state.UsbProjectionMode);
    internal void Apply(DeviceCaptureState state)
    {
        // Entries saved before the mode was persisted keep the new default.
        if (UsbMode is { } mode && Enum.IsDefined(mode)) state.UsbProjectionMode = mode;
        if (!IsValid) return;
        state.RenderWidth = Width; state.RenderHeight = Height; state.FrameRate = FrameRate; state.DecoderPreference = Decoder;
    }
}
