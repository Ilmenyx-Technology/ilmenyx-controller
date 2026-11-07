// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// Existing components retain their terms.
using System.Globalization;
using System.Net;
using System.Text;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core;

/// <summary>Passive HTML from an existing sensor snapshot. No provider, polling or network.</summary>
public static class SensorHtmlReport
{
    public const string FormatVersion = "1.0";
    public static Task ExportAsync(SensorSnapshot snapshot, string path, CancellationToken ct = default) =>
        ReportFile.WriteAsync(path, async (output, token) =>
        {
            var html = Render(snapshot, token);
            await using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, leaveOpen: true);
            await writer.WriteAsync(html.AsMemory(), token);
            await writer.FlushAsync(token);
        }, ct);

    public static string Render(SensorSnapshot snapshot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var html = new StringBuilder(8192);
        html.Append("""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
            <title>Ilmenyx — Relatório de sensores</title>
            <style>
            *{box-sizing:border-box}body{margin:0;font:15px system-ui,sans-serif;line-height:1.5}
            main{max-width:1400px;margin:auto;padding:24px}section{margin:24px 0}p,td,dd{overflow-wrap:anywhere}
            .table-wrap{overflow-x:auto}table{width:100%;border-collapse:collapse;font-size:13px}
            th,td{padding:8px;text-align:left;vertical-align:top;border:1px solid #888;min-width:90px}
            dl{display:grid;grid-template-columns:minmax(120px,1fr) 3fr;gap:8px}dd{margin:0}
            footer{white-space:pre-wrap}@media(max-width:600px){main{padding:12px}dl{display:block}dd{margin-bottom:8px}}
            @media print{main{padding:0}.table-wrap{overflow:visible}th,td{padding:4px;min-width:0}}
            </style></head><body><main><header><h1>Ilmenyx</h1><p>by Bismuth Technology</p><p>Relatório de sensores · arquivo offline</p></header>
            <p>Leituras brutas do kernel, sem inferência de saúde, calibração ou proteção térmica. Exportar não inicia monitoramento, SMART ou benchmarks.</p>
            <section><h2>Proveniência</h2><dl>
            """);
        Metadata("Formato HTML", FormatVersion); Metadata("Schema de sensores", snapshot.SchemaVersion);
        Metadata("Coleta UTC", Utc(snapshot.CollectedAt)); Metadata("Estado agregado", snapshot.Status);
        Metadata("Motivo", snapshot.Reason ?? ""); html.Append("</dl></section>");
        StartTable("Leituras de sensores", ["Dispositivo", "Chip", "Canal", "Rótulo", "Valor", "Unidade", "Estado", "Coleta UTC", "Qualidade", "Fonte", "Motivo"]);
        foreach (var sensor in snapshot.Readings)
            Row(sensor.Device, sensor.Chip, sensor.Channel, sensor.Label, Value(sensor.Reading), sensor.Reading.Unit,
                sensor.Reading.Status, Utc(sensor.CollectedAt), sensor.Quality, sensor.Reading.Source, sensor.Reading.Reason ?? "");
        EndTable();
        if (snapshot.Readings.Count == 0) html.Append("<p>Indisponível: nenhuma leitura de sensor informada.</p>");
        StartTable("Bateria e alimentação — metadados brutos", ["Dispositivo", "Campo", "Valor", "Unidade", "Estado", "Fonte", "Motivo"]);
        foreach (var supply in snapshot.PowerSupplies ?? [])
        {
            Power(supply.Device, "Tipo", supply.Type); Power(supply.Device, "Status", supply.Status); Power(supply.Device, "Presente", supply.Present);
        }
        EndTable();
        if (snapshot.PowerSupplies is not { Count: > 0 }) html.Append("<p>Indisponível: nenhuma fonte de alimentação informada.</p>");
        html.Append("<section><h2>Limitações</h2><ul>");
        foreach (var limitation in snapshot.Limitations)
        { ct.ThrowIfCancellationRequested(); html.Append("<li>").Append(E(limitation)).Append("</li>"); }
        html.Append("</ul></section><footer><p>").Append(E(ProductIdentity.Attribution)).Append("</p><p>")
            .Append(E(ProductIdentity.SharingNotice)).Append("</p></footer></main></body></html>\n");
        return html.ToString();

        void Metadata(string key, string value) => html.Append("<dt>").Append(E(key)).Append("</dt><dd>").Append(E(value)).Append("</dd>");
        void StartTable(string title, string[] headings)
        {
            html.Append("<section><h2>").Append(E(title)).Append("</h2><div class=\"table-wrap\"><table><thead><tr>");
            foreach (var heading in headings) html.Append("<th scope=\"col\">").Append(E(heading)).Append("</th>");
            html.Append("</tr></thead><tbody>");
        }
        void EndTable() => html.Append("</tbody></table></div></section>");
        void Row(params string[] cells)
        {
            ct.ThrowIfCancellationRequested(); html.Append("<tr>");
            foreach (var cell in cells) html.Append("<td>").Append(E(cell)).Append("</td>");
            html.Append("</tr>");
        }
        void Power<T>(string device, string field, Reading<T> reading) =>
            Row(device, field, Value(reading), reading.Unit, reading.Status, reading.Source, reading.Reason ?? "");
    }
    private static string Value<T>(Reading<T> reading) => reading.Status == "available" && reading.Value is { } value
        ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "Indisponível";
    private static string Utc(DateTimeOffset date) => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string E(string text) => WebUtility.HtmlEncode(text);
}
