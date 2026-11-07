// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Runs the ray tracing benchmark adapted from RESET EXPLODE GpuRtBench (MIT, see NOTICE.md).
using System.Globalization;
using System.Text.Json;
using Ilmenyx.Gpu.Vulkan;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Cli;

/// <summary>Parsed `bench gpu-rt` / `bench gpu-rt-snapshot` request (pure: no device, no load).</summary>
public sealed record GpuRtRequest(GpuRtOptions Options, int? Adapter, double TempLimit, bool Json, string? Output, double T);

/// <summary>`bench gpu-rt` (warm-up + passes, score from the median) and `bench gpu-rt-snapshot` (one PNG frame). Both need --allow-gpu-load.</summary>
public static class GpuRtCommand
{
    public const int DefaultWarmup = 5, DefaultPass = 10, DefaultPasses = 3;
    public const double DefaultTempLimit = 90;

    /// <summary>Validates flags and limits; returns the request or a readable error (exit code 2).</summary>
    public static (GpuRtRequest? Request, string? Error) Parse(string command, IReadOnlyDictionary<string, string?> flags)
    {
        string[] known = ["--allow-gpu-load", "--adapter", "--preset", "--resolution", "--warmup-seconds", "--pass-seconds", "--passes", "--json", "--temp-limit", "--output", "--t"];
        if (flags.Keys.FirstOrDefault(k => !known.Contains(k)) is { } unknown) return (null, "Opção desconhecida: " + unknown);
        if (!flags.ContainsKey("--allow-gpu-load")) return (null, command + " exige --allow-gpu-load (carga máxima na GPU).");
        try
        {
            int? adapter = flags.ContainsKey("--adapter") ? Int(flags, "--adapter", 0, 0, 16) : null;
            string preset = flags.TryGetValue("--preset", out var p) && p is not null ? p.ToLowerInvariant() : "alta";
            string resolution = flags.TryGetValue("--resolution", out var r) && r is not null ? r.ToLowerInvariant() : "1080p";
            if (GpuRtPresets.Quality(preset) is null) return (null, "--preset deve ser normal, alta ou extrema.");
            if (GpuRtPresets.Size(resolution) is null) return (null, "--resolution deve ser 1080p, 1440p ou 4k.");
            var options = new GpuRtOptions(adapter ?? 0, preset, resolution,
                TimeSpan.FromSeconds(Int(flags, "--warmup-seconds", DefaultWarmup, 0, GpuRtOptions.MaxWarmupSeconds)),
                TimeSpan.FromSeconds(Int(flags, "--pass-seconds", DefaultPass, 1, GpuRtOptions.MaxPassSeconds)),
                Int(flags, "--passes", DefaultPasses, 1, GpuRtOptions.MaxPasses), true);
            double limit = DefaultTempLimit;
            if (flags.TryGetValue("--temp-limit", out var tl) && tl is not null && (!double.TryParse(tl, NumberStyles.Float, CultureInfo.InvariantCulture, out limit) || limit is < 50 or > 105))
                return (null, "--temp-limit deve estar entre 50 e 105 °C.");
            string? output = null; double t = 0.1;
            if (command == "gpu-rt-snapshot")
            {
                if (!flags.TryGetValue("--output", out output) || output is null || !Path.IsPathFullyQualified(output) || !output.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    return (null, "gpu-rt-snapshot exige --output com caminho absoluto de um PNG novo.");
                if (flags.TryGetValue("--t", out var ts) && ts is not null && (!double.TryParse(ts, NumberStyles.Float, CultureInfo.InvariantCulture, out t) || t is < 0 or > 1))
                    return (null, "--t deve estar entre 0 e 1.");
            }
            else if (flags.ContainsKey("--output") || flags.ContainsKey("--t")) return (null, "--output e --t são do gpu-rt-snapshot.");
            return (new GpuRtRequest(options, adapter, limit, flags.ContainsKey("--json"), output, t), null);
        }
        catch (ArgumentException e) { return (null, e.Message); }
    }

    private static int Int(IReadOnlyDictionary<string, string?> flags, string name, int fallback, int min, int max)
    {
        if (!flags.TryGetValue(name, out string? text) || text is null) return fallback;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
            throw new ArgumentException($"{name} deve estar entre {min} e {max}.");
        return value;
    }

    public static string Text(GpuRtResult r)
    {
        string head = $"Ray tracing · {r.Status} · {r.Adapter}";
        if (r.Status != GpuRunStatus.Completed) return head + "\n" + r.Reason;
        var lines = new List<string>
        {
            head,
            string.Create(CultureInfo.InvariantCulture, $"{GpuRtPresets.Label(r.Preset)} · {r.Width}×{r.Height} · nota {r.Score:0} · {r.MedianFps:0.00} FPS (mediana) · 1% low {r.MedianLow1:0.00}"),
            string.Create(CultureInfo.InvariantCulture, $"Variação entre passadas: {r.VariationPct:0.0}% · {(r.Reliable ? "confiável" : r.Passes.Count < 3 ? "menos de 3 passadas: não confiável" : "acima de 3%: não confiável")}"),
            string.Create(CultureInfo.InvariantCulture, $"Raios por segundo (estimado): {r.GigaRaysPerSec:0.000} bilhões · tempo de quadro: {(r.GpuTimestamps ? "timestamps da GPU" : "relógio da CPU (fila sem timestamps)")}"),
        };
        for (int i = 0; i < r.Passes.Count; i++) lines.Add(string.Create(CultureInfo.InvariantCulture, $"Passada {i + 1}: {r.Passes[i].AvgFps:0.00} FPS · 1% low {r.Passes[i].Low1Fps:0.00} · {r.Passes[i].Frames} quadros"));
        lines.Add(GpuRtResult.Comparability);
        return string.Join("\n", lines);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static async Task<int> RunAsync(string command, IReadOnlyDictionary<string, string?> flags, CancellationToken ct)
    {
        var (request, error) = Parse(command, flags);
        if (request is null) { Console.Error.WriteLine(error); return 2; }
        var devices = VulkanDevices.List();
        var (adapter, why) = GpuRtSupport.Pick(devices, request.Adapter);
        if (adapter is null) { Console.Error.WriteLine("Ray tracing indisponível: " + why); return 1; }
        if (adapter.IsSoftware) Console.Error.WriteLine($"AVISO: o adaptador {adapter.Index} ({adapter.Name}) é renderização por CPU, escolhido só porque foi pedido com --adapter.");
        var options = request.Options with { AdapterIndex = adapter.Index };
        bool integrated = adapter.Kind == "GPU integrada" || adapter.IsSoftware;
        Func<double?> temperature = integrated ? () => LinuxThermalProbe.CpuTempC() : () => LinuxThermalProbe.GpuTempC(adapter.VendorId, adapter.DeviceId);
        string sensor = integrated ? "CPU" : "GPU";
        if (temperature() is null) Console.Error.WriteLine($"ATENÇÃO: nenhum sensor de temperatura da {sensor} está sendo lido; o corte automático NÃO funcionará.");
        else Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Corte por temperatura: {sensor} a {request.TempLimit:0} °C") + (integrated && !adapter.IsSoftware ? " (GPU integrada: vale a temperatura da CPU)." : "."));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        string? abort = null; double maxTemp = double.NaN;
        var guard = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                if (temperature() is double t)
                {
                    maxTemp = double.IsNaN(maxTemp) ? t : Math.Max(maxTemp, t);
                    if (t >= request.TempLimit) { abort = string.Create(CultureInfo.InvariantCulture, $"ABORTADO: {sensor} a {t:0.#} °C, limite {request.TempLimit:0} °C"); cts.Cancel(); return; }
                }
                try { await Task.Delay(1000, cts.Token); } catch (OperationCanceledException) { return; }
            }
        });
        GpuRtResult r;
        try
        {
            if (request.Output is { } png)
            {
                r = await new GpuRtBench().SnapshotAsync(options, request.T, png, cts.Token);
                Console.WriteLine(r.Status == GpuRunStatus.Completed ? $"Quadro salvo em {png} ({r.Width}×{r.Height}, {GpuRtPresets.Label(r.Preset)}) · {r.Adapter}" : $"Ray tracing snapshot · {r.Status}\n{abort ?? r.Reason}");
                return r.Status == GpuRunStatus.Completed ? 0 : 1;
            }
            var progress = new Progress<GpuRtProgress>(p => { if (!request.Json) Console.Error.Write(string.Create(CultureInfo.InvariantCulture, $"\r{p.Phase} · {p.Fraction:P0} · {p.Fps:0.0} FPS      ")); });
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Ray tracing · {adapter.Name} · {GpuRtPresets.Label(options.Preset)} {options.Width}×{options.Height} · aquecimento {options.EffectiveWarmup.TotalSeconds:0} s + {options.Passes}×{options.EffectivePass.TotalSeconds:0} s"));
            r = await new GpuRtBench().RunAsync(options, cts.Token, progress);
            if (!request.Json) Console.Error.WriteLine();
            if (abort is not null && r.Status == GpuRunStatus.Cancelled) r = r with { Status = GpuRunStatus.Failed, Reason = abort };
            Console.WriteLine(request.Json ? JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }) : Text(r));
            if (!request.Json && !double.IsNaN(maxTemp)) Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Temperatura máxima lida ({sensor}): {maxTemp:0.#} °C"));
            return r.Status == GpuRunStatus.Completed ? 0 : 1;
        }
        finally { cts.Cancel(); await guard; }
    }
}
