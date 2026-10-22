// New Ilmenyx additions: see LICENSING.md.
using System.Globalization;
using System.Net;
using System.Text;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core;

/// <summary>Passive HTML for an offline analysis; never claims a fresh device query.</summary>
public static class SmartImportHtmlReport
{
    public const string FormatVersion = "1.0";
    public static Task ExportAsync(SmartImportReport report, string path, CancellationToken ct = default) =>
        ReportFile.WriteAsync(path, async (output, token) =>
        {
            var html = Render(report, token);
            await using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, leaveOpen: true);
            await writer.WriteAsync(html.AsMemory(), token); await writer.FlushAsync(token);
        }, ct);

    public static string Render(SmartImportReport report, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var d = report.Diagnostic;
        var html = new StringBuilder(4096);
        html.Append("""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
            <title>Ilmenyx — SMART importado</title><style>
            *{box-sizing:border-box}body{margin:0;font:15px system-ui,sans-serif;line-height:1.5}main{max-width:1000px;margin:auto;padding:24px}
            p,dd,li{overflow-wrap:anywhere}dl{display:grid;grid-template-columns:minmax(150px,1fr) 3fr;gap:8px}dd{margin:0}
            footer{white-space:pre-wrap}@media(max-width:600px){main{padding:12px}dl{display:block}dd{margin-bottom:8px}}
            </style></head><body><main><h1>Ilmenyx</h1><p>by Bismuth Technology</p><h2>SMART importado — análise offline</h2>
            <p>Relatório histórico fornecido pelo usuário. Nenhuma nova consulta ao disco. Origem e data original não confirmadas; não é garantia de saúde atual.</p><dl>
            """);
        Field("Formato HTML", FormatVersion); Field("Schema de importação", report.SchemaVersion); Field("Tipo de relatório", report.ReportKind);
        Field("Análise UTC", Utc(report.ImportedAt)); Field("Registro da análise (DTO) UTC", Utc(d.CollectedAt));
        Field("Schema de diagnóstico", d.SchemaVersion); Field("Dispositivo declarado", d.Device); Field("Estado", d.Status);
        Field("Motivo", d.Reason ?? ""); Field("Código smartctl da entrada", d.ToolExitCode?.ToString(CultureInfo.InvariantCulture) ?? "Indisponível");
        Field("reportedPassed — relato do firmware", d.ReportedPassed.Status == "available" && d.ReportedPassed.Value is { } value ? (value ? "true" : "false") : "Indisponível");
        Field("Estado da leitura", d.ReportedPassed.Status); Field("Unidade", d.ReportedPassed.Unit); Field("Fonte declarada", d.ReportedPassed.Source);
        Field("Motivo da leitura", d.ReportedPassed.Reason ?? "");
        foreach (var metric in d.Metrics ?? []) { ct.ThrowIfCancellationRequested(); Field("Métrica bruta: " + metric.Name, SmartMetricText.Describe(metric) + " · fonte " + metric.Source); }
        html.Append("</dl>");
        Limitations("Limitações da importação", report.Limitations); Limitations("Limitações do diagnóstico", d.Limitations);
        html.Append("<footer><p>").Append(E(ProductIdentity.Attribution)).Append("</p><p>").Append(E(ProductIdentity.SharingNotice))
            .Append("</p></footer></main></body></html>\n");
        return html.ToString();
        void Field(string name, string value) => html.Append("<dt>").Append(E(name)).Append("</dt><dd>").Append(E(value)).Append("</dd>");
        void Limitations(string title, IReadOnlyList<string> items)
        {
            html.Append("<h2>").Append(E(title)).Append("</h2><ul>");
            foreach (var item in items) { ct.ThrowIfCancellationRequested(); html.Append("<li>").Append(E(item)).Append("</li>"); }
            html.Append("</ul>");
        }
    }
    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string E(string text) => WebUtility.HtmlEncode(text);
}
