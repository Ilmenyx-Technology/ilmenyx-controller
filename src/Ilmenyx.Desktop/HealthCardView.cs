// New Ilmenyx additions (card layout follows the original CardTemplate: title/meta, verdict badge, reasons, 2-column metrics;
// cinematic surface and semantic verdict colours 2026-11-06): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public static class HealthCardView
{
    /// <summary>Semantic colour of a verdict: success, warning, error, or muted when there is no data.</summary>
    public static IBrush VerdictBrush(HealthVerdict verdict) => verdict switch
    {
        HealthVerdict.Good => Palette.Success, HealthVerdict.Warning => Palette.Warn, HealthVerdict.Critical => Palette.Error, _ => Palette.Muted,
    };

    public static Control Build(HealthCard c, Control? extra = null)
    {
        var tone = VerdictBrush(c.Verdict);
        // Tinted capsule: the tint is the verdict colour at low opacity, so it follows theme switches.
        var badge = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                new Border { Background = tone, Opacity = 0.16, CornerRadius = new CornerRadius(IlmenyxTheme.Pill) },
                new Border
                {
                    Padding = new Thickness(11, 4),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        Children =
                        {
                            new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = tone, VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Name = "Verdict_" + c.Device, Text = c.VerdictText, FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.4, Foreground = tone },
                        }
                    }
                },
            }
        };
        var top = new Grid { ColumnDefinitions = new("*,Auto") };
        top.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 12, 0), Children = { new TextBlock { Text = c.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = c.Meta, Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap } } });
        Grid.SetColumn(badge, 1); top.Children.Add(badge);
        var metrics = new UniformGrid { Columns = 2 };
        foreach (var (key, value) in c.Metrics)
            metrics.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 16, 8), Child = new StackPanel { Children = { new TextBlock { Text = key, Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = value, FontSize = 13.5, FontWeight = FontWeight.Medium, FontFamily = IlmenyxTheme.Mono, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) } } } });
        var body = new StackPanel { Children = { top } };
        if (c.Reasons.Length > 0) body.Children.Add(new TextBlock { Text = c.Reasons, FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) });
        if (c.Reasons.Length == 0) metrics.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(metrics);
        if (extra is not null) body.Children.Add(extra);
        var card = UiKit.Card(body); card.Padding = new Thickness(20, 16);
        return card;
    }

    /// <summary>
    /// Responsive two-column flow for cards of the same kind (disks, batteries): one column under <paramref name="breakpoint"/> px
    /// or when there is a single card, so nothing gets squeezed at the window minimum.
    /// </summary>
    public static Grid Columns(IReadOnlyList<Control> cards, double breakpoint = 760, double gap = 12, double initialWidth = 0, Func<Control, double>? weight = null)
    {
        var left = new StackPanel(); var right = new StackPanel { Margin = new Thickness(gap, 0, 0, 0) };
        var grid = new Grid { ColumnDefinitions = new("*,*") };
        Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right);
        int columns = 0;
        void Flow(int n)
        {
            if (cards.Count < 2) n = 1;
            if (n == columns) return;
            columns = n; left.Children.Clear(); right.Children.Clear();
            grid.ColumnDefinitions[1].Width = n == 1 ? new GridLength(0) : new GridLength(1, GridUnitType.Star); right.IsVisible = n > 1;
            double l = 0, r = 0; // greedy balance by weight (default: one per card), keeping the reading order
            foreach (var card in cards)
            {
                double w = weight?.Invoke(card) ?? 1;
                if (n == 1 || l <= r) { left.Children.Add(card); l += w; } else { right.Children.Add(card); r += w; }
            }
        }
        Flow(initialWidth >= breakpoint ? 2 : 1);
        grid.SizeChanged += (_, e) => Flow(e.NewSize.Width < breakpoint ? 1 : 2);
        return grid;
    }
}
