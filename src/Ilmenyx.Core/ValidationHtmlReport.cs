// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Existing hardware renderer and upstream algorithms retain their own license terms.
using System.Globalization;
using System.Net;
using System.Text;
using Ilmenyx.Contracts;
using Ilmenyx.Core.Benchmarks;

namespace Ilmenyx.Core;

/// <summary>Offline rendering of an existing reduced-validation snapshot; never runs a workload.</summary>
public static class ValidationHtmlReport
{
    public static string Render(CpuBenchmarkReport report, CancellationToken ct = default)
    {
        var r = report.Benchmark;
        if (r.Workload != CpuBenchmarkRunner.DescribeWorkload(CpuWorkloadProfile.Validation) || r.ExperimentalScore is not null)
            throw new ArgumentException(L.T("HTML CPU aceita somente validação reduzida sem score."));
        return Append(report.Hardware, L.T("CPU — validação reduzida"), [
            (L.T("Schema do resultado"), report.SchemaVersion), (L.T("Estado"), r.Status.ToString()),
            ("Workload", r.Workload.Id), (L.T("Algoritmo"), r.AlgorithmVersion), (L.T("Método"), r.MeasurementVersion),
            (L.T("Referência upstream"), r.ReferenceCommit), (L.T("Coleta UTC"), r.StartedAt.ToUniversalTime().ToString("O")),
            ("Workers", Text(r.Threads)), (L.T("Dimensões"), $"{r.Workload.Width} × {r.Workload.Height}"),
            (L.T("Amostras por pixel"), Text(r.Workload.SamplesPerPixel)), (L.T("Amostras concluídas"), Text(r.CompletedSamples)),
            (L.T("Passadas"), Text(r.CompletedPasses)), (L.T("SHA256 imagem"), r.ImageSha256),
            ("Score", L.T("indisponível — validação não mede desempenho")), (L.T("Comparabilidade"), r.ScoreComparability)
        ], r.Limitations, ct);
    }
    public static string Render(MemoryValidationReport report, CancellationToken ct = default)
    {
        var r = report.Validation;
        if (r.Score is not null || r.WorkloadId != MemoryValidationRunner.WorkloadId) throw new ArgumentException(L.T("HTML memória aceita somente validação reduzida sem score."));
        return Append(report.Hardware, L.T("Memória — validação de algoritmos"), [
            (L.T("Schema do resultado"), report.SchemaVersion), (L.T("Estado"), r.Status.ToString()),
            ("Workload", r.WorkloadId), (L.T("Algoritmo"), r.AlgorithmVersion), (L.T("Referência upstream"), r.ReferenceCommit),
            (L.T("Coleta UTC"), r.StartedAt.ToUniversalTime().ToString("O")), ("Workers", Text(r.Threads)),
            ("Buffer (B)", Text(r.WorkingSetBytes)), (L.T("Buffers principais (B)"), Text(r.MainBufferBytes)),
            (L.T("Passos solicitados"), Text(r.RequestedTraversalSteps)), (L.T("Passos concluídos"), Text(r.CompletedTraversalSteps)),
            (L.T("Soma leitura"), Text(r.ReadSum)), (L.T("Soma cópia"), Text(r.CopySum)),
            (L.T("SHA256 cadeia"), r.RingSha256 ?? L.T("indisponível")), (L.T("SHA256 cópia"), r.CopySha256 ?? L.T("indisponível")),
            (L.T("Métricas"), L.T("Não mede latência, GB/s, estabilidade ou saúde; sem score."))
        ], r.Limitations, ct);
    }
    public static string Render(GameSimValidationReport report, CancellationToken ct = default)
    {
        var r = report.Validation;
        if (r.Score is not null || r.Workload.Entities != 256 || r.Workload.Steps != 16) throw new ArgumentException(L.T("HTML GameSim aceita somente validação reduzida sem score."));
        return Append(report.Hardware, L.T("GameSim — validação reduzida"), [
            (L.T("Schema do resultado"), report.SchemaVersion), (L.T("Estado"), r.Status.ToString()),
            ("Workload", r.Workload.Id), (L.T("Algoritmo"), r.AlgorithmVersion), (L.T("Referência upstream"), r.ReferenceCommit),
            (L.T("Coleta UTC"), r.StartedAt.ToUniversalTime().ToString("O")), ("Workers", Text(r.Threads)),
            (L.T("Entidades"), Text(r.Workload.Entities)), (L.T("Passos solicitados"), Text(r.Workload.Steps)),
            (L.T("Passos concluídos"), Text(r.CompletedSteps)), (L.T("Formato do hash"), r.HashFormat),
            (L.T("SHA256 estado"), r.StateSha256 ?? L.T("indisponível")), (L.T("Métricas"), L.T("Simulação sintética; sem FPS, low1 ou score."))
        ], r.Limitations, ct);
    }
    public static Task ExportAsync(CpuBenchmarkReport report, string path, CancellationToken ct = default) => Write(Render(report, ct), path, ct);
    public static Task ExportAsync(MemoryValidationReport report, string path, CancellationToken ct = default) => Write(Render(report, ct), path, ct);
    public static Task ExportAsync(GameSimValidationReport report, string path, CancellationToken ct = default) => Write(Render(report, ct), path, ct);
    private static Task Write(string html, string path, CancellationToken ct) => ReportFile.WriteAsync(path, async (output, token) =>
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), 4096, leaveOpen: true);
        await writer.WriteAsync(html.AsMemory(), token); await writer.FlushAsync(token);
    }, ct);
    private static string Append(HardwareSnapshot hardware, string title, (string Label, string Value)[] values, IReadOnlyList<string> limitations, CancellationToken ct)
    {
        var section = new StringBuilder(L.T("<section id=\"validation-result\"><h2>")).Append(E(title)).Append(L.T("</h2><p>Resultado já calculado. Exportar este snapshot não executa carga adicional. Sem pontuação de desempenho.</p><dl>"));
        foreach (var pair in values) { ct.ThrowIfCancellationRequested(); section.Append("<dt>").Append(E(pair.Label)).Append("</dt><dd>").Append(E(pair.Value)).Append("</dd>"); }
        section.Append("</dl><ul>");
        foreach (var text in limitations) { ct.ThrowIfCancellationRequested(); section.Append("<li>").Append(E(text)).Append("</li>"); }
        section.Append("</ul></section>");
        return HardwareHtmlReport.Render(hardware, ct).Replace("</main>", section + "</main>", StringComparison.Ordinal);
    }
    private static string Text(object? value) => value is null ? L.T("indisponível") : Convert.ToString(value, CultureInfo.InvariantCulture) ?? L.T("indisponível");
    private static string E(string text) => WebUtility.HtmlEncode(text);
}
