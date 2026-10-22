// New Ilmenyx additions: see LICENSING.md. Proposal from Claude Code, reviewed locally.
using System.Text.Json;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

// Fixtures abaixo são sintéticos: nenhum serial, modelo, mensagem ou horário de hardware real.
// CLI covered separately in SmartImportCliTests.
public sealed class SmartImportTests
{
    private const string FakeSerial = "FIXTURE-SERIAL-NOT-REAL";
    private const string FakeModel = "FIXTURE-MODEL-NOT-REAL";
    private const string FakeMessage = "FIXTURE-MESSAGE-NOT-REAL";
    private const string FakeTime = "FIXTURE-TIME-NOT-REAL";
    private const string Source = "fixture-source";
    private static readonly DateTimeOffset Fixed = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static string Fixture(string device = "sda", int exit = 0, bool? passed = true) =>
        "{\"json_format_version\":[1,0],\"smartctl\":{\"version\":[7,4],\"exit_status\":" + exit +
        ",\"messages\":[{\"string\":\"" + FakeMessage + "\",\"severity\":\"error\"}]}," +
        "\"device\":{\"name\":\"/dev/" + device + "\",\"type\":\"fixture\"},\"model_name\":\"" + FakeModel +
        "\",\"serial_number\":\"" + FakeSerial + "\",\"local_time\":{\"time_t\":0,\"asctime\":\"" + FakeTime + "\"}" +
        (passed is { } p ? ",\"smart_status\":{\"passed\":" + (p ? "true" : "false") + "}" : "") + "}";

    private static SmartImportReport Manual(Reading<bool?> passed, string text = "fixture") =>
        new("1.0", "smart-import", Fixed, new SmartDiagnostic("1.0", Fixed, "sda", passed.Status, passed.Reason, 0, passed, [text]), ["limitation " + text]);

    [Theory]
    [InlineData("sda", 0, true)]
    [InlineData("nvme0n1", 8, false)]
    [InlineData("sdzzzz", 0, true)]
    public void ConsistentResponseIsAvailableWithoutTouchingDisk(string device, int exit, bool expected)
    {
        var report = SmartctlReportImport.Parse(device, Fixture(device, exit, expected));
        Assert.Equal("1.0", report.SchemaVersion);
        Assert.Equal("smart-import", report.ReportKind);
        Assert.Equal(device, report.Diagnostic.Device);
        Assert.Equal("available", report.Diagnostic.Status);
        Assert.Equal(exit, report.Diagnostic.ToolExitCode);
        Assert.Equal("available", report.Diagnostic.ReportedPassed.Status);
        Assert.Equal(expected, report.Diagnostic.ReportedPassed.Value);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(8, true)]
    [InlineData(4, true)]
    [InlineData(1, null)]
    [InlineData(0, null)]
    public void InconsistentOrErrorResponseBecomesUnavailable(int exit, bool? passed)
    {
        var report = SmartctlReportImport.Parse("sda", Fixture(exit: exit, passed: passed));
        Assert.Equal("unavailable", report.Diagnostic.Status);
        Assert.Equal(exit, report.Diagnostic.ToolExitCode);
        Assert.Equal("unavailable", report.Diagnostic.ReportedPassed.Status);
        Assert.Null(report.Diagnostic.ReportedPassed.Value);
    }

    [Fact]
    public async Task TimestampsReflectAnalysisTimeNotOriginalCollection()
    {
        var before = DateTimeOffset.UtcNow;
        var parsed = SmartctlReportImport.Parse("sda", Fixture());
        var read = await SmartctlReportImport.ReadAsync("sda", new StringReader(Fixture(exit: 8, passed: false)));
        var after = DateTimeOffset.UtcNow;
        foreach (var report in new[] { parsed, read })
        {
            Assert.InRange(report.ImportedAt, before, after);
            Assert.InRange(report.Diagnostic.CollectedAt, before, after);
        }
        Assert.False(read.Diagnostic.ReportedPassed.Value);
    }

    [Fact]
    public void LimitationsDeclareHistoricalUnverifiedImport()
    {
        var text = string.Join(" ", SmartctlReportImport.Parse("sda", Fixture()).Limitations).ToLowerInvariant();
        Assert.Contains("históric", text);
        Assert.Contains("consulta", text);
        Assert.Contains("origem", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sda1")]
    [InlineData("SDA")]
    [InlineData("hda")]
    [InlineData("sdaaaaa")]
    [InlineData("nvme0")]
    [InlineData("/dev/sda")]
    [InlineData("../sda")]
    [InlineData("sda\n")]
    public async Task InvalidDeviceIsRejected(string device)
    {
        Assert.ThrowsAny<ArgumentException>(() => SmartctlReportImport.Parse(device, Fixture()));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => SmartctlReportImport.ReadAsync(device, new StringReader(Fixture())));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"sda\"")]
    [InlineData("{}")]
    [InlineData("""{"smartctl":""")]
    [InlineData("""{"smartctl":{"exit_status":0},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"device":{"name":"/dev/sdb"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"device":{"name":"sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"device":{"name":"/dev/SDA"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"device":"/dev/sda","smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"device":{"name":null},"smart_status":{"passed":true}}""")]
    [InlineData("""{"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{},"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":"0"},"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":256},"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":-1},"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0.5},"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":true},"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"smartctl":{"exit_status":0},"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"device":{"name":"/dev/sda"},"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"device":{"name":"/dev/sda"},"smart_status":{"passed":true},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0,"exit_status":8},"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"device":{"name":"/dev/sdb","name":"/dev/sda"},"smart_status":{"passed":true}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"device":{"name":"/dev/sda"},"smart_status":{"passed":true,"passed":false}}""")]
    [InlineData("""{"smartctl":{"exit_status":0},"device":{"name":"/dev/sda"},"smart_status":{"passed":true}}{}""")]
    public void InvalidJsonOrMetadataIsRejected(string json) =>
        Assert.ThrowsAny<ArgumentException>(() => SmartctlReportImport.Parse("sda", json));

    [Fact]
    public void JsonDeeperThanLimitIsRejected()
    {
        var json = Fixture()[..^1] + ",\"fixture_nesting\":" + new string('[', 40) + new string(']', 40) + "}";
        Assert.ThrowsAny<ArgumentException>(() => SmartctlReportImport.Parse("sda", json));
    }

    [Fact]
    public async Task InputAtLimitIsAcceptedAndAboveLimitThrowsIOException()
    {
        var atLimit = Fixture().PadRight(65_536);
        Assert.Equal("available", SmartctlReportImport.Parse("sda", atLimit).Diagnostic.Status);
        Assert.Equal("available", (await SmartctlReportImport.ReadAsync("sda", new StringReader(atLimit))).Diagnostic.Status);
        Assert.ThrowsAny<IOException>(() => SmartctlReportImport.Parse("sda", atLimit + " "));
        await Assert.ThrowsAnyAsync<IOException>(() => SmartctlReportImport.ReadAsync("sda", new StringReader(atLimit + " ")));
    }

    [Fact]
    public async Task PreCancelledReadThrowsOperationCanceled() =>
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SmartctlReportImport.ReadAsync("sda", new StringReader(Fixture()), new CancellationToken(true)));

    [Fact]
    public async Task ExternalCancellationDuringReadPropagates()
    {
        using var source = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SmartctlReportImport.ReadAsync("sda", new CancellingReader(source), source.Token));
        Assert.True(source.IsCancellationRequested);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, null)]
    public void RawIdentifiersAndMessagesAreNotExported(int exit, bool? passed)
    {
        var report = SmartctlReportImport.Parse("sda", Fixture(exit: exit, passed: passed));
        foreach (var text in new[] { SnapshotReport.Json(report), SmartImportHtmlReport.Render(report) })
            foreach (var raw in new[] { FakeSerial, FakeModel, FakeMessage, FakeTime, "serial_number", "model_name", "local_time" })
                Assert.DoesNotContain(raw, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonUsesImportContract()
    {
        using var doc = JsonDocument.Parse(SnapshotReport.Json(SmartctlReportImport.Parse("sda", Fixture())));
        var root = doc.RootElement;
        Assert.Equal("smart-import", root.GetProperty("reportKind").GetString());
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("sda", root.GetProperty("diagnostic").GetProperty("device").GetString());
        Assert.True(root.GetProperty("diagnostic").GetProperty("reportedPassed").GetProperty("value").GetBoolean());
        Assert.NotEqual(0, root.GetProperty("limitations").GetArrayLength());
    }

    [Fact]
    public void HtmlIsPassiveAndCredited()
    {
        var report = SmartctlReportImport.Parse("sda", Fixture());
        var html = SmartImportHtmlReport.Render(report);
        Assert.Contains("Content-Security-Policy", html);
        Assert.Contains("default-src 'none'", html);
        Assert.Contains("Ilmenyx", html);
        Assert.Contains("Bismuth Technology", html);
        Assert.Contains(ProductIdentity.Attribution, System.Net.WebUtility.HtmlDecode(html));
        Assert.Contains(ProductIdentity.SharingNotice, System.Net.WebUtility.HtmlDecode(html));
        Assert.Contains("histórico", System.Net.WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(html, SmartImportHtmlReport.Render(report));
    }

    [Fact]
    public void HtmlShowsPassedOnlyWhenAvailable()
    {
        Assert.NotEqual(SmartImportHtmlReport.Render(Manual(Reading<bool?>.Available(true, "boolean", Source))),
            SmartImportHtmlReport.Render(Manual(Reading<bool?>.Available(false, "boolean", Source))));
        var stale = new bool?[] { true, false, null }
            .Select(v => SmartImportHtmlReport.Render(Manual(new Reading<bool?>(v, "boolean", Source, "unavailable", "fixture reason"))))
            .Distinct().ToList();
        Assert.Single(stale);
    }

    [Fact]
    public void HtmlEscapesEveryField()
    {
        const string payload = "\"'></title><script>alert(1)</script><img src=x onerror=alert(1)>&amp;";
        var passed = new Reading<bool?>(true, payload, payload, payload, payload);
        var report = new SmartImportReport(payload, payload, Fixed,
            new SmartDiagnostic(payload, Fixed, payload, payload, payload, 0, passed, [payload]), [payload]);
        var html = SmartImportHtmlReport.Render(report);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(payload, html);
        Assert.Contains(payload, System.Net.WebUtility.HtmlDecode(html));
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExportWritesNewFileAndNeverOverwrites(bool html)
    {
        var dir = Directory.CreateTempSubdirectory("ilmenyx-smart-import-");
        try
        {
            var report = Manual(Reading<bool?>.Available(true, "boolean", Source));
            Func<string, Task> export = path => html ? SmartImportHtmlReport.ExportAsync(report, path) : SnapshotReport.ExportAsync(report, path);
            var path = Path.Combine(dir.FullName, "smart-import.out");
            await export(path);
            Assert.Equal(html ? SmartImportHtmlReport.Render(report) : SnapshotReport.Json(report), await File.ReadAllTextAsync(path));

            var existing = Path.Combine(dir.FullName, "existing.out");
            await File.WriteAllTextAsync(existing, "keep");
            await Assert.ThrowsAnyAsync<IOException>(() => export(existing));
            Assert.Equal("keep", await File.ReadAllTextAsync(existing));
        }
        finally { dir.Delete(recursive: true); }
    }

    private sealed class CancellingReader(CancellationTokenSource source) : TextReader
    {
        public override int Read() => Cancel();
        public override int Read(char[] buffer, int index, int count) => Cancel();
        public override int Read(Span<char> buffer) => Cancel();
        public override Task<int> ReadAsync(char[] buffer, int index, int count) => Task.FromResult(Cancel());
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken ct = default) => ValueTask.FromResult(Cancel());
        public override Task<string> ReadToEndAsync(CancellationToken ct) => Task.FromResult(Cancel().ToString());
        private int Cancel() { source.Cancel(); throw new OperationCanceledException(source.Token); }
    }
}
