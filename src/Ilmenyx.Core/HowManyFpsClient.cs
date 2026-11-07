// New Ilmenyx additions, adapted from the original HowManyFpsClient (MIT, see NOTICE): see LICENSING.md.
// Network use: only on an explicit click with a user-provided key. Responses are cached on disk for seven days to spare the plan's quota.
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ilmenyx.Core;

public sealed class HowManyFpsClient
{
    public const string Endpoint = "https://howmanyfps.com/api";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);
    private readonly HttpClient http;
    private readonly string cachePath;
    private readonly Func<DateTime> utcNow;
    private readonly Dictionary<string, CacheEntry> cache;
    public string? ApiKey { get; set; }
    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey);
    public sealed record Result(double AvgFps, double Min1Fps);
    private sealed record CacheEntry(double AvgFps, double Min1Fps, DateTime Fetched);
    private sealed class Response { [JsonPropertyName("avgFps")] public double? AvgFps { get; set; } [JsonPropertyName("min1Fps")] public double? Min1Fps { get; set; } }

    public HowManyFpsClient(string cachePath, HttpMessageHandler? handler = null, Func<DateTime>? utcNow = null)
    {
        this.cachePath = cachePath; this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        http = handler is null ? new() { Timeout = TimeSpan.FromSeconds(15) } : new(handler) { Timeout = TimeSpan.FromSeconds(15) };
        try { cache = File.Exists(cachePath) && new FileInfo(cachePath).Length < 4 << 20 ? JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(cachePath)) ?? [] : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { cache = []; }
    }

    public bool IsCached(string cpu, string gpu, string game, string resolution, string upscaling) =>
        cache.TryGetValue(Key(cpu, gpu, game, resolution, upscaling), out var c) && utcNow() - c.Fetched < CacheTtl;
    private static string Key(string cpu, string gpu, string game, string resolution, string upscaling) => $"{cpu}|{gpu}|{game}|{resolution}|{upscaling}";

    public async Task<Result> GetAsync(string cpu, string gpu, string game, string resolution, string upscaling, CancellationToken ct = default)
    {
        string key = Key(cpu, gpu, game, resolution, upscaling);
        if (cache.TryGetValue(key, out var c) && utcNow() - c.Fetched < CacheTtl) return new(c.AvgFps, c.Min1Fps);
        if (!Configured) throw new InvalidOperationException("Chave da API não configurada.");
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(new { cpu, gpu, game, resolution, upscaling }) };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiKey}");
        req.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException((int)resp.StatusCode switch { 401 or 403 => "chave recusada pela API", 404 => "jogo ou hardware não encontrado na base", 429 => "cota de requisições esgotada", var code => $"HTTP {code}" });
        var data = await resp.Content.ReadFromJsonAsync<Response>(cancellationToken: ct) ?? throw new HttpRequestException("resposta vazia");
        if (data.AvgFps is not double avg || !double.IsFinite(avg) || avg < 0 || avg > 10000) throw new HttpRequestException("resposta sem avgFps válido");
        var result = new Result(avg, data.Min1Fps is { } m && double.IsFinite(m) && m >= 0 && m <= 10000 ? m : 0);
        cache[key] = new(result.AvgFps, result.Min1Fps, utcNow()); SaveCache();
        return result;
    }

    private void SaveCache()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!); string tmp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(cache)); File.Move(tmp, cachePath, overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Card model of the original GameRow, without UI notifications.</summary>
public sealed class GameFpsRow(GameInfo game)
{
    public const string SourceEstimate = "ESTIMATIVA", SourceMeasured = "MEDIDO NESTE PC", SourceApi = "HOWMANYFPS";
    public GameInfo Game { get; } = game;
    public string Name => Game.Name;
    public string Genre => Game.Genre;
    public double Fps { get; private set; }
    public double Low { get; private set; }
    public string Source { get; private set; } = "";
    public string Detail { get; private set; } = "";
    /// <summary>Local estimate range (null for measured or API values). The card shows the range, never a single estimated number.</summary>
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
