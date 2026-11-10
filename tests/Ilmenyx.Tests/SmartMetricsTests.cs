// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text.Json;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

// Fixtures sintéticos no formato do smartctl; não provam leitura em hardware real (SMART-01 segue NÃO TESTADO).
public sealed class SmartMetricsTests
{
    private const string Serial = "FIXTURE-SERIAL-NOT-REAL";
    private static string Nvme(string extra = "") =>
        "{\"smartctl\":{\"exit_status\":0},\"device\":{\"name\":\"/dev/nvme0n1\"},\"serial_number\":\"" + Serial + "\",\"smart_status\":{\"passed\":true}," +
        "\"temperature\":{\"current\":41},\"power_on_time\":{\"hours\":1234},\"power_cycle_count\":99," +
        "\"nvme_smart_health_information_log\":{\"percentage_used\":7,\"available_spare\":100,\"media_errors\":0,\"num_err_log_entries\":3,\"unsafe_shutdowns\":12,\"critical_warning\":0,\"data_units_read\":2000000,\"data_units_written\":1000000" + extra + "}}";
    private static string Ata(string table) =>
        "{\"smartctl\":{\"exit_status\":0},\"device\":{\"name\":\"/dev/sda\"},\"smart_status\":{\"passed\":true},\"temperature\":{\"current\":35},\"power_on_time\":{\"hours\":5000},\"ata_smart_attributes\":{\"table\":" + table + "}}";
    private static double Value(SmartDiagnostic d, string name) => d.Metrics!.Single(m => m.Name == name).Value;

    [Fact]
    public void NvmeLogProducesRawLabelledMetricsAndConvertsDataUnits()
    {
        var report = SmartctlReportImport.Parse("nvme0n1", Nvme());
        var d = report.Diagnostic;
        Assert.Equal("1.1", d.SchemaVersion);
        Assert.Equal(41, Value(d, "Temperatura")); Assert.Equal(1234, Value(d, "Horas ligado")); Assert.Equal(99, Value(d, "Ciclos liga/desliga"));
        Assert.Equal(7, Value(d, "Percentual usado (NVMe)")); Assert.Equal(100, Value(d, "Reserva disponível (NVMe)"));
        Assert.Equal(12, Value(d, "Desligamentos inseguros")); Assert.Equal(3, Value(d, "Entradas no log de erros"));
        Assert.Equal(2000000 * 512000.0, Value(d, "Total lido")); Assert.Equal(1000000 * 512000.0, Value(d, "Total gravado"));
        Assert.All(d.Metrics!, m => Assert.StartsWith("smartctl:", m.Source));
        Assert.DoesNotContain(Serial, JsonSerializer.Serialize(report)); // identifiers never leak
    }

    [Fact]
    public void AtaTableKeepsOnlyKnownCountersAndFirstOccurrence()
    {
        var d = SmartctlReportImport.Parse("sda", Ata("[{\"id\":5,\"raw\":{\"value\":8}},{\"id\":5,\"raw\":{\"value\":99}},{\"id\":197,\"raw\":{\"value\":1}},{\"id\":198,\"raw\":{\"value\":2}},{\"id\":9,\"raw\":{\"value\":5000}},{\"id\":177,\"raw\":{\"value\":3}}]")).Diagnostic;
        Assert.Equal(8, Value(d, "Setores realocados")); Assert.Equal(1, Value(d, "Setores pendentes")); Assert.Equal(2, Value(d, "Setores incorrigíveis (offline)"));
        Assert.Equal(3, d.Metrics!.Count(m => m.Unit == "setores"));
        Assert.Equal(35, Value(d, "Temperatura")); Assert.Equal(5000, Value(d, "Horas ligado"));
    }

    [Theory]
    [InlineData("\"temperature\":{\"current\":999}", "Temperatura")]
    [InlineData("\"temperature\":{\"current\":-99}", "Temperatura")]
    [InlineData("\"power_on_time\":{\"hours\":-5}", "Horas ligado")]
    [InlineData("\"power_cycle_count\":\"many\"", "Ciclos liga/desliga")]
    public void ImplausibleOrWrongTypedValuesAreDropped(string field, string name)
    {
        string json = "{\"smartctl\":{\"exit_status\":0},\"device\":{\"name\":\"/dev/sda\"},\"smart_status\":{\"passed\":true}," + field + "}";
        Assert.DoesNotContain(SmartctlReportImport.Parse("sda", json).Diagnostic.Metrics!, m => m.Name == name);
    }

    [Fact]
    public void ReportWithoutExtraFieldsKeepsEmptyMetricsAndHealthOnlyBehaviour()
    {
        string json = "{\"smartctl\":{\"exit_status\":0},\"device\":{\"name\":\"/dev/nvme0n1\"},\"smart_status\":{\"passed\":true}}";
        var d = SmartctlReportImport.Parse("nvme0n1", json).Diagnostic;
        Assert.Empty(d.Metrics!); Assert.Equal("available", d.Status); Assert.True(d.ReportedPassed.Value);
        Assert.Empty(SmartMetricText.Lines(d));
    }

    [Fact]
    public void FailedOrContradictoryReportsCarryNoMetrics()
    {
        var failed = SmartctlDiagnostic.Parse("sda", new SmartctlOutput(1, Ata("[]")));
        Assert.Equal("unavailable", failed.Status); Assert.Null(failed.Metrics);
    }

    [Fact]
    public void TextAndHtmlShowMetricsWithoutVerdictOrEscapingBreaks()
    {
        var report = SmartctlReportImport.Parse("nvme0n1", Nvme());
        var lines = SmartMetricText.Lines(report.Diagnostic);
        Assert.Contains("Temperatura: 41 °C", lines); Assert.Contains("Percentual usado (NVMe): 7%", lines); Assert.Contains("Total lido: 1.02 TB", lines);
        string html = SmartImportHtmlReport.Render(report);
        string decoded = System.Net.WebUtility.HtmlDecode(html);
        Assert.Contains("Métrica bruta: Temperatura", decoded); Assert.Contains("41 °C", decoded);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        foreach (var word in new[] { "SAUDÁVEL", "ATENÇÃO", "CRÍTICO", "saudável" }) Assert.DoesNotContain(word, string.Join(" ", lines) + decoded);
    }
}
