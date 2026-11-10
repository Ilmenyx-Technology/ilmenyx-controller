// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class HostSpawnTests
{
    [Fact]
    public void NativeRunIsUnchanged()
    {
        var (program, args) = HostSpawn.Wrap("/usr/bin/nvidia-smi", ["-q"], new Dictionary<string, string> { ["LC_ALL"] = "C" }, "/tmp", inFlatpak: false);
        Assert.Equal("/usr/bin/nvidia-smi", program);
        Assert.Equal(["-q"], args);
    }

    [Fact]
    public void FlatpakRunsHostProgramsThroughFlatpakSpawnWithEnvironmentAndDirectory()
    {
        var env = new Dictionary<string, string> { ["LC_ALL"] = "C", ["DRI_PRIME"] = "1" };
        var (program, args) = HostSpawn.Wrap("/usr/bin/pkexec", ["/bin/sh", "-c", "chmod o+r x"], env, "/home/u/game", inFlatpak: true);
        Assert.Equal("/usr/bin/flatpak-spawn", program);
        Assert.Equal(["--host", "--watch-bus", "--directory=/home/u/game", "--env=DRI_PRIME=1", "--env=LC_ALL=C", "/usr/bin/pkexec", "/bin/sh", "-c", "chmod o+r x"], args);
    }

    [Fact]
    public void FlatpakKeepsSandboxProgramsInTheSandbox()
    {
        var (program, args) = HostSpawn.Wrap("/app/lib/ilmenyx/tool", ["a"], null, null, inFlatpak: true);
        Assert.Equal("/app/lib/ilmenyx/tool", program);
        Assert.Equal(["a"], args);
    }

    [Fact]
    public void InvalidEnvironmentNameIsRefused() =>
        Assert.Throws<ArgumentException>(() => HostSpawn.Wrap("/usr/bin/x", [], new Dictionary<string, string> { ["A=B"] = "1" }, null, inFlatpak: true));

    [Theory]
    [InlineData("/etc/os-release", "/run/host/etc/os-release")]
    [InlineData("/usr/share/hwdata/pci.ids", "/run/host/usr/share/hwdata/pci.ids")]
    [InlineData("/lib/modules", "/run/host/lib/modules")]
    [InlineData("/usr", "/run/host/usr")]
    [InlineData("/sys/class/powercap/intel-rapl:0/energy_uj", "/sys/class/powercap/intel-rapl:0/energy_uj")]
    [InlineData("/proc/cpuinfo", "/proc/cpuinfo")]
    [InlineData("/usrlocal/x", "/usrlocal/x")]
    [InlineData("relative/etc", "relative/etc")]
    public void HostPathMapsOnlyHostSystemDirectories(string path, string expected)
    {
        Assert.Equal(expected, HostSpawn.HostPath(path, inFlatpak: true));
        Assert.Equal(path, HostSpawn.HostPath(path, inFlatpak: false));
    }

    [Fact]
    public void AppPathIsReadFromTheInstanceSection()
    {
        const string info = "[Application]\nname=io.github.ilmenyx_technology.IlmenyxController\n\n[Instance]\ninstance-id=1\napp-path=/home/u/.local/share/flatpak/app/io.github.ilmenyx_technology.IlmenyxController/x86_64/master/abc/files\n";
        string? host = HostSpawn.AppPathFromInfo(info);
        Assert.Equal("/home/u/.local/share/flatpak/app/io.github.ilmenyx_technology.IlmenyxController/x86_64/master/abc/files", host);
        Assert.Equal(host + "/lib/ilmenyx/components/mangohud/lib64", HostSpawn.ForHost("/app/lib/ilmenyx/components/mangohud/lib64", host));
        Assert.Equal("/usr/lib/x", HostSpawn.ForHost("/usr/lib/x", host));
        Assert.Equal("/app/x", HostSpawn.ForHost("/app/x", null));
        Assert.Equal("/application/x", HostSpawn.ForHost("/application/x", host));
        Assert.Null(HostSpawn.AppPathFromInfo("[Application]\napp-path=/wrong\n"));
    }

    [Fact]
    public void HostStatDumpIsParsedWithSpacesInNames()
    {
        const string dump = "1 (systemd) S 0 1 1 0 -1 4194560\n4242 (Web Content) S 1 2 3\n77 (steam) S 1\ngarbage\n";
        var list = HostProcesses.Parse(dump).ToList();
        Assert.Equal([(1, "systemd"), (4242, "Web Content"), (77, "steam")], list.Select(p => (p.Pid, p.Comm)));
        Assert.StartsWith("4242 (Web Content)", list[1].Line);
    }

    [Theory]
    [InlineData("/sys/module/nvidia/version", true)]
    [InlineData("/sys/firmware/efi", true)]
    [InlineData("/sys/class/powercap/intel-rapl:0/energy_uj", false)]
    [InlineData("/sys/devices/pci0000:00/0000:00:02.0/vendor", false)]
    [InlineData("/sys/block/sda/size", false)]
    [InlineData("/sys/classic", true)]
    [InlineData("/sys", false)]
    [InlineData("/proc/modules", false)]
    public void OnlySysfsPathsHiddenByFlatpakGoToTheHost(string path, bool hidden) => Assert.Equal(hidden, HostSysfs.HiddenInFlatpak(path));

    [Fact]
    public void GameIsFoundInHostPsOutput()
    {
        const string ps = "  100 /usr/bin/steam -silent HOME=/home/u\n  200 /home/u/.steam/steamapps/common/Game/game.x86_64 SteamGameId=570 HOME=/home/u\n  300 /opt/Other/run SteamAppId=5700\n  400 /home/u/Games/Native/bin/native --x\n";
        var steam = new LibGame("Steam", "570", "Game", "/home/u/.steam/steamapps/common/Game");
        Assert.Equal([200], GameProcesses.FindInPs(ps, steam, null));
        var native = new LibGame("Manual", "n", "Native", "/home/u/Games/Native");
        Assert.Equal([400], GameProcesses.FindInPs(ps, native, null));
    }
}
