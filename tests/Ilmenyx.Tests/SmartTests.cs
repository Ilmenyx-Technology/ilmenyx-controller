using System.Text.Json;
using Ilmenyx.Cli;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class SmartTests
{
    [Fact]
    public void CapturedRealNvmeReportIsAcceptedByProductionParser()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "smart-nvme-cachyos.json"));
        var result = SmartctlDiagnostic.Parse("nvme0n1", new(0, json));
        Assert.Equal("available", result.Status); Assert.True(result.ReportedPassed.Value); Assert.Equal(0, result.ToolExitCode);
    }
    [Theory]
    [InlineData("/dev/sda")]
    [InlineData("sda1")]
    [InlineData("../sda")]
    [InlineData("sda;id")]
    [InlineData("nvme0n1p1")]
    [InlineData("nvme0n1\n")]
    [InlineData("loop0")]
    public void InvalidNamesNeverBecomeArguments(string name) => Assert.Throws<ArgumentException>(() => SmartctlQuery.Arguments(name));
    [Fact]
    public void QueryArgumentsAllowOnlyHealthReadAndRequireCliOptIn()
    {
        Assert.Equal(new[] { "--json", "--health", "--nocheck=standby,3,5", "--device=ata", "/dev/sda" }, SmartctlQuery.Arguments("sda"));
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["smart", "--device", "sda"]));
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["smart", "--allow-device-query"]));
        Assert.Equal("nvme0n1", CliArguments.Parse(["smart", "--device", "nvme0n1", "--allow-device-query", "--json"]).Device);
    }
    [Theory]
    [InlineData(0, true)]
    [InlineData(8, false)]
    [InlineData(16, true)]
    public void ReportedStatusIsExplicitAndPrivate(int exit, bool passed)
    {
        var s = SmartctlDiagnostic.Parse("sda", new(exit, $$"""{"smartctl":{"exit_status":{{exit}}},"smart_status":{"passed":{{passed.ToString().ToLowerInvariant()}}},"serial_number":"PRIVATE"}"""));
        Assert.Equal("available", s.Status); Assert.Equal(passed, s.ReportedPassed.Value);
        Assert.DoesNotContain("PRIVATE", SnapshotReport.Json(s)); Assert.Equal(exit, s.ToolExitCode);
    }
    [Theory]
    [InlineData(2, "{}")]
    [InlineData(3, "{}")]
    [InlineData(5, "{}")]
    [InlineData(0, "not-json")]
    [InlineData(0, "{}")]
    [InlineData(0, "[]")]
    [InlineData(0, "{\"smartctl\":{\"exit_status\":8},\"smart_status\":{\"passed\":true}}")]
    [InlineData(0, "{\"smartctl\":{\"exit_status\":0},\"smart_status\":{\"passed\":false}}")]
    [InlineData(8, "{\"smartctl\":{\"exit_status\":8},\"smart_status\":{\"passed\":true}}")]
    [InlineData(0, "{\"smartctl\":{\"exit_status\":0},\"smart_status\":{\"passed\":\"true\"}}")]
    public void ErrorsNeverProduceHealthyStatus(int exit, string json)
    {
        var s = SmartctlDiagnostic.Parse("sda", new(exit, json)); Assert.Equal("unavailable", s.Status); Assert.Null(s.ReportedPassed.Value);
    }
    private sealed class Query : ISmartctlQuery
    {
        public bool Denied;
        public async Task<SmartctlOutput> QueryAsync(string device, CancellationToken ct)
        {
            if (Denied) throw new UnauthorizedAccessException();
            await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException();
        }
    }
    [Fact]
    public async Task PermissionAndCancellationAreNotHealth()
    {
        Assert.Equal("unavailable", (await new SmartctlDiagnostic(new Query { Denied = true }).CollectAsync("sda")).Status);
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SmartctlDiagnostic(new Query()).CollectAsync("sda", ct.Token));
    }
    [Fact]
    public async Task OwnDeadlineProducesUnavailable()
    {
        var s = await new SmartctlDiagnostic(new Query()).CollectAsync("sda");
        Assert.Equal("unavailable", s.Status); Assert.Contains("Timeout", s.Reason!); Assert.Null(s.ReportedPassed.Value);
    }
    [Fact]
    public async Task OutputAndAttributeLimitsAreEnforced()
    {
        await Assert.ThrowsAsync<IOException>(() => SmartctlQuery.ReadBounded(new StringReader(new string('x', 1025)), 1024, default));
        Assert.Equal(1024, (await SmartctlQuery.ReadBounded(new StringReader(new string('x', 1024)), 1024, default)).Length);
        var directory = Path.Combine(Path.GetTempPath(), "ilmenyx-reader-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "attribute");
        try
        {
            await File.WriteAllTextAsync(path, new string('x', 4097));
            await Assert.ThrowsAsync<IOException>(() => DiagnosticSysfsFiles.ReadBoundedAsync(path, default));
            await File.WriteAllTextAsync(path, new string('x', 4096));
            Assert.Equal(4096, (await DiagnosticSysfsFiles.ReadBoundedAsync(path, default)).Length);
        }
        finally { Directory.Delete(directory, true); }
    }
}
