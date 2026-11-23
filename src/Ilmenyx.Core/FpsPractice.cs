// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

/// <summary>FPS of one session from the practice method: typical FPS (median of 1 s windows), P10-P90 range, 1% low, valid seconds, loading stutters left out.</summary>
public sealed record PlayStats(double TypicalFps, double P10Fps, double P90Fps, double Low1Fps, double ValidSeconds, int Stalls, string? Gpu, double? CappedAt = null);

/// <summary>
/// FPS from play, not from a simple mean (docs/fps-medido.md): launcher, menus and loading screens are in the log too. The first
/// <see cref="SkipStart"/> s and the last <see cref="SkipEnd"/> s are dropped, frametimes above <see cref="StallMs"/> are counted apart and kept out of the
/// average, and the rest is grouped in 1 s windows (frames / time); the session value is the median of the windows, so a few seconds of menu at
/// 500 FPS do not move it.
/// </summary>
public static class MangoHudPlay
{
    public const double SkipStart = 20, SkipEnd = 3, StallMs = 250, MinValidSeconds = 60;

    public static PlayStats? Summarize(string text)
    {
        var (rows, gpu) = MangoHudSession.ParseRows(text);
        if (rows.Count == 0) return null;
        double end = rows[^1].T - SkipEnd, prev = 0;
        var win = new SortedDictionary<int, (double Frames, double Dt)>(); int stalls = 0; bool inStall = false;
        foreach (var (fps, frame, t) in rows)
        {
            double dt = Math.Min(t - prev, 2); prev = t;
            if (t < SkipStart || t > end || dt <= 0) continue;
            if (frame > StallMs) { if (!inStall) stalls++; inStall = true; continue; }
            inStall = false;
            int k = (int)t; var w = win.GetValueOrDefault(k);
            win[k] = (w.Frames + fps * dt, w.Dt + dt);
        }
        double valid = win.Values.Sum(w => w.Dt);
        if (valid < MinValidSeconds) return null;
        var perWindow = win.Values.Where(w => w.Dt >= 0.5).Select(w => w.Frames / w.Dt).Order().ToList();
        if (perWindow.Count == 0) return null;
        double median = Percentile(perWindow, 50);
        // 1% low = the worst 1% of the 1-second windows: MangoHud logs one frame per 100 ms sample, so a lone hitch frame (a room transition
        // nobody feels) used to drag the "worst 1% of frames" down to single digits.
        return new(median, Percentile(perWindow, 10), Percentile(perWindow, 90), Percentile(perWindow, 1), valid, stalls, gpu, MangoHudSession.Cap(median, perWindow));
    }

    /// <summary>Percentile by linear interpolation of an ascending list.</summary>
    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        double x = (sorted.Count - 1) * p / 100, lo = Math.Floor(x); int i = (int)lo;
        return i + 1 < sorted.Count ? sorted[i] + (sorted[i + 1] - sorted[i]) * (x - lo) : sorted[i];
    }

    /// <summary>Session to keep, or null (nothing usable, or fewer than <see cref="MinValidSeconds"/> of play).</summary>
    public static FpsSession? ToSession(PlayStats? s, string game, DateTimeOffset at, string? resolution, string? preset = null, string? upscaling = null, string? qualityNote = null)
    {
        string name = game.Trim();
        if (s is null || name.Length == 0 || s.ValidSeconds < MinValidSeconds || !double.IsFinite(s.TypicalFps) || s.TypicalFps <= 0) return null;
        return new(name.Length > 120 ? name[..120] : name, at, Math.Round(s.TypicalFps, 1), double.IsFinite(s.Low1Fps) ? Math.Round(s.Low1Fps, 1) : 0, Math.Round(s.ValidSeconds), s.Gpu,
            resolution is { } r && r != "default" ? r : null, preset is "Baixo" or "Médio" or "Alto" or "Ultra" ? preset : null,
            upscaling is "Native" or "Quality" or "Performance" ? upscaling : null, s.CappedAt, Math.Round(s.P10Fps, 1), Math.Round(s.P90Fps, 1), Math.Round(s.ValidSeconds), s.Stalls,
            qualityNote is { Length: > 0 and <= 40 } ? qualityNote : null);
    }

    /// <summary>Process name in a MangoHud log file name (the part before _YYYY-MM-DD_), or null.</summary>
    public static string? LogPrefix(string fileName)
    {
        var m = Regex.Match(Path.GetFileName(fileName), @"^(.+?)_\d{4}-\d{2}-\d{2}_");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string Norm(string s)
    {
        string n = new(s.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        return n.EndsWith("exe", StringComparison.Ordinal) && n.Length > 3 ? n[..^3] : n;
    }

    /// <summary>The log belongs to the game's process: equal after normalising (case, punctuation, ".exe"), or one starts with the other (the kernel cuts process names at 15 characters).</summary>
    public static bool LogMatches(string fileName, string? processName)
    {
        if (LogPrefix(fileName) is not { } p || processName is not { Length: > 0 }) return false;
        string a = Norm(p), b = Norm(processName);
        return a.Length >= 4 && b.Length >= 4 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal));
    }
}

/// <summary>What the library shows for one game and one resolution + quality: the last sessions of the group, combined.</summary>
public sealed record FpsGroup(string? Resolution, string? Quality, double TypicalFps, double P10Fps, double P90Fps, double Low1Fps, int Sessions, double Hours, DateTimeOffset Last, double? CappedAt);

public static class FpsHistory
{
    public const int PerGroup = 10;

    /// <summary>
    /// Median of the values weighted by time: the value at which half of the play time is reached. When the time below the crossing value is
    /// already 40–50 % of the total (two sessions of similar length, e.g. 63 and 102 FPS), the median is ambiguous, so the two neighbours are
    /// averaged by their time (~83) instead of picking the larger one.
    /// </summary>
    public static double WeightedMedian(IEnumerable<(double Value, double Weight)> items)
    {
        var l = items.Where(i => i.Weight > 0).OrderBy(i => i.Value).ToList();
        if (l.Count == 0) return double.NaN;
        double total = l.Sum(i => i.Weight), acc = 0;
        for (int i = 0; i < l.Count; i++)
        {
            double before = acc; acc += l[i].Weight;
            if (acc < total / 2) continue;
            if (i > 0 && before >= 0.4 * total)
                return (l[i - 1].Value * l[i - 1].Weight + l[i].Value * l[i].Weight) / (l[i - 1].Weight + l[i].Weight);
            return l[i].Value;
        }
        return l[^1].Value;
    }

    /// <summary>
    /// Groups of one game (by resolution and quality), newest group first. Only practice sessions count (old simple-average ones have no
    /// valid seconds); only the current GPU (default: the one of the newest session); at most <see cref="PerGroup"/> sessions per group.
    /// </summary>
    public static List<FpsGroup> Build(IEnumerable<FpsSession> sessions, string game, string? currentGpu = null, IReadOnlyCollection<string>? sameGame = null)
    {
        // sameGame: names of library entries joined with this one (same program), whose sessions count too.
        bool named(string? n) => string.Equals(n, game, StringComparison.OrdinalIgnoreCase) || sameGame?.Contains(n ?? "", StringComparer.OrdinalIgnoreCase) == true;
        var mine = sessions.Where(s => s is { ValidSeconds: >= MangoHudPlay.MinValidSeconds, P10Fps: not null, P90Fps: not null } && named(s.Game))
            .OrderByDescending(s => s.At).ToList();
        if (mine.Count == 0) return [];
        string? gpu = currentGpu ?? mine[0].Gpu;
        return mine.Where(s => string.Equals(s.Gpu, gpu, StringComparison.OrdinalIgnoreCase)).GroupBy(s => (s.Resolution, Q: s.Preset ?? s.QualityNote)).Select(g =>
        {
            var l = g.Take(PerGroup).ToList();
            double W(FpsSession s) => s.ValidSeconds!.Value;
            return new FpsGroup(g.Key.Resolution, g.Key.Q, WeightedMedian(l.Select(s => (s.AvgFps, W(s)))), WeightedMedian(l.Select(s => (s.P10Fps!.Value, W(s)))),
                WeightedMedian(l.Select(s => (s.P90Fps!.Value, W(s)))), WeightedMedian(l.Select(s => (s.Low1Fps, W(s)))), l.Count, l.Sum(W) / 3600,
                l[0].At, l.All(s => s.CappedAt is not null) ? l.Max(s => s.CappedAt) : null);
        }).OrderByDescending(g => g.Last).ToList();
    }
}
