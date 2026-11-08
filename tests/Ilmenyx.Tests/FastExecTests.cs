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
        Assert.Equal([Path.Combine(plan.TempPrefix, "drive_c", "Instalador", "setup.exe")], plan.Arguments);
        Assert.Equal(Installer, plan.SourceInstaller);
        Assert.Equal(Path.Combine(root, "fx", "fx-abc"), plan.TempPrefix);
        Assert.Contains(new KeyValuePair<string, string>("WINEPREFIX", plan.TempPrefix), plan.Environment);
        Assert.Contains(new KeyValuePair<string, string>("WINEDLLOVERRIDES", "winemenubuilder.exe=d"), plan.Environment);
        Assert.Contains(new KeyValuePair<string, string>("GAMEID", "umu-default"), plan.Environment);
    }

    [Fact]
    public void InstallerRunsInCompatibilityModeWithALogAndOptionalGeProton()
    {
        var plan = FastExec.Plan(Installer, Path.Combine(root, "J"), n => "/usr/bin/" + n, Path.Combine(root, "fx"), "c1");
        Assert.DoesNotContain(plan.Environment, e => e.Key.StartsWith("PROTON_NO_", StringComparison.Ordinal)); // fsync/esync stay on (speed)
        Assert.Contains(new KeyValuePair<string, string>("PROTON_USE_XALIA", "0"), plan.Environment);
        Assert.Contains(new KeyValuePair<string, string>("PROTON_LOG_DIR", Path.Combine(root, "fx", "logs")), plan.Environment);
        Assert.DoesNotContain(plan.Environment, e => e.Key == "PROTONPATH");
        var ge = FastExec.Plan(Installer, Path.Combine(root, "J"), n => "/usr/bin/" + n, Path.Combine(root, "fx"), "c2", geProton: true);
        Assert.Contains(new KeyValuePair<string, string>("PROTONPATH", "GE-Proton"), ge.Environment);
    }

    [Fact]
    public void ClassicProtonPathOverridesTheDefaultAndGe()
    {
        var plan = FastExec.Plan(Installer, Path.Combine(root, "J"), n => "/usr/bin/" + n, Path.Combine(root, "fx"), "cl", geProton: true, protonPath: "/r/GE-Proton9-27");
        Assert.Single(plan.Environment, e => e.Key == "PROTONPATH");
        Assert.Contains(new KeyValuePair<string, string>("PROTONPATH", "/r/GE-Proton9-27"), plan.Environment);
        Assert.Equal(Path.Combine("/d", "ilmenyx", "runners", "GE-Proton9-27"), ClassicProton.Folder(ClassicProton.Root("/d")));
    }

    [Fact]
    public void AddressSpaceExhaustionIsRecognisedInTheProtonLog()
    {
        string log = Path.Combine(root, "steam-default.log");
        File.WriteAllLines(log, Enumerable.Repeat("73643.087:0128:02d4:err:virtual:allocate_virtual_memory out of memory for allocation, base (nil) size 50031000", 6));
        Assert.True(ClassicProton.LogShowsAddressSpaceExhaustion(log));
        File.WriteAllLines(log, ["err:virtual:allocate_virtual_memory out of memory for allocation", "fixme:x"]);
        Assert.False(ClassicProton.LogShowsAddressSpaceExhaustion(log));
        Assert.False(ClassicProton.LogShowsAddressSpaceExhaustion(Path.Combine(root, "missing.log")));
    }

    [Fact]
    public async Task ClassicProtonDownloadIsRejectedWhenTheHashDoesNotMatch()
    {
        var handler = new FakeHttp(new byte[1024]);
        using var http = new HttpClient(handler);
        string runners = Path.Combine(root, "runners");
        await Assert.ThrowsAsync<IOException>(() => ClassicProton.EnsureAsync(http, null, CancellationToken.None, runners));
        Assert.False(ClassicProton.IsInstalled(runners));
        Assert.Empty(Directory.GetFiles(runners)); // the bad download was discarded
        Assert.Equal(ClassicProton.Url, handler.Requested);
    }

    private sealed class FakeHttp(byte[] body) : HttpMessageHandler
    {
        public string? Requested { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Requested = request.RequestUri!.ToString(); return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) }); }
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
    public void InstalledGameIsMovedOutOfTheTemporaryWindowsBeforeCleanup()
    {
        if (!OperatingSystem.IsLinux()) return;
        var plan = FastExec.Plan(Installer, Path.Combine(root, "Jogos"), n => "/usr/bin/" + n, Path.Combine(root, "fx"), "mv");
        FastExec.Prepare(plan);
        Assert.True(Directory.Exists(FastExec.InstallFolder(plan)), "C:\\Jogos must exist for the installer");
        string c = Path.Combine(plan.TempPrefix, "drive_c");
        void Make(string rel) { string p = Path.Combine(c, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, [1, 2, 3]); }
        Make("Jogos/My Game/game.exe"); Make("Games/Other/run.exe"); Make("Program Files/Tool/tool.exe"); File.WriteAllBytes(Path.Combine(c, "Program Files/Tool/tool.exe"), new byte[1_200_000]);
        Make("Program Files/Common Files/x.dll"); Make("Program Files (x86)/Internet Explorer/iexplore.exe"); Make("windows/system32/k.dll");
        Make("Program Files (x86)/Steam/steam.exe"); Make("Program Files (x86)/Microsoft/EdgeWebView/Application/x.exe"); Make("Program Files/Stub/tiny.exe"); Directory.CreateDirectory(Path.Combine(c, "Jogos", "Empty", "Sub")); // Proton's own Steam folder; empty leftovers
        Directory.CreateDirectory(Path.Combine(plan.Destination, "My Game")); // name clash -> " (2)"
        var moved = FastExec.MoveToDestination(plan);
        Assert.Equal(3, moved.Count);
        Assert.True(File.Exists(Path.Combine(plan.Destination, "My Game (2)", "game.exe")));
        Assert.True(File.Exists(Path.Combine(plan.Destination, "Other", "run.exe")));
        Assert.True(File.Exists(Path.Combine(plan.Destination, "Tool", "tool.exe")));
        Assert.False(Directory.Exists(Path.Combine(plan.Destination, "Common Files")));
        Assert.False(Directory.Exists(Path.Combine(plan.Destination, "Steam")));
        Assert.False(Directory.Exists(Path.Combine(plan.Destination, "Microsoft")));
        Assert.False(Directory.Exists(Path.Combine(plan.Destination, "Stub")));
        Assert.False(Directory.Exists(Path.Combine(plan.Destination, "Empty")));
        FastExec.Cleanup(plan);
        Assert.False(Directory.Exists(plan.TempPrefix));
        Assert.True(File.Exists(Path.Combine(plan.Destination, "Tool", "tool.exe")));
    }

    [Fact]
    public void InstallerFolderIsPlacedInsideTheTemporaryWindowsWithoutTouchingTheOriginals()
    {
        if (!OperatingSystem.IsLinux()) return;
        string src = Path.Combine(root, "My Installer [v1]"); Directory.CreateDirectory(Path.Combine(src, "MD5"));
        string setup = Path.Combine(src, "setup.exe"); File.WriteAllBytes(setup, [0x4d, 0x5a, 1]);
        File.WriteAllBytes(Path.Combine(src, "data-01.bin"), new byte[4096]); File.WriteAllText(Path.Combine(src, "MD5", "list.md5"), "x");
        var plan = FastExec.Plan(setup, Path.Combine(root, "J"), n => "/usr/bin/" + n, Path.Combine(root, "fx"), "st");
        FastExec.Prepare(plan); FastExec.StageInstaller(plan);
        string staged = Path.GetDirectoryName(plan.Arguments[0])!;
        Assert.True(File.Exists(plan.Arguments[0]));
        Assert.True(File.Exists(Path.Combine(staged, "data-01.bin")));
        Assert.True(File.Exists(Path.Combine(staged, "MD5", "list.md5")));
        Assert.Equal(Inode(Path.Combine(src, "data-01.bin")), Inode(Path.Combine(staged, "data-01.bin"))); // hard link: no extra space
        FastExec.Cleanup(plan);
        Assert.False(Directory.Exists(plan.TempPrefix));
        Assert.Equal(4096, new FileInfo(Path.Combine(src, "data-01.bin")).Length); // originals untouched
        Assert.True(File.Exists(setup));
    }

    private static string Inode(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("stat", ["-c", "%i", path]) { RedirectStandardOutput = true })!.StandardOutput.ReadToEnd().Trim();

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

    private string FakeRunner(string parent, string name, bool proton = false)
    {
        string dir = Path.Combine(parent, name, "bin"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "wine"), "#!/bin/sh\necho \"wine ran $1\"\nexit 3\n");
        File.WriteAllText(Path.Combine(dir, "wineserver"), "#!/bin/sh\necho \"waited $1\" >> \"$(dirname \"$0\")/../waited\"\n");
        if (OperatingSystem.IsLinux()) foreach (var f in new[] { "wine", "wineserver" }) File.SetUnixFileMode(Path.Combine(dir, f), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (proton) File.WriteAllText(Path.Combine(parent, name, "proton"), "#!/usr/bin/env python3");
        return Path.Combine(dir, "wine");
    }

    [Fact]
    public void PlainWineIsFoundInBottlesRunnersButNeverAProton()
    {
        string home = Path.Combine(root, "home"), runners = Path.Combine(home, ".var/app/com.usebottles.bottles/data/bottles/runners");
        Assert.Null(PlainWine.Find(home));
        FakeRunner(runners, "soda-9.0-1"); string newest = FakeRunner(runners, "soda-11.0-10"); FakeRunner(runners, "ge-proton9-27", proton: true);
        Assert.Equal(newest, PlainWine.Find(home));
        Assert.Equal("soda-11.0-10", PlainWine.Name(newest));
        string own = FakeRunner(Path.Combine(home, ".local/share/ilmenyx/runners"), "wine-11.0-staging");
        Assert.Equal(own, PlainWine.Find(home)); // the Ilmenyx's own Wine wins
    }

    [Fact]
    public async Task PlainWinePlanNeedsNoUmuRunsFromTheStagedCopyAndWaitsForHelpers()
    {
        if (!OperatingSystem.IsLinux()) return;
        string wine = FakeRunner(Path.Combine(root, "runners"), "soda-11.0-10");
        string fx = Path.Combine(root, "fx");
        var plan = FastExec.Plan(Installer, Path.Combine(root, "J"), _ => null, fx, "pw", wineBin: wine);
        Assert.Equal("/bin/sh", plan.Program);
        Assert.Equal(Path.Combine(plan.TempPrefix, "drive_c", "Instalador", "setup.exe"), plan.Arguments[^1]);
        Assert.Contains(new KeyValuePair<string, string>("WINEPREFIX", plan.TempPrefix), plan.Environment);
        Assert.Contains(plan.Environment, e => e.Key == "WINEDLLOVERRIDES" && e.Value.Contains("winemenubuilder.exe=d", StringComparison.Ordinal));
        FastExec.Prepare(plan); FastExec.StageInstaller(plan);
        Assert.Equal("../drive_c", new FileInfo(Path.Combine(plan.TempPrefix, "dosdevices", "c:")).LinkTarget); // plain Wine would not make C: itself
        int code = await FastExec.RunAsync(plan, CancellationToken.None);
        Assert.Equal(3, code); // the installer's own exit code comes back
        string log = FastExec.LatestLog(fx)!;
        Assert.Equal(Path.Combine(fx, "logs", "wine-pw.log"), log);
        Assert.Contains("wine ran " + plan.Arguments[^1], File.ReadAllText(log));
        Assert.Contains("waited -w", File.ReadAllText(Path.Combine(root, "runners", "soda-11.0-10", "waited"))); // wineserver -w ran after it
        FastExec.Cleanup(plan);
    }
}
