// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Calibration of this PC (method C), spec-based GPU fallback (method B) and the personal correction factor from measured sessions
// (method D). Methodology, anchors and limits: docs/fps-estimativa.md and docs/fps-estimativa-metodos.md.
using System.Globalization;

namespace Ilmenyx.Core;

/// <summary>Where the GPU index of an estimate came from.</summary>
public enum FpsBasis { Table, Specs, Calibrated }

/// <summary>State of the calibration gate of the FPS page: estimates are provisional until <see cref="Calibrated"/>.</summary>
public enum FpsGate { Uncalibrated, Stale, Running, Calibrated }

/// <summary>Raw result of one calibration run (null = that part did not produce a value; <see cref="Reason"/> says why).</summary>
public sealed record FpsCalibrationMeasurement(double? SceneScore, double? SceneFps, string? Adapter, double? CpuSimFps, string? Reason);

/// <summary>Calibration kept in settings.json: when, for which hardware/driver (<see cref="Fingerprint"/>) and the two measurements.</summary>
public sealed record FpsCalibration(DateTimeOffset At, string Fingerprint, string GpuName, string Driver, string DriverVersion, string CpuName,
    double? SceneScore, string? Adapter, double? CpuSimFps, string? Note = null);

/// <summary>Personal correction from sessions measured on this PC: factor applied to estimates with the same limiter and preset.</summary>
public sealed record PersonalFactor(double Factor, int Sessions, IReadOnlyList<string> Games)
{
    public string Text => Sessions == 1 ? "sua 1 sessão medida" : $"suas {Sessions} sessões medidas";
}

public static class FpsModels
{
    /// <summary>Invariant culture with a decimal comma: pt-BR numbers in texts without depending on ICU culture data.</summary>
    public static readonly CultureInfo DecimalComma = MakeDecimalComma();
    private static CultureInfo MakeDecimalComma()
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.NumberDecimalSeparator = ","; c.NumberFormat.NumberGroupSeparator = ".";
        return CultureInfo.ReadOnly(c);
    }

    // ---- C: calibration with Ilmenyx's own benchmarks ----

    /// <summary>GPU scene: 1920×1080, 15 s, shadows 4096, 40 000 trees (same options as the scenes in docs/evidencias).</summary>
    public const int SceneSeconds = 15, SceneWidth = 1920, SceneHeight = 1080, SceneShadow = 4096, SceneTrees = 40_000;
    /// <summary>CPU: GameSim (30 000 entities) on one thread for 10 s after its 2 s warm-up.</summary>
    public const int SimSeconds = 10, SimTimeoutSeconds = 14;
    /// <summary>The calibration stops when the CPU or GPU reaches this temperature (°C).</summary>
    public const double AbortTempC = 90;

    /// <summary>
    /// GPU index per scene point. Fit index = k × score (slope fixed at 1: the scene is a fixed amount of work, so its FPS is proportional to
    /// GPU throughput) through the three GPUs measured by the team with their table indices: GeForce 940MX 172 → 4,5; Intel HD 620 109 → 2,5;
    /// Radeon 610M 150 → 3 (docs/evidencias, 2026-11-06/07). Geometric mean of index/score = 0,0229. Cross-check: an RTX 4070 (index 100)
    /// would score ≈ 4370, i.e. ≈ 25× the 940MX, in line with their FP32 throughput ratio (29,1 vs 0,95 TFLOPS ≈ 31×).
    /// </summary>
    public const double SceneK = 0.0229;

    /// <summary>
    /// CPU index per GameSim single-thread FPS. One anchor: Intel Core i7-7500U (table index 36) measured 22,09 / 22,06 / 21,33 FPS in three
    /// 10 s runs on 2026-11-08 (median 22,06; Dell Inspiron 7460, on AC). Slope fixed at 1 for the same reason as the scene.
    /// </summary>
    public const double CpuK = 36 / 22.06;

    public static double SceneToGpuIndex(double score) => Math.Round(SceneK * score, 2);
    public static double SimToCpuIndex(double simFps) => Math.Round(CpuK * simFps, 1);

    /// <summary>Hardware + driver identity of a calibration: a change asks for a new one.</summary>
    public static string Fingerprint(string gpuVendorId, string gpuDeviceId, string gpuDriver, string gpuDriverVersion, string cpuName) =>
        string.Join('|', (gpuVendorId + ":" + gpuDeviceId).ToLowerInvariant(), gpuDriver, gpuDriverVersion, GameCatalog.ShortCpuName(cpuName));

    /// <summary>Never calibrated, calibrated for other hardware/driver (stale) or calibrated for this exact fingerprint.</summary>
    public static FpsGate GateFor(FpsCalibration? calibration, string fingerprint) =>
        calibration is null ? FpsGate.Uncalibrated : calibration.Fingerprint == fingerprint ? FpsGate.Calibrated : FpsGate.Stale;

    /// <summary>GPU and CPU indices from a calibration (null where that part was not measured).</summary>
    public static (double? Gpu, double? Cpu) Indices(FpsCalibration c) =>
        (c.SceneScore is > 0 and double s ? SceneToGpuIndex(s) : null, c.CpuSimFps is > 0 and double f ? SimToCpuIndex(f) : null);

    // ---- B: spec-based GPU index ----

    /// <summary>
    /// index = k_arch × TFLOPS^β, TFLOPS = shaders × 2 × clock. β and k fitted (least squares in log space, common β, one k per
    /// architecture) to the 29 GPUs that are both in <see cref="GpuSpecDb"/> and in the index table: β = 0,703, error ≤ 9 % on those.
    /// Maxwell and RDNA 2 have one (low-end) point each, so their k comes from that point alone.
    /// </summary>
    public const double SpecBeta = 0.703;
    private static readonly Dictionary<string, double> ArchK = new(StringComparer.Ordinal)
    {
        ["Blackwell"] = 10.385, ["Ada Lovelace"] = 9.63, ["Ampere"] = 10.362, ["RDNA 4"] = 17.611, ["RDNA 3"] = 14.235, ["RDNA 2"] = 4.983, ["Maxwell"] = 4.652,
    };

    public static double Tflops(int shaders, int mhz) => shaders * 2.0 * mhz / 1e6;

    /// <summary>Spec index of a GPU; the driver's maximum clock replaces the boost clock only when lower (laptops that clock down).</summary>
    public static double? SpecGpuIndex(GpuSpec spec, int? driverMaxMhz = null)
    {
        if (!ArchK.TryGetValue(spec.Arch, out double k) || spec.Shaders <= 0) return null;
        int mhz = driverMaxMhz is > 0 and int d && d < spec.BoostMHz ? d : spec.BoostMHz;
        if (mhz <= 0) return null;
        return Math.Round(k * Math.Pow(Tflops(spec.Shaders, mhz), SpecBeta), 2);
    }

    // ---- D: personal correction factor ----

    public const double MinFactor = 0.5, MaxFactor = 2.0;
    /// <summary>Shrinkage toward 1: the factor uses n/(n+Shrink) of the measured log-ratio, so one session moves the estimate only a third.</summary>
    public const double Shrink = 2;
    /// <summary>With this many matching sessions the confidence goes up one level.</summary>
    public const int ConfidentSessions = 3;

    /// <summary>
    /// Personal factor for estimates limited by <paramref name="limiter"/> at <paramref name="preset"/>: for each catalog game, the newest
    /// session with that preset and a known resolution (and the same GPU, when the session names one) is compared with the estimate at the
    /// session's own resolution/preset/upscaling; sessions whose estimate has another limiter (or the engine cap) are skipped.
    /// factor = exp(median(ln(measured/estimated)) × n/(n+2)), limited to 0,5…2. Null when no session qualifies.
    /// </summary>
    public static PersonalFactor? Personal(IEnumerable<FpsSession> sessions, IEnumerable<GameInfo> games, double gpuIdx, double cpuIdx, string preset, Bottleneck limiter, string? currentGpu = null)
    {
        if (limiter is not (Bottleneck.Gpu or Bottleneck.Cpu)) return null;
        var list = sessions.Where(s => s.IsValid && s.Preset == preset && s.Resolution is { Length: > 0 } && SameGpu(s.Gpu, currentGpu)).ToList();
        var ratios = new List<double>(); var names = new List<string>();
        foreach (var g in games)
        {
            if (list.Where(s => FpsAccuracy.SameGame(g.Name, s.Game)).OrderByDescending(s => s.At).FirstOrDefault() is not { } s) continue;
            var e = GameCatalog.Estimate(g, gpuIdx, cpuIdx, s.Resolution!, s.Preset!, s.Upscaling ?? "Native");
            if (e.Limiter != limiter || e.Avg <= 0) continue;
            ratios.Add(Math.Log(s.AvgFps / e.Avg)); names.Add(g.Name);
        }
        if (ratios.Count == 0) return null;
        ratios.Sort();
        int n = ratios.Count;
        double median = n % 2 == 1 ? ratios[n / 2] : (ratios[n / 2 - 1] + ratios[n / 2]) / 2;
        double f = Math.Clamp(Math.Exp(median * n / (n + Shrink)), MinFactor, MaxFactor);
        return new(Math.Round(f, 3), n, names);
    }

    /// <summary>Sessions recorded on another GPU do not count (only when both names resolve to different table entries or names).</summary>
    public static bool SameGpu(string? sessionGpu, string? currentGpu)
    {
        if (string.IsNullOrWhiteSpace(sessionGpu) || string.IsNullOrWhiteSpace(currentGpu)) return true;
        string? a = GameCatalog.GpuMatch(sessionGpu)?.Key, b = GameCatalog.GpuMatch(currentGpu)?.Key;
        if (a is not null && b is not null) return a == b;
        return FpsAccuracy.NormalizeName(FpsAccuracy.DisplayGpuName(sessionGpu)) == FpsAccuracy.NormalizeName(FpsAccuracy.DisplayGpuName(currentGpu));
    }

    // ---- confidence by basis ----

    /// <summary>
    /// Confidence after calibration or from specs. Calibrated GPU (scene on this PC): low, ±25 % + distance, because it already includes this
    /// machine's power limits and RAM but is one synthetic scene. Specs: very low, ±45 %. Table: <see cref="FpsAccuracy.Assess"/>.
    /// A CPU index measured by GameSim drops the "laptop CPU" reason (the measurement already includes it).
    /// </summary>
    public static FpsAssessment Assess(FpsBasis basis, HardwareMatch tableMatch, double gpuIdx, bool laptopCpu, bool cpuMeasured, double cpuIdx)
    {
        if (basis == FpsBasis.Table && !cpuMeasured) return FpsAccuracy.Assess(tableMatch, gpuIdx, laptopCpu, cpuIdx);
        var reasons = new List<string>();
        double gpuDist = Math.Abs(Math.Log2(Math.Max(gpuIdx, 0.1) / 100)), cpuDist = Math.Abs(Math.Log2(Math.Max(cpuIdx, 0.1) / 100));
        FpsConfidence level; double spread;
        switch (basis)
        {
            case FpsBasis.Calibrated:
                level = FpsConfidence.Low; spread = FpsAccuracy.LowSpread;
                reasons.Add("GPU medida neste PC pela cena de teste do Ilmenyx (uma cena sintética: cada jogo reage diferente)");
                break;
            case FpsBasis.Specs:
                level = FpsConfidence.VeryLow; spread = 0.45;
                reasons.Add("GPU fora da tabela: força calculada pelas especificações (shaders × clock), sem medição");
                break;
            default:
                var t = FpsAccuracy.Assess(tableMatch, gpuIdx, false, cpuIdx);
                level = t.Confidence; spread = t.Spread - 0.03 * cpuDist; reasons.AddRange(t.Reasons.Where(r => !r.StartsWith("GPU e CPU de desktop", StringComparison.Ordinal)));
                break;
        }
        if (cpuMeasured) reasons.Add("CPU medida neste PC (simulação de jogo em uma thread)");
        else if (laptopCpu) { reasons.Add("CPU de notebook: o clock sustentado depende de refrigeração e do limite de energia"); if (level == FpsConfidence.Medium) level = FpsConfidence.Low; spread = Math.Max(spread, FpsAccuracy.LowSpread); }
        if (basis != FpsBasis.Table)
        {
            spread += 0.04 * gpuDist;
            if (gpuDist > FpsAccuracy.FarFromReference && level != FpsConfidence.VeryLow)
            {
                level = level == FpsConfidence.Medium ? FpsConfidence.Low : FpsConfidence.VeryLow;
                reasons.Add("muito longe da GPU de referência (RTX 4070): a escala por índice erra mais");
            }
        }
        spread += 0.03 * cpuDist;
        return new(level, Math.Min(spread, FpsAccuracy.MaxSpread), reasons);
    }

    /// <summary>Applies a personal factor to an assessment: ≥ 3 sessions raise the confidence one level and narrow the range by 20 %.</summary>
    public static FpsAssessment WithPersonal(FpsAssessment a, PersonalFactor p)
    {
        var reasons = a.Reasons.Append($"ajustada por {p.Text} com a mesma qualidade gráfica (fator {p.Factor.ToString("0.00", DecimalComma)})").ToList();
        if (p.Sessions < ConfidentSessions) return a with { Reasons = reasons };
        var level = a.Confidence switch { FpsConfidence.VeryLow => FpsConfidence.Low, _ => FpsConfidence.Medium };
        return new(level, Math.Max(0.1, a.Spread * 0.8), reasons);
    }

    public static string BasisText(FpsBasis basis) => basis switch
    {
        FpsBasis.Calibrated => "benchmark deste PC",
        FpsBasis.Specs => "especificações",
        _ => "tabela de referência",
    };
}
