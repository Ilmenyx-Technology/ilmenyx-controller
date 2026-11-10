// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Entradas de menu sintéticas, copiadas do formato das reais (Hytale Launcher e SuperTuxKart via Flatpak, Steam, Lutris, Bottles, ProtonPlus).
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class NativeGamesTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("ilmenyx-native-").FullName;
    public void Dispose() => Directory.Delete(root, true);

    private static string Entry(string name, string exec, string categories, string extra = "") =>
        $"[Desktop Entry]\nType=Application\nName={name}\nExec={exec}\nCategories={categories}\n{extra}\n[Desktop Action Other]\nName=Outra\nExec=/bin/false\n";

    [Fact]
    public void FlatpakGameKeepsItsCommandWithoutFieldCodes()
    {
        var g = NativeGames.FromEntry("net.supertuxkart.SuperTuxKart",
            Entry("SuperTuxKart", "/usr/bin/flatpak run --branch=stable --arch=x86_64 --command=supertuxkart --file-forwarding net.supertuxkart.SuperTuxKart @@ %F @@", "Game;ArcadeGame;"), true)!;
        Assert.Equal("/usr/bin/flatpak", g.Program); Assert.Equal("Linux · Flatpak", g.Kind);
        Assert.DoesNotContain("%F", g.Arguments); Assert.Contains("net.supertuxkart.SuperTuxKart", g.Arguments);
        Assert.Equal(g.Arguments, GameLaunchOptions.SplitArguments(NativeGames.JoinArguments(g.Arguments)));
    }

    [Fact]
    public void LocalizedNameAndQuotedArgumentsSurvive()
    {
        var g = NativeGames.FromEntry("jogo", Entry("Game", "\"/opt/Meu Jogo/start.sh\" --pasta \"a b\" 100%%", "Game;", "Name[pt_BR]=Jogo"), false)!;
        Assert.Equal("Jogo", g.Name); Assert.Equal("/opt/Meu Jogo/start.sh", g.Program); Assert.Equal(["--pasta", "a b", "100%"], g.Arguments);
        Assert.Equal("Linux", g.Kind);
        Assert.Equal(g.Arguments, GameLaunchOptions.SplitArguments(NativeGames.JoinArguments(g.Arguments)));
    }

    [Theory]
    [InlineData("steam", "Steam", "steam %U", "Network;FileTransfer;Game;")]
    [InlineData("net.lutris.Lutris", "Lutris", "lutris %U", "Game;PackageManager;")]
    [InlineData("com.usebottles.bottles", "Bottles", "bottles %u", "Utility;Game;Emulator;")]
    [InlineData("com.vysp3r.ProtonPlus", "ProtonPlus", "protonplus", "Game;")]
    [InlineData("Brawlhalla", "Brawlhalla", "steam steam://rungameid/291550", "Game;")] // already listed by Steam
    [InlineData("gimp", "GIMP", "gimp %U", "Graphics;")]
    public void StoresToolsAndNonGamesStayOut(string id, string name, string exec, string categories) =>
        Assert.Null(NativeGames.FromEntry(id, Entry(name, exec, categories), false));

    [Fact]
    public void HiddenEntriesStayOut()
    {
        Assert.Null(NativeGames.FromEntry("x", Entry("X", "/bin/x", "Game;", "NoDisplay=true"), false));
        Assert.Null(NativeGames.FromEntry("x", Entry("X", "/bin/x", "Game;", "Hidden=true"), false));
    }

    private static Stream Xml(string s) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(s));
    private const string Catalog = """
        <?xml version="1.0"?>
        <components origin="flathub">
          <component type="desktop-application"><id>org.outro.Jogo</id>
            <screenshots><screenshot type="default"><image type="source">https://exemplo.org/outro.png</image></screenshot></screenshots></component>
          <component type="desktop-application"><id>net.supertuxkart.SuperTuxKart</id><name>SuperTuxKart</name>
            <screenshots>
              <screenshot><image type="thumbnail" width="752" height="423">https://dl.flathub.org/b_752.png</image></screenshot>
              <screenshot type="default">
                <image type="source" width="1920" height="1080">https://dl.flathub.org/a_orig.png</image>
                <image type="thumbnail" width="1248" height="702">https://dl.flathub.org/a_1248.png</image>
                <image type="thumbnail" width="752" height="423">https://dl.flathub.org/a_752.png</image>
                <image type="thumbnail" width="224" height="126">https://dl.flathub.org/a_224.png</image>
              </screenshot>
            </screenshots></component>
        </components>
        """;

    [Fact]
    public void ScreenshotIsTheDefaultOneAtCardSize()
    {
        Assert.Equal("https://dl.flathub.org/a_752.png", NativeArt.ScreenshotUrl(Xml(Catalog), "net.supertuxkart.SuperTuxKart"));
        Assert.Equal("https://exemplo.org/outro.png", NativeArt.ScreenshotUrl(Xml(Catalog), "org.outro.Jogo")); // only the source image exists
        Assert.Null(NativeArt.ScreenshotUrl(Xml(Catalog), "com.hypixel.HytaleLauncher")); // not in the catalog: the card shows the icon
    }

    [Fact]
    public void MetainfoWithDesktopIdAndUnsafeAddresses()
    {
        string metainfo = "<component><id>jogo.desktop</id><screenshots><screenshot type=\"default\"><image>http://inseguro/a.png</image>" +
            "<image>file:///etc/passwd</image><image>https://ok.org/a.png</image></screenshot></screenshots></component>";
        Assert.Equal("https://ok.org/a.png", NativeArt.ScreenshotUrl(Xml(metainfo), "jogo"));
        Assert.Null(NativeArt.ScreenshotUrl(Xml("<component><id>jogo</id></component>"), "jogo"));
        Assert.Throws<System.Xml.XmlException>(() => NativeArt.ScreenshotUrl(Xml("<component><id>"), "jogo")); // the finder treats it as no image
    }

    [Fact]
    public void LibraryListsInstalledGamesAndUserEntryOverridesSystem()
    {
        string user = Path.Combine(root, "user"), system = Path.Combine(root, "system");
        Directory.CreateDirectory(user); Directory.CreateDirectory(system);
        string bin = Path.Combine(root, "jogo"); File.WriteAllText(bin, "#!/bin/sh\n"); File.SetUnixFileMode(bin, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(system, "jogo.desktop"), Entry("Jogo", bin + " %U", "Game;"));
        File.WriteAllText(Path.Combine(system, "escondido.desktop"), Entry("Escondido", bin, "Game;"));
        File.WriteAllText(Path.Combine(user, "escondido.desktop"), Entry("Escondido", bin, "Game;", "Hidden=true")); // user hid it
        File.WriteAllText(Path.Combine(system, "removido.desktop"), Entry("Removido", Path.Combine(root, "nao-existe"), "Game;"));
        var g = Assert.Single(new LinuxGameLibrary(root).Native([(user, false), (system, false)]));
        Assert.Equal("Linux", g.Store); Assert.Equal("Jogo", g.Name); Assert.Equal(bin, g.ExePath); Assert.Equal("", g.Args);
    }
}
