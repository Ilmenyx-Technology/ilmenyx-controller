// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Method and limits: docs/meu-pc-roda.md.
using System.Net.Http;
using System.Text.Json;

namespace Ilmenyx.Core;

public enum RequirementsFetch { Ok, NotFound, Offline, TooLarge, Invalid }

/// <summary>
/// Fetches a game's published requirements from the Steam store (store.steampowered.com/api/appdetails). Only the app id is sent. One fixed
/// host, no redirects, 10 s timeout, 2 MB limit; the HTML in the answer is later turned into text and never rendered. Only runs on an explicit user action.
/// </summary>
public sealed class SteamRequirementsClient(HttpMessageHandler? handler = null)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private readonly HttpClient http = Create(handler);

    private static HttpClient Create(HttpMessageHandler? handler)
    {
        var c = handler is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
        c.Timeout = TimeSpan.FromSeconds(10);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("IlmenyxController/0.35 (+https://github.com/Ilmenyx-Technology/ilmenyx-controller)");
        return c;
    }

    public async Task<(RequirementsFetch Result, GameRequirements? Data)> FetchAsync(string appId, CancellationToken ct = default)
    {
        if (appId.Length is 0 or > 10 || !appId.All(char.IsAsciiDigit)) return (RequirementsFetch.Invalid, null);
        try
        {
            using var response = await http.GetAsync("https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=brazilian", HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return (RequirementsFetch.Offline, null); // includes 3xx: never followed to another host
            if (response.Content.Headers.ContentLength is > MaxBytes) return (RequirementsFetch.TooLarge, null);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384]; int n;
            while ((n = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + n > MaxBytes) return (RequirementsFetch.TooLarge, null);
                buffer.Write(chunk, 0, n);
            }
            var data = GameRequirementsParser.ParseAppDetails(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), appId, DateTimeOffset.UtcNow);
            return data is null ? (RequirementsFetch.NotFound, null) : (RequirementsFetch.Ok, data);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException && !ct.IsCancellationRequested) { return (RequirementsFetch.Offline, null); }
    }
}

/// <summary>Local copy of requirements already looked up (one small JSON per app id), so a game reopens without the network.</summary>
public sealed class RequirementsCache(string folder)
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromDays(30);
    private const long MaxFile = 512 * 1024;
    public static string DefaultFolder() => Path.Combine(CompatPaths.Folder(), "requirements");

    private string PathOf(string appId) => Path.Combine(folder, appId + ".json");

    public GameRequirements? Load(string appId)
    {
        if (appId.Length is 0 or > 10 || !appId.All(char.IsAsciiDigit)) return null;
        try
        {
            var fi = new FileInfo(PathOf(appId));
            if (!fi.Exists || fi.Length > MaxFile) return null;
            var d = JsonSerializer.Deserialize<GameRequirements>(File.ReadAllText(fi.FullName));
            return d is not null && d.AppId == appId ? d : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Save(GameRequirements d)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string tmp = PathOf(d.AppId) + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(d));
            File.Move(tmp, PathOf(d.AppId), true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the cache is an optimisation only */ }
    }

    /// <summary>Most recently looked-up games first (at most <paramref name="max"/>).</summary>
    public IReadOnlyList<GameRequirements> Recent(int max = 12)
    {
        try
        {
            if (!Directory.Exists(folder)) return [];
            return new DirectoryInfo(folder).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Take(max * 2)
                .Select(f => Load(Path.GetFileNameWithoutExtension(f.Name))).Where(d => d is not null).Select(d => d!).Take(max).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Cached game whose name equals the typed one once punctuation and case are ignored (no network needed).</summary>
    public GameRequirements? FindByName(string typed)
    {
        string want = SteamAppLookup.Normalize(typed);
        return want.Length < 2 ? null : Recent(200).FirstOrDefault(d => SteamAppLookup.Normalize(d.Name) == want);
    }
}
