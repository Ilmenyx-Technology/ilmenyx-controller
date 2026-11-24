// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Desktop;
using Xunit;

namespace Ilmenyx.Tests;

public class GamesShowcaseTests
{
    private static LibGame Game(string store, string id, string? exe = null, string? kind = null) => new(store, id, "G", "/x", ExePath: exe, Kind: kind);

    [Fact]
    public void FilterClassifiesSteamWindowsAndLinux()
    {
        var steam = Game("Steam", "100"); var win = Game("Manual", "/g/a.exe", "/g/a.exe"); var native = Game("Manual", "/g/run.sh", "/g/run.sh"); var menu = Game("Linux", "foo");
        Assert.Equal(GameFilter.Steam, GameFilters.Of(steam));
        Assert.Equal(GameFilter.Windows, GameFilters.Of(win));
        Assert.Equal(GameFilter.Linux, GameFilters.Of(native));
        Assert.Equal(GameFilter.Linux, GameFilters.Of(menu));
    }

    [Fact]
    public void AllMatchesEverythingAndOthersOnlyTheirOwn()
    {
        var steam = Game("Steam", "100"); var win = Game("Manual", "/g/a.exe", "/g/a.exe");
        Assert.True(GameFilters.Matches(steam, GameFilter.All) && GameFilters.Matches(win, GameFilter.All));
        Assert.True(GameFilters.Matches(steam, GameFilter.Steam)); Assert.False(GameFilters.Matches(win, GameFilter.Steam));
        Assert.True(GameFilters.Matches(win, GameFilter.Windows)); Assert.False(GameFilters.Matches(steam, GameFilter.Linux));
    }

    [Theory]
    [InlineData("Half-Life 2", "HL")] [InlineData("Dota 2", "D2")] [InlineData("ark: survival", "AS")] [InlineData("???", "?")] [InlineData("", "?")] [InlineData("Celeste", "C")]
    public void InitialsForThePlaceholderCover(string name, string expected) => Assert.Equal(expected, GameArt.Initials(name));

    [Theory]
    [InlineData(900)] [InlineData(1000)] [InlineData(1099)] [InlineData(1100)] [InlineData(1180)] [InlineData(1600)] [InlineData(2400)]
    public void PostersAndPanelAlwaysFitTheirPage(double page)
    {
        var (panel, cols, poster) = ShowcaseLayout.Compute(page);
        Assert.True(cols >= 2 && poster >= ShowcaseLayout.MinPoster * 0.75 && poster <= ShowcaseLayout.MaxPoster);
        // grid + gap + panel (+ small paddings) never exceed the page, so the panel is never clipped
        Assert.True(cols * poster + (cols - 1) * ShowcaseLayout.Gap + ShowcaseLayout.Spacer + panel <= page, $"{page}: {cols}x{poster} + {panel}");
    }

    [Fact]
    public void PanelNarrowsBelowTheBreakpoint()
    {
        Assert.Equal(ShowcaseLayout.PanelNarrow, ShowcaseLayout.Compute(1099).Panel);
        Assert.Equal(ShowcaseLayout.PanelWide, ShowcaseLayout.Compute(1100).Panel);
    }
}
