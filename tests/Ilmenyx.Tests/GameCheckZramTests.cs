// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// "Jogo não abre?" checklist and ZRAM plan over fixtures for each distro family (Ubuntu/Mint, Debian, Fedora, Arch/CachyOS/Manjaro,
// openSUSE). Nothing here runs flatpak, pkexec or a package manager: the probes get fixture trees and fake runners; the generated
// root scripts are only checked with `sh -n` (syntax, no execution).
using System.Diagnostics;
using Ilmenyx.Cli;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class GameCheckZramTests : IDisposable
{
    private const string Mint = "NAME=\"Linux Mint\"\nVERSION=\"22.3 (Zena)\"\nID=linuxmint\nID_LIKE=\"ubuntu debian\"\nPRETTY_NAME=\"Linux Mint 22.3\"\nVERSION_ID=\"22.3\"\n";
    private const string Ubuntu = "PRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\nID=ubuntu\nID_LIKE=debian\nVERSION_ID=\"24.04\"\n";
    private const string Debian = "PRETTY_NAME=\"Debian GNU/Linux 13 (trixie)\"\nID=debian\nVERSION_ID=\"13\"\n";
    private const string Fedora = "NAME=\"Fedora Linux\"\nID=fedora\nVERSION_ID=41\nPRETTY_NAME=\"Fedora Linux 41 (Workstation Edition)\"\n";
    private const string Arch = "NAME=\"Arch Linux\"\nPRETTY_NAME=\"Arch Linux\"\nID=arch\n";
    private const string Cachy = "NAME=\"CachyOS Linux\"\nPRETTY_NAME=\"CachyOS\"\nID=cachyos\nID_LIKE=arch\n";
    private const string Manjaro = "NAME=\"Manjaro Linux\"\nID=manjaro\nID_LIKE=arch\nPRETTY_NAME=\"Manjaro Linux\"\n";
    private const string Tumbleweed = "NAME=\"openSUSE Tumbleweed\"\nID=\"opensuse-tumbleweed\"\nID_LIKE=\"opensuse suse\"\nPRETTY_NAME=\"openSUSE Tumbleweed\"\n";
    private const string Gentoo = "NAME=Gentoo\nID=gentoo\nPRETTY_NAME=\"Gentoo Linux\"\n";
    private const string PacmanOff = "[options]\nHoldPkg = pacman glibc\n\n[core]\nInclude = /etc/pacman.d/mirrorlist\n\n[extra]\nInclude = /etc/pacman.d/mirrorlist\n\n#[multilib]\n#Include = /etc/pacman.d/mirrorlist\n";
    private const string PacmanOn = "[options]\n[core]\nInclude = /etc/pacman.d/mirrorlist\n[multilib]\nInclude = /etc/pacman.d/mirrorlist\n";

    private readonly string dir = Path.Combine(Path.GetTempPath(), "ilmenyx-gamecheck-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

    private static GameCheck Get(IReadOnlyList<GameCheck> list, string id) => Assert.Single(list, c => c.Id == id);

    [Theory]
    [InlineData(Mint, DistroFamily.Ubuntu)] [InlineData(Ubuntu, DistroFamily.Ubuntu)] [InlineData(Debian, DistroFamily.Debian)] [InlineData(Fedora, DistroFamily.Fedora)]
    [InlineData(Arch, DistroFamily.Arch)] [InlineData(Cachy, DistroFamily.Arch)] [InlineData(Manjaro, DistroFamily.Arch)] [InlineData(Tumbleweed, DistroFamily.OpenSuse)] [InlineData(Gentoo, DistroFamily.Other)]
    public void DistroFamilyComesFromIdAndIdLike(string osRelease, DistroFamily family) => Assert.Equal(family, DistroInfo.Parse(osRelease).Family);

    // ---- parsers ----

    [Fact]
    public void MultilibAndRepoParsers()
    {
        Assert.False(GameLaunchChecks.MultilibEnabled(PacmanOff));
        Assert.True(GameLaunchChecks.MultilibEnabled(PacmanOn));
        Assert.False(GameLaunchChecks.MultilibEnabled("[multilib]\n#Include = /etc/pacman.d/mirrorlist\n"));
        Assert.True(GameLaunchChecks.RepoEnabled("[rpmfusion-free]\nname=RPM Fusion\nenabled=1\n[rpmfusion-free-debuginfo]\nenabled=0\n"));
        Assert.False(GameLaunchChecks.RepoEnabled("[rpmfusion-nonfree]\nenabled=0\n[x]\nenabled=1\n"));
        Assert.True(GameLaunchChecks.RepoEnabled("[rpmfusion-nonfree]\nname=x\n")); // dnf default: enabled
        Assert.False(GameLaunchChecks.RepoEnabled(""));
    }

    [Fact]
    public void FlatpakRemotesAndFilesystems()
    {
        var remotes = GameLaunchChecks.ParseRemotes("flathub\thttps://dl.flathub.org/repo/\t\nfedora\toci+https://registry.fedoraproject.org\tsystem,oci\n");
        Assert.Equal(FlathubState.Ok, GameLaunchChecks.Flathub(remotes));
        Assert.Equal(FlathubState.Disabled, GameLaunchChecks.Flathub(GameLaunchChecks.ParseRemotes("flathub\thttps://dl.flathub.org/repo/\tdisabled\n")));
        Assert.Equal(FlathubState.WrongUrl, GameLaunchChecks.Flathub(GameLaunchChecks.ParseRemotes("flathub\thttps://example.org/repo/\t\n")));
        Assert.Equal(FlathubState.Missing, GameLaunchChecks.Flathub(GameLaunchChecks.ParseRemotes("fedora\toci+https://registry.fedoraproject.org\t\n")));
        Assert.Equal(FlathubState.Missing, GameLaunchChecks.Flathub(null));

        var fs = GameLaunchChecks.ParseFilesystems("[Context]\nshared=network;ipc;\nfilesystems=xdg-music:ro;xdg-pictures:ro;/mnt/jogos;~/Games;\n\n[Session Bus Policy]\nfilesystems=/nope\n");
        Assert.Equal(["xdg-music:ro", "xdg-pictures:ro", "/mnt/jogos", "~/Games"], fs);
        Assert.True(GameLaunchChecks.Covers(fs, "/mnt/jogos", "/home/u"));
        Assert.True(GameLaunchChecks.Covers(fs, "/mnt/jogos/SteamLibrary", "/home/u"));
        Assert.False(GameLaunchChecks.Covers(fs, "/mnt/jogos2", "/home/u"));
        Assert.True(GameLaunchChecks.Covers(fs, "/home/u/Games/x", "/home/u"));
        Assert.False(GameLaunchChecks.Covers([.. fs, "!/mnt/jogos"], "/mnt/jogos", "/home/u"));
        Assert.False(GameLaunchChecks.Covers(["/media/u/HD:ro"], "/media/u/HD", "/home/u"));
        Assert.True(GameLaunchChecks.Covers(["host"], "/run/media/u/SSD", "/home/u"));
        Assert.True(GameLaunchChecks.Covers(["/media/u/HD:rw"], "/media/u/HD", "/home/u"));
    }

    [Fact]
    public void VulkanSummaryParser()
    {
        string text = "==========\nVULKANINFO\n==========\nDevices:\n========\nGPU0:\n\tapiVersion         = 1.4.318\n\tdeviceType         = PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU\n\tdeviceName         = Intel(R) HD Graphics 620 (KBL GT2)\n\tdriverName         = Intel open-source Mesa driver\nGPU1:\n\tdeviceType         = PHYSICAL_DEVICE_TYPE_CPU\n\tdeviceName         = llvmpipe (LLVM 19.1.1, 256 bits)\n\tdriverName         = llvmpipe\n";
        var d = GameLaunchChecks.ParseVulkanSummary(text);
        Assert.Equal(2, d.Count);
        Assert.Equal(("Intel(R) HD Graphics 620 (KBL GT2)", "Intel open-source Mesa driver", false), d[0]);
        Assert.True(d[1].Cpu);
    }

    [Theory]
    [InlineData(new[] { "remote-add", "--user", "--if-not-exists", "flathub", GameLaunchChecks.FlathubRepoFile }, true)]
    [InlineData(new[] { "remote-modify", "--user", "--enable", "flathub" }, true)]
    [InlineData(new[] { "override", "--user", "--filesystem=/mnt/jogos", GameLaunchChecks.SteamAppId }, true)]
    [InlineData(new[] { "override", "--user", "--nofilesystem=/media/ana/HD Jogos", GameLaunchChecks.SteamAppId }, true)]
    [InlineData(new[] { "remote-add", "--if-not-exists", "flathub", GameLaunchChecks.FlathubRepoFile }, false)] // system scope needs root
    [InlineData(new[] { "remote-add", "--user", "--if-not-exists", "flathub", "https://evil.example/repo.flatpakrepo" }, false)]
    [InlineData(new[] { "override", "--filesystem=/mnt/jogos", GameLaunchChecks.SteamAppId }, false)]
    [InlineData(new[] { "override", "--user", "--filesystem=host", GameLaunchChecks.SteamAppId }, false)]
    [InlineData(new[] { "override", "--user", "--filesystem=/mnt/x;host", GameLaunchChecks.SteamAppId }, false)]
    [InlineData(new[] { "override", "--user", "--filesystem=/mnt/x:ro", GameLaunchChecks.SteamAppId }, false)]
    [InlineData(new[] { "override", "--user", "--filesystem=/mnt/../etc", GameLaunchChecks.SteamAppId }, false)]
    [InlineData(new[] { "override", "--user", "--filesystem=/mnt/jogos", "org.other.App" }, false)]
    [InlineData(new[] { "uninstall", "--user", "-y", GameLaunchChecks.SteamAppId }, false)]
    public void OnlyWhitelistedUserLevelFlatpakCallsAreAllowed(string[] args, bool allowed) => Assert.Equal(allowed, GameLaunchChecks.IsAllowedFix(args));

    [Fact]
    public async Task RunnerRefusesAnythingOutsideTheWhitelistWithoutStartingAProcess()
    {
        var (ok, msg) = await GameUserFixRunner.RunAsync(["override", "--user", "--filesystem=host", GameLaunchChecks.SteamAppId]);
        Assert.False(ok); Assert.Contains("Recusado", msg);
    }

    // ---- checklist per family ----

    private static GameCheckFacts Base(string os) => new()
    {
        Distro = DistroInfo.Parse(os), FlatpakInstalled = true,
        SystemRemotes = GameLaunchChecks.ParseRemotes("flathub\thttps://dl.flathub.org/repo/\t\n"), UserRemotes = [],
        GpuDrivers = ["i915"], IcdLibraries = ["libvulkan_intel.so", "libvulkan_lvp.so"], MaxMapCount = 1048576, SplitLockMitigate = 1,
        UnprivilegedUsernsClone = null, MaxUserNamespaces = 62769, Home = "/home/ana",
    };

    [Fact]
    public void UbuntuAppArmorRestrictionIsExplainedAndNeverAFix()
    {
        var list = GameLaunchChecks.Build(Base(Ubuntu) with { AppArmorRestrictUserns = 1 });
        var c = Get(list, "apparmor");
        Assert.Equal(CheckStatus.Attention, c.Status);
        Assert.Empty(c.Fixes);
        Assert.NotNull(c.Warning);
        Assert.Contains(c.Commands, x => x.Command.Contains("apparmor_parser -r " + GameLaunchChecks.AppArmorProfilePath) && x.Command.Contains("userns,"));
        Assert.Contains(c.Commands, x => x.Command.Contains("apparmor_parser -R"));
        // With a bwrap profile present it is only information; with the restriction off, OK and no commands.
        Assert.Equal(CheckStatus.Info, Get(GameLaunchChecks.Build(Base(Ubuntu) with { AppArmorRestrictUserns = 1, UsernsProfiles = ["bwrap-userns-restrict"] }), "apparmor").Status);
        var off = Get(GameLaunchChecks.Build(Base(Mint) with { AppArmorRestrictUserns = 0 }), "apparmor");
        Assert.Equal(CheckStatus.Ok, off.Status); Assert.Empty(off.Commands); Assert.Null(off.Warning);
        // Kernels without the sysctl (Fedora, Arch…) do not show the item.
        Assert.DoesNotContain(GameLaunchChecks.Build(Base(Fedora)), x => x.Id == "apparmor");
    }

    [Fact]
    public void DisabledUserNamespacesAreAProblemWithCopyOnlyCommands()
    {
        var c = Get(GameLaunchChecks.Build(Base(Debian) with { UnprivilegedUsernsClone = 0, MaxUserNamespaces = 0 }), "userns");
        Assert.Equal(CheckStatus.Problem, c.Status);
        Assert.Empty(c.Fixes); Assert.NotNull(c.Warning);
        Assert.Contains(c.Commands, x => x.Command.Contains("kernel.unprivileged_userns_clone=1") && x.Command.Contains("user.max_user_namespaces="));
        Assert.Equal(CheckStatus.Ok, Get(GameLaunchChecks.Build(Base(Debian)), "userns").Status);
    }

    [Fact]
    public void OpenSuseSelinuxExecmod()
    {
        var c = Get(GameLaunchChecks.Build(Base(Tumbleweed) with { SelinuxEnforce = 1, SelinuxExecmod = false }), "selinux");
        Assert.Equal(CheckStatus.Attention, c.Status); Assert.Empty(c.Fixes); Assert.NotNull(c.Warning);
        Assert.Contains(c.Commands, x => x.Command == "sudo setsebool -P selinuxuser_execmod 1");
        Assert.DoesNotContain(GameLaunchChecks.Build(Base(Tumbleweed) with { SelinuxEnforce = 0, SelinuxExecmod = false }), x => x.Id == "selinux");
    }

    [Theory]
    [InlineData(Arch)] [InlineData(Cachy)] [InlineData(Manjaro)]
    public void ArchFamilyMultilib(string os)
    {
        var c = Get(GameLaunchChecks.Build(Base(os) with { PacmanConf = PacmanOff, SteamNative = true }), "multilib");
        Assert.Equal(CheckStatus.Problem, c.Status);
        Assert.Contains(c.Commands, x => x.Command == "sudo pacman -Syu");
        Assert.Equal(CheckStatus.Attention, Get(GameLaunchChecks.Build(Base(os) with { PacmanConf = PacmanOff }), "multilib").Status);
        Assert.Equal(CheckStatus.Ok, Get(GameLaunchChecks.Build(Base(os) with { PacmanConf = PacmanOn }), "multilib").Status);
        Assert.DoesNotContain(GameLaunchChecks.Build(Base(Mint) with { PacmanConf = PacmanOff }), x => x.Id == "multilib");
    }

    [Fact]
    public void FedoraRpmFusion()
    {
        var c = Get(GameLaunchChecks.Build(Base(Fedora) with { RpmFusionFree = true, RpmFusionNonFree = false }), "rpmfusion");
        Assert.Equal(CheckStatus.Attention, c.Status);
        Assert.Contains("rpmfusion-nonfree-release-$(rpm -E %fedora)", c.Commands.Single().Command);
        Assert.Equal(CheckStatus.Ok, Get(GameLaunchChecks.Build(Base(Fedora) with { RpmFusionFree = true, RpmFusionNonFree = true }), "rpmfusion").Status);
        Assert.DoesNotContain(GameLaunchChecks.Build(Base(Arch) with { RpmFusionFree = false }), x => x.Id == "rpmfusion");
    }

    [Fact]
    public void FlathubMissingOffersTheUserLevelFixOnly()
    {
        var c = Get(GameLaunchChecks.Build(Base(Mint) with { SystemRemotes = [], UserRemotes = [] }), "flathub");
        Assert.Equal(CheckStatus.Problem, c.Status);
        var fix = Assert.Single(c.Fixes);
        Assert.True(GameLaunchChecks.IsAllowedFix(fix.Arguments));
        Assert.Equal(["remote-add", "--user", "--if-not-exists", "flathub", GameLaunchChecks.FlathubRepoFile], fix.Arguments);
        Assert.Contains(c.Commands, x => x.Command.StartsWith("sudo flatpak remote-add --if-not-exists flathub", StringComparison.Ordinal));
        // Disabled user remote: re-enable fix; disabled system remote: command only.
        var d = Get(GameLaunchChecks.Build(Base(Fedora) with { SystemRemotes = GameLaunchChecks.ParseRemotes("flathub\thttps://dl.flathub.org/repo/\tdisabled\n"), UserRemotes = GameLaunchChecks.ParseRemotes("flathub\thttps://dl.flathub.org/repo/\tdisabled\n") }), "flathub");
        Assert.Equal(CheckStatus.Problem, d.Status);
        Assert.Equal(["remote-modify", "--user", "--enable", "flathub"], Assert.Single(d.Fixes).Arguments);
        Assert.Contains(d.Commands, x => x.Command == "sudo flatpak remote-modify --enable flathub");
        Assert.Equal(CheckStatus.Ok, Get(GameLaunchChecks.Build(Base(Arch)), "flathub").Status);
        var none = Get(GameLaunchChecks.Build(Base(Tumbleweed) with { FlatpakInstalled = false, FlatpakInstallHint = "sudo zypper install flatpak" }), "flathub");
        Assert.Equal(CheckStatus.Info, none.Status); Assert.Equal("sudo zypper install flatpak", none.Commands.Single().Command);
    }

    [Fact]
    public void SteamFlatpakDrives()
    {
        var facts = Base(Mint) with
        {
            SteamFlatpak = true, SteamFilesystems = ["xdg-music:ro", "xdg-pictures:ro", "/media/ana/SSD"],
            Drives = [new("/mnt/jogos", true), new("/media/ana/SSD", true), new("/media/ana/Pendrive", false), new("/mnt/bad;host", true)],
        };
        var c = Get(GameLaunchChecks.Build(facts), "steamdrives");
        Assert.Equal(CheckStatus.Problem, c.Status);
        Assert.Equal(2, c.Fixes.Count); // /mnt/jogos and the pendrive; the unsafe path is never offered, /media/ana/SSD already has access
        Assert.All(c.Fixes, f => { Assert.True(GameLaunchChecks.IsAllowedFix(f.Arguments)); Assert.True(GameLaunchChecks.IsAllowedFix(f.UndoArguments!)); });
        Assert.Equal(["override", "--user", "--filesystem=/mnt/jogos", GameLaunchChecks.SteamAppId], c.Fixes[0].Arguments);
        Assert.Equal(["override", "--user", "--nofilesystem=/mnt/jogos", GameLaunchChecks.SteamAppId], c.Fixes[0].UndoArguments);
        Assert.Contains("Com acesso: /media/ana/SSD", c.Found);
        Assert.Equal(CheckStatus.Info, Get(GameLaunchChecks.Build(facts with { Drives = [new("/media/ana/Pendrive", false)] }), "steamdrives").Status);
        Assert.Equal(CheckStatus.Ok, Get(GameLaunchChecks.Build(facts with { Drives = [] }), "steamdrives").Status);
        Assert.DoesNotContain(GameLaunchChecks.Build(facts with { SteamFlatpak = false }), x => x.Id == "steamdrives");
    }

    [Fact]
    public void VulkanIcdForTheActiveGpus()
    {
        var amd = Get(GameLaunchChecks.Build(Base(Arch) with { GpuDrivers = ["amdgpu"], IcdLibraries = ["libvulkan_intel.so"] }), "vulkan");
        Assert.Equal(CheckStatus.Problem, amd.Status);
        Assert.Contains(amd.Commands, x => x.Command == "sudo pacman -S --needed vulkan-radeon");
        Assert.Equal(CheckStatus.Ok, Get(GameLaunchChecks.Build(Base(Arch) with { GpuDrivers = ["amdgpu"], IcdLibraries = ["libvulkan_radeon.so"] }), "vulkan").Status);
        // Hybrid Intel + NVIDIA proprietary without its ICD: problem, link to the Driver NVIDIA tab.
        var nv = Get(GameLaunchChecks.Build(Base(Mint) with { GpuDrivers = ["i915", "nvidia"], IcdLibraries = ["libvulkan_intel.so"] }), "vulkan");
        Assert.Equal(CheckStatus.Problem, nv.Status); Assert.Equal("nvdriver", nv.LinkPage);
        // vulkaninfo sees only llvmpipe: problem even if a manifest exists.
        var cpu = Get(GameLaunchChecks.Build(Base(Fedora) with { VulkaninfoInstalled = true, VulkanSummary = "GPU0:\n\tdeviceType = PHYSICAL_DEVICE_TYPE_CPU\n\tdeviceName = llvmpipe\n" }), "vulkan");
        Assert.Equal(CheckStatus.Problem, cpu.Status);
        Assert.Contains(cpu.Commands, x => x.Command == "sudo dnf install mesa-vulkan-drivers");
        var suse = Get(GameLaunchChecks.Build(Base(Tumbleweed) with { IcdLibraries = [] }), "vulkan");
        Assert.Contains(suse.Commands, x => x.Command.StartsWith("sudo zypper install libvulkan_intel", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Mint, "/usr/lib/i386-linux-gnu", "sudo dpkg --add-architecture i386")]
    [InlineData(Debian, "/usr/lib/i386-linux-gnu", "mesa-vulkan-drivers:i386")]
    [InlineData(Fedora, "/usr/lib", "vulkan-loader.i686")]
    [InlineData(Cachy, "/usr/lib32", "lib32-vulkan-icd-loader lib32-mesa lib32-vulkan-intel")]
    [InlineData(Tumbleweed, "/usr/lib", "libvulkan1-32bit")]
    public void Lib32ForNativeSteam(string os, string folder, string command)
    {
        var c = Get(GameLaunchChecks.Build(Base(os) with { SteamNative = true, Lib32Present = ["libGL.so.1"] }), "lib32");
        Assert.Equal(CheckStatus.Problem, c.Status);
        Assert.Contains(folder, c.Found);
        Assert.Contains(command, c.Commands.Single().Command);
        Assert.Equal(CheckStatus.Ok, Get(GameLaunchChecks.Build(Base(os) with { SteamNative = true, Lib32Present = ["libGL.so.1", "libvulkan.so.1", "libvulkan_intel.so"] }), "lib32").Status);
        Assert.DoesNotContain(GameLaunchChecks.Build(Base(os)), x => x.Id == "lib32"); // only with the native Steam
    }

    [Fact]
    public void MaxMapCountLinksToOtimizaAndSplitLockIsInformationOnly()
    {
        var list = GameLaunchChecks.Build(Base(Mint) with { MaxMapCount = 65530 });
        var m = Get(list, "maxmap");
        Assert.Equal(CheckStatus.Attention, m.Status); Assert.Equal("optimize", m.LinkPage); Assert.Empty(m.Commands);
        var s = Get(list, "splitlock");
        Assert.Equal(CheckStatus.Info, s.Status); Assert.Empty(s.Fixes);
        Assert.Contains(s.Commands, x => x.Command == "sudo sysctl -w kernel.split_lock_mitigate=0");
        // No check ever has a one-click fix outside the whitelist, and none runs anything with sudo.
        Assert.All(list.SelectMany(c => c.Fixes), f => Assert.True(GameLaunchChecks.IsAllowedFix(f.Arguments)));
        Assert.Contains("ponto(s) de atenção", GameLaunchChecks.Summary(list));
    }

    // ---- probe over a fixture tree ----

    private void W(string rel, string text) { string p = Path.Combine(dir, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); }

    [Fact]
    public async Task ProbeReadsAFixtureTreeAndOnlyReadOnlyPrograms()
    {
        W("etc/os-release", Ubuntu);
        W("proc/sys/kernel/apparmor_restrict_unprivileged_userns", "1\n");
        W("proc/sys/user/max_user_namespaces", "62769\n");
        W("proc/sys/vm/max_map_count", "65530\n");
        W("proc/sys/kernel/split_lock_mitigate", "1\n");
        W("etc/apparmor.d/steam", "abi <abi/4.0>,\nprofile steam @{HOME}/.local/share/Steam/** flags=(unconfined) {\n userns,\n}\n");
        W("etc/apparmor.d/usr.bin.foo", "profile foo /usr/bin/foo {}\n");
        W("usr/share/vulkan/icd.d/intel_icd.json", "{\"file_format_version\":\"1.0.1\",\"ICD\":{\"library_path\":\"libvulkan_intel.so\",\"api_version\":\"1.4.318\"}}");
        W("usr/share/vulkan/icd.d/broken.json", "{ not json");
        W("usr/lib/i386-linux-gnu/libGL.so.1", "");
        Directory.CreateDirectory(Path.Combine(dir, "sys/bus/pci/drivers/i915"));
        Directory.CreateDirectory(Path.Combine(dir, "sys/devices/pci0000:00/0000:00:02.0"));
        File.CreateSymbolicLink(Path.Combine(dir, "sys/devices/pci0000:00/0000:00:02.0/driver"), "../../../bus/pci/drivers/i915");
        Directory.CreateDirectory(Path.Combine(dir, "sys/class/drm/card1"));
        File.CreateSymbolicLink(Path.Combine(dir, "sys/class/drm/card1/device"), Path.Combine(dir, "sys/devices/pci0000:00/0000:00:02.0"));
        Directory.CreateDirectory(Path.Combine(dir, "sys/class/drm/card1-eDP-1"));
        Directory.CreateDirectory(Path.Combine(dir, "mnt/jogos/SteamLibrary/steamapps"));
        Directory.CreateDirectory(Path.Combine(dir, "media/ana/Pendrive"));
        W("proc/mounts", "/dev/nvme0n1p2 / ext4 rw 0 0\n/dev/sda1 /mnt/jogos ext4 rw 0 0\n/dev/sdb1 /media/ana/Pendrive exfat rw 0 0\ntmpfs /run tmpfs rw 0 0\n/dev/sdc1 /srv/data ext4 rw 0 0\n");
        string home = Path.Combine(dir, "home/ana");
        Directory.CreateDirectory(home);

        var calls = new List<string>();
        ReadOnlyRunner runner = (program, args, _, _) =>
        {
            calls.Add(program + " " + string.Join(' ', args));
            return Task.FromResult(string.Join(' ', args) switch
            {
                "remotes --system --columns=name,url,options" => (0, "flathub\thttps://dl.flathub.org/repo/\t\n"),
                "remotes --user --columns=name,url,options" => (0, ""),
                "info --show-permissions com.valvesoftware.Steam" => (0, "[Context]\nfilesystems=xdg-music:ro;xdg-pictures:ro;\n"),
                "override --show com.valvesoftware.Steam" => (0, ""),
                "override --user --show com.valvesoftware.Steam" => (0, "[Context]\nfilesystems=/media/ana/Pendrive;\n"),
                "--summary" => (0, "GPU0:\n\tdeviceType = PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU\n\tdeviceName = Intel(R) HD Graphics 620\n\tdriverName = Intel open-source Mesa driver\n"),
                _ => (1, ""),
            });
        };
        var probe = new GameLaunchProbe(dir, runner, p => p is "flatpak" or "vulkaninfo" or "steam", home);
        var f = await probe.CollectAsync();
        Assert.Equal(DistroFamily.Ubuntu, f.Distro.Family);
        Assert.Equal(1, f.AppArmorRestrictUserns);
        Assert.Equal(["steam"], f.UsernsProfiles);
        Assert.Equal(["i915"], f.GpuDrivers);
        Assert.Equal(["libvulkan_intel.so"], f.IcdLibraries);
        Assert.Equal(["libGL.so.1"], f.Lib32Present);
        Assert.True(f.SteamFlatpak && f.SteamNative);
        Assert.Equal([new GameDrive("/mnt/jogos", true), new GameDrive("/media/ana/Pendrive", false)], f.Drives);
        Assert.Equal(65530, f.MaxMapCount);
        Assert.Null(f.UnprivilegedUsernsClone);
        // Only read-only calls: remotes, info --show-permissions, override --show and vulkaninfo --summary.
        Assert.All(calls, c => Assert.Matches(@"^(flatpak (remotes --(system|user) --columns=name,url,options|info --show-permissions \S+|override (--user )?--show \S+)|vulkaninfo --summary)$", c));

        var checks = GameLaunchChecks.Build(f);
        Assert.Equal(CheckStatus.Attention, Get(checks, "apparmor").Status);
        Assert.Equal(CheckStatus.Ok, Get(checks, "flathub").Status);
        var drives = Get(checks, "steamdrives");
        Assert.Equal(CheckStatus.Problem, drives.Status);
        Assert.Equal("--filesystem=/mnt/jogos", Assert.Single(drives.Fixes).Arguments[2]);
        Assert.Equal(CheckStatus.Ok, Get(checks, "vulkan").Status);
        Assert.Equal(CheckStatus.Problem, Get(checks, "lib32").Status);
    }

    [Fact]
    public async Task ProbeArchAndFedoraRepositories()
    {
        W("etc/os-release", Cachy);
        W("etc/pacman.conf", PacmanOff);
        var arch = await new GameLaunchProbe(dir, (_, _, _, _) => Task.FromResult((1, "")), _ => false, dir).CollectAsync();
        Assert.False(GameLaunchChecks.MultilibEnabled(arch.PacmanConf!));
        Assert.Null(arch.RpmFusionFree);
        Assert.False(arch.FlatpakInstalled);

        W("etc/os-release", Fedora);
        W("etc/yum.repos.d/rpmfusion-free.repo", "[rpmfusion-free]\nenabled=1\n");
        W("sys/fs/selinux/enforce", "1");
        W("sys/fs/selinux/booleans/selinuxuser_execmod", "1 1");
        var fed = await new GameLaunchProbe(dir, (_, _, _, _) => Task.FromResult((1, "")), _ => false, dir).CollectAsync();
        Assert.True(fed.RpmFusionFree); Assert.False(fed.RpmFusionNonFree);
        Assert.Null(fed.PacmanConf);
        Assert.Equal(1, fed.SelinuxEnforce); Assert.True(fed.SelinuxExecmod);
    }

    // ---- ZRAM ----

    private const long GiB = 1L << 30;

    private static ZramFacts Z(string os, long ram, string swaps = "Filename\tType\tSize\tUsed\tPriority\n/swapfile file 2097148 512 -2\n") => new()
    {
        Distro = DistroInfo.Parse(os), MemTotalBytes = ram, Swaps = ZramPlanner.ParseSwaps(swaps),
    };

    [Fact]
    public void ParsersForSwapsMeminfoAndAlgorithm()
    {
        var s = ZramPlanner.ParseSwaps("Filename\t\t\t\tType\t\tSize\t\tUsed\t\tPriority\n/swapfile                               file\t\t2097148\t\t512\t\t-2\n/dev/zram0                              partition\t8388604\t\t0\t\t100\n");
        Assert.Equal(2, s.Count);
        Assert.Equal(new SwapEntry("/dev/zram0", "partition", 8388604, 0, 100), s[1]);
        Assert.Equal(-2, s[0].Priority);
        Assert.Equal(16236044L * 1024, ZramPlanner.ParseMemTotal("MemTotal:       16236044 kB\nMemFree:  1 kB\n"));
        Assert.Null(ZramPlanner.ParseMemTotal("garbage"));
        Assert.Equal("zstd", ZramPlanner.ActiveAlgorithm("lzo lzo-rle lz4 lz4hc 842 [zstd]"));
        Assert.Equal(4096, ZramPlanner.SizeMiB(8 * GiB));
        Assert.Equal(8192, ZramPlanner.SizeMiB(16 * GiB));
        Assert.Equal(7927, ZramPlanner.SizeMiB(16236044L * 1024));
        Assert.Equal(2048, ZramPlanner.SizeMiB(4 * GiB));
    }

    [Theory]
    [InlineData(Fedora, "zram-generator", "dnf install -y zram-generator")]
    [InlineData(Arch, "zram-generator", "pacman -S --needed --noconfirm zram-generator")]
    [InlineData(Cachy, "zram-generator", "pacman -S --needed --noconfirm zram-generator")]
    [InlineData(Tumbleweed, "zram-generator", "zypper --non-interactive install zram-generator")]
    [InlineData(Mint, "zram-tools", "apt-get -y install zram-tools")]
    [InlineData(Debian, "zram-tools", "apt-get -y install zram-tools")]
    public void RecommendedPlanUsesTheOfficialWayOfEachFamily(string os, string package, string install)
    {
        var plan = ZramPlanner.Plan(Z(os, 16236044L * 1024));
        Assert.Equal(ZramState.Recommended, plan.State);
        var a = Assert.Single(plan.Actions);
        Assert.Equal(ZramActionKind.Apply, a.Kind);
        Assert.False(a.Tested);
        Assert.Contains(install, a.Script);
        Assert.Contains("export DEBIAN_FRONTEND=noninteractive", a.Script);
        Assert.StartsWith("set -eu\n", a.Script);
        Assert.Contains("if grep -q '^/dev/zram' /proc/swaps; then", a.Script);
        Assert.Contains("package=" + package, a.Script);
        if (package == "zram-generator")
        {
            Assert.Contains("cat > /etc/systemd/zram-generator.conf <<'ILMENYX_EOF'", a.Script);
            Assert.Contains("zram-size = min(ram / 2, 8192)", a.Script);
            Assert.Contains("compression-algorithm = zstd", a.Script);
            Assert.Contains("systemctl start dev-zram0.swap", a.Script);
            Assert.Contains("já existe e não é do Ilmenyx", a.Script);
        }
        else
        {
            Assert.Contains("cp -p /etc/default/zramswap /etc/default/zramswap.ilmenyx-bak", a.Script);
            Assert.Contains("SIZE=7927", a.Script);
            Assert.Contains("ALGO=zstd", a.Script);
            Assert.Contains("systemctl enable zramswap.service", a.Script);
            Assert.Contains("zram-config", a.Script);
        }
        AssertShellSyntax(a.Script);
        AssertShellSyntax(ZramPlanner.UndoScript(DistroInfo.Parse(os).Family, true));
        AssertShellSyntax(ZramPlanner.UndoScript(DistroInfo.Parse(os).Family, false));
    }

    [Fact]
    public void NoRecommendationWhenZramIsActiveRamIsLargeOrConfigIsForeign()
    {
        string zram = "Filename Type Size Used Priority\n/dev/zram0 partition 8388604 0 100\n";
        var active = ZramPlanner.Plan(Z(Fedora, 8 * GiB, zram) with { VendorGeneratorConf = "[zram0]\nzram-size = min(ram, 8192)\n" });
        Assert.Equal(ZramState.Active, active.State); Assert.Empty(active.Actions);
        var big = ZramPlanner.Plan(Z(Arch, 32 * GiB));
        Assert.Equal(ZramState.NotNeeded, big.State); Assert.Empty(big.Actions);
        var foreign = ZramPlanner.Plan(Z(Arch, 8 * GiB) with { EtcGeneratorConf = "[zram0]\nzram-size = ram\n" });
        Assert.Equal(ZramState.ForeignConfig, foreign.State); Assert.Empty(foreign.Actions);
        var zramConfig = ZramPlanner.Plan(Z(Ubuntu, 8 * GiB) with { ZramConfigInstalled = true });
        Assert.Equal(ZramState.ForeignConfig, zramConfig.State);
        var other = ZramPlanner.Plan(Z(Gentoo, 8 * GiB));
        Assert.Equal(ZramState.Unsupported, other.State); Assert.Empty(other.Actions);
        Assert.Throws<ArgumentException>(() => ZramPlanner.ApplyScript(DistroFamily.Other, 4096));
        Assert.Throws<ArgumentOutOfRangeException>(() => ZramPlanner.ApplyScript(DistroFamily.Fedora, 100000));
        Assert.Equal(ZramState.Unknown, ZramPlanner.Plan(Z(Fedora, 8 * GiB) with { MemTotalBytes = null }).State);
        // Fedora with only the vendor default (zram off for some reason): our /etc file is proposed, the vendor file is untouched.
        Assert.Equal(ZramState.Recommended, ZramPlanner.Plan(Z(Fedora, 8 * GiB) with { VendorGeneratorConf = "[zram0]\n", GeneratorInstalled = true }).State);
        // zram-tools installed with the package default (not ours) is not foreign: it is backed up and replaced.
        var tools = ZramPlanner.Plan(Z(Mint, 8 * GiB) with { ZramToolsInstalled = true, ZramswapDefaults = "# ALGO=lz4\n# PERCENT=50\n" });
        Assert.Equal(ZramState.Recommended, tools.State);
        Assert.Contains("já está instalado", tools.Actions[0].Steps[0]);
    }

    [Fact]
    public void UndoOnlyForOurConfigAndPackageRemovalOnlyIfWeInstalledIt()
    {
        string zram = "Filename Type Size Used Priority\n/dev/zram0 partition 4194300 0 100\n";
        string ours = ZramPlanner.Marker + ": criado pelo Ilmenyx\n[zram0]\nzram-size = min(ram / 2, 8192)\n";
        var kept = ZramPlanner.Plan(Z(Fedora, 8 * GiB, zram) with { EtcGeneratorConf = ours, GeneratorInstalled = true });
        Assert.Equal(ZramState.ActiveOurs, kept.State);
        var undo = Assert.Single(kept.Actions);
        Assert.Equal(ZramActionKind.Undo, undo.Kind);
        Assert.Contains("rm -f /etc/systemd/zram-generator.conf", undo.Script);
        Assert.Contains("swapoff /dev/zram0", undo.Script);
        Assert.DoesNotContain("dnf remove", undo.Script);
        Assert.Contains("não existe ou não é do Ilmenyx", undo.Script);

        var installed = ZramPlanner.Plan(Z(Mint, 8 * GiB, zram) with { ZramswapDefaults = ZramPlanner.Marker + ": x\nALGO=zstd\nSIZE=4096\n", ZramToolsInstalled = true, State = "package=zram-tools\n" });
        Assert.Equal(ZramState.ActiveOurs, installed.State);
        Assert.Equal([ZramActionKind.Undo, ZramActionKind.UndoAndRemovePackage], installed.Actions.Select(a => a.Kind));
        var remove = installed.Actions[1].Script;
        Assert.Contains("apt-get -y purge zram-tools", remove);
        Assert.Contains("mv -f /etc/default/zramswap.ilmenyx-bak /etc/default/zramswap", remove);
        Assert.Contains("systemctl disable zramswap.service", remove);
        Assert.Contains("grep -qx 'package=zram-tools' /var/lib/ilmenyx/zram.state", remove);

        var inactive = ZramPlanner.Plan(Z(Arch, 8 * GiB) with { EtcGeneratorConf = ours });
        Assert.Equal(ZramState.ConfiguredInactive, inactive.State);
        Assert.Contains(inactive.Actions, a => a.Kind == ZramActionKind.Undo);
    }

    [Fact]
    public async Task ZramProbeOverFixtures()
    {
        W("etc/os-release", Mint);
        W("proc/meminfo", "MemTotal:       16236044 kB\n");
        W("proc/swaps", "Filename\tType\tSize\tUsed\tPriority\n/swapfile file 2097148 512 -2\n");
        W("lib/systemd/system/zramswap.service", "[Unit]\n");
        W("etc/default/zramswap", "# ALGO=lz4\n");
        W("sys/block/zram0/disksize", "0\n");
        W("sys/block/zram0/comp_algorithm", "lzo lzo-rle [lz4] zstd\n");
        var f = await new ZramProbe(dir, (_, _, _, _) => throw new FileNotFoundException(), _ => false).CollectAsync();
        Assert.Equal(DistroFamily.Ubuntu, f.Distro.Family);
        Assert.Equal(16236044L * 1024, f.MemTotalBytes);
        Assert.Single(f.Swaps);
        Assert.True(f.ZramToolsInstalled); Assert.False(f.GeneratorInstalled); Assert.False(f.ZramConfigInstalled);
        Assert.Equal("lz4", Assert.Single(f.Devices).Algorithm);
        Assert.Null(f.EtcGeneratorConf);
        var plan = ZramPlanner.Plan(f);
        Assert.Equal(ZramState.Recommended, plan.State);
        Assert.Equal(ZramTool.ZramTools, plan.Tool);
        Assert.Equal(7927, plan.SizeMiB);
    }

    [Fact]
    public async Task CliPrintsChecklistAndZramPlanWithoutRunningAnything()
    {
        W("etc/os-release", Fedora);
        W("proc/meminfo", "MemTotal:        8000000 kB\n");
        W("proc/swaps", "Filename\tType\tSize\tUsed\tPriority\n");
        W("proc/sys/vm/max_map_count", "1048576\n");
        var calls = new List<string>();
        ReadOnlyRunner runner = (p, a, _, _) => { calls.Add(p + " " + string.Join(' ', a)); return Task.FromResult((1, "")); };
        var o = new StringWriter();
        Assert.Equal(0, await SystemCheckCommand.RunAsync(["zram", "plan"], o, CancellationToken.None, zram: new ZramProbe(dir, runner, _ => false)));
        string text = o.ToString();
        Assert.Contains("Estado: Recommended · tamanho proposto: 3906 MiB", text);
        Assert.Contains("dnf install -y zram-generator", text);
        o = new StringWriter();
        Assert.Equal(0, await SystemCheckCommand.RunAsync(["game-check"], o, CancellationToken.None, games: new GameLaunchProbe(dir, runner, p => p == "flatpak", dir)));
        Assert.Contains("[PROBLEMA] Flatpak: remoto Flathub", o.ToString());
        Assert.Contains("(botão na interface) flatpak remote-add --user --if-not-exists flathub", o.ToString());
        Assert.All(calls, c => Assert.StartsWith("flatpak remotes", c.Replace("flatpak info", "flatpak remotes")));
        Assert.Equal(2, await SystemCheckCommand.RunAsync(["zram", "apply"], new StringWriter(), CancellationToken.None, zram: new ZramProbe(dir, runner, _ => false)));
        o = new StringWriter();
        Assert.Equal(0, await SystemCheckCommand.RunAsync(["zram", "script", "apply"], o, CancellationToken.None, zram: new ZramProbe(dir, runner, _ => false)));
        Assert.StartsWith("set -eu\n", o.ToString());
        Assert.Equal(1, await SystemCheckCommand.RunAsync(["zram", "script", "undo"], new StringWriter(), CancellationToken.None, zram: new ZramProbe(dir, runner, _ => false)));
    }

    private static void AssertShellSyntax(string script)
    {
        if (!File.Exists("/bin/sh")) return;
        var psi = new ProcessStartInfo("/bin/sh", ["-n"]) { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(script); p.StandardInput.Close();
        string err = p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(10000));
        Assert.True(p.ExitCode == 0, "sh -n: " + err + "\n" + script);
    }
}
