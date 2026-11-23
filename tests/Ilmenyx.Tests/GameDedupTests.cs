// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Pure logic; no files, no network.
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class GameDedupTests
{
    private const string Appimage = "/mnt/HDD/Jogos/PPP2/God of War III Remastered/spartanport-x86_64.AppImage";
    private const string Alias = "/run/user/1000/doc/mhjHajIq2CyyjzTir5Z4EQ/MK10.exe";
    private const string Real = "/mnt/HDD/Jogos/PPP2/Mortal Kombat XL/Binaries/Retail/MK10.exe";

    private static string? Resolve(string p) => p == Alias ? Real : p.StartsWith("/run/user/", StringComparison.Ordinal) ? null : p;
    private static LibGame Manual(string exe, string name = "Jogo") => new("Manual", exe, name, Path.GetDirectoryName(exe) ?? "", exe);
    private static LibGame Menu(string exe, string name = "SpartanPort", string? icon = "/i.png", string args = "") => new("Linux", "spartanport", name, "", exe, null, args, "Linux", icon);

    [Fact]
    public void ManualAndMenuEntryOfTheSameProgramBecomeOneCardWithTheManualOnTop()
    {
        var g = GameDedup.Group([Manual(Appimage, "God Of War III Remastered"), Menu(Appimage)], Resolve);
        var card = Assert.Single(g);
        Assert.Equal("Manual", card.Game.Store); Assert.Equal("SpartanPort", Assert.Single(card.Merged).Name);
        Assert.Equal("/i.png", card.Merged[0].Icon); // the library takes the icon from the joined entry
        Assert.Contains("SpartanPort (", GameDedup.AlsoAppearsAs(card.Merged));
    }

    [Fact]
    public void QuotedExecWithArgumentsStillMatches()
    {
        var n = NativeGames.FromEntry("spartanport", $"[Desktop Entry]\nType=Application\nName=SpartanPort\nCategories=Game;\nExec=\"{Appimage}\" --fullscreen %U\n", false)!;
        var menu = new LibGame("Linux", n.Id, n.Name, "", n.Program, null, NativeGames.JoinArguments(n.Arguments), n.Kind, n.Icon);
        Assert.Single(GameDedup.Group([Manual(Appimage), menu], Resolve));
    }

    [Fact]
    public void PortalAliasAndRealPathJoinAndTheRealPathWins()
    {
        var g = GameDedup.Group([Manual(Alias, "Mortal Kombat X"), Manual(Real, "Mortal Kombat X")], Resolve);
        var card = Assert.Single(g);
        Assert.Equal(Real, card.Game.Id); Assert.Equal(Alias, Assert.Single(card.Merged).Id);
        Assert.Contains("à mão", GameDedup.AlsoAppearsAs(card.Merged));
    }

    [Fact]
    public void MoreProfileDataBreaksATieBetweenManualDuplicates()
    {
        var a = Manual("/g/a/x.exe", "A"); var b = Manual("/g/a/../a/x.exe", "B");
        Assert.Equal("B", GameDedup.Group([a, b], Resolve, g => g.Name == "B" ? 3 : 0)[0].Game.Name);
        Assert.Equal("A", GameDedup.Group([a, b], Resolve, g => g.Name == "A" ? 3 : 0)[0].Game.Name);
    }

    [Fact]
    public void StoreBeatsMenuEntryButNotManual()
    {
        var epic = new LibGame("Epic Games", "id", "Epic", "/e", "/e/game.sh");
        Assert.Equal("Epic Games", GameDedup.Group([Menu("/e/game.sh"), epic], Resolve)[0].Game.Store);
        Assert.Equal("Manual", GameDedup.Group([epic, Manual("/e/game.sh")], Resolve)[0].Game.Store);
    }

    [Fact]
    public void NothingJoinsWhenThePathCannotBeResolvedOrIsADifferentProgram()
    {
        Assert.Equal(2, GameDedup.Group([Manual("/run/user/1000/doc/zzz/MK10.exe"), Manual(Real)], Resolve).Count);
        Assert.Equal(2, GameDedup.Group([Manual("/g/a.exe"), Manual("/g/b.exe")], Resolve).Count);
    }

    [Fact]
    public void FlatpakLaunchersAndMenuEntriesAmongThemselvesStayApart()
    {
        var fp = new LibGame("Linux", "a.b", "A", "", "/usr/bin/flatpak", null, "run a.b", "Linux · Flatpak");
        Assert.Equal(2, GameDedup.Group([fp, fp with { Id = "c.d" }], Resolve).Count);
        Assert.Equal(2, GameDedup.Group([Menu("/usr/bin/wine"), Menu("/usr/bin/wine") with { Id = "other" }], Resolve).Count);
    }

    [Fact]
    public void FpsSessionsOfJoinedNamesCountForTheVisibleGame()
    {
        var at = DateTimeOffset.UtcNow;
        FpsSession S(string game) => new(game, at, 60, 50, 600, "GPU", "1920x1080", P10Fps: 50, P90Fps: 70, ValidSeconds: 500);
        var list = new[] { S("God Of War III Remastered"), S("SpartanPort") };
        Assert.Equal(1, FpsHistory.Build(list, "God Of War III Remastered")[0].Sessions);
        Assert.Equal(2, FpsHistory.Build(list, "God Of War III Remastered", sameGame: ["SpartanPort"])[0].Sessions);
    }

    [Fact]
    public void HidingEitherEntryHidesTheWholeGroup()
    {
        var card = Assert.Single(GameDedup.Group([Manual(Appimage), Menu(Appimage)], Resolve));
        var merged = new Dictionary<string, IReadOnlyList<LibGame>> { [card.Game.Key] = card.Merged };
        Assert.Contains(card.Game.Key, GameDedup.HiddenKeys(["Linux:spartanport"], merged));
        Assert.DoesNotContain(card.Game.Key, GameDedup.HiddenKeys([], merged));
    }
}
