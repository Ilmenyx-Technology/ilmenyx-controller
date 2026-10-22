// New Ilmenyx additions, ported from the rules and advice texts of the original BottleneckAnalyzer (MIT, see NOTICE): see LICENSING.md.
namespace Ilmenyx.Core;

public enum Limiter { Gpu, Cpu, Vram, Ram, Thermal, Capped }

/// <summary>One reading (1/s) of everything that can limit FPS. Null means the source does not expose it.</summary>
public sealed record LoadSample(double? GpuLoad, double? CpuTotal, double? CpuMaxCore, string? BusiestCore, double? VramPct, double? RamPct,
    double? GpuTemp, double? CpuTemp, bool GpuThermalCap, double? Fps)
{
    /// <summary>Rules in order: VRAM full → thermal → GPU ≥ 90 % → CPU/core ≥ 85 % with GPU spare → RAM full → nothing at the limit.</summary>
    public Limiter Classify()
    {
        if (VramPct >= 97) return Limiter.Vram;
        if (GpuThermalCap || CpuTemp >= 95) return Limiter.Thermal;
        if (GpuLoad >= 90) return Limiter.Gpu;
        if (CpuMaxCore >= 85 || CpuTotal >= 85) return Limiter.Cpu;
        if (RamPct >= 92) return Limiter.Ram;
        return Limiter.Capped;
    }
}

public static class BottleneckAnalyzer
{
    public static string Title(Limiter l) => l switch
    {
        Limiter.Gpu => "GPU no limite", Limiter.Cpu => "Gargalo de CPU", Limiter.Vram => "Memória de vídeo cheia", Limiter.Ram => "Memória RAM cheia",
        Limiter.Thermal => "Limitado por temperatura", _ => "Nada no limite"
    };

    public static string Advice(Limiter l) => l switch
    {
        Limiter.Gpu => "Normal em jogos pesados: a placa de vídeo está sendo bem aproveitada. Para mais FPS, reduza resolução ou qualidade gráfica, ou ative DLSS/FSR.",
        Limiter.Cpu => "A CPU (ou um núcleo dela) não acompanha a GPU. Subir resolução/gráficos quase não custa FPS. Para ganhar FPS: reduza opções que pesam na CPU (distância de visão, multidões, física), feche programas em segundo plano e confira se o XMP/EXPO da RAM está ativo na BIOS.",
        Limiter.Vram => "A memória da placa de vídeo encheu, o que causa travadas. Reduza a qualidade das texturas ou a resolução.",
        Limiter.Ram => "Pouca RAM livre: o sistema passa a usar swap no disco e o jogo engasga. Feche navegadores e outros programas.",
        Limiter.Thermal => "Temperatura alta está derrubando o clock. Verifique ventilação do gabinete, poeira e pasta térmica.",
        _ => "Nem CPU nem GPU estão no limite: provavelmente há trava de FPS, V-Sync/G-Sync limitando, ou o jogo está em menu/pausa. Normal se o FPS já é o desejado."
    };

    /// <summary>Theoretical balance from relative performance (GPU: RTX 4070 = 100; CPU: Ryzen 7 7700X = 100).</summary>
    public static (double Ratio, string Text) Balance(double gpuIdx, double cpuIdx)
    {
        double ratio = gpuIdx / cpuIdx;
        string text = ratio switch
        {
            > 1.8 => "GPU muito mais forte que a CPU: em boa parte dos jogos a CPU vai limitar o FPS, principalmente em 1080p.",
            > 1.3 => "GPU um pouco à frente da CPU: jogos competitivos em 1080p tendem a ficar limitados pela CPU; em 1440p/4K a GPU volta a ser o limite.",
            >= 0.7 => "Hardware equilibrado: na maioria dos jogos a GPU será o limite, que é o cenário ideal.",
            > 0.4 => "CPU mais forte que a GPU: a placa de vídeo é o limite. Upgrade de GPU traria o maior ganho.",
            _ => "GPU muito fraca para esta CPU: a placa de vídeo limita quase tudo."
        };
        return (ratio, text);
    }
}

/// <summary>Accumulates samples during a session and produces the distribution (in memory only).</summary>
public sealed class BottleneckSession(TimeSpan duration, Func<DateTime>? clock = null)
{
    private readonly Func<DateTime> now = clock ?? (() => DateTime.Now);
    private readonly List<(LoadSample Sample, Limiter Limiter)> samples = [];
    private readonly DateTime start = (clock ?? (() => DateTime.Now))();
    public TimeSpan Duration { get; } = duration;
    public int Count => samples.Count;
    public bool Done => now() - start >= Duration;
    public double Progress => Math.Clamp((now() - start).TotalSeconds / Math.Max(1, Duration.TotalSeconds), 0, 1);
    public void Add(LoadSample s) => samples.Add((s, s.Classify()));
    public IReadOnlyList<(Limiter Limiter, double Pct)> Distribution() =>
        samples.GroupBy(x => x.Limiter).Select(g => (g.Key, 100.0 * g.Count() / Math.Max(1, samples.Count))).OrderByDescending(x => x.Item2).ToList();
    public Limiter? Main => samples.Count == 0 ? null : Distribution()[0].Limiter;
    public double? AvgFps { get { var f = samples.Where(x => x.Sample.Fps > 0).Select(x => x.Sample.Fps!.Value).ToList(); return f.Count > 0 ? f.Average() : null; } }
    public double? Avg(Func<LoadSample, double?> sel) { var v = samples.Select(x => sel(x.Sample)).Where(x => x != null).Select(x => x!.Value).ToList(); return v.Count > 0 ? v.Average() : null; }
    public double? Max(Func<LoadSample, double?> sel) { var v = samples.Select(x => sel(x.Sample)).Where(x => x != null).Select(x => x!.Value).ToList(); return v.Count > 0 ? v.Max() : null; }
}
