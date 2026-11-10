// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class BlockedFeaturesTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ilmenyx-probe-" + Guid.NewGuid().ToString("N"));
    public BlockedFeaturesTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    private static readonly MachineProbe None = new(false, false, false, false, false);

    [Fact]
    public void WithoutCapableHardwareEveryFeatureIsBlockedWithAReason()
    {
        foreach (var f in BlockedCatalog.All)
        {
            Assert.NotEqual(BlockedState.TestAvailable, f.State(None));
            Assert.False(string.IsNullOrWhiteSpace(f.Reason));
            Assert.False(string.IsNullOrWhiteSpace(f.KindLabel));
        }
        Assert.Equal(0, BlockedCatalog.AvailableCount(None));
        Assert.Contains(BlockedCatalog.All, f => f.Id == "rt-bench" && f.KindLabel == "Limitação de hardware do autor");
    }

    [Fact]
    public void MatchingHardwareTurnsOnlyTheMatchingFeatureIntoTestAvailable()
    {
        Assert.Equal(BlockedState.TestAvailable, BlockedCatalog.All.Single(f => f.Id == "rt-bench").State(None with { RayTracingGpu = true }));
        Assert.Equal(BlockedState.Blocked, BlockedCatalog.All.Single(f => f.Id == "gpu-amd").State(None with { RayTracingGpu = true }));
        Assert.Equal(1, BlockedCatalog.AvailableCount(None with { AmdGpu = true, AmdDedicatedGpu = true }));
        Assert.DoesNotContain(BlockedCatalog.All, f => f.Id == "game-fps"); // validated with SuperTuxKart on 2026-11-10: see docs/bloqueados-removidos.md
    }

    [Fact]
    public void ItemsThatDoNotExistOnLinuxAreNotListed()
    {
        foreach (var id in new[] { "windows-tweaks", "usb-cleaner", "winrar" }) Assert.DoesNotContain(BlockedCatalog.All, f => f.Id == id);
        Assert.DoesNotContain(BlockedCatalog.All, f => f.Name.Contains("WinRAR") || f.Name.Contains("Windows"));
        Assert.Equal(BlockedCatalog.All.Count, BlockedCatalog.All.Select(f => f.Id).Distinct().Count());
    }

    [Fact]
    public void ValidatedHardwareNoLongerUnlocksItsItem()
    {
        // Integrated Radeon (validated 2026-11-06) and a notebook NVIDIA without ray tracing (GeForce 940MX, validated 2026-11-07) have nothing left to test.
        var amdApu = None with { AmdGpu = true };
        Assert.Equal(BlockedState.Blocked, BlockedCatalog.All.Single(f => f.Id == "gpu-amd").State(amdApu));
        Assert.Equal(BlockedState.TestAvailable, BlockedCatalog.All.Single(f => f.Id == "gpu-amd").State(amdApu with { AmdDedicatedGpu = true }));
        var gtx940mx = None with { NvidiaGpu = true };
        Assert.Equal(0, BlockedCatalog.AvailableCount(gtx940mx));
        Assert.Equal(BlockedState.TestAvailable, BlockedCatalog.All.Single(f => f.Id == "gpu-nvidia").State(gtx940mx with { RayTracingGpu = true }));
        var amd = BlockedCatalog.All.Single(f => f.Id == "gpu-amd");
        Assert.Contains("integrada foi validada", amd.Reason); Assert.Contains("dedicada não foi testada", amd.Reason);
        foreach (var word in new[] { "VRAM", "ventoinha", "limite de energia", "sysfs" }) Assert.Contains(word, amd.Reason);
    }

    [Fact]
    public void ProbeTellsAnApuFromADedicatedAmdCard()
    {
        void Card(string name, byte? format, bool fan)
        {
            var dev = Path.Combine(dir, "class/drm", name, "device"); Directory.CreateDirectory(dev); File.WriteAllText(Path.Combine(dev, "vendor"), "0x1002\n");
            if (format is { } f) File.WriteAllBytes(Path.Combine(dev, "gpu_metrics"), [0x60, 0x00, f, 0x01]);
            if (fan) { Directory.CreateDirectory(Path.Combine(dev, "hwmon/hwmon3")); File.WriteAllText(Path.Combine(dev, "hwmon/hwmon3/fan1_input"), "1200\n"); }
        }
        Card("card0", 2, false); // APU table
        var probe = MachineProbeReader.Read(false, false, dir);
        Assert.True(probe.AmdGpu); Assert.False(probe.AmdDedicatedGpu);
        Card("card1", 1, false); // dedicated table
        Assert.True(MachineProbeReader.Read(false, false, dir).AmdDedicatedGpu);
        Directory.Delete(Path.Combine(dir, "class/drm/card1"), true);
        Card("card2", null, true); // older card without gpu_metrics, own fan
        Assert.True(MachineProbeReader.Read(false, false, dir).AmdDedicatedGpu);
    }

    [Fact]
    public void AKingstonPendriveIsNotAnRgbDevice()
    {
        // This Dell had a Kingston DataTraveler (0951:1666, mass storage) plugged in and the page wrongly offered the RGB test.
        var stick = Path.Combine(dir, "bus/usb/devices/2-1"); Directory.CreateDirectory(Path.Combine(stick, "2-1:1.0"));
        File.WriteAllText(Path.Combine(stick, "idVendor"), "0951\n"); File.WriteAllText(Path.Combine(stick, "2-1:1.0/bInterfaceClass"), "08\n");
        Assert.False(MachineProbeReader.Read(false, false, dir).RgbUsbDevice);
        var headset = Path.Combine(dir, "bus/usb/devices/2-2"); Directory.CreateDirectory(Path.Combine(headset, "2-2:1.3"));
        File.WriteAllText(Path.Combine(headset, "idVendor"), "0951\n"); File.WriteAllText(Path.Combine(headset, "2-2:1.3/bInterfaceClass"), "03\n");
        Assert.True(MachineProbeReader.Read(false, false, dir).RgbUsbDevice);
    }

    [Fact]
    public void ProbeReadsGpuAndRgbVendorsFromSysfs()
    {
        Directory.CreateDirectory(Path.Combine(dir, "class/drm/card0/device")); File.WriteAllText(Path.Combine(dir, "class/drm/card0/device/vendor"), "0x1002\n");
        Directory.CreateDirectory(Path.Combine(dir, "class/drm/card1/device")); File.WriteAllText(Path.Combine(dir, "class/drm/card1/device/vendor"), "0x10de\n");
        Directory.CreateDirectory(Path.Combine(dir, "bus/usb/devices/1-2")); File.WriteAllText(Path.Combine(dir, "bus/usb/devices/1-2/idVendor"), "1B1C\n");
        Directory.CreateDirectory(Path.Combine(dir, "bus/usb/devices/1-2/1-2:1.0")); File.WriteAllText(Path.Combine(dir, "bus/usb/devices/1-2/1-2:1.0/bInterfaceClass"), "03\n");
        var probe = MachineProbeReader.Read(false, true, dir);
        Assert.True(probe.AmdGpu); Assert.True(probe.NvidiaGpu); Assert.True(probe.RgbUsbDevice); Assert.True(probe.GameLibrary); Assert.False(probe.RayTracingGpu);
    }

    [Fact]
    public void ProbeIsEmptyForMissingOrUnrelatedSysfs()
    {
        Directory.CreateDirectory(Path.Combine(dir, "class/drm/card0/device")); File.WriteAllText(Path.Combine(dir, "class/drm/card0/device/vendor"), "0x8086\n");
        Directory.CreateDirectory(Path.Combine(dir, "bus/usb/devices/1-1")); File.WriteAllText(Path.Combine(dir, "bus/usb/devices/1-1/idVendor"), "8087\n");
        Assert.Equal(None, MachineProbeReader.Read(false, false, dir));
        Assert.Equal(None, MachineProbeReader.Read(false, false, Path.Combine(dir, "nope")));
    }

    [Fact]
    public void GameLibraryProbeNeedsARealManifestOrDatabase()
    {
        Assert.False(MachineProbeReader.HasGameLibrary(dir));
        Directory.CreateDirectory(Path.Combine(dir, ".local/share/Steam/steamapps")); Assert.False(MachineProbeReader.HasGameLibrary(dir));
        File.WriteAllText(Path.Combine(dir, ".local/share/Steam/steamapps/appmanifest_10.acf"), "x"); Assert.True(MachineProbeReader.HasGameLibrary(dir));
    }
}
