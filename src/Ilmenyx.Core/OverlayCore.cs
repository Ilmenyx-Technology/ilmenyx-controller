// Adapted from RESET EXPLODE OverlayMetrics.cs / MainWindow.Overlay.cs (MIT, Copyright (c) 2026 Gustavo (GustavoReset)); see NOTICE.md.
using System.Globalization;
using System.Text;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core;

/// <summary>Item that can appear in the overlay. Group: FPS, GPU, VRAM, CPU, RAM or Outros.</summary>
public sealed record OverlayMetric(string Key, string Group, string Label, bool Default);
public sealed record OverlayLine(string Label, string Value);

public static class OverlayMetrics
{
    public static readonly OverlayMetric[] All =
    [
        new("fps", "FPS", "FPS", true), new("frametime", "FPS", "Frametime (ms)", true), new("low1", "FPS", "1% low", true), new("graph", "FPS", "Gráfico de frametime", true),
        new("gpu_load", "GPU", "Uso", true), new("gpu_temp", "GPU", "Temperatura", true), new("gpu_hotspot", "GPU", "Hotspot", false), new("gpu_clock", "GPU", "Clock do núcleo", true),
        new("gpu_memclock", "GPU", "Clock da memória", false), new("gpu_power", "GPU", "Consumo", true), new("gpu_fan", "GPU", "Ventoinha", false),
        new("vram_used", "VRAM", "Usada (GB)", true), new("vram_pct", "VRAM", "Uso (%)", false),
        new("cpu_load", "CPU", "Uso total", true), new("cpu_maxcore", "CPU", "Núcleo mais ocupado", false), new("cpu_temp", "CPU", "Temperatura", true), new("cpu_clock", "CPU", "Clock médio", true), new("cpu_power", "CPU", "Consumo", true),
        new("ram_used", "RAM", "Usada (GB)", true), new("ram_pct", "RAM", "Uso (%)", false),
        new("game", "Outros", "Nome do jogo", true), new("clock", "Outros", "Hora", false),
    ];

    public static HashSet<string> Defaults() => All.Where(m => m.Default).Select(m => m.Key).ToHashSet();
    public static HashSet<string> Minimal() => ["fps", "frametime", "low1"];
    public static HashSet<string> Everything() => All.Select(m => m.Key).ToHashSet();
    /// <summary>Keeps only known keys, in catalog order (a settings file may be edited by hand).</summary>
    public static List<string> Sanitize(IEnumerable<string>? keys) => keys is null ? Defaults().ToList() : All.Select(m => m.Key).Where(k => keys.Contains(k)).ToList();
}

/// <summary>Frame statistics from a frame source (MangoHud log). Null fields mean unavailable, never zero.</summary>
public sealed record FrameStats(double? Fps, double? FrametimeMs, double? Low1Fps, IReadOnlyList<double> RecentFrametimesMs, DateTimeOffset? LastWrite);

public static class OverlayLines
{
    /// <summary>Builds GPU / VRAM / CPU / RAM / FPS lines with only the chosen items; absent readings show an em dash, never a guessed value.</summary>
    public static List<OverlayLine> Build(ISet<string> show, UsageSample? usage, GpuTelemetry? gpu, FrameStats? frames, string? game, DateTimeOffset now)
    {
        var lines = new List<OverlayLine>();
        string P(double? v) => v is double x ? x.ToString("0", CultureInfo.InvariantCulture) + "%" : "—";
        string T(double? v) => v is double x ? x.ToString("0", CultureInfo.InvariantCulture) + " °C" : "—";
        void Line(string label, List<string> parts) { if (parts.Count > 0) lines.Add(new(label, string.Join("  ", parts))); }

        var fps = new List<string>();
        if (show.Contains("fps")) fps.Add(frames?.Fps is double f ? f.ToString("0", CultureInfo.InvariantCulture) + " FPS" : "— FPS");
        if (show.Contains("frametime")) fps.Add(frames?.FrametimeMs is double ms ? ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms" : "— ms");
        if (show.Contains("low1")) fps.Add(frames?.Low1Fps is double l ? "1% " + l.ToString("0", CultureInfo.InvariantCulture) : "1% —");
        Line("FPS", fps);

        var g = new List<string>();
        if (show.Contains("gpu_load")) g.Add(P(gpu?.LoadPercent));
        if (show.Contains("gpu_temp")) g.Add(T(gpu?.TempC));
        if (show.Contains("gpu_hotspot")) g.Add("HS " + T(gpu?.TempHotspotC));
        if (show.Contains("gpu_clock")) g.Add(gpu?.CoreMHz is double c ? c.ToString("0", CultureInfo.InvariantCulture) + " MHz" : "— MHz");
        if (show.Contains("gpu_memclock")) g.Add(gpu?.MemMHz is double m ? "mem " + m.ToString("0", CultureInfo.InvariantCulture) + " MHz" : "mem —");
        if (show.Contains("gpu_power")) g.Add(gpu?.PowerW is double w ? w.ToString("0", CultureInfo.InvariantCulture) + " W" : "— W");
        if (show.Contains("gpu_fan")) g.Add(gpu?.FanRpm is double rpm && rpm > 0 ? rpm.ToString("0", CultureInfo.InvariantCulture) + " RPM" : gpu?.FanPercent is double fp ? "fan " + fp.ToString("0", CultureInfo.InvariantCulture) + "%" : "fan —");
        Line("GPU", g);

        var vram = new List<string>();
        if (show.Contains("vram_used")) vram.Add(gpu?.VramUsed is ulong u ? (u / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB" : "—");
        if (show.Contains("vram_pct")) vram.Add(gpu is { VramUsed: ulong used, VramTotal: ulong total and > 0 } ? P(100.0 * used / total) : "—");
        Line("VRAM", vram);

        var cpu = new List<string>();
        if (show.Contains("cpu_load")) cpu.Add(P(usage?.CpuPercent));
        if (show.Contains("cpu_maxcore")) cpu.Add(usage?.CpuMaxCorePercent is double mx ? "núcleo " + mx.ToString("0", CultureInfo.InvariantCulture) + "%" : "núcleo —");
        if (show.Contains("cpu_temp")) cpu.Add(T(usage?.CpuTempC));
        if (show.Contains("cpu_clock")) cpu.Add(usage?.CpuMhz is double mhz ? (mhz / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " GHz" : "— GHz");
        if (show.Contains("cpu_power")) cpu.Add("— W"); // RAPL needs privileges on current kernels; never guessed
        Line("CPU", cpu);

        var ram = new List<string>();
        if (show.Contains("ram_used")) ram.Add(usage is { MemoryUsed: long used2 } ? (used2 / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB" : "—");
        if (show.Contains("ram_pct")) ram.Add(usage is { MemoryTotal: long tot and > 0, MemoryUsed: long u2 } ? P(100.0 * u2 / tot) : "—");
        Line("RAM", ram);

        var other = new List<string>();
        if (show.Contains("game") && !string.IsNullOrWhiteSpace(game)) other.Add(game!);
        if (show.Contains("clock")) other.Add(now.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture));
        Line("", other);
        return lines;
    }
}

/// <summary>MangoHud configuration generated from the same item selection. Written to Ilmenyx's own file, never to the user's MangoHud.conf.</summary>
public static class MangoHudConfig
{
    public static readonly string[] Corners = ["top-left", "top-right", "bottom-left", "bottom-right"];
    public const double MinScale = 0.6, MaxScale = 2.0;

    public static string Build(ISet<string> show, string corner, double scale, string? logFolder)
    {
        if (!Corners.Contains(corner)) throw new ArgumentException("Canto inválido.", nameof(corner));
        if (scale is < MinScale or > MaxScale) throw new ArgumentOutOfRangeException(nameof(scale));
        var sb = new StringBuilder("# Gerado pelo Ilmenyx (by Bismuth Technology). Não é o seu MangoHud.conf; regenerado pela página Overlay.\n");
        void Flag(bool on, string key) { if (on) sb.Append(key).Append('\n'); }
        sb.Append("position=").Append(corner).Append('\n');
        sb.Append("font_scale=").Append(scale.ToString("0.0#", CultureInfo.InvariantCulture)).Append('\n');
        Flag(show.Contains("fps"), "fps"); Flag(show.Contains("frametime"), "frametime"); Flag(show.Contains("low1"), "fps_metrics=avg,0.01");
        Flag(show.Contains("graph"), "frame_timing");
        if (show.Overlaps(["gpu_load", "gpu_temp", "gpu_clock", "gpu_memclock", "gpu_power", "gpu_fan", "gpu_hotspot"]))
        {
            Flag(true, "gpu_stats"); Flag(show.Contains("gpu_temp"), "gpu_temp"); Flag(show.Contains("gpu_hotspot"), "gpu_junction_temp");
            Flag(show.Contains("gpu_clock"), "gpu_core_clock"); Flag(show.Contains("gpu_memclock"), "gpu_mem_clock"); Flag(show.Contains("gpu_power"), "gpu_power"); Flag(show.Contains("gpu_fan"), "gpu_fan");
        }
        Flag(show.Contains("vram_used") || show.Contains("vram_pct"), "vram");
        if (show.Overlaps(["cpu_load", "cpu_maxcore", "cpu_temp", "cpu_clock", "cpu_power"]))
        {
            Flag(true, "cpu_stats"); Flag(show.Contains("cpu_temp"), "cpu_temp"); Flag(show.Contains("cpu_clock"), "cpu_mhz"); Flag(show.Contains("cpu_power"), "cpu_power");
        }
        Flag(show.Contains("ram_used") || show.Contains("ram_pct"), "ram"); Flag(show.Contains("clock"), "time");
        Flag(!show.Contains("game"), "hide_engine_names"); // keeps the layout compact; the game name is shown by the Ilmenyx overlay
        if (logFolder is not null)
        {
            if (!Path.IsPathRooted(logFolder) || logFolder.Contains('\n') || logFolder.Contains('\r')) throw new ArgumentException("Pasta de log inválida.", nameof(logFolder));
            sb.Append("output_folder=").Append(logFolder).Append('\n').Append("log_interval=100\nautostart_log=1\nlog_duration=0\n");
        }
        return sb.ToString();
    }

    public static string LaunchOption(string configPath) => $"MANGOHUD_CONFIGFILE={configPath} mangohud %command%";
}

/// <summary>Reads FPS statistics from the CSV that MangoHud writes (autostart_log). Tolerates the two header lines MangoHud emits and truncated last lines.</summary>
public static class MangoHudLog
{
    public const int MaxRows = 200_000, WindowSeconds = 5;

    public static FrameStats? Parse(string text, DateTimeOffset lastWrite)
    {
        var rows = new List<(double Fps, double FrameMs, double ElapsedSeconds)>();
        int fpsCol = -1, frameCol = -1, elapsedCol = -1; bool header = false; int count = 0;
        foreach (string raw in text.Split('\n'))
        {
            if (++count > MaxRows + 8) break;
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var f = line.Split(',');
            if (!header)
            {
                if (f[0] == "fps") { fpsCol = Array.IndexOf(f, "fps"); frameCol = Array.IndexOf(f, "frametime"); elapsedCol = Array.IndexOf(f, "elapsed"); header = true; }
                continue;
            }
            if (f.Length <= Math.Max(fpsCol, Math.Max(frameCol, elapsedCol)) || elapsedCol < 0 || frameCol < 0) continue;
            if (double.TryParse(f[fpsCol], NumberStyles.Float, CultureInfo.InvariantCulture, out double fps) && double.TryParse(f[frameCol], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms)
                && double.TryParse(f[elapsedCol], NumberStyles.Float, CultureInfo.InvariantCulture, out double ns) && fps >= 0 && ms > 0 && ms < 10000)
                rows.Add((fps, ms, ns / 1e9));
        }
        if (rows.Count == 0) return null;
        double end = rows[^1].ElapsedSeconds;
        var window = rows.Where(r => end - r.ElapsedSeconds <= WindowSeconds).ToList();
        var frametimes = window.Select(r => r.FrameMs).OrderByDescending(x => x).ToList();
        double worst = frametimes.Take(Math.Max(1, frametimes.Count / 100)).Average();
        double avgFrame = window.Average(r => r.FrameMs);
        return new FrameStats(avgFrame > 0 ? 1000.0 / avgFrame : null, window[^1].FrameMs, worst > 0 ? 1000.0 / worst : null, window.TakeLast(120).Select(r => r.FrameMs).ToList(), lastWrite);
    }
}
