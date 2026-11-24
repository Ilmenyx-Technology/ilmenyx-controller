// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text.Json;
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

    [Fact]
    public void RecentNeedsARecordedTimeAndMostRecentPicksTheNewest()
    {
        var a = Game("Steam", "1"); var b = Game("Steam", "2"); var c = Game("Steam", "3");
        var times = new Dictionary<string, DateTimeOffset> { [a.Key] = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), [c.Key] = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero) };
        DateTimeOffset? At(LibGame g) => times.TryGetValue(g.Key, out var t) ? t : null;
        Assert.True(GameFilters.Matches(a, GameFilter.Recent, At)); Assert.False(GameFilters.Matches(b, GameFilter.Recent, At));
        Assert.False(GameFilters.Matches(a, GameFilter.Recent)); // no lookup, nothing is recent
        Assert.Equal(c, GameFilters.MostRecent([a, b, c], At));
        Assert.Null(GameFilters.MostRecent([b], At));
    }

    [Theory]
    [InlineData("Half-Life 2", "HL")] [InlineData("Dota 2", "D2")] [InlineData("ark: survival", "AS")] [InlineData("???", "?")] [InlineData("", "?")] [InlineData("Celeste", "C")]
    public void InitialsForThePlaceholderCover(string name, string expected) => Assert.Equal(expected, GameArt.Initials(name));

    [Theory]
    [InlineData(900)] [InlineData(1000)] [InlineData(1180)] [InlineData(1600)] [InlineData(2400)]
    public void CoversAlwaysFitTheirPage(double page)
    {
        var (cols, cover, _, _) = ShowcaseLayout.Compute(page);
        Assert.True(cols >= 2 && cover >= ShowcaseLayout.MinCover * 0.7 && cover <= ShowcaseLayout.MaxCover);
        Assert.True(cols * cover + (cols - 1) * ShowcaseLayout.Gap + ShowcaseLayout.Sides <= page, $"{page}: {cols}x{cover}");
    }

    [Fact]
    public void DrawerCoversTheWholePageBelowTheBreakpointAndHeroKeepsItsMinimum()
    {
        Assert.True(ShowcaseLayout.Compute(999).DrawerFull); Assert.False(ShowcaseLayout.Compute(1000).DrawerFull);
        Assert.Equal(ShowcaseLayout.DrawerWidth, ShowcaseLayout.Compute(1600).Drawer);
        Assert.Equal(ShowcaseLayout.MinHero, ShowcaseLayout.HeroHeight(400)); // 560 px window: the minimum wins
        Assert.Equal(450, ShowcaseLayout.HeroHeight(1000)); // otherwise ~45% of the page
    }

    [Fact]
    public void PlayTimeReadsAsMinutesThenHours()
    {
        Assert.Equal("45 min", LauncherPage.PlayTimeText(45 * 60 + 20));
        Assert.Equal("3 h 20 min", LauncherPage.PlayTimeText(3 * 3600 + 20 * 60));
    }

    /// <summary>Text on the hero is checked against the scrim over the WORST picture (all white or all black), never against a real image.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeroTextKeepsItsContrastOverAnyPicture(bool light)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ilmenyx.slnx"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Ilmenyx.Desktop", "Palette.cs"));
        var colors = System.Text.RegularExpressions.Regex.Matches(src, @"\(""(#[0-9A-Fa-f]{6,8})"",\s*""(#[0-9A-Fa-f]{6,8})""\)").Select(m => light ? m.Groups[2].Value : m.Groups[1].Value).ToList();
        var bg = ColorContrast.Parse(colors[0]);
        foreach (var picture in new[] { new ColorContrast.Rgb(255, 255, 255), new ColorContrast.Rgb(0, 0, 0) })
        {
            var under = new ColorContrast.Rgb(bg.R * HeroScrim.TextAlpha / 255 + picture.R * (255 - HeroScrim.TextAlpha) / 255, bg.G * HeroScrim.TextAlpha / 255 + picture.G * (255 - HeroScrim.TextAlpha) / 255, bg.B * HeroScrim.TextAlpha / 255 + picture.B * (255 - HeroScrim.TextAlpha) / 255);
            Assert.True(ColorContrast.Ratio(ColorContrast.Parse(colors[5]), under) >= 7, $"Fg (title, pills, buttons) over a {picture} picture");
            Assert.True(ColorContrast.Ratio(ColorContrast.Parse(colors[6]), under) >= 4.5, $"Muted (store line) over a {picture} picture");
        }
    }

    [Fact]
    public void RecordPlayKeepsTheLastStartAndAddsTheSeconds()
    {
        var p = new GameProfile(); var t1 = new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.Zero); var t2 = t1.AddDays(2);
        p.RecordPlay(t1, 600); p.RecordPlay(t2, 90.5); p.RecordPlay(t2.AddDays(1), -5); p.RecordPlay(t2.AddDays(1), double.NaN);
        Assert.Equal(t2.AddDays(1), p.LastPlayed); Assert.Equal(690.5, p.PlayedSeconds);
        Assert.Null(new GameProfile().LastPlayed); Assert.Equal(0, new GameProfile().PlayedSeconds);
    }

    [Fact]
    public void UnknownFieldsSurviveLoadAndSave()
    {
        string path = Path.Combine(Path.GetTempPath(), "ilx-fwd-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
            {
              "FutureTop": {"a": 1},
              "GameProfiles": { "Steam:1": { "MangoHud": true, "FutureProfile": "x", "LastPlayed": "2026-11-20T20:00:00+00:00", "PlayedSeconds": 120,
                                              "Helpers": [ { "Path": "/h", "FutureHelper": 7 } ] } },
              "CustomGames": [ { "Name": "N", "Exe": "/n", "FutureCustom": [1,2] } ],
              "BenchRuns": [ { "At": "2026-11-01T00:00:00+00:00", "Kind": "cpu", "Summary": "s", "Score": 1.5, "FutureBench": true } ],
              "FpsSessions": [ { "Game": "G", "At": "2026-11-01T00:00:00+00:00", "AvgFps": 60, "Low1Fps": 40, "DurationSeconds": 300, "Gpu": null, "Resolution": null, "FutureFps": "keep" } ]
            }
            """);
            IlmenyxSettings.Update(path, s => s.GameProfiles["Steam:1"].RecordPlay(DateTimeOffset.UtcNow, 10)); // any normal write
            var back = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            Assert.Equal(1, back.GetProperty("FutureTop").GetProperty("a").GetInt32());
            var profile = back.GetProperty("GameProfiles").GetProperty("Steam:1");
            Assert.Equal("x", profile.GetProperty("FutureProfile").GetString()); Assert.Equal(7, profile.GetProperty("Helpers")[0].GetProperty("FutureHelper").GetInt32());
            Assert.Equal(130, profile.GetProperty("PlayedSeconds").GetDouble());
            Assert.Equal(2, back.GetProperty("CustomGames")[0].GetProperty("FutureCustom").GetArrayLength());
            Assert.True(back.GetProperty("BenchRuns")[0].GetProperty("FutureBench").GetBoolean());
            Assert.Equal("keep", back.GetProperty("FpsSessions")[0].GetProperty("FutureFps").GetString());
            Assert.Equal("keep", IlmenyxSettings.Load(path).FpsSessions[0].UnknownFields!["FutureFps"].GetString());
        }
        finally { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f); }
    }
}
