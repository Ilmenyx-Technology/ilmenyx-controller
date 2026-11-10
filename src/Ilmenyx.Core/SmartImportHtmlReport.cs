// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
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
        html.Append($$$"""
            <!doctype html>
            <html lang="{{{L.Current}}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
            <title>{{{L.T("Ilmenyx — SMART importado")}}}</title><style>
            *{box-sizing:border-box}body{margin:0;font:15px system-ui,sans-serif;line-height:1.5}main{max-width:1000px;margin:auto;padding:24px}
            p,dd,li{overflow-wrap:anywhere}dl{display:grid;grid-template-columns:minmax(150px,1fr) 3fr;gap:8px}dd{margin:0}
            footer{white-space:pre-wrap}@media(max-width:600px){main{padding:12px}dl{display:block}dd{margin-bottom:8px}}
            </style></head><body><main><h1>Ilmenyx Controller</h1><p>by Ilmenyx Technology</p><h2>{{{L.T("SMART importado — análise offline")}}}</h2>
            <p>{{{L.T("Relatório histórico fornecido pelo usuário. Nenhuma nova consulta ao disco. Origem e data original não confirmadas; não é garantia de saúde atual.")}}}</p><dl>
            """);
        Field(L.T("Formato HTML"), FormatVersion); Field(L.T("Schema de importação"), report.SchemaVersion); Field(L.T("Tipo de relatório"), report.ReportKind);
        Field(L.T("Análise UTC"), Utc(report.ImportedAt)); Field(L.T("Registro da análise (DTO) UTC"), Utc(d.CollectedAt));
        Field(L.T("Schema de diagnóstico"), d.SchemaVersion); Field(L.T("Dispositivo declarado"), d.Device); Field(L.T("Estado"), d.Status);
        Field(L.T("Motivo"), d.Reason ?? ""); Field(L.T("Código smartctl da entrada"), d.ToolExitCode?.ToString(CultureInfo.InvariantCulture) ?? L.T("Indisponível"));
        Field(L.T("reportedPassed — relato do firmware"), d.ReportedPassed.Status == "available" && d.ReportedPassed.Value is { } value ? (value ? "true" : "false") : L.T("Indisponível"));
        Field(L.T("Estado da leitura"), d.ReportedPassed.Status); Field(L.T("Unidade"), d.ReportedPassed.Unit); Field(L.T("Fonte declarada"), d.ReportedPassed.Source);
        Field(L.T("Motivo da leitura"), d.ReportedPassed.Reason ?? "");
        foreach (var metric in d.Metrics ?? []) { ct.ThrowIfCancellationRequested(); Field(L.T("Métrica bruta: ") + metric.Name, SmartMetricText.Describe(metric) + L.T(" · fonte ") + metric.Source); }
        html.Append("</dl>");
        Limitations(L.T("Limitações da importação"), report.Limitations); Limitations(L.T("Limitações do diagnóstico"), d.Limitations);
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
