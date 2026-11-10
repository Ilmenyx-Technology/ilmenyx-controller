// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Nenhum sinal real é enviado: o envio de SIGTERM é injetado e opera só sobre diretórios de fixture.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class OptimizerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-opt-" + Guid.NewGuid().ToString("N"));
    public OptimizerTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void BuildListsOnlyDesktopAppsAndSuggestedHelpersAndNeverProtectedOrSelf()
    {
        var procs = new List<RunningProcess>
        {
            new(10, "firefox", 500L << 20, "Firefox"), new(11, "firefox", 300L << 20, "Firefox"), new(12, "baloo_file", 80L << 20, null), new(13, "pipewire", 5 << 20, "Pipewire"),
            new(14, "randomdaemon", 1 << 20, null), new(15, "plasmashell", 200L << 20, "Plasma"), new(16, "ilmenyx-self", 1 << 20, "Self"), new(17, "slack", 400L << 20, "Slack"), new(1, "systemd", 1 << 20, null)
        };
        var apps = OptimizerLogic.Build(procs, selfPid: 16);
        Assert.Equal(["slack", "baloo_file", "firefox"], apps.Select(a => a.ProcessName).ToArray());          // suggested first (by memory: slack 400 MiB > baloo 80 MiB), then the rest
        var ff = apps.Single(a => a.ProcessName == "firefox"); Assert.Equal(2, ff.Count); Assert.Equal("Firefox", ff.Title); Assert.Contains("2 processos", ff.Detail); Assert.False(ff.Selected); Assert.Equal([10, 11], ff.Pids.ToArray());
        Assert.True(apps.Single(a => a.ProcessName == "baloo_file").Selected); Assert.Equal("800 MB", ff.MemoryText.Replace(".", "")); // 500+300 MiB
        Assert.DoesNotContain(apps, a => a.ProcessName is "pipewire" or "plasmashell" or "randomdaemon" or "systemd" or "ilmenyx-self");
    }

    [Theory]
    [InlineData("/usr/bin/firefox %u", "firefox")]
    [InlineData("env MOZ_ENABLE_WAYLAND=1 firefox", "firefox")]
    [InlineData("\"/opt/My App/bin/myapp\" --flag", "myapp")]
    [InlineData("flatpak run --branch=stable org.mozilla.Firefox", "firefox")]
    [InlineData("FOO=1 BAR=2 /usr/bin/steam %U", "steam")]
    [InlineData("", null)]
    public void ExecNameFollowsWrappersAndFlatpak(string exec, string? expected) => Assert.Equal(expected, LinuxOptimizer.ExecName(exec));

    [Fact]
    public void DesktopAppsMapsExecutablesAndSkipsHiddenEntriesAndTruncatedComm()
    {
        string dir = Path.Combine(root, "apps"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.desktop"), "[Desktop Entry]\nType=Application\nName=Visible App\nExec=/usr/bin/visibleappwithalongname %U\n");
        File.WriteAllText(Path.Combine(dir, "b.desktop"), "[Desktop Entry]\nType=Application\nName=Hidden\nExec=hidden\nNoDisplay=true\n");
        File.WriteAllText(Path.Combine(dir, "c.desktop"), "[Desktop Entry]\nType=Link\nName=Link\nExec=x\n");
        var map = new LinuxOptimizer(home: root).DesktopApps([dir, Path.Combine(root, "missing")]);
        Assert.Equal("Visible App", map["visibleappwithalongname"]); Assert.Equal("Visible App", map["visibleappwitha"]); // comm is cut at 15 characters
        Assert.DoesNotContain("hidden", map.Keys); Assert.DoesNotContain("x", map.Keys);
    }

    private void Proc(string pid, string comm, int uid, long rssPages = 1000, string? cmd = null)
    {
        string d = Path.Combine(root, "proc", pid); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "comm"), comm + "\n"); File.WriteAllText(Path.Combine(d, "status"), $"Name:\t{comm}\nUid:\t{uid}\t{uid}\t{uid}\t{uid}\n"); File.WriteAllText(Path.Combine(d, "statm"), $"5000 {rssPages} 0 0 0 0 0\n");
        File.WriteAllBytes(Path.Combine(d, "cmdline"), System.Text.Encoding.UTF8.GetBytes((cmd ?? comm) + "\0"));
    }

    [Fact]
    public async Task ListAndCloseUseFixtureProcessesOnlyAndSigtermIsInjected()
    {
        Directory.CreateDirectory(Path.Combine(root, "proc/self")); File.WriteAllText(Path.Combine(root, "proc/self/status"), "Uid:\t1000\t1\t1\t1\nGid:\t1000\t1\t1\t1\nGroups:\t1000\n");
        Proc("100", "firefox", 1000); Proc("101", "firefox", 1000); Proc("102", "firefox", 0); Proc("103", "baloo_file", 1000); Proc("104", "othercomm", 1000, cmd: "/usr/bin/myapp");
        var termSent = new List<int>();
        var opt = new LinuxOptimizer(Path.Combine(root, "proc"), Path.Combine(root, "sys"), root, null, (pids, _) => { termSent.AddRange(pids); foreach (var p in pids.Where(p => p != 101)) Directory.Delete(Path.Combine(root, "proc", p.ToString()), true); return Task.CompletedTask; });
        var apps = opt.ListApps(new Dictionary<string, string> { ["firefox"] = "Firefox", ["myapp"] = "My App" }, selfPid: 999);
        Assert.Equal(["baloo_file", "firefox", "othercomm"], apps.Select(a => a.ProcessName).OrderBy(x => x).ToArray());
        var ff = apps.Single(a => a.ProcessName == "firefox"); Assert.Equal([100, 101], ff.Pids.OrderBy(x => x).ToArray()); // pid 102 belongs to another user: excluded
        Assert.Equal("My App", apps.Single(a => a.ProcessName == "othercomm").Title); // matched through argv0
        Assert.Equal(1000L * Environment.SystemPageSize * 2, ff.MemoryBytes);
        var result = await opt.CloseAsync([apps.Single(a => a.ProcessName == "baloo_file"), ff]);
        Assert.Equal(["Baloo_file".ToLowerInvariant()], result.Closed.Select(t => t.ToLowerInvariant()).Where(t => t == "baloo_file").ToArray());
        Assert.Contains("Firefox", result.NotClosed);  // pid 101 survived the fake SIGTERM
        Assert.Equal([100, 101, 103], termSent.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void GameTweakRulesReportStatesAndKeepRootItemsReadOnlyWithCommands()
    {
        var low = GameTweakRules.MaxMapCount(65530); Assert.False(low.IsOn); Assert.Contains("baixo", low.Status); Assert.Contains("sysctl -w vm.max_map_count", low.Effect); Assert.False(low.Adjustable); Assert.Equal("REQUER ROOT", low.Badge);
        Assert.True(GameTweakRules.MaxMapCount(2147483642).IsOn); Assert.Equal("Não foi possível ler.", GameTweakRules.MaxMapCount(null).Status);
        Assert.True(GameTweakRules.Swappiness(10).IsOn); Assert.False(GameTweakRules.Swappiness(60).IsOn);
        Assert.True(GameTweakRules.Governor("performance").IsOn); Assert.Contains("schedutil", GameTweakRules.Governor("schedutil").Status); Assert.Contains("Não informado", GameTweakRules.Governor(null).Status);
        Assert.Equal(1048576, GameTweakRules.ParseOpenFiles("Limit  Soft Limit  Hard Limit  Units\nMax open files            1048576              1048576              files\n")); Assert.Null(GameTweakRules.ParseOpenFiles("nothing"));
        Assert.False(GameTweakRules.OpenFiles(1024).IsOn); Assert.True(GameTweakRules.OpenFiles(1048576).IsOn);
    }

    [Fact]
    public void ReadOnlyTweaksComeFromProcSysAndLimitsFixtures()
    {
        string proc = Path.Combine(root, "proc2"), sys = Path.Combine(root, "sys2");
        Directory.CreateDirectory(Path.Combine(proc, "sys/vm")); Directory.CreateDirectory(Path.Combine(proc, "self")); Directory.CreateDirectory(Path.Combine(sys, "devices/system/cpu/cpu0/cpufreq"));
        File.WriteAllText(Path.Combine(proc, "sys/vm/max_map_count"), "2147483642\n"); File.WriteAllText(Path.Combine(proc, "sys/vm/swappiness"), "60\n");
        File.WriteAllText(Path.Combine(proc, "self/limits"), "Max open files            1024                 524288               files\n"); File.WriteAllText(Path.Combine(sys, "devices/system/cpu/cpu0/cpufreq/scaling_governor"), "schedutil\n");
        var t = new LinuxOptimizer(proc, sys, root).ReadOnlyTweaks().ToDictionary(x => x.Id);
        Assert.True(t["max_map_count"].IsOn); Assert.False(t["swappiness"].IsOn); Assert.False(t["governor"].IsOn); Assert.False(t["nofile"].IsOn); Assert.Equal(4, t.Count);
        Assert.Null(new LinuxOptimizer(Path.Combine(root, "none"), Path.Combine(root, "none"), root).SysctlLong("vm.swappiness"));
    }

    [Fact]
    public async Task MouseAccelerationUsesGsettingsWithValidatedProfiles()
    {
        var calls = new List<IReadOnlyList<string>>();
        var opt = new LinuxOptimizer(home: root, gsettings: (args, _) => { calls.Add(args); return Task.FromResult((0, args[0] == "get" ? "'adaptive'\n" : "")); });
        Assert.Equal("adaptive", await opt.MouseAccelProfileAsync());
        Assert.True(await opt.SetMouseAccelProfileAsync("flat")); Assert.Contains(calls, c => c.SequenceEqual(["set", "org.gnome.desktop.peripherals.mouse", "accel-profile", "flat"]));
        await Assert.ThrowsAsync<ArgumentException>(() => opt.SetMouseAccelProfileAsync("flat; rm -rf /"));
        Assert.Null(await new LinuxOptimizer(home: root, gsettings: (_, _) => Task.FromResult((1, ""))).MouseAccelProfileAsync());
    }
}
