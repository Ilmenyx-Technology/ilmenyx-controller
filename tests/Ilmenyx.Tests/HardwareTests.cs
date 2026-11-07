// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using System.Globalization;
using System.Text.Json;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;
namespace Ilmenyx.Tests;

public class HardwareTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    [Fact]
    public void ParsesCpuWithNonContiguousIds()
    {
        var cpu = LinuxParsers.Cpu(Fixture("cpuinfo")); Assert.Equal("Fixture CPU", cpu.Name.Value); Assert.Equal(2, cpu.LogicalProcessors.Value);
    }
    [Theory]
    [InlineData("")]
    [InlineData("processor: nope")]
    [InlineData("processor: 0\nprocessor: 0")]
    [InlineData("processor: 0\nprocessor: 00")]
    public void RejectsInvalidProcessors(string input) { Assert.Equal("unavailable", LinuxParsers.Cpu(input).LogicalProcessors.Status); }
    [Fact]
    public void MemoryIsBinaryKilobytesRegardlessOfLocale()
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo("pt-BR"); var m = LinuxParsers.Memory(Fixture("meminfo")); Assert.Equal(33554432, m.Total.Value); Assert.Equal(4194304, m.Available.Value); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Theory]
    [InlineData("MemTotal: -1 kB")]
    [InlineData("MemTotal: 1 MB")]
    [InlineData("MemTotal: 9223372036854775807 kB")]
    [InlineData("MemTotal: 1 kB\nMemTotal: 2 kB")]
    [InlineData("MemTotal: 0 kB")]
    public void RejectsInvalidMemory(string text) { Assert.Null(LinuxParsers.Memory(text).Total.Value); }
    [Fact] public void MissingAvailableIsNotZero() { Assert.Null(LinuxParsers.Memory("MemTotal: 10 kB").Available.Value); }
    [Fact] public void ImpossibleAvailableIsMissing() { Assert.Null(LinuxParsers.Memory("MemTotal: 10 kB\nMemAvailable: 11 kB").Available.Value); }
    [Fact] public void ZeroAvailableIsReal() { Assert.Equal(0, LinuxParsers.Memory("MemTotal: 10 kB\nMemAvailable: 0 kB").Available.Value); }
    [Fact] public void CapacityUses512ByteSectors() { Assert.Equal(1073741824, LinuxParsers.Capacity(Fixture("size"), "fixture").Value); }
    [Theory]
    [InlineData("-1")]
    [InlineData("1.2")]
    [InlineData("9223372036854775807")]
    [InlineData("")]
    public void InvalidCapacityDoesNotOverflow(string text) { Assert.Null(LinuxParsers.Capacity(text, "fixture").Value); }
    [Fact] public void OsReleaseIsDataNotShell() { Assert.Equal("$(touch file)", LinuxParsers.OsName("PRETTY_NAME=\"$(touch file)\"")); Assert.Null(LinuxParsers.OsName("NAME=Other")); }
    [Fact]
    public async Task PermissionFailureProducesUnavailableAndNulls()
    {
        var snapshot = await new LinuxHardwareInfoProvider(new DeniedFiles()).CollectAsync();
        Assert.Null(snapshot.Memory.Total.Value); Assert.Contains("Permissão", snapshot.Memory.Total.Reason!);
        Assert.Equal("unavailable", snapshot.Disks.Status); Assert.All(snapshot.Capabilities.Where(c => c.Name == "sensors"), c => Assert.Equal("unavailable", c.Status));
    }
    [Fact]
    public async Task SnapshotExportsSchemaAndRefusesOverwrite()
    {
        var snapshot = await new LinuxHardwareInfoProvider(new FixtureFiles()).CollectAsync();
        using var doc = JsonDocument.Parse(SnapshotReport.Json(snapshot));
        Assert.Equal("1.3", doc.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("cpu").GetProperty("logicalProcessors").GetProperty("value").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("hostname", out _));
        var path = Path.Combine(Path.GetTempPath(), "ilmenyx-test-" + Guid.NewGuid() + ".json");
        try { await SnapshotReport.ExportAsync(snapshot, path); await Assert.ThrowsAsync<IOException>(() => SnapshotReport.ExportAsync(snapshot, path)); Assert.Equal(SnapshotReport.Json(snapshot), await File.ReadAllTextAsync(path)); }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task CancellationPropagates()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LinuxHardwareInfoProvider(new FixtureFiles()).CollectAsync(cts.Token));
    }
    [Fact]
    public async Task ReaderBoundsInput()
    {
        var path = Path.Combine(Path.GetTempPath(), "ilmenyx-limit-" + Guid.NewGuid());
        try { await File.WriteAllTextAsync(path, new string('x', LinuxFiles.MaxCharacters + 1)); await Assert.ThrowsAsync<IOException>(() => new LinuxFiles().ReadAsync(path, default)); }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task MissingFilesAndEmptySysAreUnavailable()
    {
        var snapshot = await new LinuxHardwareInfoProvider(new EmptyFiles()).CollectAsync();
        Assert.Equal("unavailable", snapshot.OperatingSystem.Status);
        Assert.Null(snapshot.Memory.Total.Value);
        Assert.Null(snapshot.Cpu.LogicalProcessors.Value);
        Assert.Equal("unavailable", snapshot.Disks.Status);
    }
    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("bad/name")]
    public async Task UntrustedDeviceNamesAreRejected(string name)
    {
        var snapshot = await new LinuxHardwareInfoProvider(new OneDeviceFiles(name, "4096")).CollectAsync();
        Assert.Equal("unavailable", snapshot.Disks.Status);
    }
    [Fact]
    public async Task MalformedDiskDoesNotDiscardOtherHardware()
    {
        var snapshot = await new LinuxHardwareInfoProvider(new OneDeviceFiles("sda", "overflow!")).CollectAsync();
        Assert.Equal("available", snapshot.Disks.Status);
        Assert.Null(Assert.Single(snapshot.Disks.Value!).Capacity.Value);
        Assert.Equal("Fixture CPU", snapshot.Cpu.Name.Value);
    }
    [Fact]
    public async Task MissingDiskSizeHasExplicitReason()
    {
        var snapshot = await new LinuxHardwareInfoProvider(new OneDeviceFiles("sda", null)).CollectAsync();
        var capacity = Assert.Single(snapshot.Disks.Value!).Capacity;
        Assert.Equal("unavailable", capacity.Status); Assert.NotNull(capacity.Reason);
    }
    [Fact]
    public async Task ExportCancellationLeavesNoPartialFile()
    {
        var snapshot = await new LinuxHardwareInfoProvider(new FixtureFiles()).CollectAsync();
        var destination = Path.Combine(Path.GetTempPath(), "ilmenyx-cancel-" + Guid.NewGuid() + ".json");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SnapshotReport.ExportAsync(snapshot, destination, cts.Token));
        Assert.False(File.Exists(destination));
    }
    [Fact]
    public async Task TimeoutInterruptsSourceRead()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LinuxHardwareInfoProvider(new SlowFiles()).CollectAsync());
    }
    [Fact]
    public void MalformedOsNameIsUnavailable()
    {
        Assert.Null(LinuxParsers.OsName("PRETTY_NAME=\"unfinished"));
        Assert.Null(LinuxParsers.OsName("PRETTY_NAME=one\nPRETTY_NAME=two"));
    }
    private sealed class SlowFiles : ILinuxFiles
    {
        public async Task<string> ReadAsync(string path, CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); return ""; }
        public IReadOnlyList<string> BlockDevices() => Array.Empty<string>();
    }
    private sealed class EmptyFiles : ILinuxFiles
    {
        public Task<string> ReadAsync(string path, CancellationToken ct) => throw new FileNotFoundException();
        public IReadOnlyList<string> BlockDevices() => Array.Empty<string>();
    }
    private sealed class OneDeviceFiles(string name, string? size) : ILinuxFiles
    {
        public Task<string> ReadAsync(string path, CancellationToken ct) => path.StartsWith("/sys/")
            ? size is null ? throw new FileNotFoundException() : Task.FromResult(size)
            : new FixtureFiles().ReadAsync(path, ct);
        public IReadOnlyList<string> BlockDevices() => new[] { name };
    }
    private sealed class DeniedFiles : ILinuxFiles
    {
        public Task<string> ReadAsync(string path, CancellationToken ct) => throw new UnauthorizedAccessException();
        public IReadOnlyList<string> BlockDevices() => throw new UnauthorizedAccessException();
    }
    private sealed class FixtureFiles : ILinuxFiles
    {
        public Task<string> ReadAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(path switch { "/proc/cpuinfo" => Fixture("cpuinfo"), "/proc/meminfo" => Fixture("meminfo"), "/etc/os-release" => "PRETTY_NAME=Fixture Linux", "/proc/sys/kernel/osrelease" => "test-kernel", _ => Fixture("size") });
        }
        public IReadOnlyList<string> BlockDevices() => new[] { "nvme0n1" };
    }
}
