// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Text.RegularExpressions;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public class ScrollChainTests
{
    [Fact]
    public void Down_scrolls_the_page_first_then_the_list()
    {
        Assert.Equal((100.0, 0.0), ScrollChain.Step(50, 300, 0, 2000, 50));
        // The step that reaches the sticky point carries the remainder into the list: no lost wheel notch, no jump.
        Assert.Equal((300.0, 30.0), ScrollChain.Step(280, 300, 0, 2000, 50));
        Assert.Equal((300.0, 550.0), ScrollChain.Step(300, 300, 500, 2000, 50));
    }

    [Fact]
    public void Up_returns_the_list_to_its_top_before_the_page()
    {
        Assert.Equal((300.0, 450.0), ScrollChain.Step(300, 300, 500, 2000, -50));
        Assert.Equal((280.0, 0.0), ScrollChain.Step(300, 300, 30, 2000, -50));
        Assert.Equal((250.0, 0.0), ScrollChain.Step(300, 300, 0, 2000, -50));
        // Even if the page scroll bar was dragged up while the list was scrolled, going up empties the list first.
        Assert.Equal((100.0, 150.0), ScrollChain.Step(100, 300, 200, 2000, -50));
    }

    [Fact]
    public void Offsets_are_clamped_at_both_ends()
    {
        Assert.Equal((0.0, 0.0), ScrollChain.Step(10, 300, 0, 2000, -500));
        Assert.Equal((300.0, 2000.0), ScrollChain.Step(0, 300, 0, 2000, 1e9));
        Assert.Equal((0.0, 0.0), ScrollChain.Step(0, -5, 0, -1, 50)); // nothing to scroll
        Assert.Equal((300.0, 0.0), ScrollChain.Step(400, 300, -3, 2000, 0)); // stale offsets after a resize
        Assert.Equal((20.0, 0.0), ScrollChain.Step(20, 300, 0, 2000, double.NaN));
    }

    [Fact]
    public void Ease_out_starts_at_zero_and_ends_at_one()
    {
        Assert.Equal(0, ScrollChain.EaseOut(0)); Assert.Equal(1, ScrollChain.EaseOut(1)); Assert.Equal(1, ScrollChain.EaseOut(3));
        Assert.True(ScrollChain.EaseOut(0.5) > 0.5);
    }
}

public class ColorContrastTests
{
    [Fact]
    public void Ratio_matches_wcag_reference_values()
    {
        Assert.Equal(21, ColorContrast.Ratio(ColorContrast.Parse("#000000"), ColorContrast.Parse("#FFFFFF")), 3);
        Assert.Equal(1, ColorContrast.Ratio(ColorContrast.Parse("#777777"), ColorContrast.Parse("#777777")), 3);
        Assert.Equal(4.48, ColorContrast.Ratio(ColorContrast.Parse("#777777"), ColorContrast.Parse("#FFFFFF")), 2);
        var over = ColorContrast.Parse("#80FFFFFF", new ColorContrast.Rgb(0, 0, 0));
        Assert.Equal(128, over.R, 0);
    }

    /// <summary>The colours of the Palette (dark, light), read from the source so the test follows any palette change.</summary>
    private static IReadOnlyList<(string Dark, string Light)> PaletteColors()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ilmenyx.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Ilmenyx.Desktop", "Palette.cs"));
        return Regex.Matches(src, @"\(""(#[0-9A-Fa-f]{6,8})"",\s*""(#[0-9A-Fa-f]{6,8})""\)").Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Report_page_text_reaches_wcag_aa_on_cards(bool light)
    {
        var c = PaletteColors();
        string Pick(int i) => light ? c[i].Light : c[i].Dark;
        // Bg (0), Card (2) over Bg, plus the darkest dark ambient (wine glow over the base) as the worst case behind the card.
        var backgrounds = new List<ColorContrast.Rgb> { ColorContrast.Parse(Pick(2), ColorContrast.Parse(Pick(0))) };
        if (!light) backgrounds.Add(ColorContrast.Parse(Pick(2), ColorContrast.Parse("#4A6F1C2A", ColorContrast.Parse("#19171C"))));
        foreach (var bg in backgrounds)
        {
            Assert.True(ColorContrast.Ratio(ColorContrast.Parse(Pick(5)), bg) >= 7, "Fg (titles)");
            Assert.True(ColorContrast.Ratio(ColorContrast.Parse(Pick(6)), bg) >= 4.5, "Muted (descriptions)");
        }
    }
}
