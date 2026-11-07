// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class GpuTelemetrySensorsTests
{
    private static GpuRaw Raw(string vendor = "1002") => new("AMD Radeon RX 7800 XT", vendor, "747e", "", "", "c8", "0000:03:00.0", "amdgpu", "", "", 16UL << 30, 0, false, 4, 16, 4, 16, 2430, 1250, 0, 263, 263, 0, 0, null, null, [], "");

    private static GpuTelemetry Live() => new(63, 61, 78, 58, 2300, 1249, 6UL << 30, 16UL << 30, 140.5, 263, 41, 1180, null, null, null, null, null, 4, 16);

    private static GpuTelemetrySensors Provider(params (GpuRaw, GpuTelemetry)[] gpus) => new(_ => Task.FromResult<IReadOnlyList<(GpuRaw, GpuTelemetry)>>(gpus));

    [Theory]
    [InlineData("10de", "nouveau", true)]
    [InlineData("10de", "nvidia", false)]
    [InlineData("1002", "amdgpu", false)]
    public async Task MissingReasonExplainsNouveauOnlyForNvidia(string vendor, string driver, bool nouveau)
    {
        var raw = Raw(vendor) with { Driver = driver };
        var empty = new GpuTelemetry(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var snapshot = await Provider((raw, empty)).CollectAsync();
        Assert.All(snapshot.Readings, r => Assert.Equal(nouveau ? GpuTelemetrySensors.NouveauReason : "Contador não exposto por este driver/GPU.", r.Reading.Reason));
        if (nouveau) Assert.All(snapshot.Readings, r => Assert.Equal("sysfs (nouveau)", r.Reading.Source));
        Assert.Contains("Drivers", GpuTelemetrySensors.NouveauReason);
    }

    [Fact]
    public async Task TelemetryBecomesSensorChannelsWithUnits()
    {
        var snapshot = await Provider((Raw(), Live())).CollectAsync();
        Assert.Equal("available", snapshot.Status);
        double V(string channel) => snapshot.Readings.Single(r => r.Channel == channel).Reading.Value!.Value;
        Assert.Equal(63, V("load")); Assert.Equal(61, V("temp1")); Assert.Equal(78, V("temp2")); Assert.Equal(140.5, V("power1")); Assert.Equal(2300, V("freq1")); Assert.Equal(6144, V("vram_used")); Assert.Equal(16384, V("vram_total"));
        var power = snapshot.Readings.Single(r => r.Channel == "power1");
        Assert.Equal("W", power.Reading.Unit); Assert.Equal("gpu:0000:03:00.0", power.Device); Assert.Contains("amdgpu", power.Reading.Source);
        Assert.All(snapshot.Readings, r => Assert.Equal("driver", r.Quality));
    }

    [Fact]
    public async Task AbsentCountersAreUnavailableNeverZero()
    {
        var empty = new GpuTelemetry(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var snapshot = await Provider((Raw("8086"), empty)).CollectAsync();
        Assert.Equal("partial", snapshot.Status);
        Assert.All(snapshot.Readings, r => { Assert.Null(r.Reading.Value); Assert.Equal("unavailable", r.Reading.Status); Assert.NotNull(r.Reading.Reason); });
        Assert.Contains("sysfs", snapshot.Readings[0].Reading.Source);
    }

    [Fact]
    public async Task NvidiaSourceIsNamed()
    {
        var snapshot = await Provider((Raw("10de"), Live())).CollectAsync();
        Assert.All(snapshot.Readings.Where(r => r.Reading.Status == "available"), r => Assert.Equal("nvidia-smi", r.Reading.Source));
    }

    [Fact]
    public async Task NoGpuOrAFailedReadIsUnavailableWithAReason()
    {
        var none = await Provider().CollectAsync();
        Assert.Equal("unavailable", none.Status); Assert.Empty(none.Readings);
        var failed = await new GpuTelemetrySensors(_ => throw new IOException("nvidia-smi travou")).CollectAsync();
        Assert.Equal("unavailable", failed.Status); Assert.Contains("nvidia-smi travou", failed.Reason);
    }

    private sealed class Fixed(SensorSnapshot snapshot) : ISensorProvider { public Task<SensorSnapshot> CollectAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot); }

    [Fact]
    public async Task LinuxSensorProviderMergesTheGpuPartAndKeepsTheOthers()
    {
        var other = new SensorSnapshot("1.1", DateTimeOffset.UtcNow, "available", null, [new("hwmon0", "coretemp", "temp1", "Package", DateTimeOffset.UtcNow, "kernel-raw", Reading<double?>.Available(50, "°C", "x"))], ["a"]);
        var merged = await new LinuxSensorProvider(new Fixed(other), new Fixed(other with { Readings = [] }), Provider((Raw(), Live()))).CollectAsync();
        Assert.Contains(merged.Readings, r => r.Chip == "coretemp");
        Assert.Contains(merged.Readings, r => r.Device.StartsWith("gpu:", StringComparison.Ordinal));
        Assert.Equal("available", merged.Status);
        var withoutGpu = await new LinuxSensorProvider(new Fixed(other), new Fixed(other)).CollectAsync();
        Assert.DoesNotContain(withoutGpu.Readings, r => r.Device.StartsWith("gpu:", StringComparison.Ordinal));
    }
}
