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
    public GameInfo Game { get; } = game;
    public string Name => Game.Name;
    public string Genre => Game.Genre;
    public double Fps { get; private set; }
    public double Low { get; private set; }
    public string Source { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public string FpsText => Fps > 0 ? Fps.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "—";
    public string LowText => Low > 0 ? $"1% low {Low.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}" : "";
    /// <summary>Common ranges of FPS calculators: 144+ excellent, 90+ great, 60+ playable.</summary>
    public string Rating => Fps switch { <= 0 => "", >= 144 => "EXCELENTE", >= 90 => "ÓTIMO", >= 60 => "JOGÁVEL", >= 30 => "FRACO", _ => "INJOGÁVEL" };
    public double Bar => Math.Clamp(Fps / 240.0, 0, 1);
    public void Set(double fps, double low, string source, string detail) { Fps = fps; Low = low; Source = source; Detail = detail; }

    public static string LimiterText(FpsEstimate e, GameInfo g) => e.Limiter switch { Bottleneck.Cpu => "gargalo: CPU", Bottleneck.Gpu => "gargalo: GPU", Bottleneck.Engine => $"trava do jogo em {g.EngineCap:0} FPS", _ => "" };

    public static void RefreshLocal(IEnumerable<GameFpsRow> rows, double gpuIdx, double cpuIdx, string res, string preset, string upscaling)
    {
        foreach (var row in rows) { var est = GameCatalog.Estimate(row.Game, gpuIdx, cpuIdx, res, preset, upscaling); row.Set(est.Avg, 0, "ESTIMATIVA", LimiterText(est, row.Game)); }
    }
}
