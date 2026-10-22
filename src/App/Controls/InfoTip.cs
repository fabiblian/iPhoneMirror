using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace IPhoneMirror.App.Controls;

// A small "i" beside a setting. Hovering shows Text as a tooltip; a click,
// Enter or Space pins the same text as a popover until the user clicks or
// tabs away or presses Esc.
public sealed class InfoTip : Button
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(InfoTip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsPopoverOpenProperty = DependencyProperty.Register(
        nameof(IsPopoverOpen), typeof(bool), typeof(InfoTip),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnIsPopoverOpenChanged));

    private long _closedByOutsideClickAt;
    private bool _closingFromCode;

    public InfoTip()
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Text)) { Source = this });
        var tip = new ToolTip { Content = text };
        tip.SetResourceReference(FontFamilyProperty, "NavigationTextFontFamily");
        ToolTip = tip;
        ToolTipService.SetInitialShowDelay(this, 150);
        ToolTipService.SetShowDuration(this, 20000);
        ToolTipService.SetShowOnDisabled(this, true);
        SetResourceReference(AutomationProperties.NameProperty, "InfoTipName");
        SetBinding(AutomationProperties.HelpTextProperty,
            new Binding(nameof(Text)) { Source = this });
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsPopoverOpen
    {
        get => (bool)GetValue(IsPopoverOpenProperty);
        set => SetValue(IsPopoverOpenProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        // The popover closes itself on a mouse-down outside it; when that
        // mouse-down was on this button, the click must not reopen it.
        if (Environment.TickCount64 - _closedByOutsideClickAt < 300) return;
        SetPopoverOpen(!IsPopoverOpen);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsPopoverOpen)
        {
            SetPopoverOpen(false);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (!IsKeyboardFocusWithin) SetPopoverOpen(false);
    }

    private void SetPopoverOpen(bool open)
    {
        _closingFromCode = !open;
        try { IsPopoverOpen = open; }
        finally { _closingFromCode = false; }
    }

    private static void OnIsPopoverOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var info = (InfoTip)d;
        var open = (bool)e.NewValue;
        if (info.ToolTip is ToolTip tip && open) tip.IsOpen = false;
        ToolTipService.SetIsEnabled(info, !open);
        // Only the popup itself closes the popover without going through code.
        if (!open && !info._closingFromCode) info._closedByOutsideClickAt = Environment.TickCount64;
    }
}
