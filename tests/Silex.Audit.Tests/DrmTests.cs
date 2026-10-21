using Silex.Audit.Platform.Linux;
using Xunit;

namespace Silex.Audit.Tests;

public class DrmTests
{
    private static EnergyTests.Files Fixture(string vendor = "0x8086")
    {
        var f = new EnergyTests.Files(); f.Values.Clear();
        f.Values["Drm/card1/device/vendor"] = vendor; f.Values["Drm/card1/device/device"] = "0x9a49"; f.Values["Drm/card1/device/class"] = "0x030000";
        return f;
    }
    [Theory]
    [InlineData("0x12xz")]
    [InlineData("0x12345")]
    [InlineData("1234")]
    [InlineData("0x1234\nextra")]
    public void InvalidHexHasNoId(string raw) => Assert.Null(DrmGpuInfoProvider.Hex(raw, 4, "fixture").Value);
    [Theory]
    [InlineData("-1", "B")]
    [InlineData("101", "%")]
    [InlineData("9223372036854775808", "B")]
    [InlineData("1.5", "%")]
    public void InvalidCountersHaveNoValue(string raw, string unit) => Assert.Null(DrmGpuInfoProvider.Counter(raw, unit, "fixture").Value);
    [Fact]
    public async Task IntelIdsWithoutInventedVramOrLoad()
    {
        var s = await new DrmGpuInfoProvider(Fixture()).CollectAsync(); Assert.Equal("available", s.Status);
        var g = Assert.Single(s.Devices); Assert.Equal("0x8086", g.VendorId.Value); Assert.Equal("0x9a49", g.DeviceId.Value);
        Assert.Null(g.VramTotal.Value); Assert.Null(g.BusyPercent.Value);
        Assert.Equal("/sys/class/drm/card1/device/vendor", g.VendorId.Source);
    }
    [Fact]
    public async Task AmdCountersPreserveZeroAndRejectInconsistentVram()
    {
        var f = Fixture("0x1002"); f.Values["Drm/card1/device/mem_info_vram_total"] = "100";
        f.Values["Drm/card1/device/mem_info_vram_used"] = "101"; f.Values["Drm/card1/device/gpu_busy_percent"] = "0";
        var g = Assert.Single((await new DrmGpuInfoProvider(f).CollectAsync()).Devices);
        Assert.Equal(0, g.BusyPercent.Value); Assert.Equal(100, g.VramTotal.Value); Assert.Null(g.VramUsed.Value);
    }
    [Fact]
    public async Task NvidiaIsIdentificationOnlyAndUnknownVendorIsPreserved()
    {
        foreach (var vendor in new[] { "0x10de", "0xabcd" })
        {
            var g = Assert.Single((await new DrmGpuInfoProvider(Fixture(vendor)).CollectAsync()).Devices);
            Assert.Equal(vendor, g.VendorId.Value); Assert.Null(g.BusyPercent.Value);
        }
    }
    [Fact]
    public async Task HotplugAndPermissionFailuresAreExplicit()
    {
        var f = Fixture(); var p = new DrmGpuInfoProvider(f);
        Assert.Single((await p.CollectAsync()).Devices); f.Removed = true;
        Assert.Empty((await p.CollectAsync()).Devices); f.Removed = false;
        f.DeniedAttribute = "device/vendor"; Assert.Equal("unavailable", (await p.CollectAsync()).Status);
        f.DeniedArea = SysfsArea.Drm; Assert.Empty((await p.CollectAsync()).Devices);
    }
    [Fact]
    public async Task DiscoveryIsLimitedToSixteenCards()
    {
        var f = Fixture(); f.DevicesCount = 65;
        var s = await new DrmGpuInfoProvider(f).CollectAsync();
        Assert.Equal(16, s.Devices.Count); Assert.Equal("partial", s.Status);
        Assert.Contains(s.Limitations, l => l.Contains("16 placas"));
    }
    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DrmGpuInfoProvider(Fixture()).CollectAsync(ct.Token));
    }
}
