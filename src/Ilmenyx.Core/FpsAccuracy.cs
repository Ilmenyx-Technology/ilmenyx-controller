// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Methodology and limits: docs/fps-estimativa.md.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

/// <summary>How specific the catalog match for a GPU or CPU was.</summary>
public enum HardwareMatch { Desktop, Laptop, Integrated, Generic }

public enum FpsConfidence { Medium, Low, VeryLow }

/// <summary>Confidence of the local estimate for this PC, the relative spread of the range and the short reasons shown to the user.</summary>
public sealed record FpsAssessment(FpsConfidence Confidence, double Spread, IReadOnlyList<string> Reasons)
{
    public string ConfidenceText => Confidence switch { FpsConfidence.Medium => "confiança média", FpsConfidence.Low => "confiança baixa", _ => "confiança muito baixa" };
}

/// <summary>Estimated FPS range around the central estimate. Never a measurement.</summary>
public sealed record FpsRange(double Low, double High, double Center)
{
    public string Text => Low == High ? Fmt(Low) : Fmt(Low) + "–" + Fmt(High);
    private static string Fmt(double v) => v.ToString("0", CultureInfo.InvariantCulture);
}

public static class FpsAccuracy
{
    /// <summary>Relative spread with GPU and CPU both desktop entries at the reference (RTX 4070 / Ryzen 7 7700X).</summary>
    public const double BaseSpread = 0.15, LowSpread = 0.25, MaxSpread = 0.5;
    /// <summary>More than this many halvings/doublings away from the reference GPU lowers the confidence one step.</summary>
    public const double FarFromReference = 3.5;

    private static readonly Regex MobileChip = new(@"\b(G[KMP]|TU|GA|AD|GB)\d{3}M\b", RegexOptions.CultureInvariant);
    private static readonly Regex MobileSuffix = new(@"\b(Laptop|Mobile|Max-Q|Max-P)\b|\bRX \d{4}[MS]\b|\b\d{3,4}MX?\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex LaptopCpu = new(@"\d{3,5}(U|H|HS|HX|HQ|HK|HL|P|Y|G\d)\b|Custom APU|Core Ultra \d \d{3}[UHV]\b", RegexOptions.CultureInvariant);

    /// <summary>Kind of match for a GPU: generic integrated names (several different chips share them), integrated, laptop or desktop.</summary>
    public static HardwareMatch ClassifyGpu(string gpuName, string key, bool integrated = false)
    {
        if (key is "Radeon(TM) Graphics" or "Radeon Graphics" or "UHD Graphics") return HardwareMatch.Generic;
        if (integrated || key.Contains("HD Graphics", StringComparison.Ordinal) || key.StartsWith("Iris", StringComparison.Ordinal) || key.StartsWith("Vega", StringComparison.Ordinal)
            || key is "Mendocino" or "Steam Deck" or "VanGogh" or "Van Gogh" || Regex.IsMatch(key, @"^Radeon \d{3}M$", RegexOptions.CultureInvariant)) return HardwareMatch.Integrated;
        if (key.StartsWith("MX", StringComparison.Ordinal) || MobileSuffix.IsMatch(key) || MobileSuffix.IsMatch(gpuName) || MobileChip.IsMatch(gpuName)) return HardwareMatch.Laptop;
        return HardwareMatch.Desktop;
    }

    /// <summary>Readable GPU name: the marketing name inside pci.ids brackets ("GM108M [GeForce 940MX]" → "940MX"), else the short name.</summary>
    public static string DisplayGpuName(string gpuName)
    {
        int a = gpuName.IndexOf('['), b = gpuName.LastIndexOf(']');
        string s = a >= 0 && b > a + 1 ? gpuName[(a + 1)..b] : gpuName;
        string shortName = GameCatalog.ShortGpuName(s);
        return shortName.Length > 0 ? shortName : gpuName.Trim();
    }

    public static bool IsLaptopCpu(string cpuName) => LaptopCpu.IsMatch(GameCatalog.ShortCpuName(cpuName));

    /// <summary>
    /// Confidence and spread from the kind of match and the distance to the reference parts. Desktop GPU + desktop CPU = medium (±15 %);
    /// laptop/integrated GPU, generic GPU name or laptop CPU = low (±25 %); each halving away from the reference widens the range,
    /// and very far from the reference lowers the confidence one more step.
    /// </summary>
    public static FpsAssessment Assess(HardwareMatch gpu, double gpuIdx, bool laptopCpu, double cpuIdx)
    {
        var reasons = new List<string>();
        bool weak = gpu != HardwareMatch.Desktop || laptopCpu;
        double spread = weak ? LowSpread : BaseSpread;
        switch (gpu)
        {
            case HardwareMatch.Laptop: reasons.Add("GPU de notebook: o limite de energia/temperatura do modelo muda o resultado"); break;
            case HardwareMatch.Integrated: reasons.Add("GPU integrada: divide memória e energia com a CPU; RAM em canal único derruba o FPS"); break;
            case HardwareMatch.Generic: reasons.Add("nome genérico da GPU integrada: pode ser chips bem diferentes"); spread += 0.1; break;
        }
        if (laptopCpu) reasons.Add("CPU de notebook: o clock sustentado depende de refrigeração e do limite de energia");
        double gpuDist = Math.Abs(Math.Log2(Math.Max(gpuIdx, 0.1) / 100)), cpuDist = Math.Abs(Math.Log2(Math.Max(cpuIdx, 0.1) / 100));
        spread += 0.04 * gpuDist + 0.03 * cpuDist;
        var level = weak ? FpsConfidence.Low : FpsConfidence.Medium;
        if (gpuDist > FarFromReference)
        {
            level = level == FpsConfidence.Medium ? FpsConfidence.Low : FpsConfidence.VeryLow;
            reasons.Add("muito longe da GPU de referência (RTX 4070): a escala por índice erra mais");
        }
        if (reasons.Count == 0) reasons.Add("GPU e CPU de desktop na tabela; a faixa cobre versões do jogo, drivers e cenas diferentes");
        return new(level, Math.Min(spread, MaxSpread), reasons);
    }

    /// <summary>Range around the central estimate; an engine FPS cap is never exceeded.</summary>
    public static FpsRange Range(FpsEstimate e, GameInfo g, FpsAssessment a)
    {
        double lo = Math.Max(1, Math.Round(e.Avg * (1 - a.Spread))), hi = Math.Max(lo, Math.Round(e.Avg * (1 + a.Spread)));
        if (g.EngineCap > 0 && hi > g.EngineCap) { hi = g.EngineCap; lo = Math.Min(lo, hi); }
        return new(lo, hi, e.Avg);
    }

    /// <summary>Game name for matching: lower-case ASCII letters and digits only (accents, ™, ®, punctuation and spaces dropped).</summary>
    public static string NormalizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name.Normalize(NormalizationForm.FormD))
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // Store names that differ from the catalog spelling (normalized store name → catalog name).
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["grandtheftautov"] = "GTA V", ["grandtheftautovenhanced"] = "GTA V", ["grandtheftautovlegacy"] = "GTA V",
        ["tomclancysrainbowsixsiege"] = "Rainbow Six Siege", ["tomclancysrainbowsixsiegex"] = "Rainbow Six Siege", ["rainbowsixsiegex"] = "Rainbow Six Siege",
        ["thewitcher3wildhuntcompleteedition"] = "The Witcher 3: Wild Hunt", ["witcher3wildhunt"] = "The Witcher 3: Wild Hunt",
        ["pubg"] = "PUBG: Battlegrounds", ["cs2"] = "Counter-Strike 2", ["easpfc25"] = "EA Sports FC 25",
        ["helldiversii"] = "Helldivers 2", ["godofwarragnarok"] = "God of War Ragnarök",
    };

    /// <summary>True when a recorded session name refers to the catalog game (normalized equality or a known store alias).</summary>
    public static bool SameGame(string catalogName, string sessionName)
    {
        string a = NormalizeName(catalogName), b = NormalizeName(sessionName);
        if (a.Length == 0 || b.Length == 0) return false;
        return a == b || Aliases.TryGetValue(b, out var alias) && NormalizeName(alias) == a;
    }

    /// <summary>Newest valid session for the catalog game, if any.</summary>
    public static FpsSession? LatestSession(IEnumerable<FpsSession> sessions, string catalogName) =>
        sessions.Where(s => s.IsValid && SameGame(catalogName, s.Game)).OrderByDescending(s => s.At).FirstOrDefault();
}

/// <summary>Local hardware facts the FPS page uses for alerts. Null = could not be read (no alert is shown from a guess).</summary>
public sealed record FpsContext(string GpuName, string GpuVendorId, string GpuDeviceId, string GpuDriver, bool GpuIntegrated, ulong GpuVramBytes,
    string? OtherIntegratedGpu, bool? OnBattery, int ProfilesWithPrime, int Profiles, string GpuDriverVersion = "", int? GpuMaxCoreMHz = null);

public sealed record FpsAlert(string Title, string Text);

public static class FpsAlerts
{
    public const ulong LowVram = 4UL << 30;

    /// <summary>On battery when a battery is present and mains is off, or a battery reports "Discharging".</summary>
    public static bool? OnBattery(IReadOnlyList<BatteryInput> batteries, bool? mainsOnline)
    {
        if (batteries.Count == 0) return null;
        if (mainsOnline == false || batteries.Any(b => b.Status == "Discharging")) return true;
        return mainsOnline is null && batteries.All(b => b.Status is "" or "Unknown") ? null : false;
    }

    /// <summary>NVIDIA GPU older than Turing (PCI device below 0x1E00: Kepler, Maxwell, Pascal, Volta).</summary>
    public static bool NvidiaPreTuring(string vendorId, string deviceId) =>
        vendorId.Equals("10de", StringComparison.OrdinalIgnoreCase) && int.TryParse(deviceId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int dev) && dev < 0x1E00;

    public static List<FpsAlert> Build(FpsContext c)
    {
        var alerts = new List<FpsAlert>();
        string gpu = FpsAccuracy.DisplayGpuName(c.GpuName);
        bool nvidia = c.GpuVendorId.Equals("10de", StringComparison.OrdinalIgnoreCase);
        if (nvidia && c.GpuDriver == "nouveau")
            alerts.Add(new("Driver nouveau na GeForce", "O nouveau roda bem abaixo do driver NVIDIA (na maioria das GeForce não sobe o clock)" +
                (NvidiaPreTuring(c.GpuVendorId, c.GpuDeviceId) ? " e, nesta geração (anterior à Turing), em geral não oferece Vulkan, que o Proton/DXVK exige." : ".") +
                " A estimativa assume o driver NVIDIA; instale-o pela página Drivers."));
        else if (!c.GpuIntegrated && c.GpuDriver.Length == 0 && c.GpuVendorId.Length > 0)
            alerts.Add(new("GPU sem driver", $"Nenhum driver está carregado para a {gpu}: os jogos não vão usá-la."));
        if (!c.GpuIntegrated && c.GpuVramBytes > 0 && c.GpuVramBytes < LowVram)
            alerts.Add(new($"{(c.GpuVramBytes / 1073741824.0).ToString("0.#", CultureInfo.InvariantCulture)} GB de VRAM", "Texturas no Alto/Ultra passam da memória da placa: travadas e queda de FPS. Use texturas no Médio ou Baixo."));
        if (c.OnBattery == true)
            alerts.Add(new("Notebook na bateria", "Na bateria o sistema limita CPU e GPU; o FPS fica abaixo da estimativa até ligar o carregador."));
        if (!c.GpuIntegrated && c.OtherIntegratedGpu is { Length: > 0 } igpu)
        {
            string i = FpsAccuracy.DisplayGpuName(igpu);
            alerts.Add(new("Notebook com duas GPUs", c.ProfilesWithPrime == 0
                ? $"Nenhum perfil de jogo força a placa dedicada (PRIME offload, página Jogos): sem isso o jogo roda na {i} e o FPS fica muito abaixo desta estimativa, que é para a {gpu}."
                : $"Jogos sem a GPU dedicada (PRIME) ligada no perfil rodam na {i}, bem abaixo desta estimativa ({c.ProfilesWithPrime} de {c.Profiles} perfis com PRIME)."));
        }
        return alerts;
    }
}

/// <summary>Whole-session statistics from a MangoHud CSV. Gpu = the GPU name MangoHud wrote in the log header, when present.</summary>
public sealed record MangoHudSessionStats(double AvgFps, double Low1Fps, double DurationSeconds, int Samples, string? Gpu);

public static class MangoHudSession
{
    /// <summary>
    /// Average FPS (mean of the per-interval "fps" column: the rows are taken at a fixed interval, so this is frames/time) and 1% low
    /// (1000 / mean of the worst 1 % of the logged frametimes, the same method as the live overlay). Null when no valid row exists.
    /// </summary>
    public static MangoHudSessionStats? Summarize(string text)
    {
        int fpsCol = -1, frameCol = -1, elapsedCol = -1, gpuCol = -1, count = 0; bool header = false, specValues = false; string? gpu = null;
        var fps = new List<double>(); var frames = new List<double>(); double first = double.NaN, last = double.NaN;
        foreach (string raw in text.Split('\n'))
        {
            if (++count > MangoHudLog.MaxRows + 8) break;
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var f = line.Split(',');
            if (!header)
            {
                if (f[0] == "os") { gpuCol = Array.IndexOf(f, "gpu"); specValues = true; continue; }
                if (specValues) { specValues = false; if (gpuCol >= 0 && gpuCol < f.Length) gpu = CleanGpu(f[gpuCol]); continue; }
                if (f[0] == "fps") { fpsCol = 0; frameCol = Array.IndexOf(f, "frametime"); elapsedCol = Array.IndexOf(f, "elapsed"); header = true; }
                continue;
            }
            if (frameCol < 0 || elapsedCol < 0 || f.Length <= Math.Max(frameCol, elapsedCol)) continue;
            if (double.TryParse(f[fpsCol], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.TryParse(f[frameCol], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms)
                && double.TryParse(f[elapsedCol], NumberStyles.Float, CultureInfo.InvariantCulture, out double ns) && v > 0 && v < 10000 && ms > 0 && ms < 10000)
            {
                fps.Add(v); frames.Add(ms);
                double s = ns / 1e9; if (double.IsNaN(first)) first = s; last = s;
            }
        }
        if (fps.Count == 0) return null;
        var worst = frames.OrderByDescending(x => x).Take(Math.Max(1, frames.Count / 100)).Average();
        return new(fps.Average(), 1000.0 / worst, Math.Max(0, last - first), fps.Count, gpu);
    }

    private static string? CleanGpu(string value)
    {
        string s = new(value.Where(c => !char.IsControl(c)).ToArray());
        s = s.Trim();
        return s.Length is 0 or > 96 ? null : s;
    }

    /// <summary>Session to keep, or null when it does not count (no samples or shorter than <see cref="FpsSession.MinSeconds"/>).</summary>
    public static FpsSession? ToSession(MangoHudSessionStats? stats, string game, DateTimeOffset at, string? resolution, string? preset = null, string? upscaling = null)
    {
        if (stats is null || stats.Samples == 0 || stats.DurationSeconds < FpsSession.MinSeconds || !double.IsFinite(stats.AvgFps) || stats.AvgFps <= 0) return null;
        string name = game.Trim(); if (name.Length == 0) return null;
        return new(name.Length > 120 ? name[..120] : name, at, Math.Round(stats.AvgFps, 1), double.IsFinite(stats.Low1Fps) ? Math.Round(stats.Low1Fps, 1) : 0,
            Math.Round(stats.DurationSeconds), stats.Gpu, resolution is { } r && r != "default" ? r : null,
            preset is "Baixo" or "Médio" or "Alto" or "Ultra" ? preset : null, upscaling is "Native" or "Quality" or "Performance" ? upscaling : null);
    }
}
