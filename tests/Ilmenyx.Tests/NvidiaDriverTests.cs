// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Fixtures only: pkexec, apt (real install/remove) and nvidia-smi are never run by these tests.
// The "incident" fixtures reconstruct the real case of 2026-11-07 on a Dell Inspiron 7460 (GeForce 940MX 10de:134d, Linux Mint 22.3,
// kernel 6.14.0-37-generic): `ubuntu-drivers install nvidia:580` pulled kernel 7.0.0-42 with incomplete headers, the prebuilt module
// package failed (iF) and nvidia-driver-580 stayed unconfigured (iU). apt-sim-noop-real.txt is real output of this machine; the other
// apt-get -s files are reconstructed from that incident's package list.
using Ilmenyx.Cli;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class NvidiaDriverTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nvidia-driver", name));
    private const string Running = "6.14.0-37-generic";

    private static KernelHeaders Complete(string k) => new(k, true, []);

    /// <summary>The machine right after the failed `ubuntu-drivers install` (before the lead's repair).</summary>
    private static NvidiaDiagnosis Incident() => new()
    {
        Gpus = [new NvidiaGpu("0000:01:00.0", "134d", "GM108M [GeForce 940MX]", NvidiaGeneration.Maxwell, "GM108", "nouveau")],
        OtherGpus = ["Intel HD Graphics 620 (i915)"],
        Distro = DistroInfo.Parse(Fixture("os-release-mint-22.3")), RunningKernel = Running,
        Kernels = [Complete(Running), new KernelHeaders("7.0.0-42-generic", true, ["scripts/module.lds", "tools/objtool/objtool"])],
        SecureBoot = SecureBootState.Disabled, PrimeMode = "on-demand", PrimeSelectAvailable = true,
        Packages = DpkgPackage.Parse(Fixture("dpkg-incident-mint-22.3.txt")),
    };

    /// <summary>The same machine today: DKMS driver installed for 6.14, rebooted, nvidia-smi answers.</summary>
    private static NvidiaDiagnosis Working(string? candidate = "580.178.04-0ubuntu0.24.04.1") => new()
    {
        Gpus = [new NvidiaGpu("0000:01:00.0", "134d", "GM108M [GeForce 940MX]", NvidiaGeneration.Maxwell, "GM108", "nvidia")],
        OtherGpus = ["Intel HD Graphics 620 (i915)"],
        Distro = DistroInfo.Parse(Fixture("os-release-mint-22.3")), RunningKernel = Running, Kernels = [Complete(Running)],
        SecureBoot = SecureBootState.Disabled, PrimeMode = "on-demand", PrimeSelectAvailable = true,
        LoadedVersion = "580.178.04", SmiVersion = "580.178.04", SmiInstalled = true,
        Packages = DpkgPackage.Parse("ii |nvidia-driver-580|580.178.04-0ubuntu0.24.04.1\nii |nvidia-dkms-580|580.178.04-0ubuntu0.24.04.1\nii |libnvidia-gl-580:amd64|580.178.04-0ubuntu0.24.04.1\nii |nvidia-prime|0.8.17.2\nii |linux-image-6.14.0-37-generic|6.14.0-37.37~24.04.1\n"),
        Dkms = DkmsEntry.Parse("nvidia/580.178.04, 6.14.0-37-generic, x86_64: installed\n"), CandidateVersion = candidate,
    };

    private static NvidiaDiagnosis Fresh(string device, NvidiaGeneration gen, string os = "ID=ubuntu\nID_LIKE=debian\nPRETTY_NAME=\"Ubuntu 24.04\"\n", SecureBootState sb = SecureBootState.Disabled, string kernel = Running) => new()
    {
        Gpus = [new NvidiaGpu("0000:01:00.0", device, "GPU", gen, "", "nouveau")], Distro = DistroInfo.Parse(os), RunningKernel = kernel,
        Kernels = [new KernelHeaders(kernel, false, KernelHeaders.RequiredFiles)], SecureBoot = sb,
    };

    [Theory]
    [InlineData("0x134d", NvidiaGeneration.Maxwell, "GM108")]
    [InlineData("1c8d", NvidiaGeneration.Pascal, "GP107")]
    [InlineData("0x1f99", NvidiaGeneration.Turing, "TU117")]
    [InlineData("2184", NvidiaGeneration.Turing, "TU116")]
    [InlineData("2520", NvidiaGeneration.Ampere, "GA106")]
    [InlineData("2786", NvidiaGeneration.Ada, "AD104")]
    [InlineData("2c05", NvidiaGeneration.Blackwell, "GB203")]
    [InlineData("1180", NvidiaGeneration.Kepler, "GK104")]
    [InlineData("0de1", NvidiaGeneration.Fermi, "GF106/GF108")]
    [InlineData("0640", NvidiaGeneration.Tesla, "NV4x/G8x/G9x")]
    [InlineData("3a00", NvidiaGeneration.Unknown, "")]
    [InlineData("zz", NvidiaGeneration.Unknown, "")]
    public void DeviceIdsMapToGenerations(string id, NvidiaGeneration gen, string chip) => Assert.Equal((gen, chip), NvidiaGenerations.Classify(id));

    [Fact]
    public void BranchTableFollowsTheSupportMatrix()
    {
        Assert.Equal("580", NvidiaGenerations.Support(NvidiaGeneration.Maxwell).Branch);
        Assert.True(NvidiaGenerations.Support(NvidiaGeneration.Pascal).Legacy);
        Assert.Equal("580", NvidiaGenerations.Support(NvidiaGeneration.Volta).Branch);
        Assert.Equal(NvidiaGenerations.CurrentBranch, NvidiaGenerations.Support(NvidiaGeneration.Turing).Branch);
        Assert.Equal(NvidiaOpenModules.Required, NvidiaGenerations.Support(NvidiaGeneration.Blackwell).Open);
        Assert.Equal(("470", true), (NvidiaGenerations.Support(NvidiaGeneration.Kepler).Branch, NvidiaGenerations.Support(NvidiaGeneration.Kepler).Legacy));
        Assert.Null(NvidiaGenerations.Support(NvidiaGeneration.Fermi).Branch);
        Assert.Null(NvidiaGenerations.Support(NvidiaGeneration.Unknown).Branch);
        Assert.Equal(new HashSet<string> { "470", "580", "595" }, NvidiaGenerations.Branches.ToHashSet());
        Assert.Equal("GeForce 940MX", NvidiaGenerations.MarketingName("GM108M [GeForce 940MX]"));
        Assert.Equal("GPU 10de:134d", NvidiaGenerations.MarketingName("GPU 10de:134d"));
    }

    [Fact]
    public void NewestInBranchNeverReturnsANewerBranch()
    {
        string listing = "<a href='580.105.08/'>580.105.08/</a> <a href='580.178.04/'>580.178.04/</a> <a href='580.95.05/'>580.95.05/</a> <a href='595.112.07/'>595.112.07/</a> <a href='1580.1/'>x</a> latest.txt";
        Assert.Equal("580.178.04", NvidiaGenerations.NewestInBranch(listing, "580"));
        Assert.Equal("595.112.07", NvidiaGenerations.NewestInBranch(listing, "595"));
        Assert.Null(NvidiaGenerations.NewestInBranch(listing, "470"));
        Assert.Null(NvidiaGenerations.NewestInBranch(listing, "999"));
        var g = new GpuDriverStatus { Vendor = "NVIDIA", DeviceId = "134d", Installed = "580.178.04" };
        Assert.Equal("580", g.SupportedBranch);
        g.Latest = NvidiaGenerations.NewestInBranch(listing, g.SupportedBranch!)!; Assert.False(g.UpdateAvailable); // 595 exists, but not for Maxwell
    }

    [Theory]
    [InlineData("ID=linuxmint\nID_LIKE=\"ubuntu debian\"", DistroFamily.Ubuntu)]
    [InlineData("ID=pop\nID_LIKE=\"ubuntu debian\"", DistroFamily.Ubuntu)]
    [InlineData("ID=debian", DistroFamily.Debian)]
    [InlineData("ID=nobara\nID_LIKE=\"rhel centos fedora\"", DistroFamily.Fedora)]
    [InlineData("ID=fedora", DistroFamily.Fedora)]
    [InlineData("ID=cachyos\nID_LIKE=arch", DistroFamily.Arch)]
    [InlineData("ID=endeavouros\nID_LIKE=arch", DistroFamily.Arch)]
    [InlineData("ID=manjaro\nID_LIKE=arch", DistroFamily.Arch)]
    [InlineData("ID=\"opensuse-tumbleweed\"\nID_LIKE=\"opensuse suse\"", DistroFamily.OpenSuse)]
    [InlineData("ID=gentoo", DistroFamily.Other)]
    [InlineData("", DistroFamily.Other)]
    public void OsReleaseSelectsTheFamily(string text, DistroFamily family) => Assert.Equal(family, DistroInfo.Parse(text).Family);

    [Fact]
    public void RealMintOsRelease()
    {
        var d = DistroInfo.Parse(Fixture("os-release-mint-22.3"));
        Assert.Equal(("linuxmint", DistroFamily.Ubuntu, "Linux Mint 22.3"), (d.Id, d.Family, d.Name));
    }

    [Fact]
    public void DpkgStatusFindsHalfInstalledPackages()
    {
        var p = DpkgPackage.Parse(Fixture("dpkg-incident-mint-22.3.txt"));
        Assert.DoesNotContain(p, x => x.Name == "libnvidia-gl-390"); // "un": never installed
        Assert.Single(p, x => x.Name == "libnvidia-compute-580"); // :amd64 and :i386 merged
        var broken = p.Where(x => x.Broken).Select(x => x.Name).ToList();
        Assert.Equal(["nvidia-driver-580", "linux-modules-nvidia-580-7.0.0-42-generic"], broken);
        Assert.Contains("configuração falhou", p.Single(x => x.Name == "linux-modules-nvidia-580-7.0.0-42-generic").StatusText);
        // dpkg -l format and reinstall-required flag.
        var l = DpkgPackage.Parse("Desired=Unknown/Install/Remove/Purge/Hold\n||/ Name Version Architecture Description\n+++-====\niHR nvidia-dkms-580 580.1 amd64 NVIDIA DKMS\nii  nvidia-prime 0.8 all x\nrc  nvidia-utils-570 570.1 amd64 y\n");
        Assert.True(l.Single(x => x.Name == "nvidia-dkms-580").Broken);
        Assert.False(l.Single(x => x.Name == "nvidia-utils-570").Installed);
        Assert.Empty(DpkgPackage.Parse("ii |-o;rm -rf|1\nii |Nvidia Bad|1\n")); // names are validated
    }

    [Fact]
    public void AptSimulationParsesInstallsRemovalsAndErrors()
    {
        var s = AptSimulation.Parse(Fixture("apt-sim-ubuntu-drivers-hwe.txt"), 0);
        Assert.Equal(7, s.Installs.Count());
        Assert.Contains(s.Installs, a => a.Package == "linux-image-7.0.0-42-generic" && a.Version == "7.0.0-42.42~24.04.1");
        Assert.Contains("9 newly installed", s.Summary);
        var r = AptSimulation.Parse(Fixture("apt-sim-repair.txt"), 0);
        Assert.Equal(9, r.Removals.Count());
        var noop = AptSimulation.Parse(Fixture("apt-sim-noop-real.txt"), 0);
        Assert.Empty(noop.Actions); Assert.Empty(noop.Errors); Assert.Contains("0 newly installed", noop.Summary);
        var e = AptSimulation.Parse("Reading package lists...\nE: Unable to locate package nvidia-driver-999\n", 100);
        Assert.Equal(["Unable to locate package nvidia-driver-999"], e.Errors);
    }

    [Fact]
    public void ProbeTextParsers()
    {
        Assert.Equal(SecureBootState.Disabled, NvidiaProbeText.SecureBoot("SecureBoot disabled\n"));
        Assert.Equal(SecureBootState.Enabled, NvidiaProbeText.SecureBoot("SecureBoot enabled\n"));
        Assert.Equal(SecureBootState.NotUefi, NvidiaProbeText.SecureBoot("This system doesn't support Secure Boot\n"));
        Assert.Equal(SecureBootState.Unknown, NvidiaProbeText.SecureBoot(""));
        Assert.Equal("on-demand", NvidiaProbeText.Prime("on-demand\n"));
        Assert.Null(NvidiaProbeText.Prime("Error: no profile\n"));
        Assert.Equal(("580.178.04-0ubuntu0.24.04.1", "580.178.04-0ubuntu0.24.04.2"), NvidiaProbeText.Policy("nvidia-driver-580:\n  Installed: 580.178.04-0ubuntu0.24.04.1\n  Candidate: 580.178.04-0ubuntu0.24.04.2\n  Version table:\n"));
        Assert.Equal((null, "580.1"), NvidiaProbeText.Policy("x:\n  Installed: (none)\n  Candidate: 580.1\n"));
        Assert.Equal("580.178.04", NvidiaProbeText.SmiVersion("580.178.04\n"));
        Assert.Null(NvidiaProbeText.SmiVersion("NVIDIA-SMI has failed because it couldn't communicate with the NVIDIA driver."));
        var dkms = DkmsEntry.Parse("nvidia/580.178.04, 6.14.0-37-generic, x86_64: installed\nnvidia, 470.256.02, 6.8.0-1-generic, x86_64: built\nnvidia/580.1, 7.0.0-42-generic, x86_64: installed (WARNING! Diff between built and installed module!)\n");
        Assert.Equal(3, dkms.Count); Assert.Equal(("nvidia", "470.256.02", "6.8.0-1-generic", "built"), (dkms[1].Module, dkms[1].Version, dkms[1].Kernel, dkms[1].State));
        Assert.Equal("installed", dkms[2].State);
    }

    [Fact]
    public void ValidationRejectsShellTextAndOutOfTableBranches()
    {
        Assert.True(NvidiaScripts.ValidBranch("580")); Assert.False(NvidiaScripts.ValidBranch("590")); Assert.False(NvidiaScripts.ValidBranch("58O")); Assert.False(NvidiaScripts.ValidBranch(null));
        Assert.True(NvidiaScripts.ValidKernel(Running)); Assert.True(NvidiaScripts.ValidKernel("6.17.7-arch1-1")); Assert.True(NvidiaScripts.ValidKernel("6.11.4-301.fc41.x86_64"));
        Assert.False(NvidiaScripts.ValidKernel("6.14.0-37-generic; reboot")); Assert.False(NvidiaScripts.ValidKernel("$(id)")); Assert.False(NvidiaScripts.ValidKernel(""));
        Assert.False(NvidiaScripts.ValidPackage("-y")); Assert.False(NvidiaScripts.ValidPackage("a b")); Assert.False(NvidiaScripts.ValidPackage("x'y"));
        Assert.True(NvidiaScripts.IsNvidiaPackage("nvidia-firmware-580-580.178.04")); Assert.True(NvidiaScripts.IsNvidiaPackage("libnvidia-cfg1-580"));
        Assert.False(NvidiaScripts.IsNvidiaPackage("nvidia-prime")); Assert.False(NvidiaScripts.IsNvidiaPackage("nvidia-settings")); Assert.False(NvidiaScripts.IsNvidiaPackage("libnvidia-egl-wayland1"));
        Assert.Throws<ArgumentException>(() => NvidiaPlanner.UbuntuPackages("590", false, Running));
        Assert.Throws<ArgumentException>(() => NvidiaPlanner.UbuntuPackages("580", false, "6.17.7-arch1-1"));
    }

    [Fact]
    public void RealIncidentIsDetectedAndRepairedWithDkmsForTheRunningKernel()
    {
        var d = Incident();
        Assert.Equal(NvidiaDriverState.Broken, NvidiaPlanner.Evaluate(d));
        Assert.Equal(["7.0.0-42-generic"], NvidiaPlanner.SuspectKernels(d));
        var plan = NvidiaPlanner.Plan(d);
        var repair = plan.Actions.Single(a => a.Kind == NvidiaActionKind.Repair);
        Assert.Contains(plan.Actions, a => a.Kind == NvidiaActionKind.Revert);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == NvidiaActionKind.Install);
        // Exactly the packages the lead purged by hand (plus the 7.0 headers), never the running kernel or its meta-packages.
        Assert.Equal(["linux-headers-7.0.0-42-generic", "linux-image-7.0.0-42-generic", "linux-main-modules-v4l2loopback-7.0.0-42-generic", "linux-main-modules-zfs-7.0.0-42-generic",
            "linux-modules-7.0.0-42-generic", "linux-modules-nvidia-580-7.0.0-42-generic", "linux-modules-nvidia-580-generic-hwe-24.04", "linux-objects-nvidia-580-7.0.0-42-generic",
            "linux-signatures-nvidia-7.0.0-42-generic"], repair.AllowedRemovals);
        Assert.Equal(["install", "--purge", "nvidia-dkms-580", "nvidia-driver-580", "linux-headers-6.14.0-37-generic"], repair.SimulationArguments!.Take(5));
        Assert.Contains("linux-image-7.0.0-42-generic-", repair.SimulationArguments!);
        Assert.DoesNotContain(repair.SimulationArguments!, a => a.Contains("6.14.0-37") && a.EndsWith('-'));
        Assert.StartsWith("set -eu\n", repair.Script);
        Assert.Contains("apt-get -y 'install' '--purge' 'nvidia-dkms-580' 'nvidia-driver-580' 'linux-headers-6.14.0-37-generic'", repair.Script);
        Assert.Contains("dkms status -k '6.14.0-37-generic'", repair.Script);
        Assert.Contains("prime-select on-demand", repair.Script);
        Assert.Contains("6\\.14\\.0-37-generic( |$)", repair.Script); // root-side guard protects the running kernel
        Assert.DoesNotContain("ubuntu-drivers", repair.Script);
        Assert.True(repair.Tested);
        Assert.Empty(NvidiaPlanner.CheckSimulation(repair, AptSimulation.Parse(Fixture("apt-sim-repair.txt"), 0), Running));
        Assert.Contains(plan.Findings, f => f.Contains("7.0.0-42-generic") && f.Contains("INCOMPLETOS"));
        Assert.Contains(plan.Findings, f => f.Contains("Pacote pela metade: nvidia-driver-580") && f.Contains("iU"));
    }

    [Fact]
    public void SimulationThatPullsAKernelIsRefused()
    {
        var install = NvidiaPlanner.Plan(Fresh("134d", NvidiaGeneration.Maxwell)).Actions.Single(a => a.Kind == NvidiaActionKind.Install);
        var problems = NvidiaPlanner.CheckSimulation(install, AptSimulation.Parse(Fixture("apt-sim-ubuntu-drivers-hwe.txt"), 0), Running);
        Assert.Contains(problems, p => p.Contains("linux-image-7.0.0-42-generic"));
        Assert.Contains(problems, p => p.Contains("linux-modules-nvidia-580-7.0.0-42-generic"));
        Assert.Empty(NvidiaPlanner.CheckSimulation(install, AptSimulation.Parse(Fixture("apt-sim-dkms-install.txt"), 0), Running));
        // Removing the running kernel or a non-NVIDIA package is refused even for the repair.
        var repair = NvidiaPlanner.Plan(Incident()).Actions.Single(a => a.Kind == NvidiaActionKind.Repair);
        Assert.NotEmpty(NvidiaPlanner.CheckSimulation(repair, AptSimulation.Parse("Purg linux-image-6.14.0-37-generic [6.14.0-37.37]\n", 0), Running));
        Assert.NotEmpty(NvidiaPlanner.CheckSimulation(repair, AptSimulation.Parse("Remv linux-modules-6.14.0-37-generic [6.14.0-37.37]\n", 0), Running));
        Assert.NotEmpty(NvidiaPlanner.CheckSimulation(install, AptSimulation.Parse("Remv steam-installer [1]\n", 0), Running));
        Assert.NotEmpty(NvidiaPlanner.CheckSimulation(install, AptSimulation.Parse("Inst linux-headers-7.0.0-42-generic (7.0)\n", 0), Running));
        Assert.NotEmpty(NvidiaPlanner.CheckSimulation(install, AptSimulation.Parse("E: Unable to locate package nvidia-dkms-580\n", 100), Running));
    }

    [Fact]
    public void FreshMaxwellOnMintGetsTheDkmsInstall()
    {
        var plan = NvidiaPlanner.Plan(Fresh("134d", NvidiaGeneration.Maxwell, "ID=linuxmint\nID_LIKE=\"ubuntu debian\"\nPRETTY_NAME=\"Linux Mint 22.3\""));
        Assert.Equal(NvidiaDriverState.NotInstalled, plan.State);
        var a = Assert.Single(plan.Actions);
        Assert.Equal((NvidiaActionKind.Install, "INSTALAR"), (a.Kind, a.Button));
        Assert.Equal(["install", "nvidia-dkms-580", "nvidia-driver-580", "linux-headers-6.14.0-37-generic"], a.SimulationArguments);
        Assert.Contains(a.Risks, r => r.Contains("último que suporta Maxwell"));
        Assert.Contains("Inst linux-(image|modules|objects|signatures|main-modules)-", a.Script);
        Assert.DoesNotContain("prime-select on-demand", a.Script); // not hybrid
    }

    [Fact]
    public void TuringUsesTheCurrentBranchWithOpenModulesAndKeplerTheLegacyOne()
    {
        var turing = NvidiaPlanner.Plan(Fresh("1f99", NvidiaGeneration.Turing)).Actions.Single();
        Assert.Equal(["install", "nvidia-dkms-595-open", "nvidia-driver-595-open", "linux-headers-6.14.0-37-generic"], turing.SimulationArguments);
        var kepler = NvidiaPlanner.Plan(Fresh("1180", NvidiaGeneration.Kepler)).Actions.Single();
        Assert.Contains("nvidia-driver-470", kepler.SimulationArguments!);
        Assert.Contains(kepler.Risks, r => r.Contains("legado 470"));
        var fermi = NvidiaPlanner.Plan(Fresh("0de1", NvidiaGeneration.Fermi));
        Assert.Equal(NvidiaDriverState.Unsupported, fermi.State); Assert.Empty(fermi.Actions); Assert.Contains("nouveau", fermi.Guidance);
    }

    [Fact]
    public void SecureBootOnAddsTheMokStep()
    {
        var a = NvidiaPlanner.Plan(Fresh("134d", NvidiaGeneration.Maxwell, sb: SecureBootState.Enabled)).Actions.Single();
        Assert.Contains(a.Risks, r => r.Contains("mokutil --import") && r.Contains("Enroll MOK"));
    }

    [Fact]
    public void WorkingMachineIsUpToDateAndOffersOnlyTheUndo()
    {
        var plan = NvidiaPlanner.Plan(Working());
        Assert.Equal(NvidiaDriverState.Working, plan.State);
        Assert.StartsWith("Funcionando · driver 580.178.04", plan.Headline);
        Assert.Contains("GeForce 940MX", plan.Summary);
        var revert = Assert.Single(plan.Actions);
        Assert.Equal(NvidiaActionKind.Revert, revert.Kind);
        Assert.Equal(["purge", "libnvidia-gl-580", "nvidia-dkms-580", "nvidia-driver-580"], revert.SimulationArguments);
        Assert.DoesNotContain("nvidia-prime", revert.Script);
        Assert.Contains(plan.Findings, f => f.Contains("em dia"));
        // A newer candidate in the same branch → ATUALIZAR, with the same DKMS packages.
        var update = NvidiaPlanner.Plan(Working("580.178.04-0ubuntu0.24.04.2")).Actions.Single(a => a.Kind == NvidiaActionKind.Update);
        Assert.Equal(["install", "nvidia-dkms-580", "nvidia-driver-580", "linux-headers-6.14.0-37-generic"], update.SimulationArguments);
        Assert.Equal("ATUALIZAR", update.Button);
    }

    [Fact]
    public void InstalledButNotLoadedAsksForAReboot()
    {
        var d = Working() with { Gpus = [new NvidiaGpu("0000:01:00.0", "134d", "GM108M [GeForce 940MX]", NvidiaGeneration.Maxwell, "GM108", "nouveau")], LoadedVersion = null, SmiVersion = null };
        var plan = NvidiaPlanner.Plan(d);
        Assert.Equal(NvidiaDriverState.NeedsReboot, plan.State);
        Assert.Equal([NvidiaActionKind.Revert], plan.Actions.Select(a => a.Kind));
        Assert.True(NvidiaPlanner.Verify(NvidiaActionKind.Install, d).Ok);
        Assert.Contains("dkms: nvidia/580.178.04 installed", NvidiaPlanner.Verify(NvidiaActionKind.Install, d).Text);
        Assert.False(NvidiaPlanner.Verify(NvidiaActionKind.Install, Incident()).Ok);
        Assert.False(NvidiaPlanner.Verify(NvidiaActionKind.Revert, d).Ok);
        Assert.True(NvidiaPlanner.Verify(NvidiaActionKind.Revert, d with { Packages = [] }).Ok);
    }

    [Fact]
    public void OtherFamiliesGetFixedUntestedScripts()
    {
        var fedora = NvidiaPlanner.Plan(Fresh("2786", NvidiaGeneration.Ada, "ID=fedora\nPRETTY_NAME=\"Fedora 43\"", kernel: "6.17.4-300.fc43.x86_64")).Actions.Single();
        Assert.False(fedora.Tested); Assert.Null(fedora.SimulationArguments); Assert.Contains("akmod-nvidia", fedora.Script); Assert.Contains("rpmfusion", fedora.Script);
        var fedoraMaxwell = NvidiaPlanner.Plan(Fresh("134d", NvidiaGeneration.Maxwell, "ID=fedora", kernel: "6.17.4-300.fc43.x86_64")).Actions.Single();
        Assert.Contains("akmod-nvidia-580xx", fedoraMaxwell.Script);
        var arch = NvidiaPlanner.Plan(Fresh("2786", NvidiaGeneration.Ada, "ID=arch", kernel: "6.17.7-arch1-1")).Actions.Single();
        Assert.Contains("pacman -S --needed --noconfirm nvidia-open-dkms nvidia-utils 'linux-headers'", arch.Script);
        var archMaxwell = NvidiaPlanner.Plan(Fresh("134d", NvidiaGeneration.Maxwell, "ID=cachyos\nID_LIKE=arch", kernel: "6.17.7-2-cachyos")).Actions.Single();
        Assert.NotNull(archMaxwell.Blocked); Assert.Contains("AUR", archMaxwell.Blocked);
        var suse = NvidiaPlanner.Plan(Fresh("2786", NvidiaGeneration.Ada, "ID=opensuse-tumbleweed", kernel: "6.17.7-1-default")).Actions.Single();
        Assert.Contains("nvidia-open-driver-G06-signed-kmp-default", suse.Script);
        var other = NvidiaPlanner.Plan(Fresh("2786", NvidiaGeneration.Ada, "ID=gentoo", kernel: "6.17.7-gentoo"));
        Assert.Empty(other.Actions); Assert.Contains("gerenciador de drivers", other.Guidance);
        Assert.Equal("linux-cachyos-headers", NvidiaPlanner.ArchHeaders("6.17.7-2-cachyos"));
        Assert.Equal("linux66-headers", NvidiaPlanner.ArchHeaders("6.6.54-1-MANJARO"));
        Assert.Equal("linux-lts-headers", NvidiaPlanner.ArchHeaders("6.12.9-1-lts"));
    }

    [Fact]
    public void NoNvidiaGpuIsAnEmptyState()
    {
        var plan = NvidiaPlanner.Plan(new NvidiaDiagnosis { OtherGpus = ["Intel UHD"], Distro = DistroInfo.Parse("ID=ubuntu"), RunningKernel = Running });
        Assert.Equal(NvidiaDriverState.NoNvidiaGpu, plan.State); Assert.Empty(plan.Actions);
    }

    /// <summary>Probe over a fake root (sysfs, /etc, /usr/src, /lib/modules) and a fake runner; records every program it asks for.</summary>
    [Fact]
    public async Task ProbeReadsTheFakeRootAndRunsOnlyReadOnlyPrograms()
    {
        string root = Path.Combine(Path.GetTempPath(), "ilx-nv-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            void W(string rel, string text) { var p = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); }
            W("sys/bus/pci/devices/0000:01:00.0/class", "0x030200\n"); W("sys/bus/pci/devices/0000:01:00.0/vendor", "0x10de\n"); W("sys/bus/pci/devices/0000:01:00.0/device", "0x134d\n");
            W("sys/bus/pci/devices/0000:00:02.0/class", "0x030000\n"); W("sys/bus/pci/devices/0000:00:02.0/vendor", "0x8086\n"); W("sys/bus/pci/devices/0000:00:02.0/device", "0x5916\n");
            Directory.CreateDirectory(Path.Combine(root, "sys/bus/pci/drivers/nouveau"));
            Directory.CreateSymbolicLink(Path.Combine(root, "sys/bus/pci/devices/0000:01:00.0/driver"), Path.Combine(root, "sys/bus/pci/drivers/nouveau"));
            W("etc/os-release", Fixture("os-release-mint-22.3")); W("proc/sys/kernel/osrelease", Running + "\n");
            W("boot/vmlinuz-" + Running, ""); W("boot/vmlinuz-7.0.0-42-generic", "");
            foreach (var f in KernelHeaders.RequiredFiles) W($"usr/src/linux-headers-{Running}/{f}", "");
            W("usr/src/linux-headers-7.0.0-42-generic/Makefile", "");
            Directory.CreateDirectory(Path.Combine(root, "lib/modules", Running));
            var calls = new List<string>();
            ReadOnlyRunner runner = (p, a, t, ct) =>
            {
                calls.Add(p + " " + string.Join(' ', a));
                return Task.FromResult(p switch
                {
                    "mokutil" => (0, "SecureBoot disabled\n"), "prime-select" => (0, "on-demand\n"), "dpkg-query" => (1, Fixture("dpkg-incident-mint-22.3.txt")),
                    "apt-cache" => (0, "nvidia-driver-580:\n  Installed: (none)\n  Candidate: 580.178.04-0ubuntu0.24.04.1\n"), "dkms" => (0, ""),
                    "apt-get" => (0, Fixture("apt-sim-repair.txt")), _ => throw new FileNotFoundException(p),
                });
            };
            var probe = new NvidiaDriverProbe(root, runner, p => p is not "nvidia-smi", PciIds.Parse("10de  NVIDIA Corporation\n\t134d  GM108M [GeForce 940MX]\n8086  Intel Corporation\n\t5916  HD Graphics 620\n"));
            var d = await probe.DiagnoseAsync();
            Assert.Equal(("134d", NvidiaGeneration.Maxwell, "nouveau", "GM108M [GeForce 940MX]"), (d.Primary!.DeviceId, d.Primary.Generation, d.KernelDriver, d.Primary.Name));
            Assert.True(d.Hybrid); Assert.Equal(DistroFamily.Ubuntu, d.Distro.Family); Assert.Equal(SecureBootState.Disabled, d.SecureBoot); Assert.Equal("on-demand", d.PrimeMode);
            Assert.True(d.RunningHeaders!.Complete);
            Assert.Equal(["scripts/module.lds", "tools/objtool/objtool"], d.Kernels.Single(k => k.Kernel == "7.0.0-42-generic").Missing);
            Assert.False(d.SmiInstalled); Assert.Null(d.LoadedVersion);
            var plan = NvidiaPlanner.Plan(d);
            Assert.Equal(NvidiaDriverState.Broken, plan.State);
            var sim = await probe.SimulateAsync(plan.Actions.Single(a => a.Kind == NvidiaActionKind.Repair));
            Assert.Equal(9, sim.Removals.Count());
            Assert.All(calls, c => Assert.DoesNotContain("pkexec", c));
            Assert.Contains(calls, c => c.StartsWith("apt-get -s install --purge nvidia-dkms-580", StringComparison.Ordinal));
            Assert.DoesNotContain(calls, c => c.StartsWith("apt-get", StringComparison.Ordinal) && !c.StartsWith("apt-get -s ", StringComparison.Ordinal));
            await Assert.ThrowsAsync<ArgumentException>(() => probe.SimulateAsync(plan.Actions[0] with { SimulationArguments = ["install", "x;reboot"] }));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task CliStatusAndPlanAreReadOnly()
    {
        var calls = new List<string>();
        ReadOnlyRunner runner = (p, a, t, ct) => { calls.Add(p + " " + string.Join(' ', a)); return Task.FromResult(p == "apt-get" ? (0, Fixture("apt-sim-noop-real.txt")) : (0, "")); };
        string root = Path.Combine(Path.GetTempPath(), "ilx-nv-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "etc")); File.WriteAllText(Path.Combine(root, "etc/os-release"), "ID=ubuntu\n");
        try
        {
            var o = new StringWriter();
            Assert.Equal(0, await NvidiaDriverCommand.RunAsync(["nvidia-driver", "status"], o, CancellationToken.None, new NvidiaDriverProbe(root, runner, _ => true, PciIds.Parse(""))));
            Assert.Contains("Nenhuma placa NVIDIA", o.ToString());
            Assert.Equal(2, await NvidiaDriverCommand.RunAsync(["nvidia-driver", "install"], new StringWriter(), CancellationToken.None, new NvidiaDriverProbe(root, runner, _ => true, PciIds.Parse(""))));
            Assert.All(calls, c => Assert.DoesNotContain("pkexec", c));
        }
        finally { Directory.Delete(root, true); }
    }
}
