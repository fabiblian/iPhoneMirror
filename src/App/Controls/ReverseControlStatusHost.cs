using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace IPhoneMirror.App.Controls;

/// <summary>
/// Stacks reverse-control status cards. The main window places one under its
/// toolbar, outside the native preview: WPF cannot paint over that HWND.
/// </summary>
internal sealed class ReverseControlStatusHost : StackPanel
{
    // A card never shrinks below its one-line header; its details scroll.
    private const double MinimumCardHeight = 56;
    // Most of the shared height stays with the preview.
    private const double PreviewReserve = 200;

    /// <summary>
    /// The star-sized element below the cards (the preview). Cards grow only
    /// into its height and leave it most of that, so the rows below keep their
    /// size in small windows.
    /// </summary>
    public static readonly DependencyProperty SharesHeightWithProperty = DependencyProperty.Register(
        nameof(SharesHeightWith), typeof(FrameworkElement), typeof(ReverseControlStatusHost),
        new PropertyMetadata(null, OnSharesHeightWithChanged));

    private event Action? Emptied;

    public ReverseControlStatusHost()
    {
        Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(this, "ReverseControlStatusHost");
        SizeChanged += (_, _) => LimitCardHeight();
    }

    public FrameworkElement? SharesHeightWith
    {
        get => (FrameworkElement?)GetValue(SharesHeightWithProperty);
        set => SetValue(SharesHeightWithProperty, value);
    }

    private static void OnSharesHeightWithChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var host = (ReverseControlStatusHost)target;
        if (args.OldValue is FrameworkElement previous) previous.SizeChanged -= host.OnSharedSizeChanged;
        if (args.NewValue is FrameworkElement current) current.SizeChanged += host.OnSharedSizeChanged;
        host.LimitCardHeight();
    }

    private void OnSharedSizeChanged(object sender, SizeChangedEventArgs e) => LimitCardHeight();

    private void LimitCardHeight()
    {
        if (SharesHeightWith is not { } shared || Children.Count == 0) return;
        // The cards and the shared element split one star row, so their sum
        // is the space they can share. While a new card still overflows, the
        // sum shrinks with each pass until the card fits.
        var cards = Children.OfType<FrameworkElement>().ToArray();
        var space = ActualHeight + shared.ActualHeight;
        var available = space - cards.Sum(card => card.Margin.Top + card.Margin.Bottom) -
            Math.Min(PreviewReserve, space / 2);
        var limit = Math.Max(MinimumCardHeight, Math.Floor(available / cards.Length));
        foreach (var card in cards)
            if (Math.Abs(card.MaxHeight - limit) >= 1) card.MaxHeight = limit;
    }

    internal static ReverseControlStatusHost? Find(DependencyObject root)
    {
        // The logical tree finds the host before the first layout pass; the
        // visual tree reaches content that a template presents, such as the
        // main window's NavigationView.ContentOverlay.
        var visited = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>([root]);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node)) continue;
            if (node is ReverseControlStatusHost host) return host;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                pending.Push(child);
            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                    pending.Push(VisualTreeHelper.GetChild(node, index));
        }
        return null;
    }

    // Tray mode hides the main window, so prompts there need their own small
    // window. It closes with its last card.
    internal static ReverseControlStatusHost CreateFloating(Window owner)
    {
        var host = new ReverseControlStatusHost { Width = 520, Margin = new Thickness(10) };
        var window = new Window
        {
            Title = (string)(owner.TryFindResource("ReverseControlTitle") ?? "iPhoneMirror"),
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = host,
        };
        window.SizeChanged += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            window.Left = area.Right - window.ActualWidth - 8;
            window.Top = area.Bottom - window.ActualHeight - 8;
        };
        host.Emptied += window.Close;
        window.Show();
        return host;
    }

    internal void Add(ReverseControlStatusCard card)
    {
        Children.Add(card);
        Visibility = Visibility.Visible;
        LimitCardHeight();
    }

    internal void Remove(ReverseControlStatusCard card)
    {
        Children.Remove(card);
        if (Children.Count > 0) return;
        Visibility = Visibility.Collapsed;
        Emptied?.Invoke();
    }
}
