using System.Windows;
using System.Windows.Controls;

namespace IPhoneMirror.App.Controls;

// Lays out a label and its InfoTip side by side. The label (first child) gets
// the width left after the tip, so long text wraps instead of pushing the tip
// out of view; the tip (second child) follows right after the label.
public sealed class InfoRow : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0) return default;
        var label = InternalChildren[0];
        var tip = InternalChildren.Count > 1 ? InternalChildren[1] : null;
        tip?.Measure(availableSize);
        var tipWidth = tip?.DesiredSize.Width ?? 0;
        label.Measure(new Size(Math.Max(0, availableSize.Width - tipWidth), availableSize.Height));
        return new Size(label.DesiredSize.Width + tipWidth,
            Math.Max(label.DesiredSize.Height, tip?.DesiredSize.Height ?? 0));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count == 0) return finalSize;
        var label = InternalChildren[0];
        var tip = InternalChildren.Count > 1 ? InternalChildren[1] : null;
        var tipWidth = tip?.DesiredSize.Width ?? 0;
        var labelWidth = Math.Min(label.DesiredSize.Width, Math.Max(0, finalSize.Width - tipWidth));
        label.Arrange(new Rect(0, 0, labelWidth, finalSize.Height));
        tip?.Arrange(new Rect(labelWidth, 0, tipWidth, finalSize.Height));
        return finalSize;
    }
}
