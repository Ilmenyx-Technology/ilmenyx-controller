// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions (the original OSD is a transparent always-on-top click-through window): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>
/// Small borderless always-on-top window with the chosen metrics. It is a normal Ilmenyx window: it does not inject into games, and
/// whether it stays above a fullscreen game (and whether clicks pass through) is decided by the window manager/compositor.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly StackPanel body = new() { Name = "OverlayLines", Spacing = 1 };
    private readonly Polyline graph = new() { Name = "OverlayGraph", Stroke = Palette.Accent, StrokeThickness = 1.5, Height = 40, Width = 200, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
    private double scale = 1;
    public string Corner { get; set; } = "top-left";

    public OverlayWindow()
    {
        Icon = AppIcon.Window; Title = "Ilmenyx · Overlay"; WindowDecorations = WindowDecorations.None; Topmost = true; ShowInTaskbar = false; ShowActivated = false; CanResize = false;
        Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent]; SizeToContent = SizeToContent.WidthAndHeight; Focusable = false;
        Content = new Border { Background = Brush.Parse("#CC181A1C"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = new StackPanel { Children = { body, graph } } };
        SizeChanged += (_, _) => Reposition();
    }

    public void SetScale(double value) { scale = Math.Clamp(value, MangoHudConfig.MinScale, MangoHudConfig.MaxScale); }

    public void Update(IReadOnlyList<OverlayLine> lines, IReadOnlyList<double> frametimesMs, bool showGraph)
    {
        body.Children.Clear();
        foreach (var line in lines)
            body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = {
                new TextBlock { Text = line.Label, Foreground = Palette.Faint, FontSize = 11 * scale, Width = line.Label.Length == 0 ? 0 : 42 * scale, FontWeight = FontWeight.Bold },
                new TextBlock { Text = line.Value, Foreground = Brushes.White, FontSize = 13 * scale, FontFamily = new FontFamily("monospace") } } });
        graph.IsVisible = showGraph && frametimesMs.Count > 1;
        if (graph.IsVisible)
        {
            double max = Math.Max(33.3, frametimesMs.Max()), w = 200 * scale, h = 40;
            graph.Width = w;
            graph.Points = frametimesMs.Select((v, i) => new Point(w * i / (frametimesMs.Count - 1), h - h * Math.Min(v, max) / max)).ToList();
        }
        Reposition();
    }

    public void Reposition()
    {
        if (Screens.Primary?.WorkingArea is not { } area || !IsVisible) return;
        double s = Screens.Primary.Scaling; int w = (int)(Bounds.Width * s), h = (int)(Bounds.Height * s), margin = 16;
        int x = Corner.EndsWith("right", StringComparison.Ordinal) ? area.Right - w - margin : area.X + margin;
        int y = Corner.StartsWith("bottom", StringComparison.Ordinal) ? area.Bottom - h - margin : area.Y + margin;
        Position = new PixelPoint(x, y);
    }
}
