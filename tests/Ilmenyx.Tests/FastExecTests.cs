// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class FastExecTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilx-fx-" + Guid.NewGuid());
    private string Installer { get; }

    public FastExecTests()
    {
        Directory.CreateDirectory(root);
        Installer = Path.Combine(root, "setup.exe"); File.WriteAllBytes(Installer, [0x4d, 0x5a]);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    [Fact]
    public void PlanUsesAThrowawayPrefixAndNoMenuEntries()
    {
        var plan = FastExec.Plan(Installer, Path.Combine(root, "Jogos"), n => "/usr/bin/" + n, Path.Combine(root, "fx"), "abc");
        Assert.Equal("/usr/bin/umu-run", plan.Program);
        Assert.Equal([Installer], plan.Arguments);
        Assert.Equal(Path.Combine(root, "fx", "fx-abc"), plan.TempPrefix);
        Assert.Contains(new KeyValuePair<string, string>("WINEPREFIX", plan.TempPrefix), plan.Environment);
        Assert.Contains(new KeyValuePair<string, string>("WINEDLLOVERRIDES", "winemenubuilder.exe=d"), plan.Environment);
        Assert.Contains(new KeyValuePair<string, string>("GAMEID", "umu-default"), plan.Environment);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/usr")]
    [InlineData("/proc/1")]
    [InlineData("relative/dir")]
    public void RefusesSystemOrRelativeDestinations(string dest) =>
        Assert.Throws<ArgumentException>(() => FastExec.Plan(Installer, dest, n => "/usr/bin/" + n, root));

    [Fact]
    public void RefusesTheWholeHomeFolderAndNonExeInstallers()
    {
        Assert.Throws<ArgumentException>(() => FastExec.Plan(Installer, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), n => "/x/" + n, root));
        string sh = Path.Combine(root, "a.sh"); File.WriteAllText(sh, "x");
        Assert.Throws<ArgumentException>(() => FastExec.Plan(sh, Path.Combine(root, "J"), n => "/x/" + n, root));
        Assert.Throws<InvalidOperationException>(() => FastExec.Plan(Installer, Path.Combine(root, "J"), _ => null, root));
    }

    [Fact]
    public void CleanupRemovesThePrefixButNeverTheInstalledGame()
    {
        if (!OperatingSystem.IsLinux()) return;
        var plan = FastExec.Plan(Installer, Path.Combine(root, "Jogos"), n => "/usr/bin/" + n, Path.Combine(root, "fx"), "t1");
        FastExec.Prepare(plan);
        Assert.Equal(plan.Destination, new FileInfo(Path.Combine(plan.TempPrefix, "dosdevices", "d:")).LinkTarget);
        string game = Path.Combine(plan.Destination, "Game", "game.exe"); Directory.CreateDirectory(Path.GetDirectoryName(game)!); File.WriteAllBytes(game, [1, 2, 3]);
        Directory.CreateDirectory(Path.Combine(plan.TempPrefix, "drive_c", "windows")); File.WriteAllText(Path.Combine(plan.TempPrefix, "system.reg"), "reg");
        FastExec.Cleanup(plan);
        Assert.False(Directory.Exists(plan.TempPrefix));
        Assert.True(File.Exists(game), "the installed game must survive the cleanup");
    }

    [Fact]
    public void StaleCleanupOnlyTouchesItsOwnFolders()
    {
        string fx = Path.Combine(root, "fx"); Directory.CreateDirectory(Path.Combine(fx, "fx-old")); Directory.CreateDirectory(Path.Combine(fx, "keep-me"));
        Assert.Equal(1, FastExec.CleanupStale(fx));
        Assert.True(Directory.Exists(Path.Combine(fx, "keep-me")));
        Assert.False(Directory.Exists(Path.Combine(fx, "fx-old")));
    }

    [Fact]
    public void FindsTheNewGameExecutableAndSkipsInstallerLeftovers()
    {
        string dest = Path.Combine(root, "J"); var since = DateTime.UtcNow.AddSeconds(-5);
        void Make(string rel, int size) { string p = Path.Combine(dest, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, new byte[size]); }
        Make("My Game/Binaries/Win64/MyGame.exe", 5000);
        Make("My Game/unins000.exe", 9000);
        Make("My Game/_Redist/vcredist_x64.exe", 9000);
        Make("My Game/Launcher.exe", 100);
        var found = FastExec.NewExecutables(dest, since);
        Assert.Equal(Path.Combine(dest, "My Game/Binaries/Win64/MyGame.exe"), found[0]);
        Assert.DoesNotContain(found, f => f.Contains("unins") || f.Contains("vcredist"));
    }
}
