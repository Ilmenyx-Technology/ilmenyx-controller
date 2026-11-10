// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using System.Text.Json;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Platform.Linux;
using Ilmenyx.Core;

namespace Ilmenyx.Cli;

/// <summary>`bench disk` and `bench memory-full`: heavy runs that need an explicit --allow-* flag and never run by default.</summary>
public static class HeavyBenchCommand
{
    public static string Help => string.Join("\n",
        CliText.Syntax("""
              bench memory-full --allow-memory-load [--timeout-seconds 5..300] [--json]
              bench gpu-list [--json]
              bench gpu-burn --allow-gpu-load [--adapter N] [--seconds 1..600] [--shells 1..256] [--iterations 0..512] [--msaa 1|2|4|8] [--width W --height H] [--json]
              bench gpu-scene --allow-gpu-load [--adapter N] [--seconds 1..300] [--width W --height H] [--shadow 512..4096] [--trees 0..200000] [--json]
              bench gpu-snapshot --allow-gpu-load --output ARQUIVO.png [--t 0..1] [--adapter N] [--width W --height H] [--shadow N] [--trees N]
              bench gpu-rt --allow-gpu-load [--adapter N] [--preset normal|alta|extrema] [--resolution 1080p|1440p|4k] [--warmup-seconds 0..60] [--pass-seconds 1..60] [--passes 1..5] [--temp-limit 50..105] [--json]
              bench gpu-rt-snapshot --allow-gpu-load --output ARQUIVO.png [--t 0..1] [--adapter N] [--preset P] [--resolution R]
              bench disk --directory PASTA --allow-disk-load [--size-mb 64..8192] [--phase-seconds 1..10] [--timeout-seconds N] [--json]
            """),
        L.T("gpu-rt: ray tracing Vulkan (ray query), aquecimento + passadas, nota = FPS mediano × 10; nunca escolhe llvmpipe sem --adapter; corte a 90 °C (CPU se a GPU for integrada)."),
        L.T("memory-full: aloca ~2,3 GiB (recusa sem 1 GiB de reserva livre). disk: um arquivo temporário novo (0600) na pasta indicada, E/S direta, apagado ao final; nunca dispositivos."));

    public static bool Handles(string[] args) => args is ["bench", "disk" or "memory-full" or "gpu-list" or "gpu-burn" or "gpu-scene" or "gpu-snapshot" or "gpu-rt" or "gpu-rt-snapshot", ..];

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var flags = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int i = 2; i < args.Length; i++)
        {
            string flag = args[i];
            if (flags.ContainsKey(flag)) { Console.Error.WriteLine(L.T("Argumento duplicado: ") + flag); return 2; }
            bool boolean = flag is "--allow-memory-load" or "--allow-disk-load" or "--allow-gpu-load" or "--json";
            if (!boolean && (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))) { Console.Error.WriteLine(L.T("Falta valor para ") + flag); return 2; }
            flags[flag] = boolean ? null : args[++i];
        }
        bool json = flags.ContainsKey("--json");
        if (args[1] is "gpu-rt" or "gpu-rt-snapshot") return await GpuRtCommand.RunAsync(args[1], flags, ct);
        try
        {
            if (args[1] == "gpu-list")
            {
                var list = Ilmenyx.Gpu.Vulkan.VulkanDevices.List();
                Console.WriteLine(json ? JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }) :
                    list.Error ?? (list.Adapters.Count == 0 ? L.T("Nenhum adaptador Vulkan.") : string.Join("\n", list.Adapters.Select(a => $"[{a.Index}] {a.Description}" + (a.SupportsVulkan13 ? "" : L.T(" · SEM Vulkan 1.3")) + (a.SupportsRayTracingPipeline ? " · ray tracing" : "") + (a.SupportsRayQuery ? " · ray query" : "")))));
                return list.Error is null ? 0 : 1;
            }
            if (args[1] == "gpu-burn")
            {
                if (!flags.ContainsKey("--allow-gpu-load")) { Console.Error.WriteLine(L.T("gpu-burn exige --allow-gpu-load (carga máxima na GPU).")); return 2; }
                var o = new Ilmenyx.Gpu.Vulkan.GpuBurnOptions(Int(flags, "--adapter", 0, 0, 16), Int(flags, "--width", 1920, 320, 3840), Int(flags, "--height", 1080, 240, 2160), Int(flags, "--msaa", 4, 1, 8), Int(flags, "--shells", 32, 1, 256),
                    Int(flags, "--iterations", 24, 0, 512), TimeSpan.FromSeconds(Int(flags, "--seconds", 10, 1, 600)), true);
                var progress = new Progress<Ilmenyx.Gpu.Vulkan.GpuBurnProgress>(p => { if (!json) Console.Error.Write(L.T($"\r{p.Seconds:0.0} s · {p.Frames} quadros · {p.Fps:0.0} FPS   ")); });
                var burn = await new Ilmenyx.Gpu.Vulkan.GpuBurn().RunAsync(o, ct, progress);
                if (!json) Console.Error.WriteLine();
                Console.WriteLine(json ? JsonSerializer.Serialize(burn, new JsonSerializerOptions { WriteIndented = true }) :
                    $"GPU burn · {StatusText.Of(burn.Status)} · {burn.Adapter}" + (burn.Status == Ilmenyx.Gpu.Vulkan.GpuRunStatus.Completed ? L.T($"\n{burn.Frames} quadros em {burn.Seconds:0.0} s · {burn.AverageFps:0.0} FPS médio") : "\n" + burn.Reason));
                return burn.Status == Ilmenyx.Gpu.Vulkan.GpuRunStatus.Completed ? 0 : 1;
            }
            if (args[1] is "gpu-scene" or "gpu-snapshot")
            {
                if (!flags.ContainsKey("--allow-gpu-load")) { Console.Error.WriteLine(args[1] + L.T(" exige --allow-gpu-load (carga máxima na GPU).")); return 2; }
                var so = new Ilmenyx.Gpu.Vulkan.GpuSceneOptions(Int(flags, "--adapter", 0, 0, 16), Int(flags, "--width", 1920, 320, 3840), Int(flags, "--height", 1080, 240, 2160), TimeSpan.FromSeconds(Int(flags, "--seconds", 60, 1, 300)),
                    Int(flags, "--shadow", 4096, 512, 4096), Int(flags, "--trees", 40000, 0, 200000), true);
                if (args[1] == "gpu-snapshot")
                {
                    if (!flags.TryGetValue("--output", out string? png) || png is null || !Path.IsPathFullyQualified(png)) { Console.Error.WriteLine(L.T("gpu-snapshot exige --output com caminho absoluto de um PNG novo.")); return 2; }
                    double t = flags.TryGetValue("--t", out string? ts) && double.TryParse(ts, NumberStyles.Float, CultureInfo.InvariantCulture, out double tv) ? tv : 0.1;
                    var snap = await new Ilmenyx.Gpu.Vulkan.GpuScene().SnapshotAsync(so, t, png, ct);
                    Console.WriteLine(snap.Status == Ilmenyx.Gpu.Vulkan.GpuRunStatus.Completed ? L.T($"Quadro salvo em {png} ({snap.Width}×{snap.Height}) · {snap.Adapter}") : $"GPU snapshot · {StatusText.Of(snap.Status)}\n{snap.Reason}");
                    return snap.Status == Ilmenyx.Gpu.Vulkan.GpuRunStatus.Completed ? 0 : 1;
                }
                var sprogress = new Progress<Ilmenyx.Gpu.Vulkan.GpuSceneProgress>(p => { if (!json) Console.Error.Write(L.T($"\r{p.Seconds:0.0} s · {p.Frames} quadros · {p.Fps:0.0} FPS   ")); });
                var scene = await new Ilmenyx.Gpu.Vulkan.GpuScene().RunAsync(so, ct, sprogress);
                if (!json) Console.Error.WriteLine();
                Console.WriteLine(json ? JsonSerializer.Serialize(scene, new JsonSerializerOptions { WriteIndented = true }) :
                    L.T($"Cena de GPU · {StatusText.Of(scene.Status)} · {scene.Adapter}") + (scene.Status == Ilmenyx.Gpu.Vulkan.GpuRunStatus.Completed
                        ? L.T($"\n{scene.Width}×{scene.Height} · {scene.Frames} quadros em {scene.Seconds:0.0} s · {scene.AverageFps:0.0} FPS médio · 1% low {scene.Low1Fps:0.0} · nota {scene.Score:0}\n{Ilmenyx.Gpu.Vulkan.GpuSceneResult.Comparability}")
                        : "\n" + scene.Reason));
                return scene.Status == Ilmenyx.Gpu.Vulkan.GpuRunStatus.Completed ? 0 : 1;
            }
            if (args[1] == "memory-full")
            {
                if (!flags.ContainsKey("--allow-memory-load")) { Console.Error.WriteLine(L.T("memory-full exige --allow-memory-load (aloca ~2,3 GiB).")); return 2; }
                int timeout = Int(flags, "--timeout-seconds", 120, 5, 300);
                long available = LinuxMemoryInfo.AvailableBytes();
                var r = await MemoryBench.RunAsync(available, TimeSpan.FromSeconds(timeout), ct);
                Console.WriteLine(json ? JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }) :
                    L.T($"Memória · {StatusText.Of(r.Status)}") + (r.Status == BenchRunStatus.Completed
                        ? L.T($"\nLatência: {r.LatencyNs:0.0} ns · Leitura: {r.ReadGBs:0.0} GB/s · Cópia: {r.CopyGBs:0.0} GB/s · Threads: {r.Threads}")
                        : "\n" + r.Reason));
                return r.Status == BenchRunStatus.Completed ? 0 : 1;
            }
            if (!flags.ContainsKey("--allow-disk-load")) { Console.Error.WriteLine(L.T("disk exige --allow-disk-load (escreve e lê um arquivo temporário).")); return 2; }
            if (!flags.TryGetValue("--directory", out string? dir) || dir is null) { Console.Error.WriteLine(L.T("disk exige --directory PASTA.")); return 2; }
            var options = new Ilmenyx.Core.Benchmarks.DiskBenchOptions(dir, Int(flags, "--size-mb", 1024, 64, 8192), Int(flags, "--phase-seconds", 4, 1, 10),
                TimeSpan.FromSeconds(Int(flags, "--timeout-seconds", 180, 10, 600)));
            var d = await LinuxDiskBench.RunAsync(options, ct);
            Console.WriteLine(json ? JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }) :
                L.T($"Disco · {StatusText.Of(d.Status)} · {d.Directory}") + (d.Status == BenchRunStatus.Completed
                    ? L.T($"\nSeq. leitura: {d.SeqReadMBs:0} MB/s · Seq. escrita: {d.SeqWriteMBs:0} MB/s\n4K Q1 leitura: {d.ReadIopsQ1:0} IOPS ({d.ReadLatencyUs:0.0} µs) · 4K Q1 escrita: {d.WriteIopsQ1:0} IOPS ({d.WriteLatencyUs:0.0} µs) · 4K Q8 leitura: {d.ReadIopsQ8:0} IOPS\nNota do histórico: {d.Score:0}")
                    : "\n" + d.Reason));
            return d.Status == BenchRunStatus.Completed ? 0 : 1;
        }
        catch (ArgumentException e) { Console.Error.WriteLine(e.Message); return 2; }
    }

    private static int Int(Dictionary<string, string?> flags, string name, int fallback, int min, int max)
    {
        if (!flags.TryGetValue(name, out string? text) || text is null) return fallback;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
            throw new ArgumentException(L.T($"{name} deve estar entre {min} e {max}."));
        return value;
    }
}
