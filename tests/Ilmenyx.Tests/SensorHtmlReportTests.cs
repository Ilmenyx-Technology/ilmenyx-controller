// New Ilmenyx additions: see LICENSING.md. Proposed with Claude Code; reviewed and executed locally.
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Ilmenyx.Cli;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class SensorHtmlReportTests
{
    // Fixtures sinteticas: nenhum valor abaixo vem de hardware, sysfs ou processos reais.
    private static readonly DateTimeOffset FixtureSnapshotAt = new(2026, 3, 14, 9, 26, 53, TimeSpan.Zero);
    private static readonly DateTimeOffset FixtureMeasuredAt = new(2026, 3, 14, 9, 26, 50, TimeSpan.Zero);

    private static readonly string[] HealthWords =
    [
        "saudável", "saudavel", "healthy", "unhealthy", "bom estado", "problema", "falha", "crítico", "critico", "defeito"
    ];

    [Fact]
    public void Render_ProducesStandaloneOfflinePortugueseDocument()
    {
        string html = SensorHtmlReport.Render(FixtureSnapshot());

        Assert.Equal("1.0", SensorHtmlReport.FormatVersion);
        Assert.StartsWith("<!DOCTYPE html", html.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Matches(new Regex("lang\\s*=\\s*[\"']pt-BR[\"']", RegexOptions.IgnoreCase), html);
        Assert.Contains("Content-Security-Policy", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default-src 'none'", html, StringComparison.OrdinalIgnoreCase);

        foreach (string forbidden in new[] { "<script", "<link", "<img", "<iframe", "<object", "<embed", "<audio", "<video", "<source", "@import" })
        {
            Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotMatch(new Regex("\\ssrc\\s*=", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex("url\\(\\s*['\"]?(https?:)?//", RegexOptions.IgnoreCase), html);
    }

    [Fact]
    public void Render_ShowsMeasurementPowerLimitationsAndProductFields()
    {
        string html = SensorHtmlReport.Render(FixtureSnapshot());
        string text = WebUtility.HtmlDecode(html);

        Assert.Contains("2026-03-14T09:26:53", text);
        Assert.Contains("fixture-hwmon0", text);
        Assert.Contains("fixture-k10temp", text);
        Assert.Contains("temp1_input", text);
        Assert.Contains("Temperatura do núcleo (fixture)", text);
        Assert.Contains("°C", text);
        Assert.Contains("fixture-sysfs-source", text);
        Assert.Contains("fixture-direct-quality", text);

        Assert.Contains("FIXTURE-BAT0", text);
        Assert.Contains("FixtureBattery", text);
        Assert.Contains("FixtureDischarging", text);

        Assert.Contains("Limitação de fixture: leitura parcial.", text);
        Assert.Contains(ProductIdentity.Attribution, text);
        Assert.Contains(ProductIdentity.SharingNotice, text);
    }

    [Theory]
    [InlineData("device")]
    [InlineData("chip")]
    [InlineData("channel")]
    [InlineData("label")]
    [InlineData("quality")]
    [InlineData("source")]
    [InlineData("unit")]
    [InlineData("status")]
    [InlineData("reason")]
    [InlineData("schema")]
    [InlineData("limitation")]
    [InlineData("snapshotStatus")]
    [InlineData("snapshotReason")]
    [InlineData("powerDevice")]
    [InlineData("powerType")]
    [InlineData("powerStatus")]
    [InlineData("powerReason")]
    public void Render_EncodesDynamicFieldAsText(string field)
    {
        string payload = $"<script>alert('{field}')</script>\"&<img src=x onerror=1>";

        string html = SensorHtmlReport.Render(PayloadSnapshot(field, payload));

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(payload, WebUtility.HtmlDecode(html));
    }

    [Fact]
    public void Render_FormatsNumbersWithInvariantCulture()
    {
        var snapshot = SnapshotWith(
        [
            Measurement("temp1_input", "Negativo (fixture)", Reading<double?>.Available(-12.5, "°C", "fixture-source")),
            Measurement("in0_input", "Fracionario (fixture)", Reading<double?>.Available(3.25, "V", "fixture-source"))
        ]);

        string text = Text(WithCulture("pt-BR", () => SensorHtmlReport.Render(snapshot)));

        Assert.Contains("-12.5", text);
        Assert.Contains("3.25", text);
        Assert.DoesNotContain("-12,5", text);
        Assert.DoesNotContain("3,25", text);
    }

    [Fact]
    public void Render_ShowsZeroAsAvailableValue()
    {
        string zero = Text(WithCulture("pt-BR", () => SensorHtmlReport.Render(SnapshotWith(
            [Measurement("fan1_input", "Ventoinha (fixture)", Reading<double?>.Available(0, "RPM", "fixture-source"))]))));
        string seven = Text(WithCulture("pt-BR", () => SensorHtmlReport.Render(SnapshotWith(
            [Measurement("fan1_input", "Ventoinha (fixture)", Reading<double?>.Available(7, "RPM", "fixture-source"))]))));

        Assert.True(CountStandalone(zero, "0") > CountStandalone(seven, "0"));
        Assert.Equal(Count(seven, "indispon"), Count(zero, "indispon"));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("error")]
    [InlineData("permission-denied")]
    public void Render_HidesStaleValueWhenReadingIsNotAvailable(string status)
    {
        var snapshot = SnapshotWith(
            [Measurement("temp1_input", "Obsoleto (fixture)", new Reading<double?>(987.654, "°C", "fixture-source", status, "fixture sem leitura"))],
            [new PowerSupplyInfo("FIXTURE-AC",
                new Reading<string>("FixtureStaleType", "", "fixture-power-source", status, "fixture sem tipo"),
                new Reading<string>("FixtureStaleStatus", "", "fixture-power-source", status, "fixture sem estado"),
                new Reading<double?>(555.25, "", "fixture-power-source", status, "fixture sem presença"))]);

        string html = SensorHtmlReport.Render(snapshot);
        string text = WebUtility.HtmlDecode(html);

        foreach (string stale in new[] { "987.654", "987,654", "987.65", "987,65", "FixtureStaleType", "FixtureStaleStatus", "555.25", "555,25" })
        {
            Assert.DoesNotContain(stale, text);
        }
        Assert.Contains("indispon", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fixture sem leitura", text);
    }

    [Fact]
    public void Render_DoesNotInferHealthFromAvailabilityOrCount()
    {
        string allAvailable = Text(SensorHtmlReport.Render(SnapshotWith(
        [
            Measurement("temp1_input", "A (fixture)", Reading<double?>.Available(40, "°C", "fixture-source")),
            Measurement("temp2_input", "B (fixture)", Reading<double?>.Available(41, "°C", "fixture-source"))
        ]))).ToLowerInvariant();
        string allMissing = Text(SensorHtmlReport.Render(SnapshotWith(
        [
            Measurement("temp1_input", "A (fixture)", Reading<double?>.Missing("°C", "fixture-source", "fixture sem leitura")),
            Measurement("temp2_input", "B (fixture)", Reading<double?>.Missing("°C", "fixture-source", "fixture sem leitura"))
        ]))).ToLowerInvariant();
        string empty = Text(SensorHtmlReport.Render(SnapshotWith([]))).ToLowerInvariant();

        foreach (string word in HealthWords)
        {
            Assert.Equal(Count(allMissing, word), Count(allAvailable, word));
            Assert.Equal(Count(empty, word), Count(allAvailable, word));
        }
    }

    [Fact]
    public void Render_PreCanceledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => SensorHtmlReport.Render(FixtureSnapshot(), cts.Token));
    }

    [Fact]
    public async Task ExportAsync_WritesRenderedReport()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "sensores.html");

            await SensorHtmlReport.ExportAsync(FixtureSnapshot(), path);

            string html = await File.ReadAllTextAsync(path);
            Assert.Equal(SensorHtmlReport.Render(FixtureSnapshot()), html);
            Assert.Contains("default-src 'none'", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Temperatura do núcleo (fixture)", WebUtility.HtmlDecode(html));
            Assert.Empty(Directory.GetFiles(directory, ".ilmenyx-*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAsync_DoesNotOverwriteExistingFile()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "sensores.html");
            await File.WriteAllTextAsync(path, "conteudo existente da fixture");

            await Assert.ThrowsAnyAsync<IOException>(() => SensorHtmlReport.ExportAsync(FixtureSnapshot(), path));

            Assert.Equal("conteudo existente da fixture", await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.GetFiles(directory, ".ilmenyx-*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAsync_PreCanceledToken_LeavesNoFiles()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "sensores.html");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SensorHtmlReport.ExportAsync(FixtureSnapshot(), path, cts.Token));

            Assert.False(File.Exists(path));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExportAsync_BlankPath_Throws(string path)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => SensorHtmlReport.ExportAsync(FixtureSnapshot(), path));
    }

    [Fact]
    public void CliArguments_SensorsHtml_SelectsHtml()
    {
        var parsed = CliArguments.Parse(["sensors", "--html"]);

        Assert.True(parsed.Html);
        Assert.False(parsed.Json);
    }

    [Theory]
    [InlineData("--html", "--json")]
    [InlineData("--json", "--html")]
    public void CliArguments_SensorsHtmlAndJson_IsRejected(string first, string second)
    {
        Assert.Throws<ArgumentException>(() => CliArguments.Parse(["sensors", first, second]));
    }

    private static SensorSnapshot FixtureSnapshot() => new(
        "1.0",
        FixtureSnapshotAt,
        "partial",
        null,
        [
            new SensorMeasurement("fixture-hwmon0", "fixture-k10temp", "temp1_input", "Temperatura do núcleo (fixture)",
                FixtureMeasuredAt, "fixture-direct-quality", Reading<double?>.Available(42.5, "°C", "fixture-sysfs-source"))
        ],
        ["Limitação de fixture: leitura parcial."],
        [
            new PowerSupplyInfo("FIXTURE-BAT0",
                Reading<string>.Available("FixtureBattery", "", "fixture-power-source"),
                Reading<string>.Available("FixtureDischarging", "", "fixture-power-source"),
                Reading<double?>.Available(1, "", "fixture-power-source"))
        ]);

    private static SensorSnapshot SnapshotWith(IReadOnlyList<SensorMeasurement> readings, IReadOnlyList<PowerSupplyInfo>? power = null) =>
        new("1.0", FixtureSnapshotAt, "partial", null, readings, [], power);

    private static SensorMeasurement Measurement(string channel, string label, Reading<double?> reading) =>
        new("fixture-hwmon0", "fixture-chip", channel, label, FixtureMeasuredAt, "fixture-direct-quality", reading);

    private static SensorSnapshot PayloadSnapshot(string field, string payload)
    {
        string V(string name, string fallback) => field == name ? payload : fallback;

        Reading<double?> reading = field switch
        {
            "status" => new Reading<double?>(null, "°C", "fixture-source", payload, "fixture sem leitura"),
            "reason" => Reading<double?>.Missing("°C", "fixture-source", payload),
            _ => Reading<double?>.Available(42.5, V("unit", "°C"), V("source", "fixture-source"))
        };
        Reading<string> powerType = field == "powerReason"
            ? Reading<string>.Missing("", "fixture-power-source", payload)
            : Reading<string>.Available(V("powerType", "FixtureMains"), "", "fixture-power-source");

        return new SensorSnapshot(
            V("schema", "1.0"),
            FixtureSnapshotAt,
            V("snapshotStatus", "partial"),
            field == "snapshotReason" ? payload : null,
            [
                new SensorMeasurement(V("device", "fixture-hwmon0"), V("chip", "fixture-chip"), V("channel", "temp1_input"),
                    V("label", "Fixture"), FixtureMeasuredAt, V("quality", "fixture-direct-quality"), reading)
            ],
            [V("limitation", "Limitação de fixture.")],
            [
                new PowerSupplyInfo(V("powerDevice", "FIXTURE-AC"), powerType,
                    Reading<string>.Available(V("powerStatus", "FixtureUnknown"), "", "fixture-power-source"),
                    Reading<double?>.Available(1, "", "fixture-power-source"))
            ]);
    }

    // Texto visivel aproximado: remove head/style, tags e entidades para comparar conteudo sem depender da marcacao.
    private static string Text(string html)
    {
        string body = Regex.Replace(html, "<(head|style)\\b[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        string stripped = Regex.Replace(body, "<[^>]*>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(stripped), "\\s+", " ");
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.OrdinalIgnoreCase); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }
        return count;
    }

    private static int CountStandalone(string text, string number) =>
        Regex.Matches(text, "(?<![\\d.,\\-])" + Regex.Escape(number) + "(?![\\d.,])").Count;

    private static T WithCulture<T>(string name, Func<T> action)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ilmenyx-html-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
