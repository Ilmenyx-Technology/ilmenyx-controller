// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (visual follows the original Meter template: label, mono value, 4 px rounded accent bar): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

public sealed class MeterView : Grid
{
    private readonly TextBlock value = new() { FontFamily = IlmenyxTheme.Mono, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border fill = new() { Background = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Left, Height = 4, CornerRadius = new CornerRadius(2) };
    private readonly Grid track = new() { Height = 4, Margin = new Thickness(0, 7, 0, 0) };
    public string Label { get; }

    public MeterView(string label, string? name = null)
    {
        Label = label; Margin = new Thickness(0, 0, 0, 12); RowDefinitions = new("Auto,Auto"); ColumnDefinitions = new("Auto,*");
        if (name is not null) Name = name;
        Children.Add(new TextBlock { Text = label, Foreground = Palette.Muted, FontSize = 12.5, Margin = new Thickness(0, 0, 12, 0) });
        Grid.SetColumn(value, 1); Children.Add(value);
        track.Children.Add(new Border { Background = Palette.Hover, CornerRadius = new CornerRadius(2) }); track.Children.Add(fill); Grid.SetRow(track, 1); Grid.SetColumnSpan(track, 2); Children.Add(track);
        track.SizeChanged += (_, _) => Apply();
        Set("—", 0);
    }

    private double fraction;
    public string Text => value.Text ?? "";
    public double Fraction => fraction;
    public void Set(string text, double fraction)
    {
        value.Text = text; this.fraction = Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1); Apply();
    }
    private void Apply() => fill.Width = track.Bounds.Width * fraction;
}
