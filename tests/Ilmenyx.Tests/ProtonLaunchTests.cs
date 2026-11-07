// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
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
}
