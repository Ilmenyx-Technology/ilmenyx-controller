using System.Globalization;
using System.Text.Json;
using Silex.Audit.Contracts;
using Silex.Audit.Core;
using Silex.Audit.Platform.Linux;
using Xunit;

namespace Silex.Audit.Tests;

public class EnergyTests
{
    internal sealed class Files : IDiagnosticSysfsFiles
    {
        public Dictionary<string, string> Values = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "energy.json")))!;
        public bool Removed, Slow;
        public SysfsArea? DeniedArea;
        public string? DeniedAttribute;
        public int DevicesCount;
        public IReadOnlyList<string> Devices(SysfsArea area) => DeniedArea == area ? throw new UnauthorizedAccessException() : Removed ? [] : DevicesCount > 0 ? Enumerable.Range(0, DevicesCount).Select(i => area == SysfsArea.Thermal ? $"thermal_zone{i}" : area == SysfsArea.Drm ? $"card{i}" : $"BAT{i}").ToArray() : Values.Keys.Where(k => k.StartsWith(area + "/")).Select(k => k.Split('/')[1]).Distinct().ToArray();
        public async Task<string> ReadAsync(SysfsArea area, string device, string attribute, CancellationToken ct)
        {
            if (Slow) await Task.Delay(Timeout.Infinite, ct);
            if (DeniedAttribute == attribute) throw new UnauthorizedAccessException();
            if (Values.TryGetValue($"{area}/{device}/{attribute}", out var value)) return value;
            throw new FileNotFoundException();
        }
    }
    [Theory]
    [InlineData("capacity", "75", 75, "%")]
    [InlineData("voltage_now", "12500000", 12.5, "V")]
    [InlineData("current_now", "-1500000", -1.5, "A")]
    [InlineData("power_now", "2300000", 2.3, "W")]
    [InlineData("energy_now", "37500000", 37.5, "Wh")]
    [InlineData("charge_now", "3000000", 3, "Ah")]
    [InlineData("temp", "285", 28.5, "°C")]
    [InlineData("cycle_count", "0", 0, "cycles")]
    [InlineData("online", "0", 0, "boolean")]
    public void PowerUnitsUseTheirOwnAbi(string attribute, string raw, double value, string unit)
    {
        var old = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR"); var r = KernelDiagnosticSensors.ParsePower(raw, attribute, "fixture"); Assert.Equal(value, r.Value); Assert.Equal(unit, r.Unit); }
        finally { CultureInfo.CurrentCulture = old; }
    }
    [Theory]
    [InlineData("capacity", "101")]
    [InlineData("present", "2")]
    [InlineData("online", "-1")]
    [InlineData("energy_now", "-1")]
    [InlineData("charge_now", "-1")]
    [InlineData("temp", "-2732")]
    [InlineData("temp", "28.5")]
    [InlineData("power_now", "9223372036854775808")]
    [InlineData("capacity", "75,0")]
    public void InvalidPowerIsUnavailable(string attr, string raw) => Assert.Null(KernelDiagnosticSensors.ParsePower(raw, attr, "fixture").Value);
    [Theory]
    [InlineData(SysfsArea.Thermal, "thermal_zone0\n", "temp")]
    [InlineData(SysfsArea.Thermal, "cooling_device0", "temp")]
    [InlineData(SysfsArea.Thermal, "thermal_zone0", "mode")]
    [InlineData(SysfsArea.PowerSupply, "../BAT1", "type")]
    [InlineData(SysfsArea.PowerSupply, "BAT1", "serial_number")]
    [InlineData(SysfsArea.Drm, "card1-DP-1", "device/vendor")]
    [InlineData(SysfsArea.Drm, "card1", "device/remove")]
    public async Task UnsafePathsAndControlAttributesRejected(SysfsArea area, string device, string attribute) =>
        await Assert.ThrowsAsync<IOException>(() => new DiagnosticSysfsFiles().ReadAsync(area, device, attribute, default));
    [Fact]
    public async Task FixturePreservesTypeStatusAndSeparateTemperatureUnits()
    {
        var s = await new KernelDiagnosticSensors(new Files()).CollectAsync();
        Assert.Equal("available", s.Status);
        Assert.Equal(42.5, Assert.Single(s.Readings, r => r.Device.StartsWith("Thermal/")).Reading.Value);
        Assert.Equal(28.5, Assert.Single(s.Readings, r => r.Channel == "temp" && r.Device.StartsWith("PowerSupply/")).Reading.Value);
        var p = Assert.Single(s.PowerSupplies!); Assert.Equal("Battery", p.Type.Value); Assert.Equal("Discharging", p.Status.Value);
        Assert.Equal("/sys/class/power_supply/BAT1/type", p.Type.Source);
        Assert.Equal(0, Assert.Single(s.Readings, r => r.Channel == "cycle_count").Reading.Value);
        using var json = JsonDocument.Parse(SnapshotReport.Json(s)); Assert.Equal("1.1", json.RootElement.GetProperty("schemaVersion").GetString());
    }
    [Theory]
    [InlineData("0")]
    [InlineData("invalid")]
    public async Task AbsentOrInvalidPresenceDoesNotPublishStaleBatteryValues(string present)
    {
        var f = new Files(); f.Values["PowerSupply/BAT1/present"] = present;
        var s = await new KernelDiagnosticSensors(f).CollectAsync();
        Assert.All(s.Readings.Where(r => r.Device.StartsWith("PowerSupply/")), r => Assert.Null(r.Reading.Value));
        Assert.Equal("partial", s.Status);
    }
    [Fact]
    public async Task DeniedPresenceBlocksBatteryValues()
    {
        var s = await new KernelDiagnosticSensors(new Files { DeniedAttribute = "present" }).CollectAsync();
        Assert.All(s.Readings.Where(r => r.Device.StartsWith("PowerSupply/")), r => Assert.Null(r.Reading.Value));
    }
    [Fact]
    public async Task MissingTreeDoesNotHideOtherSourcesAndHotplugIsFresh()
    {
        var f = new Files { DeniedArea = SysfsArea.Thermal }; var p = new KernelDiagnosticSensors(f);
        var s = await p.CollectAsync(); Assert.Equal("partial", s.Status); Assert.NotEmpty(s.PowerSupplies!);
        f.Removed = true; Assert.Empty((await p.CollectAsync()).Readings);
        f.Removed = false; Assert.NotEmpty((await p.CollectAsync()).Readings);
    }
    [Fact]
    public async Task MainsNeedsNoBatteryStatusOrPresence()
    {
        var f = new Files(); f.Values.Clear(); f.Values["PowerSupply/AC/type"] = "Mains"; f.Values["PowerSupply/AC/online"] = "1";
        var s = await new KernelDiagnosticSensors(f).CollectAsync(); Assert.Equal("available", s.Status); Assert.Equal(1, Assert.Single(s.Readings).Reading.Value);
        Assert.Null(Assert.Single(s.PowerSupplies!).Status.Value);
    }
    [Fact]
    public async Task DeadlineAndExternalCancellationAreDistinct()
    {
        var p = new KernelDiagnosticSensors(new Files { Slow = true }, TimeSpan.FromMilliseconds(20));
        var s = await p.CollectAsync(); Assert.Equal("unavailable", s.Status); Assert.Contains(s.Limitations, x => x.Contains("Timeout"));
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p.CollectAsync(ct.Token));
    }
    [Fact]
    public async Task LimitsPreventUnboundedDiscovery()
    {
        var s = await new KernelDiagnosticSensors(new Files { DevicesCount = 65 }).CollectAsync();
        Assert.Equal(64, s.Readings.Count(r => r.Device.StartsWith("Thermal/"))); Assert.Equal(64, s.PowerSupplies!.Count);
        Assert.Contains(s.Limitations, x => x.Contains("64 dispositivos"));
    }
    private sealed class Fixed(SensorSnapshot value) : ISensorProvider
    { public Task<SensorSnapshot> CollectAsync(CancellationToken ct = default) => Task.FromResult(value); }
    [Fact]
    public async Task AggregatorPreservesDuplicateSourcesAndPartialStatus()
    {
        var s = await new KernelDiagnosticSensors(new Files()).CollectAsync();
        var a = await new LinuxSensorProvider(new Fixed(s), new Fixed(s with { Status = "partial" })).CollectAsync();
        Assert.Equal("partial", a.Status); Assert.Equal(2 * s.Readings.Count, a.Readings.Count);
        Assert.Equal(2, a.PowerSupplies!.Count);
    }
}
