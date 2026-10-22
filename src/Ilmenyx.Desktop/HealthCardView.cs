// New Ilmenyx additions (card layout follows the original CardTemplate: title/meta, verdict badge, reasons, 2-column metrics): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public static class HealthCardView
{
    public static Control Build(HealthCard c, Control? extra = null)
    {
        var badge = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, BorderBrush = Palette.Fg, BorderThickness = new Thickness(1), Padding = new Thickness(12, 4),
            CornerRadius = new CornerRadius(4), Background = c.IsAlert ? Palette.Fg : Brushes.Transparent,
            Child = new TextBlock { Name = "Verdict_" + c.Device, Text = c.VerdictText, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = c.IsAlert ? Palette.OnFg : Palette.Fg }
        };
        var top = new Grid();
        top.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 130, 0), Children = { new TextBlock { Text = c.Title, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = c.Meta, Foreground = Palette.Muted, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap } } });
        top.Children.Add(badge);
        var metrics = new UniformGrid { Columns = 2 };
        foreach (var (key, value) in c.Metrics)
            metrics.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 16, 8), Child = new StackPanel { Children = { new TextBlock { Text = key, Foreground = Palette.Muted, FontSize = 11 }, new TextBlock { Text = value, FontFamily = new FontFamily("monospace"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) } } } });
        var body = new StackPanel { Children = { top, new TextBlock { Text = c.Reasons, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) }, metrics } };
        if (extra is not null) body.Children.Add(extra);
        return new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 18), Margin = new Thickness(0, 0, 0, 14), Child = body };
    }
}
