// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Fixtures sintéticos; nenhum jogo é iniciado e nenhum arquivo real da Steam é alterado.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class GamesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-games-" + Guid.NewGuid().ToString("N"));
    public GamesTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private const string LibraryVdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"{0}\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"1493710\"\t\t\"1453632518\"\n\t\t}\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"{1}\"\n\t}\n\t\"2\"\n\t{\n\t\t\"path\"\t\t\"/nonexistent/lib\"\n\t}\n}\n";
    private static string Acf(string id, string name, string dir, int flags = 4) => $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{id}\"\n\t\"name\"\t\t\"{name}\"\n\t\"StateFlags\"\t\t\"{flags}\"\n\t\"installdir\"\t\t\"{dir}\"\n}}\n";

    private string SteamTree(out string home)
    {
        home = Path.Combine(root, "home"); string steam = Path.Combine(home, ".local/share/Steam"); string apps = Path.Combine(steam, "steamapps"); Directory.CreateDirectory(Path.Combine(apps, "common"));
        string lib2 = Path.Combine(root, "SteamLibrary"); Directory.CreateDirectory(Path.Combine(lib2, "steamapps/common"));
        File.WriteAllText(Path.Combine(apps, "libraryfolders.vdf"), LibraryVdf.Replace("{0}", steam).Replace("{1}", lib2));
        Directory.CreateDirectory(Path.Combine(apps, "common/Game One")); File.WriteAllText(Path.Combine(apps, "appmanifest_100.acf"), Acf("100", "Game One", "Game One"));
        Directory.CreateDirectory(Path.Combine(apps, "common/Proton - Experimental")); File.WriteAllText(Path.Combine(apps, "appmanifest_1493710.acf"), Acf("1493710", "Proton Experimental", "Proton - Experimental"));
        Directory.CreateDirectory(Path.Combine(apps, "common/Half")); File.WriteAllText(Path.Combine(apps, "appmanifest_101.acf"), Acf("101", "Half Installed", "Half", flags: 1026));
        File.WriteAllText(Path.Combine(apps, "appmanifest_102.acf"), Acf("102", "Escape", "../../../../etc"));
        File.WriteAllText(Path.Combine(apps, "appmanifest_103.acf"), Acf("1x3", "Bad Id", "Game One"));
        Directory.CreateDirectory(Path.Combine(lib2, "steamapps/common/Second")); File.WriteAllText(Path.Combine(lib2, "steamapps/appmanifest_200.acf"), Acf("200", "Game Two", "Second"));
        return steam;
    }

    [Fact]
    public void VdfRoundTripsRealLibraryFoldersAndPreservesOrderAndEscapes()
    {
        var node = Vdf.Parse(LibraryVdf.Replace("{0}", "/a b").Replace("{1}", "/c"));
        Assert.Equal("/a b", node.Child("libraryfolders")!.Child("0")!.Get("path"));
        string again = Vdf.Serialize(node); Assert.Equal(again, Vdf.Serialize(Vdf.Parse(again)));
        var n = new VdfNode(); n.Set("k", "a\"b\\c\nd"); Assert.Equal("a\"b\\c\nd", Vdf.Parse(Vdf.Serialize(n)).Get("k"));
        Assert.Empty(Vdf.Parse("").Items); Assert.Null(Vdf.Parse("\"a\" { \"b\"").Child("a")?.Get("zzz"));
    }

    [Fact]
    public void SteamScanReadsAllLibrariesAndSkipsRuntimesIncompleteAndEscapingManifests()
    {
        SteamTree(out string home);
        var games = new LinuxGameLibrary(home).Steam().OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(["100", "200"], games.Select(g => g.Id).ToArray());
        Assert.Equal("Game One", games[0].Name); Assert.All(games, g => Assert.Equal("Steam", g.Store)); Assert.Equal("Steam:100", games[0].Key);
        Assert.EndsWith("steamapps/common/Game One", games[0].InstallDir);
    }

    [Fact]
    public void LutrisHeroicAndLegendaryFormatsAreParsedDefensively()
    {
        var lutris = LinuxGameLibrary.ParseLutris("2026-01-01: log line\n[{\"id\":1,\"slug\":\"x\",\"name\":\"It Takes Two\",\"runner\":\"wine\",\"directory\":\"/g/ittakestwo\"},{\"id\":\"bad\"},{\"id\":2,\"name\":\"\"}]");
        var l = Assert.Single(lutris); Assert.Equal("Lutris", l.Store); Assert.Equal("lutris:rungameid/1", l.LaunchUri); Assert.Equal("/g/ittakestwo", l.InstallDir);
        Assert.Empty(LinuxGameLibrary.ParseLutris("no json here"));
        var epic = LinuxGameLibrary.ParseLegendary("{\"Fortnite\":{\"title\":\"Fortnite\",\"install_path\":\"/g/fn\",\"executable\":\"bin/fn\"},\"Bad\":{\"title\":\"Rel\",\"install_path\":\"relative\"},\"Esc\":{\"title\":\"E\",\"install_path\":\"/g/e\",\"executable\":\"../../x\"}}");
        Assert.Equal(2, epic.Count); Assert.Equal("/g/fn/bin/fn", epic[0].ExePath); Assert.Equal("heroic://launch/legendary/Fortnite", epic[0].LaunchUri); Assert.Null(epic[1].ExePath);
        var gog = LinuxGameLibrary.ParseHeroicGog("{\"installed\":[{\"appName\":\"123\",\"install_path\":\"/g/gog\"},{\"appName\":\"9\",\"install_path\":\"rel\"}]}", "{\"games\":[{\"app_name\":\"123\",\"title\":\"Witcher\"}]}");
        var one = Assert.Single(gog); Assert.Equal("Witcher", one.Name); Assert.Equal("GOG", one.Store); Assert.Equal("heroic://launch/gog/123", one.LaunchUri);
        Assert.Empty(LinuxGameLibrary.ParseHeroicGog("{}", "not json"));
    }

    [Fact]
    public async Task ScanMergesStoresSortsManualGamesAndRejectsUnsafeExecutables()
    {
        SteamTree(out string home);
        string ok = Path.Combine(root, "bin/mygame.sh"); Directory.CreateDirectory(Path.GetDirectoryName(ok)!); File.WriteAllText(ok, "#!/bin/sh\n"); File.SetUnixFileMode(ok, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string noExec = Path.Combine(root, "bin/noexec"); File.WriteAllText(noExec, "x");
        string link = Path.Combine(root, "bin/link"); File.CreateSymbolicLink(link, ok);
        var lib = new LinuxGameLibrary(home, (_, _) => Task.FromResult((0, "[{\"id\":7,\"name\":\"Aaa Lutris\"}]")));
        var all = await lib.ScanAsync([new() { Name = "Manual Game", Exe = ok }, new() { Name = "NoExec", Exe = noExec }, new() { Name = "Link", Exe = link }, new() { Name = "Rel", Exe = "relative" }, new() { Name = "Proc", Exe = "/proc/self/exe" }]);
        Assert.Equal(["Aaa Lutris", "Game One", "Game Two", "Manual Game"], all.Select(g => g.Name).ToArray());
        Assert.Equal(ok, all.Single(g => g.Store == "Manual").ExePath);
        Assert.True(LinuxGameLibrary.IsRunnableFile(ok)); Assert.False(LinuxGameLibrary.IsRunnableFile(noExec)); Assert.False(LinuxGameLibrary.IsRunnableFile(link)); Assert.False(LinuxGameLibrary.IsRunnableFile("/dev/null"));
    }

    [Fact]
    public void MainExeIsFoundByNameElseLargestAndIgnoresInstallersAndHelpers()
    {
        string dir = Path.Combine(root, "game"); Directory.CreateDirectory(Path.Combine(dir, "bin"));
        void F(string rel, int size) { string p = Path.Combine(dir, rel); File.WriteAllBytes(p, new byte[size]); File.SetUnixFileMode(p, UnixFileMode.UserExecute | UnixFileMode.UserRead); }
        F("bin/crashhandler", 90000); F("uninstall.sh", 80000); F("bin/Big Adventure", 20000); F("bin/biggest", 70000);
        Assert.EndsWith("bin/Big Adventure", LinuxGameLibrary.FindMainExe(dir, "Big Adventure"));
        Assert.EndsWith("bin/biggest", LinuxGameLibrary.FindMainExe(dir, "Unrelated Title"));
        Assert.Null(LinuxGameLibrary.FindMainExe(Path.Combine(root, "missing"), "x"));
    }

    [Theory]
    [InlineData("source", "full", "1920x1080", 144, "", "-fullscreen -w 1920 -h 1080 -freq 144")]
    [InlineData("source", "borderless", "default", 0, "-novid", "-windowed -noborder -novid")]
    [InlineData("unity", "full", "2560x1440", 144, "", "-screen-fullscreen 1 -screen-width 2560 -screen-height 1440")]
    [InlineData("unity", "borderless", "default", 0, "", "-popupwindow")]
    [InlineData("unreal", "window", "1280x720", 0, "-dx12", "-windowed -ResX=1280 -ResY=720 -dx12")]
    [InlineData("source", "default", "garbage", 0, "", "")]
    public void GraphicsArgumentsMatchTheOriginalStyles(string style, string mode, string res, int hz, string extra, string expected) =>
        Assert.Equal(expected, GameLaunchOptions.BuildArgs(new() { Style = style, WinMode = mode, Res = res, Hz = hz, Extra = extra }));

    [Fact]
    public void SteamOptionsComposeEnvironmentWrappersCommandAndArgs()
    {
        Assert.Equal("", GameLaunchOptions.BuildSteamOptions(new(), "NVIDIA"));
        var p = new GameProfile { GpuHigh = true, MangoHud = true, GameMode = true, WinMode = "full", Extra = "-novid" };
        Assert.Equal("__NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia __VK_LAYER_NV_optimus=NVIDIA_only DXVK_CONFIG='dxgi.syncInterval = 1; d3d9.presentInterval = 1' mangohud gamemoderun %command% -fullscreen -novid", GameLaunchOptions.BuildSteamOptions(p, "NVIDIA"));
        Assert.Equal("DRI_PRIME=1 %command%", GameLaunchOptions.BuildSteamOptions(new() { GpuHigh = true }, "AMD"));
        Assert.Equal("%command% -novid", GameLaunchOptions.BuildSteamOptions(new() { Extra = "-novid" }, "AMD"));
    }

    [Theory]
    [InlineData("a b  c", new[] { "a", "b", "c" })]
    [InlineData("--name \"two words\" 'x y' z", new[] { "--name", "two words", "x y", "z" })]
    [InlineData("a\\ b \"q\\\"x\"", new[] { "a b", "q\"x" })]
    [InlineData("", new string[0])]
    [InlineData("''", new[] { "" })]
    [InlineData("$HOME;rm `id`", new[] { "$HOME;rm", "`id`" })] // no expansion: arguments stay literal
    public void ArgumentSplittingHasNoShellExpansion(string text, string[] expected) => Assert.Equal(expected, GameLaunchOptions.SplitArguments(text));

    [Fact]
    public void UnterminatedQuoteIsRejected() => Assert.Throws<FormatException>(() => GameLaunchOptions.SplitArguments("a \"b"));

    [Fact]
    public void ManualLaunchCommandUsesWrappersAndPrimeEnvironmentWithoutAShell()
    {
        var p = new GameProfile { MangoHud = true, GameMode = true, GpuHigh = true, WinMode = "window", Extra = "-x" };
        var cmd = LinuxGameLauncher.BuildManual("/g/game", p, "--flag \"a b\"", "AMD", n => "/usr/bin/" + n);
        Assert.Equal("/usr/bin/mangohud", cmd.Program); Assert.Equal(["/usr/bin/gamemoderun", "/g/game", "--flag", "a b", "-windowed", "-x"], cmd.Arguments.ToArray()); Assert.Equal([new("DRI_PRIME", "1")], cmd.Environment.ToArray());
        var plain = LinuxGameLauncher.BuildManual("/g/game", new(), "", "AMD", _ => null); Assert.Equal("/g/game", plain.Program); Assert.Empty(plain.Arguments); Assert.Empty(plain.Environment);
        var missingTools = LinuxGameLauncher.BuildManual("/g/game", new() { MangoHud = true }, "", "AMD", _ => null); Assert.Equal("/g/game", missingTools.Program); // tool absent: launch without it
    }

    [Fact]
    public void GameProcessesAreFoundBySteamEnvironmentOrInstallFolderCommandLine()
    {
        string proc = Path.Combine(root, "proc"); Directory.CreateDirectory(Path.Combine(proc, "10")); Directory.CreateDirectory(Path.Combine(proc, "11")); Directory.CreateDirectory(Path.Combine(proc, "self"));
        File.WriteAllBytes(Path.Combine(proc, "10/environ"), System.Text.Encoding.UTF8.GetBytes("A=1\0SteamGameId=730\0B=2\0")); File.WriteAllBytes(Path.Combine(proc, "10/cmdline"), System.Text.Encoding.UTF8.GetBytes("x\0"));
        File.WriteAllBytes(Path.Combine(proc, "11/cmdline"), System.Text.Encoding.UTF8.GetBytes("/g/mygame/bin/run\0--x\0")); File.WriteAllBytes(Path.Combine(proc, "11/environ"), []);
        Assert.Equal([10], GameProcesses.Find(proc, new("Steam", "730", "CS", "/s/cs"), null).ToArray());
        Assert.Empty(GameProcesses.Find(proc, new("Steam", "731", "X", "/s/x"), null));
        Assert.Equal([11], GameProcesses.Find(proc, new("Manual", "e", "G", "/g/mygame"), null).ToArray());
        Assert.Equal([11], GameProcesses.Find(proc, new("Manual", "e", "G", "/other"), "/g/mygame/bin/run").ToArray());
        Assert.Empty(GameProcesses.Find(Path.Combine(root, "none"), new("Steam", "1", "x", "/x"), null));
    }

    [Fact]
    public void SteamLaunchOptionsAreWrittenWithBackupOnlyWhenSteamIsClosed()
    {
        string steam = SteamTree(out string home); string cfgDir = Path.Combine(steam, "userdata/123/config"); Directory.CreateDirectory(cfgDir);
        string cfg = Path.Combine(cfgDir, "localconfig.vdf");
        File.WriteAllText(cfg, "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"100\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-old\"\n\t\t\t\t\t\t\"Other\"\t\t\"keep\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n");
        string proc = Path.Combine(root, "proc"); Directory.CreateDirectory(Path.Combine(proc, "5")); File.WriteAllText(Path.Combine(proc, "5/comm"), "steam\n");
        var running = new SteamInfo(home, proc);
        Assert.True(running.IsRunning); Assert.Contains("Feche-a", running.SetLaunchOptions("100", "-new")); Assert.Equal("-old", running.GetLaunchOptions("100"));
        var closed = new SteamInfo(home, Path.Combine(root, "noproc"));
        Assert.False(closed.IsRunning);
        Assert.Null(closed.SetLaunchOptions("100", "DRI_PRIME=1 %command% -fullscreen")); Assert.Equal("DRI_PRIME=1 %command% -fullscreen", closed.GetLaunchOptions("100"));
        Assert.True(File.Exists(cfg + ".ilmenyx.bak")); Assert.Contains("-old", File.ReadAllText(cfg + ".ilmenyx.bak"));
        Assert.Contains("\"Other\"\t\t\"keep\"", File.ReadAllText(cfg)); // other keys preserved
        Assert.Null(closed.SetLaunchOptions("555", "-x")); Assert.Equal("-x", closed.GetLaunchOptions("555"));
        Assert.Null(closed.SetLaunchOptions("100", "  ")); Assert.Null(closed.GetLaunchOptions("100"));
        Assert.Contains("inválido", closed.SetLaunchOptions("1;2", "-x"));
        Assert.Contains("localconfig", new SteamInfo(Path.Combine(root, "nohome"), Path.Combine(root, "noproc")).SetLaunchOptions("100", "-x"));
    }

    [Fact]
    public void SettingsRoundTripAtomicallyAndSurviveCorruptFiles()
    {
        string path = Path.Combine(root, "cfg/ilmenyx/settings.json");
        var s = new IlmenyxSettings(); s.GameProfiles["Steam:100"] = new GameProfile { HighPerfPlan = true, Res = "1920x1080", Helpers = [new() { Path = "/bin/true", Args = "-q" }] }; s.CustomGames.Add(new() { Name = "G", Exe = "/g" });
        s.Save(path); Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        var back = IlmenyxSettings.Load(path); Assert.True(back.GameProfiles["Steam:100"].HighPerfPlan); Assert.Equal("-q", back.GameProfiles["Steam:100"].Helpers[0].Args); Assert.Single(back.CustomGames);
        File.WriteAllText(path, "{corrupt"); Assert.Empty(IlmenyxSettings.Load(path).GameProfiles); Assert.Empty(IlmenyxSettings.Load(Path.Combine(root, "none.json")).GameProfiles);
    }
}
