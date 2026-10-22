using System.Globalization;
using System.Text.Json;
using Ilmenyx.Core.Benchmarks;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Cli;

/// <summary>`bench disk` and `bench memory-full`: heavy runs that need an explicit --allow-* flag and never run by default.</summary>
public static class HeavyBenchCommand
{
    public const string Help = """
          bench memory-full --allow-memory-load [--timeout-seconds 5..300] [--json]
          bench disk --directory PASTA --allow-disk-load [--size-mb 64..8192] [--phase-seconds 1..10] [--timeout-seconds N] [--json]
        memory-full: aloca ~2,3 GiB (recusa sem 1 GiB de reserva livre). disk: um arquivo temporário novo (0600) na pasta indicada, E/S direta, apagado ao final; nunca dispositivos.
        """;

    public static bool Handles(string[] args) => args is ["bench", "disk" or "memory-full", ..];

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var flags = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int i = 2; i < args.Length; i++)
        {
            string flag = args[i];
            if (flags.ContainsKey(flag)) { Console.Error.WriteLine("Argumento duplicado: " + flag); return 2; }
            bool boolean = flag is "--allow-memory-load" or "--allow-disk-load" or "--json";
            if (!boolean && (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))) { Console.Error.WriteLine("Falta valor para " + flag); return 2; }
            flags[flag] = boolean ? null : args[++i];
        }
        bool json = flags.ContainsKey("--json");
        try
        {
            if (args[1] == "memory-full")
            {
                if (!flags.ContainsKey("--allow-memory-load")) { Console.Error.WriteLine("memory-full exige --allow-memory-load (aloca ~2,3 GiB)."); return 2; }
                int timeout = Int(flags, "--timeout-seconds", 120, 5, 300);
                long available = LinuxMemoryInfo.AvailableBytes();
                var r = await MemoryBench.RunAsync(available, TimeSpan.FromSeconds(timeout), ct);
                Console.WriteLine(json ? JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }) :
                    $"Memória · {r.Status}" + (r.Status == BenchRunStatus.Completed
                        ? string.Create(CultureInfo.InvariantCulture, $"\nLatência: {r.LatencyNs:0.0} ns · Leitura: {r.ReadGBs:0.0} GB/s · Cópia: {r.CopyGBs:0.0} GB/s · Threads: {r.Threads}")
                        : "\n" + r.Reason));
                return r.Status == BenchRunStatus.Completed ? 0 : 1;
            }
            if (!flags.ContainsKey("--allow-disk-load")) { Console.Error.WriteLine("disk exige --allow-disk-load (escreve e lê um arquivo temporário)."); return 2; }
            if (!flags.TryGetValue("--directory", out string? dir) || dir is null) { Console.Error.WriteLine("disk exige --directory PASTA."); return 2; }
            var options = new Ilmenyx.Core.Benchmarks.DiskBenchOptions(dir, Int(flags, "--size-mb", 1024, 64, 8192), Int(flags, "--phase-seconds", 4, 1, 10),
                TimeSpan.FromSeconds(Int(flags, "--timeout-seconds", 180, 10, 600)));
            var d = await LinuxDiskBench.RunAsync(options, ct);
            Console.WriteLine(json ? JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }) :
                $"Disco · {d.Status} · {d.Directory}" + (d.Status == BenchRunStatus.Completed
                    ? string.Create(CultureInfo.InvariantCulture, $"\nSeq. leitura: {d.SeqReadMBs:0} MB/s · Seq. escrita: {d.SeqWriteMBs:0} MB/s\n4K Q1 leitura: {d.ReadIopsQ1:0} IOPS ({d.ReadLatencyUs:0.0} µs) · 4K Q1 escrita: {d.WriteIopsQ1:0} IOPS ({d.WriteLatencyUs:0.0} µs) · 4K Q8 leitura: {d.ReadIopsQ8:0} IOPS\nNota do histórico: {d.Score:0}")
                    : "\n" + d.Reason));
            return d.Status == BenchRunStatus.Completed ? 0 : 1;
        }
        catch (ArgumentException e) { Console.Error.WriteLine(e.Message); return 2; }
    }

    private static int Int(Dictionary<string, string?> flags, string name, int fallback, int min, int max)
    {
        if (!flags.TryGetValue(name, out string? text) || text is null) return fallback;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
            throw new ArgumentException($"{name} deve estar entre {min} e {max}.");
        return value;
    }
}
