// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// RAPL layout copied from a real ASUS E1504FA (AMD Mendocino, kernel 6.14): package-0 + core.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class CpuPowerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-rapl-" + Guid.NewGuid().ToString("N"));
    public CpuPowerTests() => Directory.CreateDirectory(root);
    public void Dispose() { foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite); Directory.Delete(root, true); }

    private string Zone(long energy, string name = "package-0")
    {
        string z = Path.Combine(root, "class/powercap/intel-rapl:0"); Directory.CreateDirectory(Path.Combine(z, "intel-rapl:0:0"));
        File.WriteAllText(Path.Combine(z, "name"), name + "\n"); File.WriteAllText(Path.Combine(z, "max_energy_range_uj"), "65532610987\n");
        File.WriteAllText(Path.Combine(z, "energy_uj"), energy + "\n"); File.WriteAllText(Path.Combine(z, "intel-rapl:0:0", "name"), "core\n");
        return z;
    }

    [Fact]
    public void PackagePowerFromEnergyDeltaWithWrap()
    {
        string z = Zone(1_000_000);
        var r = new RaplPowerReader(root);
        Assert.Null(r.Sample(10)); Assert.Equal(RaplState.Available, r.State); // first sample has no rate
        File.WriteAllText(Path.Combine(z, "energy_uj"), "13000000\n");
        Assert.Equal(6.0, r.Sample(12)!.Value, 6); // 12 J in 2 s
        var wrap = new RaplPowerReader(root);
        File.WriteAllText(Path.Combine(z, "energy_uj"), "65532000000\n"); Assert.Null(wrap.Sample(20));
        File.WriteAllText(Path.Combine(z, "energy_uj"), "9389013\n"); // wrapped at max_energy_range_uj (65 532 610 987)
        Assert.Equal(5.0, wrap.Sample(22)!.Value, 6); // (610 987 + 9 389 013) µJ = 10 J in 2 s
    }

    [Fact]
    public void RootOnlyCounterIsReportedAsNoPermissionAndMissingAsNotPresent()
    {
        Assert.Null(new RaplPowerReader(root).Sample(1));
        var absent = new RaplPowerReader(root); absent.Sample(1); Assert.Equal(RaplState.NotPresent, absent.State);
        string z = Zone(5);
        if (Environment.UserName == "root") return; // root reads anything: the permission case cannot be reproduced
        File.SetUnixFileMode(Path.Combine(z, "energy_uj"), UnixFileMode.None); // like -r-------- root on a real kernel
        var denied = new RaplPowerReader(root);
        Assert.Null(denied.Sample(1)); Assert.Null(denied.Sample(3)); Assert.Equal(RaplState.NoPermission, denied.State);
    }

    [Fact]
    public void SensorHelpExplainsAndShowsCommandsButNeverRunsThem()
    {
        var fixes = SensorHelp.Build(true, CpuPowerAccess.NoPermission, "AMD Ryzen 5 7520U", "/sys/devices/virtual/powercap/intel-rapl/intel-rapl:0", false);
        Assert.Equal(2, fixes.Count);
        Assert.Contains(fixes[0].Commands, c => c.Command == "sudo modprobe k10temp");
        Assert.Contains(fixes[1].Commands, c => c.Command == "sudo chmod o+r /sys/class/powercap/intel-rapl:0/energy_uj");
        Assert.Contains("CVE-2020-8694", fixes[1].Warning);
        Assert.Equal("coretemp", SensorHelp.TemperatureModule("Intel(R) Core(TM) i5-1135G7"));
        Assert.Empty(SensorHelp.Build(false, CpuPowerAccess.Available, "x", null, false));
        Assert.Empty(SensorHelp.Build(true, CpuPowerAccess.Available, "AMD", null, true).Single().Commands); // VM: nothing to run
    }

    [Fact]
    public void OneClickScriptsUseOnlyWhitelistedValues()
    {
        var rapl = SensorHelp.Build(false, CpuPowerAccess.NoPermission, "AMD", "/sys/class/powercap/intel-rapl:0", false).Single();
        Assert.StartsWith("chmod o+r /sys/class/powercap/intel-rapl:0/energy_uj && ", rapl.Apply);
        Assert.Contains("/etc/udev/rules.d/90-ilmenyx-rapl.rules", rapl.Apply); Assert.Contains("rm -f /etc/udev/rules.d/90-ilmenyx-rapl.rules", rapl.Undo);
        var evil = SensorHelp.Build(false, CpuPowerAccess.NoPermission, "AMD", "/sys/class/powercap/intel-rapl:0; rm -rf /", false).Single();
        Assert.DoesNotContain("rm -rf /", evil.Apply); Assert.Contains("/sys/class/powercap/intel-rapl:0/energy_uj", evil.Apply); // falls back to the default zone
        var temp = SensorHelp.Build(true, CpuPowerAccess.Available, "Intel(R) Core(TM) i5; reboot", null, false).Single();
        Assert.Equal("modprobe coretemp && printf '%s\\n' coretemp > /etc/modules-load.d/ilmenyx-coretemp.conf", temp.Apply); // CPU text never reaches the script
        Assert.Null(SensorHelp.Build(true, CpuPowerAccess.Available, "AMD", null, true).Single().Apply); // VM: nothing to run
    }

    [Fact]
    public void MangoHudConfigUsesTheOriginalOverlayShortcut() =>
        Assert.Contains("toggle_hud=Control_L+Shift_L+o", MangoHudConfig.Build(new HashSet<string> { "fps" }, "top-left", 1.0, null).Split('\n'));
}
