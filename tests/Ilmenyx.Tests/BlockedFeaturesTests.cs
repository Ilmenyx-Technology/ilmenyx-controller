// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
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
    public void WithoutCapableHardwareEveryFeatureIsBlockedOrNotApplicableWithAReason()
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
        Assert.Equal(1, BlockedCatalog.AvailableCount(None with { NvidiaGpu = true }));
        Assert.Equal(BlockedState.TestAvailable, BlockedCatalog.All.Single(f => f.Id == "game-fps").State(None with { GameLibrary = true }));
    }

    [Fact]
    public void WindowsOnlyFeaturesNeverBecomeTestable()
    {
        var all = new MachineProbe(true, true, true, true, true);
        Assert.All(BlockedCatalog.All.Where(f => f.Kind == BlockReason.WindowsOnly), f => Assert.Equal(BlockedState.NotApplicable, f.State(all)));
    }

    [Fact]
    public void ProbeReadsGpuAndRgbVendorsFromSysfs()
    {
        Directory.CreateDirectory(Path.Combine(dir, "class/drm/card0/device")); File.WriteAllText(Path.Combine(dir, "class/drm/card0/device/vendor"), "0x1002\n");
        Directory.CreateDirectory(Path.Combine(dir, "class/drm/card1/device")); File.WriteAllText(Path.Combine(dir, "class/drm/card1/device/vendor"), "0x10de\n");
        Directory.CreateDirectory(Path.Combine(dir, "bus/usb/devices/1-2")); File.WriteAllText(Path.Combine(dir, "bus/usb/devices/1-2/idVendor"), "1B1C\n");
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
