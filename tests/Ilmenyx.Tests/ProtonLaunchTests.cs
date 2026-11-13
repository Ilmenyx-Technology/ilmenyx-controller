// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class ProtonLaunchTests
{
    private static string? Find(string n) => "/usr/bin/" + n;

    [Fact]
    public void WindowsGameRunsThroughUmuWithAPrefixPerGame()
    {
        var cmd = LinuxGameLauncher.BuildManual("/media/u/jogos/Mortal Kombat XL/Binaries/Retail/MK10.exe", new() { ProtonGameId = "307780" }, "", "NVIDIA", Find, prefixRoot: "/p");
        Assert.Equal("/usr/bin/umu-run", cmd.Program);
        Assert.Equal(["/media/u/jogos/Mortal Kombat XL/Binaries/Retail/MK10.exe"], cmd.Arguments);
        Assert.Contains(new KeyValuePair<string, string>("WINEPREFIX", "/p/mortal-kombat-xl"), cmd.Environment);
        Assert.Contains(new KeyValuePair<string, string>("GAMEID", "umu-307780"), cmd.Environment);
        Assert.Contains(new KeyValuePair<string, string>("STORE", "none"), cmd.Environment);
    }

    [Fact]
    public void WrappersStayInFrontAndPrimeVariablesAreKept()
    {
        var cmd = LinuxGameLauncher.BuildManual("/g/Some Title/game.EXE", new() { MangoHud = true, GameMode = true, GpuHigh = true }, "-dx11", "NVIDIA", Find, "/cfg/m.conf", "/p");
        Assert.Equal("/usr/bin/mangohud", cmd.Program);
        Assert.Equal(["/usr/bin/gamemoderun", "/usr/bin/umu-run", "/g/Some Title/game.EXE", "-dx11"], cmd.Arguments);
        Assert.Contains(cmd.Environment, e => e.Key == "__NV_PRIME_RENDER_OFFLOAD");
        Assert.Contains(new KeyValuePair<string, string>("MANGOHUD_CONFIGFILE", "/cfg/m.conf"), cmd.Environment);
        Assert.Contains(new KeyValuePair<string, string>("GAMEID", "umu-default"), cmd.Environment);
        Assert.Contains(new KeyValuePair<string, string>("WINEPREFIX", "/p/some-title"), cmd.Environment);
    }

    [Theory]
    [InlineData("12a")]
    [InlineData("12345678901")]
    [InlineData("")]
    [InlineData(null)]
    public void OnlyANumericSteamIdBecomesAGameId(string? id) =>
        Assert.Contains(new KeyValuePair<string, string>("GAMEID", "umu-default"), LinuxGameLauncher.ProtonEnvironment("/g/x/a.exe", id, "/p"));

    [Theory]
    [InlineData("/d/Some Game!!/bin/x64/a.exe", "some-game")]
    [InlineData("/d/Ação & Cia/a.exe", "a-o-cia")]
    [InlineData("/a.exe", "jogo")]
    public void PrefixFolderIsASafeSlug(string exe, string slug) =>
        Assert.Contains(new KeyValuePair<string, string>("WINEPREFIX", "/p/" + slug), LinuxGameLauncher.ProtonEnvironment(exe, null, "/p"));

    [Fact]
    public void WithoutUmuAWindowsGameFailsClearly() =>
        Assert.Contains("umu-launcher", Assert.Throws<InvalidOperationException>(() => LinuxGameLauncher.BuildManual("/g/a.exe", new(), "", "AMD", _ => null)).Message);

    [Fact]
    public void FullscreenGuardForcesVsyncOnlyForProtonOnTheNvidiaPrimeGpu()
    {
        var on = LinuxGameLauncher.BuildManual("/g/X/a.exe", new() { GpuHigh = true }, "", "NVIDIA", Find, prefixRoot: "/p");
        Assert.Contains(new KeyValuePair<string, string>("DXVK_CONFIG", GameLaunchOptions.GuardValue), on.Environment);
        Assert.DoesNotContain(LinuxGameLauncher.BuildManual("/g/X/a.exe", new() { GpuHigh = true, FullscreenGuard = false }, "", "NVIDIA", Find, prefixRoot: "/p").Environment, e => e.Key == "DXVK_CONFIG");
        Assert.DoesNotContain(LinuxGameLauncher.BuildManual("/g/X/a.exe", new() { GpuHigh = false }, "", "NVIDIA", Find, prefixRoot: "/p").Environment, e => e.Key == "DXVK_CONFIG");
        Assert.DoesNotContain(LinuxGameLauncher.BuildManual("/g/X/a.exe", new() { GpuHigh = true }, "", "AMD", Find, prefixRoot: "/p").Environment, e => e.Key == "DXVK_CONFIG");
        Assert.DoesNotContain(LinuxGameLauncher.BuildManual("/g/game", new() { GpuHigh = true }, "", "NVIDIA", Find).Environment, e => e.Key == "DXVK_CONFIG"); // native game
        Assert.Contains("DXVK_CONFIG='dxgi.syncInterval = 1; d3d9.presentInterval = 1' %command%", GameLaunchOptions.BuildSteamOptions(new() { GpuHigh = true }, "NVIDIA"));
        Assert.DoesNotContain("DXVK_CONFIG", GameLaunchOptions.BuildSteamOptions(new() { GpuHigh = true, FullscreenGuard = false }, "NVIDIA"));
    }

    [Fact]
    public void NativeGamesAreUnchanged() => Assert.Equal("/g/game", LinuxGameLauncher.BuildManual("/g/game", new(), "", "AMD", Find).Program);

    [Fact]
    public void WindowsExeNeedsNoExecuteBit()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ilx-exe-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "Game.exe"); File.WriteAllBytes(exe, [0x4d, 0x5a]);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.True(LinuxGameLibrary.IsWindowsExe(exe)); Assert.False(LinuxGameLibrary.IsRunnableFile(exe));
        Assert.False(LinuxGameLibrary.IsWindowsExe(Path.Combine(dir, "missing.exe"))); Assert.False(LinuxGameLibrary.IsWindowsExe("relative.exe"));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void BundledUmuWrapperRunsTheZipappWithPython()
    {
        string root = Path.Combine(Path.GetTempPath(), "ilx-umu-" + Guid.NewGuid()), data = Path.Combine(root, "data");
        Directory.CreateDirectory(Path.Combine(root, "umu")); File.WriteAllText(Path.Combine(root, "umu", "umu-run"), "zip");
        string wrapper = BundledComponents.ResolveIn("umu-run", root, data)!;
        string text = File.ReadAllText(wrapper);
        Assert.Contains("exec python3 " + GameLaunchOptions.ShellQuote(Path.Combine(root, "umu", "umu-run")) + " \"$@\"", text);
        Assert.Contains("command -v python3", text);
        Assert.Null(BundledComponents.ResolveIn("umu-run", Path.Combine(root, "none"), data));
        Directory.Delete(root, true);
    }

    [Fact]
    public void TheProfileChoosesThePrefixTheProtonBuildOrAPlainWine()
    {
        string root = Directory.CreateTempSubdirectory("ilx-runners").FullName;
        try
        {
            string ge = Path.Combine(root, ".local/share/Steam/compatibilitytools.d/GE-Proton10-34"), wine = Path.Combine(root, ".local/share/ilmenyx/runners/wine-soda-11.0-10");
            Directory.CreateDirectory(ge); File.WriteAllText(Path.Combine(ge, "proton"), "");
            Directory.CreateDirectory(Path.Combine(wine, "bin")); File.WriteAllText(Path.Combine(wine, "bin/wine"), ""); File.WriteAllText(Path.Combine(wine, "bin/wineserver"), "");
            Directory.CreateDirectory(Path.Combine(root, ".local/share/Steam/compatibilitytools.d/not-a-runner"));
            Assert.Equal([("wine-soda-11.0-10", true), ("GE-Proton10-34", false)], GameRunners.Find(root).Select(r => (r.Name, r.Wine)).OrderByDescending(r => r.Wine));

            var auto = LinuxGameLauncher.BuildManual("/g/X/a.exe", new(), "", "AMD", Find, prefixRoot: "/p");
            Assert.DoesNotContain(auto.Environment, e => e.Key == "PROTONPATH");
            var chosen = LinuxGameLauncher.BuildManual("/g/X/a.exe", new() { Runner = ge, WinePrefix = "/saves/x" }, "", "AMD", Find, prefixRoot: "/p");
            Assert.Contains(new KeyValuePair<string, string>("PROTONPATH", ge), chosen.Environment);
            Assert.Contains(new KeyValuePair<string, string>("WINEPREFIX", "/saves/x"), chosen.Environment);
            Assert.Contains(new KeyValuePair<string, string>("PROTONPATH", "GE-Proton"), LinuxGameLauncher.BuildManual("/g/X/a.exe", new() { Runner = GameRunners.LatestGe }, "", "AMD", Find, prefixRoot: "/p").Environment);
            Assert.DoesNotContain(LinuxGameLauncher.BuildManual("/g/X/a.exe", new() { Runner = "/gone" }, "", "AMD", Find, prefixRoot: "/p").Environment, e => e.Key == "PROTONPATH"); // deleted build: back to automatic

            var plain = LinuxGameLauncher.BuildManual("/g/X/a.exe", new() { Runner = wine, MangoHud = true }, "", "AMD", Find, prefixRoot: "/p");
            Assert.Equal("/usr/bin/mangohud", plain.Program); // wrappers still come first
            Assert.Equal([Path.Combine(wine, "bin/wine"), "/g/X/a.exe"], plain.Arguments.Take(2));
            Assert.DoesNotContain(plain.Environment, e => e.Key is "GAMEID" or "PROTONPATH");
            Assert.Contains(new KeyValuePair<string, string>("WINEPREFIX", "/p/x"), plain.Environment);

            Assert.Null(GameRunners.PrefixProblem(Path.Combine(root, "novo"), root));
            string docs = Path.Combine(root, "Documentos"); Directory.CreateDirectory(docs); File.WriteAllText(Path.Combine(docs, "cv.odt"), "");
            Assert.NotNull(GameRunners.PrefixProblem(docs, root)); // would fill a personal folder with a C: drive
            File.WriteAllText(Path.Combine(docs, "system.reg"), ""); Assert.Null(GameRunners.PrefixProblem(docs, root)); // an existing prefix
            Assert.NotNull(GameRunners.PrefixProblem(root, root)); Assert.NotNull(GameRunners.PrefixProblem("/", root)); Assert.NotNull(GameRunners.PrefixProblem("relativa", root));
        }
        finally { Directory.Delete(root, true); }
    }
}
