// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Card model adapted from the original GameRow (MIT, see NOTICE; it lived in HowManyFpsClient.cs until the HowManyFPS client was
// removed on 2026-11-08). Ilmenyx additions (estimate range, confidence, sessions measured on this PC): see LICENSING.md.
namespace Ilmenyx.Core;

/// <summary>Card model of the original GameRow, without UI notifications.</summary>
public sealed class GameFpsRow(GameInfo game)
{
    public const string SourceEstimate = "ESTIMATIVA", SourceMeasured = "MEDIDO NESTE PC";
    public GameInfo Game { get; } = game;
    public string Name => Game.Name;
    public string Genre => Game.Genre;
    public double Fps { get; private set; }
    public double Low { get; private set; }
    public string Source { get; private set; } = "";
    public string Detail { get; private set; } = "";
    /// <summary>Local estimate range (null for measured values). The card shows the range, never a single estimated number.</summary>
    public FpsRange? Range { get; private set; }
    public FpsAssessment? Assessment { get; private set; }
    public FpsSession? Measured { get; private set; }
    public bool IsMeasured => Measured is not null;
    public string FpsText => Range is { } r ? r.Text : Fps > 0 ? Fps.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "—";
    public string LowText => Assessment is { } a ? a.ConfidenceText : Low > 0 ? $"1% low {Low.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}" : "";
    /// <summary>Common ranges of FPS calculators: 144+ excellent, 90+ great, 60+ playable. A range is rated by its centre.</summary>
    public string Rating => Fps switch { <= 0 => "", >= 144 => "EXCELENTE", >= 90 => "ÓTIMO", >= 60 => "JOGÁVEL", >= 30 => "FRACO", _ => "INJOGÁVEL" };
    public double Bar => Math.Clamp(Fps / 240.0, 0, 1);
    /// <summary>Longer text for tooltips and screen readers.</summary>
    public string Summary { get; private set; } = "";
    public void Set(double fps, double low, string source, string detail) { Fps = fps; Low = low; Source = source; Detail = detail; Range = null; Assessment = null; Measured = null; Summary = ""; }
    public void Note(string detail) => Detail = detail;

    public void SetEstimate(FpsEstimate e, FpsAssessment a)
    {
        Set(e.Avg, 0, SourceEstimate, LimiterText(e, Game));
        Range = FpsAccuracy.Range(e, Game, a); Assessment = a;
        Summary = $"Estimativa (não é medição): {Range.Text} FPS, {a.ConfidenceText} — {string.Join("; ", a.Reasons)}.";
    }

    public void SetMeasured(FpsSession s)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string when = s.At.ToLocalTime().ToString("dd/MM", inv);
        string dur = s.DurationSeconds >= 3600 ? $"{Math.Floor(s.DurationSeconds / 3600):0} h {Math.Floor(s.DurationSeconds % 3600 / 60):00} min" : $"{Math.Max(1, Math.Round(s.DurationSeconds / 60)):0} min";
        Set(s.AvgFps, s.Low1Fps, SourceMeasured, $"última sessão {when} · {dur}");
        Measured = s;
        Summary = $"Medido neste PC: {s.AvgFps.ToString("0", inv)} FPS" + (s.Low1Fps > 0 ? $" (1% low {s.Low1Fps.ToString("0", inv)})" : "") + $" · última sessão {when}, {dur}"
            + (s.Gpu is { Length: > 0 } g ? $" · GPU {g}" : "") + (s.Resolution is { } r ? $" · {r}" : "") + ". Medido com MangoHud nas configurações que você usou no jogo.";
    }

    public static string LimiterText(FpsEstimate e, GameInfo g) => e.Limiter switch { Bottleneck.Cpu => "gargalo: CPU", Bottleneck.Gpu => "gargalo: GPU", Bottleneck.Engine => $"trava do jogo em {g.EngineCap:0} FPS", _ => "" };

    /// <summary>Local estimate as a range with its confidence, then sessions measured on this PC take priority for their games.</summary>
    public static void RefreshLocal(IEnumerable<GameFpsRow> rows, double gpuIdx, double cpuIdx, string res, string preset, string upscaling, FpsAssessment assessment, IReadOnlyList<FpsSession>? sessions = null)
    {
        foreach (var row in rows)
        {
            row.SetEstimate(GameCatalog.Estimate(row.Game, gpuIdx, cpuIdx, res, preset, upscaling), assessment);
            if (sessions is { Count: > 0 } && FpsAccuracy.LatestSession(sessions, row.Name) is { } s) row.SetMeasured(s);
        }
    }

    /// <summary>Applies measured sessions only (used when the hardware is not in the tables and there is no estimate).</summary>
    public static void ApplyMeasured(IEnumerable<GameFpsRow> rows, IReadOnlyList<FpsSession> sessions)
    {
        foreach (var row in rows) if (FpsAccuracy.LatestSession(sessions, row.Name) is { } s) row.SetMeasured(s);
    }
}
