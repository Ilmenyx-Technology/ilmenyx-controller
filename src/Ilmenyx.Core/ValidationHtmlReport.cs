// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
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
            throw new ArgumentException("HTML CPU aceita somente validação reduzida sem score.");
        return Append(report.Hardware, "CPU — validação reduzida", [
            ("Schema do resultado", report.SchemaVersion), ("Estado", r.Status.ToString()),
            ("Workload", r.Workload.Id), ("Algoritmo", r.AlgorithmVersion), ("Método", r.MeasurementVersion),
            ("Referência upstream", r.ReferenceCommit), ("Coleta UTC", r.StartedAt.ToUniversalTime().ToString("O")),
            ("Workers", Text(r.Threads)), ("Dimensões", $"{r.Workload.Width} × {r.Workload.Height}"),
            ("Amostras por pixel", Text(r.Workload.SamplesPerPixel)), ("Amostras concluídas", Text(r.CompletedSamples)),
            ("Passadas", Text(r.CompletedPasses)), ("SHA256 imagem", r.ImageSha256),
            ("Score", "indisponível — validação não mede desempenho"), ("Comparabilidade", r.ScoreComparability)
        ], r.Limitations, ct);
    }
    public static string Render(MemoryValidationReport report, CancellationToken ct = default)
    {
        var r = report.Validation;
        if (r.Score is not null || r.WorkloadId != MemoryValidationRunner.WorkloadId) throw new ArgumentException("HTML memória aceita somente validação reduzida sem score.");
        return Append(report.Hardware, "Memória — validação de algoritmos", [
            ("Schema do resultado", report.SchemaVersion), ("Estado", r.Status.ToString()),
            ("Workload", r.WorkloadId), ("Algoritmo", r.AlgorithmVersion), ("Referência upstream", r.ReferenceCommit),
            ("Coleta UTC", r.StartedAt.ToUniversalTime().ToString("O")), ("Workers", Text(r.Threads)),
            ("Buffer (B)", Text(r.WorkingSetBytes)), ("Buffers principais (B)", Text(r.MainBufferBytes)),
            ("Passos solicitados", Text(r.RequestedTraversalSteps)), ("Passos concluídos", Text(r.CompletedTraversalSteps)),
            ("Soma leitura", Text(r.ReadSum)), ("Soma cópia", Text(r.CopySum)),
            ("SHA256 cadeia", r.RingSha256 ?? "indisponível"), ("SHA256 cópia", r.CopySha256 ?? "indisponível"),
            ("Métricas", "Não mede latência, GB/s, estabilidade ou saúde; sem score.")
        ], r.Limitations, ct);
    }
    public static string Render(GameSimValidationReport report, CancellationToken ct = default)
    {
        var r = report.Validation;
        if (r.Score is not null || r.Workload.Entities != 256 || r.Workload.Steps != 16) throw new ArgumentException("HTML GameSim aceita somente validação reduzida sem score.");
        return Append(report.Hardware, "GameSim — validação reduzida", [
            ("Schema do resultado", report.SchemaVersion), ("Estado", r.Status.ToString()),
            ("Workload", r.Workload.Id), ("Algoritmo", r.AlgorithmVersion), ("Referência upstream", r.ReferenceCommit),
            ("Coleta UTC", r.StartedAt.ToUniversalTime().ToString("O")), ("Workers", Text(r.Threads)),
            ("Entidades", Text(r.Workload.Entities)), ("Passos solicitados", Text(r.Workload.Steps)),
            ("Passos concluídos", Text(r.CompletedSteps)), ("Formato do hash", r.HashFormat),
            ("SHA256 estado", r.StateSha256 ?? "indisponível"), ("Métricas", "Simulação sintética; sem FPS, low1 ou score.")
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
        var section = new StringBuilder("<section id=\"validation-result\"><h2>").Append(E(title)).Append("</h2><p>Resultado já calculado. Exportar este snapshot não executa carga adicional. Sem pontuação de desempenho.</p><dl>");
        foreach (var pair in values) { ct.ThrowIfCancellationRequested(); section.Append("<dt>").Append(E(pair.Label)).Append("</dt><dd>").Append(E(pair.Value)).Append("</dd>"); }
        section.Append("</dl><ul>");
        foreach (var text in limitations) { ct.ThrowIfCancellationRequested(); section.Append("<li>").Append(E(text)).Append("</li>"); }
        section.Append("</ul></section>");
        return HardwareHtmlReport.Render(hardware, ct).Replace("</main>", section + "</main>", StringComparison.Ordinal);
    }
    private static string Text(object? value) => value is null ? "indisponível" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "indisponível";
    private static string E(string text) => WebUtility.HtmlEncode(text);
}
