namespace IPhoneMirror.App.Models;

/// <summary>
/// Where a device's preview window was last: its outer bounds in physical
/// screen pixels and whether it stayed on top.
/// </summary>
internal sealed class PreviewPlacement
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool TopMost { get; set; }

    internal bool IsUsable => Width >= 48 && Height >= 48 && Width <= 32768 && Height <= 32768;

    internal PreviewPlacement Clone() => (PreviewPlacement)MemberwiseClone();
}
