// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using System.Text.Json;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Desktop;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class SensorTests
{
    private sealed class Files : IHwmonFiles
    {
        public Dictionary<string, string> Values = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "hwmon.json")))!;
        public bool Removed, Denied, Slow;
        public string? DeniedAttribute;
        public bool InputFailure;
        public int DeviceCount = 1, ChannelCount;
        public IReadOnlyList<string> Devices() => Denied ? throw new UnauthorizedAccessException() : Removed ? [] : Enumerable.Range(0, DeviceCount).Select(i => $"hwmon{i}").ToArray();
        public IReadOnlyList<string> Inputs(string device) => ChannelCount > 0 ? Enumerable.Range(1, ChannelCount).Select(i => $"fan{i}_input").ToArray() : Values.Keys.Where(k => k.EndsWith("_input")).ToArray();
        public async Task<string> ReadAsync(string device, string attribute, CancellationToken ct)
        {
            if (attribute == DeniedAttribute) throw new UnauthorizedAccessException();
            if (InputFailure && attribute.EndsWith("_input")) throw new IOException();
            if (Slow) await Task.Delay(Timeout.Infinite, ct);
            if (Values.TryGetValue(attribute, out var value)) return value;
            throw new FileNotFoundException();
        }
    }
    [Fact]
    public async Task AmdTemperatureUsesDocumentedMilliCelsius()
    {
        var f = new Files(); f.Values["name"] = "amdgpu";
        var s = await new HwmonSensorProvider(f).CollectAsync();
        Assert.Equal(42.5, Assert.Single(s.Readings, r => r.Channel == "temp1").Reading.Value);
    }
    [Theory]
    [InlineData("temp", "-2500", -2.5, "°C")]
    [InlineData("in", "12500", 12.5, "V")]
    [InlineData("curr", "-1500", -1.5, "A")]
    [InlineData("power", "2300000", 2.3, "W")]
    [InlineData("fan", "0", 0, "RPM")]
    public void UnitsAreInvariant(string kind, string raw, double expected, string unit)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var r = HwmonParser.Parse(raw, kind, "fixture");
            Assert.Equal(expected, r.Value); Assert.Equal(unit, r.Unit); Assert.Equal("available", r.Status);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
    [Theory]
    [InlineData("temp", "-273150")]
    [InlineData("temp", "1000001")]
    [InlineData("fan", "-1")]
    [InlineData("power", "9223372036854775808")]
    [InlineData("temp", "42.5")]
    [InlineData("temp", "42,5")]
    [InlineData("temp", "")]
    [InlineData("unknown", "100")]
    public void InvalidValuesAreNull(string kind, string raw) => Assert.Null(HwmonParser.Parse(raw, kind, "fixture").Value);
    [Theory]
    [InlineData("../hwmon0")]
    [InlineData("hwmon0\n")]
    [InlineData("hwmon999999")]
    public async Task DevicePathsAreValidated(string device)
    {
        Assert.False(HwmonParser.ValidDevice(device));
        var files = new HwmonFiles();
        Assert.Throws<IOException>(() => files.Inputs(device));
        await Assert.ThrowsAsync<IOException>(() => files.ReadAsync(device, "name", default));
    }
    [Theory]
    [InlineData("temp0_input")]
    [InlineData("temp1_input\n")]
    [InlineData("../temp1_input")]
    [InlineData("temp10000_input")]
    public void InvalidChannelsRejected(string name) => Assert.False(HwmonParser.TryChannel(name, out _, out _));
    [Fact]
    public async Task FixturePreservesSourcesMetadataAndZero()
    {
        var s = await new HwmonSensorProvider(new Files()).CollectAsync();
        Assert.Equal("available", s.Status); Assert.Equal(5, s.Readings.Count);
        var t = Assert.Single(s.Readings, r => r.Channel == "temp1");
        Assert.Equal(42.5, t.Reading.Value); Assert.Equal("Package id 0", t.Label);
        Assert.Equal("/sys/class/hwmon/hwmon0/temp1_input", t.Reading.Source);
        Assert.Equal("kernel-raw", t.Quality); Assert.True(t.CollectedAt <= s.CollectedAt);
        Assert.Equal(0, Assert.Single(s.Readings, r => r.Channel == "fan1").Reading.Value);
    }
    [Theory]
    [InlineData("temp1_enable", "0")]
    [InlineData("temp1_enable", "invalid")]
    [InlineData("temp1_fault", "1")]
    [InlineData("temp1_fault", "invalid")]
    [InlineData("temp1_type", "4")]
    [InlineData("temp1_type", "99")]
    [InlineData("name", "unknown-driver")]
    [InlineData("temp1_input", "invalid")]
    public async Task UnsafeChannelIsUnavailableWithoutLosingOtherReadings(string attribute, string value)
    {
        var f = new Files(); f.Values[attribute] = value;
        var s = await new HwmonSensorProvider(f).CollectAsync();
        Assert.Equal("partial", s.Status);
        Assert.Null(Assert.Single(s.Readings, r => r.Channel == "temp1").Reading.Value);
        using var json = JsonDocument.Parse(SnapshotReport.Json(s));
        Assert.Contains("\"value\": null", json.RootElement.GetRawText());
    }
    [Theory]
    [InlineData("temp1_enable")]
    [InlineData("temp1_fault")]
    [InlineData("temp1_type")]
    public async Task DeniedSafetyMetadataDoesNotBecomeOptional(string attribute)
    {
        var s = await new HwmonSensorProvider(new Files { DeniedAttribute = attribute }).CollectAsync();
        Assert.Null(Assert.Single(s.Readings, r => r.Channel == "temp1").Reading.Value);
        Assert.Equal("partial", s.Status);
    }
    [Fact]
    public async Task ReadFailureDoesNotProduceZero()
    {
        var s = await new HwmonSensorProvider(new Files { InputFailure = true }).CollectAsync();
        Assert.Equal("unavailable", s.Status);
        Assert.All(s.Readings, r => { Assert.Null(r.Reading.Value); Assert.NotNull(r.Reading.Reason); });
    }
    [Fact]
    public async Task DiscoveryIsFreshAfterHotplug()
    {
        var f = new Files(); var p = new HwmonSensorProvider(f);
        Assert.NotEmpty((await p.CollectAsync()).Readings);
        f.Removed = true; Assert.Empty((await p.CollectAsync()).Readings);
        f.Removed = false; Assert.NotEmpty((await p.CollectAsync()).Readings);
        f.Denied = true; Assert.Equal("unavailable", (await p.CollectAsync()).Status);
    }
    [Fact]
    public async Task OwnTimeoutReturnsUnavailableButCallerCancellationPropagates()
    {
        var p = new HwmonSensorProvider(new Files { Slow = true }, TimeSpan.FromMilliseconds(20));
        var s = await p.CollectAsync(); Assert.Equal("unavailable", s.Status);
        Assert.Contains(s.Limitations, x => x.Contains("Timeout"));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => p.CollectAsync(stop.Token));
    }
    [Fact]
    public async Task DiscoveryAndChannelBoundsAreExplicit()
    {
        var s = await new HwmonSensorProvider(new Files { DeviceCount = 65, ChannelCount = 129 }).CollectAsync();
        Assert.Equal(512, s.Readings.Count); Assert.Equal("partial", s.Status);
        Assert.Contains(s.Limitations, x => x.Contains("64 dispositivos"));
        Assert.Contains(s.Limitations, x => x.Contains("128 canais"));
    }
    private sealed class Provider : ISensorProvider
    {
        public int Calls;
        public readonly TaskCompletionSource<SensorSnapshot> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SensorSnapshot> CollectAsync(CancellationToken ct = default) { Interlocked.Increment(ref Calls); return Completion.Task.WaitAsync(ct); }
    }
    [Fact]
    public async Task MonitorPreventsOverlapAndSuppressesLateNotifications()
    {
        var p = new Provider(); var vm = new SensorMonitorViewModel(p);
        var task = vm.RefreshAsync(); await vm.RefreshAsync();
        Assert.True(vm.IsRefreshing); Assert.False(vm.CanExport);
        int notifications = 0; vm.PropertyChanged += (_, _) => notifications++;
        vm.Dispose(); p.Completion.SetResult(new("1.0", DateTimeOffset.UtcNow, "unavailable", "fixture", [], []));
        await task; Assert.Equal(1, p.Calls); Assert.Null(vm.Snapshot); Assert.Equal(0, notifications);
    }
    [Fact]
    public async Task PollingStopsAndCancelsPendingRead()
    {
        var p = new Provider(); using var vm = new SensorMonitorViewModel(p);
        vm.StartPolling(); vm.StartPolling(); Assert.True(vm.IsPolling);
        await vm.StopPollingAsync(); Assert.False(vm.IsPolling); Assert.False(vm.IsRefreshing);
    }
}
