// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

public static class FpsAccuracy
{
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
        ["thewitcher3wildhuntcompleteedition"] = L.T("The Witcher 3: Wild Hunt"), ["witcher3wildhunt"] = L.T("The Witcher 3: Wild Hunt"),
        ["pubg"] = L.T("PUBG: Battlegrounds"), ["cs2"] = L.T("Counter-Strike 2"), ["easpfc25"] = "EA Sports FC 25",
        ["helldiversii"] = L.T("Helldivers 2"), ["godofwarragnarok"] = L.T("God of War Ragnarök"),
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

/// <summary>Whole-session statistics from a MangoHud CSV. Gpu = the GPU name MangoHud wrote in the log header, when present.
/// CappedAt = the FPS limit the session sat on (see <see cref="MangoHudSession.Cap"/>).</summary>
public sealed record MangoHudSessionStats(double AvgFps, double Low1Fps, double DurationSeconds, int Samples, string? Gpu, double? CappedAt = null);

public static class MangoHudSession
{
    /// <summary>Usual FPS limits: screen refresh rates (vsync) and the caps games offer.</summary>
    public static readonly double[] CommonCaps = [30, 40, 45, 48, 50, 60, 72, 75, 90, 100, 120, 144, 160, 165, 170, 180, 200, 240, 280, 300, 360];

    /// <summary>
    /// The limit a session sat on, or null: the average is within 3 % below a usual limit and at most 5 % of the samples go more than 3 %
    /// above it. A GPU- or CPU-bound game varies with the scene and crosses such values; a capped one stays under them (vsync, frame limiter).
    /// </summary>
    public static double? Cap(double avg, IReadOnlyList<double> fps)
    {
        if (fps.Count < 20 || !double.IsFinite(avg) || avg <= 0) return null;
        foreach (double c in CommonCaps)
            if (avg >= c * 0.97 && avg <= c * 1.01 && fps.Count(v => v > c * 1.03) <= fps.Count * 0.05) return c;
        return null;
    }

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
        double avg = fps.Average();
        return new(avg, 1000.0 / worst, Math.Max(0, last - first), fps.Count, gpu, Cap(avg, fps));
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
            preset is "Baixo" or "Médio" or "Alto" or "Ultra" ? preset : null, upscaling is "Native" or "Quality" or "Performance" ? upscaling : null, stats.CappedAt);
    }
}
