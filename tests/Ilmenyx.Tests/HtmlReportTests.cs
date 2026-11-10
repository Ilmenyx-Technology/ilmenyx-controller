// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Ilmenyx.Cli;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public class HtmlReportTests
{
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-10-21T00:00:00Z", CultureInfo.InvariantCulture);
    private static HardwareSnapshot Snapshot(string text = "Fixture CPU") => new("1.2", "test", Timestamp, ".NET fixture",
        Reading<string>.Available("Fixture Linux", "", "/etc/os-release"), Reading<string>.Available("fixture-kernel", "", "/proc/sys/kernel/osrelease"),
        new(Reading<string>.Available(text, "", "/proc/cpuinfo"), Reading<int?>.Available(8, "logical processors", "/proc/cpuinfo")),
        new(Reading<long?>.Available(1024, "B", "/proc/meminfo"), Reading<long?>.Available(0, "B", "/proc/meminfo")),
        Reading<IReadOnlyList<DiskInfo>>.Available([new("fixture-disk", Reading<long?>.Available(2048, "B", "/sys/block/fixture/size"))], "devices", "/sys/block"),
        [new("gpu", "partial", text)], [text],
        new("1.1", Timestamp, "partial", text,
            [new("hwmon0", "fixture-chip", "temp1", text, Timestamp, "kernel-raw", Reading<double?>.Missing("°C", "/sys/class/hwmon/hwmon0/temp1_input", text)),
             new("PowerSupply/BAT1", "Battery", "capacity", "Carga", Timestamp, "kernel-raw", Reading<double?>.Available(12.5, "%", "/sys/class/power_supply/BAT1/capacity"))], [text],
            [new("BAT1", Reading<string>.Available("Battery", "", "/sys/class/power_supply/BAT1/type"), Reading<string>.Missing("", "/sys/class/power_supply/BAT1/status", text), Reading<double?>.Available(1, "boolean", "/sys/class/power_supply/BAT1/present"))]),
        new("1.0", Timestamp, "unavailable", text, [], [text]));
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("</td><img src=https://example.invalid onerror=alert(1)>")]
    [InlineData("<svg onload=alert(1)>")]
    [InlineData("\" autofocus onfocus=alert(1) x=\"")]
    [InlineData("&lt;script&gt;alert(1)&lt;/script&gt;")]
    public void UntrustedValuesRemainText(string payload)
    {
        var html = HardwareHtmlReport.Render(Snapshot(payload));
        Assert.DoesNotContain(payload, html);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(payload), html);
        Assert.DoesNotContain("<script", html); Assert.DoesNotContain("<img", html); Assert.DoesNotContain("<svg", html);
    }
    [Fact]
    public void SourcesUnitsMetadataAndOptionalGpuFieldsCannotInjectMarkup()
    {
        const string payload = "<img src='https://example.invalid' onerror='alert(1)'>";
        var id = Reading<string>.Available(payload, payload, payload);
        var bytes = Reading<long?>.Available(1, payload, payload);
        var number = Reading<double?>.Available(1, payload, payload);
        var s = Snapshot() with
        {
            SchemaVersion = payload,
            ApplicationVersion = payload,
            Runtime = payload,
            Disks = Reading<IReadOnlyList<DiskInfo>>.Available([new(payload, bytes)], "devices", payload),
            Sensors = new(payload, Timestamp, payload, payload,
                [new(payload, payload, payload, payload, Timestamp, payload, number)], [payload],
                [new(payload, id, id, number)]),
            Gpus = new(payload, Timestamp, payload, payload, [new(payload, id, id, id, number, bytes, bytes)], [payload])
        };
        var html = HardwareHtmlReport.Render(s);
        Assert.DoesNotContain("<img", html); Assert.DoesNotContain("<script", html);
        Assert.True(html.Split(System.Net.WebUtility.HtmlEncode(payload), StringSplitOptions.None).Length > 20);
    }
    [Fact]
    public void ProvenanceAvailabilityAndUnitsSurviveExport()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var html = HardwareHtmlReport.Render(Snapshot());
            Assert.Contains("/sys/class/hwmon/hwmon0/temp1_input", html); Assert.Contains("kernel-raw", html);
            Assert.Contains("2026-10-21T00:00:00.0000000+00:00", html); Assert.Contains("<td>12.5</td>", html);
            Assert.Contains("Indisponível", System.Net.WebUtility.HtmlDecode(html)); Assert.Contains("<td>0</td>", html);
            Assert.Contains(System.Net.WebUtility.HtmlEncode(ValidationNotices.AmdNvidia), html);
            Assert.Contains("default-src 'none'", html); Assert.Contains("<meta charset=\"utf-8\">", html);
            Assert.DoesNotContain("<script", html); Assert.DoesNotContain("<link", html); Assert.DoesNotContain("<iframe", html);
            Assert.DoesNotContain("<form", html);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
    [Fact]
    public void OptionalBackendsMissingAreVisibleAndOutputIsDeterministic()
    {
        var s = Snapshot() with { Sensors = null, Gpus = null, Disks = Reading<IReadOnlyList<DiskInfo>>.Missing("devices", "/sys/block", "Fixture denied") };
        var html = HardwareHtmlReport.Render(s);
        Assert.Equal(html, HardwareHtmlReport.Render(s)); Assert.Contains("leitor não configurado", html); Assert.Contains("Fixture denied", html);
    }
    [Fact]
    public async Task CancelledRenderAndExportDoNotCreateDestination()
    {
        using var ct = new CancellationTokenSource(); ct.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => HardwareHtmlReport.Render(Snapshot(), ct.Token));
        var path = Path.Combine(Path.GetTempPath(), "ilmenyx-cancel-html-" + Guid.NewGuid() + ".html");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HardwareHtmlReport.ExportAsync(Snapshot(), path, ct.Token));
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task AtomicExportPreservesDestinationsAndCleansAllTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ilmenyx-html-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "report.html");
        try
        {
            await HardwareHtmlReport.ExportAsync(Snapshot(), path);
            Assert.Equal(HardwareHtmlReport.Render(Snapshot()), await File.ReadAllTextAsync(path));
            var bytes = await File.ReadAllBytesAsync(path);
            await Assert.ThrowsAsync<IOException>(() => HardwareHtmlReport.ExportAsync(Snapshot("different"), path));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            using var ct = new CancellationTokenSource();
            var cancelled = Path.Combine(directory, "cancelled.html");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReportFile.WriteAsync(cancelled, async (stream, token) =>
            { await stream.WriteAsync(new byte[] { 1 }, token); ct.Cancel(); }, ct.Token));
            Assert.False(File.Exists(cancelled)); Assert.Single(Directory.GetFiles(directory));
            var failed = Path.Combine(directory, "failed.html");
            await Assert.ThrowsAsync<IOException>(() => ReportFile.WriteAsync(failed, (_, _) => throw new IOException("Fixture write failure"), default));
            Assert.False(File.Exists(failed)); Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task SymlinkDestinationIsPreserved()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine(Path.GetTempPath(), "ilmenyx-link-html-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "target"); var link = Path.Combine(directory, "report.html");
        try
        {
            await File.WriteAllTextAsync(target, "preserve"); File.CreateSymbolicLink(link, target);
            await Assert.ThrowsAsync<IOException>(() => HardwareHtmlReport.ExportAsync(Snapshot(), link));
            Assert.Equal("preserve", await File.ReadAllTextAsync(target)); Assert.NotNull(new FileInfo(link).LinkTarget);
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void HtmlCliIsExplicitAndConflictingFormatsAreRejected()
    {
        Assert.True(CliArguments.Parse(["info", "--html", "--output", "report.html"]).Html);
        Assert.False(CliArguments.Parse(["info", "--json"]).Html);
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["info", "--html", "--json"]));
        Assert.True(CliArguments.Parse(["sensors", "--html"]).Html);
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["sensors", "--html", "--json"]));
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["capabilities", "--html"]));
    }

    [Fact]
    public void GpuValidationNoticeOnlyForTheVendorsPresent()
    {
        Assert.Null(ValidationNotices.For(["0x8086"])); // Intel only: validated on the development machine
        Assert.Equal(ValidationNotices.Nvidia, ValidationNotices.For(["0x8086", "0x10DE"]));
        Assert.Equal(ValidationNotices.Amd, ValidationNotices.For(["0x1002\n"]));
        Assert.Equal(ValidationNotices.AmdNvidia, ValidationNotices.For(["0x1002", "0x10de"]));
        Assert.Equal(ValidationNotices.AmdNvidia, ValidationNotices.For([])); // nothing known: keep the whole notice
    }
}
