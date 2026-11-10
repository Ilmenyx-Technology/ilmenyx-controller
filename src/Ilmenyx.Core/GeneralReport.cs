// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Adapted from RESET EXPLODE ReportBuilder.cs (MIT, Copyright (c) 2026 Gustavo (GustavoReset)); see NOTICE.md.
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core;

public sealed record ReportSensorRow(string Hardware, string Name, string Value, string Quality);
public sealed record ReportGpu(string Name, string Subtitle, IReadOnlyList<InfoNode> Sections);

/// <summary>Everything that goes into the general report. Built from read-only collections; never contains serial numbers or the host name.</summary>
public sealed class ReportData
{
    public DateTimeOffset Date { get; init; } = DateTimeOffset.Now;
    public string AppVersion { get; init; } = "";
    public IReadOnlyList<InfoNode> System { get; init; } = [];
    public IReadOnlyList<ReportGpu> Gpus { get; init; } = [];
    public IReadOnlyList<HealthCard> Disks { get; init; } = [];
    public IReadOnlyList<HealthCard> Power { get; init; } = [];
    public IReadOnlyList<HealthCard> Network { get; init; } = [];
    public IReadOnlyList<ReportSensorRow> Sensors { get; init; } = [];
    public IReadOnlyList<BenchRunEntry> Benchmarks { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>Single-file HTML (no scripts, no remote resources, all variable text escaped) or JSON of the data above.</summary>
public static class GeneralReport
{
    public const string FormatVersion = "1.0";

    /// <summary>Copy of the tree without serial numbers and without nodes marked "omitido dos relatórios", such as the host name (privacy: reports are often shared).</summary>
    public static InfoNode? WithoutSerials(InfoNode node)
    {
        if (node.Header.Contains("série", StringComparison.OrdinalIgnoreCase) || node.Header.Contains("serial", StringComparison.OrdinalIgnoreCase)
            || node.Header.Contains("omitido dos relatórios", StringComparison.OrdinalIgnoreCase)) return null;
        var copy = new InfoNode { Header = node.Header, Value = node.Value };
        foreach (var child in node.Children) if (WithoutSerials(child) is { } kept) copy.Children.Add(kept);
        return copy;
    }

    private static List<InfoNode> Clean(IEnumerable<InfoNode> nodes) => nodes.Select(WithoutSerials).Where(n => n is not null).Cast<InfoNode>().ToList();

    private static object Card(HealthCard c) => new { nome = c.Title, detalhes = c.Meta, estado = c.VerdictText, motivos = c.Reasons, metricas = c.Metrics.Select(m => new { m.Key, m.Value }) };
    private static object Tree(InfoNode n) => new { nome = n.Header, valor = n.Value.Length > 0 ? n.Value : null, filhos = n.Children.Count > 0 ? n.Children.Select(Tree).ToList() : null };

    public static string Json(ReportData d) => JsonSerializer.Serialize(new
    {
        formato = FormatVersion,
        gerado_em = d.Date.ToString("s", CultureInfo.InvariantCulture),
        versao_do_programa = d.AppVersion,
        sistema = Clean(d.System).Select(Tree),
        placas_de_video = d.Gpus.Select(g => new { g.Name, g.Subtitle, secoes = Clean(g.Sections).Select(Tree) }),
        discos = d.Disks.Select(Card), energia = d.Power.Select(Card), rede = d.Network.Select(Card),
        sensores = d.Sensors, benchmarks = d.Benchmarks.Select(b => new { b.At, b.Kind, b.Summary }), observacoes = d.Notes,
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static void Tree(StringBuilder sb, IEnumerable<InfoNode> nodes, int depth)
    {
        foreach (var n in nodes)
        {
            sb.Append($"<div class=\"node\" style=\"margin-left:{Math.Min(depth, 8) * 14}px\">");
            sb.Append(n.Value.Length > 0 ? $"<span class=\"k\">{E(n.Header)}</span> {E(n.Value)}" : $"<b>{E(n.Header)}</b>");
            sb.Append("</div>");
            Tree(sb, n.Children, depth + 1);
        }
    }

    private static void Cards(StringBuilder sb, string title, IReadOnlyList<HealthCard> cards)
    {
        if (cards.Count == 0) return;
        sb.Append($"<section><h2>{E(title)}</h2>");
        foreach (var c in cards)
        {
            string cls = c.Verdict switch { HealthVerdict.Good => "ok", HealthVerdict.Unknown => "na", _ => "bad" };
            sb.Append($"<div class=\"card\"><div class=\"row\"><b>{E(c.Title)}</b><span class=\"badge {cls}\">{E(c.VerdictText)}</span></div>");
            if (c.Meta.Length > 0) sb.Append($"<p class=\"muted\">{E(c.Meta)}</p>");
            if (c.Reasons.Length > 0) sb.Append($"<p>{E(c.Reasons)}</p>");
            if (c.Metrics.Count > 0) { sb.Append("<table>"); foreach (var m in c.Metrics) sb.Append($"<tr><td class=\"k\">{E(m.Key)}</td><td>{E(m.Value)}</td></tr>"); sb.Append("</table>"); }
            sb.Append("</div>");
        }
        sb.Append("</section>");
    }

    public static string Html(ReportData d)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.Append("""
            <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
            <title>Ilmenyx — Relatório</title><style>
            *{box-sizing:border-box}body{margin:0;font:15px system-ui,sans-serif;line-height:1.5}.wrap{max-width:1100px;margin:auto;padding:24px}
            h1{margin:0}h2{margin:28px 0 8px}.sub,.muted{opacity:.7}.card{border:1px solid #8884;border-radius:12px;padding:12px 16px;margin:10px 0}
            .row{display:flex;justify-content:space-between;gap:12px}.badge{border:1px solid #888;border-radius:12px;padding:0 10px;font-size:12px;font-weight:700}
            .badge.ok{border-color:#2a9d4a}.badge.bad{border-color:#d9534f}table{width:100%;border-collapse:collapse;font-size:13px}
            td,th{padding:5px 8px;text-align:left;border-top:1px solid #8883;vertical-align:top;overflow-wrap:anywhere}.k{opacity:.7;padding-right:8px}
            .node{font-size:13px;overflow-wrap:anywhere}.num{text-align:right}footer{margin-top:28px;font-size:12px;opacity:.7}
            @media(max-width:600px){.wrap{padding:12px}}@media print{.wrap{padding:0}}</style></head><body><div class="wrap">
            """);
        sb.Append($"<header><h1>Ilmenyx Controller</h1><p class=\"sub\">by Ilmenyx Technology · Relatório de hardware · {E(d.Date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}");
        if (d.AppVersion.Length > 0) sb.Append($" · v{E(d.AppVersion)}");
        sb.Append("</p></header><p class=\"muted\">Números de série e o nome do computador foram omitidos. Arquivo offline: sem scripts nem recursos remotos.</p>");
        foreach (var note in d.Notes) sb.Append($"<p class=\"muted\">{E(note)}</p>");
        Cards(sb, "Saúde dos discos", d.Disks); Cards(sb, "Bateria e energia", d.Power); Cards(sb, "Teste de rede", d.Network);
        if (d.System.Count > 0)
        {
            sb.Append("<section><h2>Sistema</h2>");
            foreach (var n in Clean(d.System)) { sb.Append($"<div class=\"card\"><h3>{E(n.Header)}</h3>"); Tree(sb, n.Children, 0); sb.Append("</div>"); }
            sb.Append("</section>");
        }
        foreach (var g in d.Gpus)
        {
            sb.Append($"<section><h2>Placa de vídeo · {E(g.Name)}</h2>");
            if (g.Subtitle.Length > 0) sb.Append($"<p class=\"muted\">{E(g.Subtitle)}</p>");
            foreach (var s in Clean(g.Sections)) { sb.Append($"<div class=\"card\"><h3>{E(s.Header)}</h3>"); Tree(sb, s.Children, 0); sb.Append("</div>"); }
            sb.Append("</section>");
        }
        if (d.Sensors.Count > 0)
        {
            sb.Append("<section><h2>Sensores (leitura no momento da exportação)</h2>");
            foreach (var grp in d.Sensors.GroupBy(s => s.Hardware))
            {
                sb.Append($"<div class=\"card\"><h3>{E(grp.Key)}</h3><table><tr><th>Sensor</th><th class=\"num\">Valor</th><th>Qualidade</th></tr>");
                foreach (var s in grp) sb.Append($"<tr><td>{E(s.Name)}</td><td class=\"num\">{E(s.Value)}</td><td class=\"muted\">{E(s.Quality)}</td></tr>");
                sb.Append("</table></div>");
            }
            sb.Append("</section>");
        }
        if (d.Benchmarks.Count > 0)
        {
            sb.Append("<section><h2>Benchmarks e testes recentes</h2><div class=\"card\"><table><tr><th>Data</th><th>Teste</th><th>Resultado</th></tr>");
            foreach (var b in d.Benchmarks) sb.Append($"<tr><td class=\"muted\">{E(b.At.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}</td><td>{E(b.Kind)}</td><td>{E(b.Summary)}</td></tr>");
            sb.Append("</table><p class=\"muted\">Pontuações experimentais do port Linux; não comparáveis a scores do Windows.</p></div></section>");
        }
        sb.Append("<footer>Gerado pelo Ilmenyx (adaptação independente e não oficial do RESET EXPLODE, de Gustavo/GustavoReset, MIT). Dados lidos de /proc, /sys, /etc e das ferramentas locais; podem variar conforme o hardware. Revise antes de compartilhar.</footer></div></body></html>");
        return sb.ToString();
    }

    public static Task ExportAsync(ReportData data, string path, bool html, CancellationToken ct = default) =>
        ReportFile.WriteAsync(path, async (output, token) =>
        {
            await using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, leaveOpen: true);
            await writer.WriteAsync((html ? Html(data) : Json(data)).AsMemory(), token);
            await writer.FlushAsync(token);
        }, ct);
}
