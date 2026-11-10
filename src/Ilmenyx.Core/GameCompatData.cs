// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Network use only on an explicit user action: four fixed HTTPS hosts, nothing about the user is sent besides the Steam app id asked for.
// The ProtonDB export (ODbL) is downloaded once by the user, checked against the git blob SHA-1 GitHub publishes for it,
// reduced to a local index and deleted. The index stays on this machine (it is not redistributed).
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

public sealed record CompatDumpInfo(string Name, long Size, string GitSha, string DownloadUrl, DateOnly? Date);
public sealed record CompatMeta(string Source, string GitSha, DateTimeOffset BuiltAt, int Apps, int Reports, DateTimeOffset Oldest, DateTimeOffset Newest);

public static class CompatPaths
{
    public static string Folder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } c && Path.IsPathRooted(c) ? c : Path.Combine(home, ".cache");
        return Path.Combine(cache, "ilmenyx", "compat");
    }
}

/// <summary>Local index of ProtonDB reports: 256 gzip JSON-lines shards by app id, a title list and a metadata file.</summary>
public sealed class CompatDatabase(string folder)
{
    public const int MaxReportsPerApp = 80, MaxSameGpuPerApp = 40;
    public static readonly string Attribution = L.T("Relatos: ProtonDB (protondb.com), Open Database License 1.0, via github.com/bdefore/protondb-data");
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private Dictionary<string, string>? titles;
    public string Folder => folder;

    // Steam app ids are mostly multiples of 10: a multiplicative hash spreads them over the 256 shards.
    private static string Shard(string appId) => "r-" + (ulong.TryParse(appId, out ulong n) ? (n * 0x9E3779B97F4A7C15UL) >> 56 : 0).ToString("x2") + ".jsonl.gz";

    public CompatMeta? ReadMeta()
    {
        try { string p = Path.Combine(folder, "meta.json"); return File.Exists(p) && new FileInfo(p).Length < 65536 ? JsonSerializer.Deserialize<CompatMeta>(File.ReadAllText(p)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public IReadOnlyList<CompatReport> Reports(string appId)
    {
        var list = new List<CompatReport>();
        if (appId.Length is 0 or > 12 || !appId.All(char.IsAsciiDigit)) return list;
        string path = Path.Combine(folder, Shard(appId));
        if (!File.Exists(path)) return list;
        try
        {
            using var reader = new StreamReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress), Encoding.UTF8);
            string needle = "{\"a\":\"" + appId + "\"";
            while (reader.ReadLine() is { } line)
                if (line.StartsWith(needle, StringComparison.Ordinal) && JsonSerializer.Deserialize<CompatReport>(line) is { } r) list.Add(r);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { }
        return list.OrderByDescending(r => r.Timestamp).ToList();
    }

    public string? Title(string appId) => Titles().GetValueOrDefault(appId);

    /// <summary>Case- and accent-insensitive title search; an all-digit query is also tried as a Steam app id.</summary>
    public IReadOnlyList<(string AppId, string Title)> Search(string query, int max = 30)
    {
        var all = Titles(); string q = Fold(query.Trim());
        if (q.Length == 0) return [];
        var hits = new List<(string, string, int)>();
        if (query.Trim().All(char.IsAsciiDigit) && all.TryGetValue(query.Trim(), out var byId)) hits.Add((query.Trim(), byId, -1));
        foreach (var (id, title) in all)
        {
            string f = Fold(title); int at = f.IndexOf(q, StringComparison.Ordinal);
            if (at >= 0) hits.Add((id, title, (at == 0 ? 0 : 1000) + f.Length));
            if (hits.Count > 5000) break;
        }
        return hits.OrderBy(h => h.Item3).ThenBy(h => h.Item2, StringComparer.OrdinalIgnoreCase).DistinctBy(h => h.Item1).Take(max).Select(h => (h.Item1, h.Item2)).ToList();
    }

    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Normalize(NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        // "Counter-Strike 2" and "counter strike" fold to the same text; punctuation and repeated spaces do not matter.
        return Regex.Replace(sb.ToString(), " {2,}", " ").Trim();
    }

    private Dictionary<string, string> Titles()
    {
        if (titles is not null) return titles;
        try
        {
            string p = Path.Combine(folder, "titles.json.gz");
            if (!File.Exists(p)) return titles = [];
            using var s = new GZipStream(File.OpenRead(p), CompressionMode.Decompress);
            return titles = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? [];
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { return titles = []; }
    }

    /// <summary>
    /// Builds the index from the export (.tar.gz holding one JSON array) into a new folder, then swaps it in. Keeps the newest
    /// <see cref="MaxReportsPerApp"/> reports per game. Reads the array element by element: memory stays bounded by the kept reports.
    /// </summary>
    /// <param name="machine">When given, up to <see cref="MaxSameGpuPerApp"/> older reports with this machine's GPU model, series or chip are kept too.</param>
    public static async Task<CompatMeta> BuildAsync(Stream tarGz, string folder, CompatDumpInfo source, IProgress<int>? progress = null, CancellationToken ct = default, CompatHardware? machine = null)
    {
        var byApp = new Dictionary<string, List<CompatReport>>(StringComparer.Ordinal);
        var sameGpu = new Dictionary<string, List<CompatReport>>(StringComparer.Ordinal);
        static void Keep(Dictionary<string, List<CompatReport>> map, CompatReport r, int max)
        {
            if (!map.TryGetValue(r.AppId, out var list)) map[r.AppId] = list = [];
            list.Add(r);
            if (list.Count > max * 2) { list.Sort((x, y) => y.Timestamp.CompareTo(x.Timestamp)); list.RemoveRange(max, list.Count - max); }
        }
        int seen = 0;
        await using (var gz = new GZipStream(tarGz, CompressionMode.Decompress, leaveOpen: true))
        await using (var tar = new TarReader(gz, leaveOpen: true))
        {
            TarEntry? entry;
            while ((entry = await tar.GetNextEntryAsync(copyData: false, ct)) is not null)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || !entry.Name.EndsWith(".json", StringComparison.Ordinal) || entry.DataStream is null) continue;
                await foreach (var element in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(entry.DataStream, cancellationToken: ct))
                {
                    if (++seen % 20_000 == 0) progress?.Report(seen);
                    if (CompatReport.FromDump(element) is not { } r) continue;
                    Keep(byApp, r, MaxReportsPerApp);
                    if (machine is not null && CompatMatcher.SameGpu(machine, r.Gpu)) Keep(sameGpu, r, MaxSameGpuPerApp);
                }
                break;
            }
        }
        if (byApp.Count == 0) throw new InvalidDataException(L.T("O arquivo baixado não tem relatos do ProtonDB no formato esperado."));
        string parent = Path.GetDirectoryName(Path.GetFullPath(folder))!;
        Directory.CreateDirectory(parent);
        string work = Path.Combine(parent, "compat.new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var shards = new Dictionary<string, StreamWriter>(StringComparer.Ordinal);
            var titleMap = new Dictionary<string, string>(StringComparer.Ordinal);
            int total = 0; long oldest = long.MaxValue, newest = 0;
            try
            {
                foreach (var (app, list) in byApp)
                {
                    ct.ThrowIfCancellationRequested();
                    var recent = list.OrderByDescending(r => r.Timestamp).Take(MaxReportsPerApp);
                    var mine = sameGpu.TryGetValue(app, out var s) ? s.OrderByDescending(r => r.Timestamp).Take(MaxSameGpuPerApp) : [];
                    var keep = recent.Concat(mine).DistinctBy(r => (r.Timestamp, r.Gpu, r.LaunchOptions, r.Os)).OrderByDescending(r => r.Timestamp).ToList();
                    if (keep.FirstOrDefault(r => r.Title is not null)?.Title is { } t) titleMap[app] = t;
                    string name = Shard(app);
                    if (!shards.TryGetValue(name, out var w))
                        shards[name] = w = new StreamWriter(new GZipStream(File.Create(Path.Combine(work, name)), CompressionLevel.SmallestSize), new UTF8Encoding(false));
                    foreach (var r in keep)
                    {
                        await w.WriteLineAsync(JsonSerializer.Serialize(r with { Title = null }, Json)); total++;
                        oldest = Math.Min(oldest, r.Timestamp); newest = Math.Max(newest, r.Timestamp);
                    }
                }
            }
            finally { foreach (var w in shards.Values) await w.DisposeAsync(); }
            await using (var tf = new GZipStream(File.Create(Path.Combine(work, "titles.json.gz")), CompressionLevel.SmallestSize))
                await JsonSerializer.SerializeAsync(tf, titleMap, cancellationToken: ct);
            var meta = new CompatMeta(source.Name, source.GitSha, DateTimeOffset.UtcNow, byApp.Count, total, DateTimeOffset.FromUnixTimeSeconds(oldest), DateTimeOffset.FromUnixTimeSeconds(newest));
            await File.WriteAllTextAsync(Path.Combine(work, "meta.json"), JsonSerializer.Serialize(meta), ct);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.Move(work, folder);
            return meta;
        }
        catch { try { Directory.Delete(work, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } throw; }
    }
}

/// <summary>Online lookups. Each call is made only after the user asked; answers are cached on disk for seven days.</summary>
public sealed class CompatOnline
{
    public const string ProtonDbHost = "https://www.protondb.com", SteamHost = "https://store.steampowered.com",
        GitHubApi = "https://api.github.com/repos/bdefore/protondb-data/contents/reports", AntiCheatUrl = "https://raw.githubusercontent.com/AreWeAntiCheatYet/AreWeAntiCheatYet/master/games.json";
    /// <summary>Steam's image CDN, the host the Steam store API itself returns for header_image (checked 2026-11-07).</summary>
    public const string ArtHost = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps";
    public static readonly string[] Hosts = ["www.protondb.com", "store.steampowered.com", "raw.githubusercontent.com", "api.github.com", "shared.akamai.steamstatic.com"];
    public const long MaxArtBytes = 3L << 20, MaxArtFolderBytes = 40L << 20;
    private static readonly TimeSpan ArtTtl = TimeSpan.FromDays(30);
    public const long MaxDumpBytes = 300L << 20;
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
    private readonly HttpClient http;
    private readonly string cacheFolder;
    private readonly Func<DateTime> utcNow;
    private Dictionary<string, AntiCheatInfo>? antiCheat;

    public CompatOnline(string cacheFolder, HttpMessageHandler? handler = null, Func<DateTime>? utcNow = null)
    {
        this.cacheFolder = cacheFolder; this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        http = handler is null ? new() : new(handler);
        http.Timeout = Timeout.InfiniteTimeSpan; // per call below; the dump download may take minutes
        http.DefaultRequestHeaders.UserAgent.ParseAdd("IlmenyxController/0.30 (+https://github.com/Ilmenyx-Technology/ilmenyx)");
    }

    private async Task<(HttpStatusCode Code, string Body)> GetCachedAsync(string key, string url, int maxChars, CancellationToken ct)
    {
        string file = Path.Combine(cacheFolder, "online", Regex.Replace(key, "[^a-z0-9_-]", "_") + ".json");
        try
        {
            if (File.Exists(file) && utcNow() - File.GetLastWriteTimeUtc(file) < Ttl && new FileInfo(file).Length <= maxChars * 4L)
            {
                string cached = await File.ReadAllTextAsync(file, ct);
                return cached.StartsWith("404:", StringComparison.Ordinal) ? (HttpStatusCode.NotFound, "") : (HttpStatusCode.OK, cached);
            }
        }
        catch (IOException) { }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(20));
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, limit.Token);
        if (resp.StatusCode == HttpStatusCode.NotFound) { Save(file, "404:"); return (HttpStatusCode.NotFound, ""); }
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException((int)resp.StatusCode == 429 ? L.T("o serviço pediu para esperar (limite de consultas)") : $"HTTP {(int)resp.StatusCode}");
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(limit.Token));
        var buffer = new char[maxChars + 1]; int n = await reader.ReadBlockAsync(buffer, limit.Token);
        if (n > maxChars) throw new HttpRequestException(L.T("resposta maior que o esperado"));
        string body = new(buffer, 0, n); Save(file, body);
        return (HttpStatusCode.OK, body);
    }

    private static void Save(string file, string body)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); string tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp"; File.WriteAllText(tmp, body); File.Move(tmp, file, overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string Id(string appId) => appId.Length is > 0 and <= 12 && appId.All(char.IsAsciiDigit) ? appId : throw new ArgumentException(L.T("Identificador Steam inválido."), nameof(appId));

    /// <summary>Null when ProtonDB has no reports for the game.</summary>
    public async Task<ProtonSummary?> SummaryAsync(string appId, CancellationToken ct = default)
    {
        var (code, body) = await GetCachedAsync("protondb-" + Id(appId), $"{ProtonDbHost}/api/v1/reports/summaries/{appId}.json", 4096, ct);
        return code == HttpStatusCode.NotFound ? null : ProtonSummary.Parse(body) ?? throw new HttpRequestException(L.T("resposta do ProtonDB fora do formato"));
    }

    public async Task<DeckStatus?> DeckAsync(string appId, CancellationToken ct = default)
    {
        var (code, body) = await GetCachedAsync("deck-" + Id(appId), $"{SteamHost}/saleaction/ajaxgetdeckappcompatibilityreport?nAppID={appId}", 65536, ct);
        return code == HttpStatusCode.NotFound ? null : DeckStatus.Parse(body);
    }

    /// <summary>The whole AreWeAntiCheatYet list (~0.5 MB) is fetched once a week; null when the game is not in it (most single-player games).</summary>
    public async Task<AntiCheatInfo?> AntiCheatAsync(string appId, CancellationToken ct = default)
    {
        Id(appId);
        if (antiCheat is null)
        {
            var (_, body) = await GetCachedAsync("anticheat-games", AntiCheatUrl, 4 << 20, ct);
            antiCheat = AntiCheatInfo.ParseAll(body);
        }
        return antiCheat.GetValueOrDefault(appId);
    }

    private string ArtFolder => Path.Combine(cacheFolder, "art");

    /// <summary>Artwork already on disk from an earlier allowed lookup (no network). Null when absent or not an image.</summary>
    public string? CachedArt(string appId)
    {
        string file = Path.Combine(ArtFolder, Id(appId) + ".img");
        try { var info = new FileInfo(file); return info.Exists && info.Length is > 16 and <= MaxArtBytes && IsImage(File.ReadAllBytes(file)) ? file : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>JPEG, PNG or WebP by their signatures: anything else (HTML error pages, truncated bodies) is not cached nor shown.</summary>
    public static bool IsImage(ReadOnlySpan<byte> b) =>
        b.Length > 16 && (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF
            || b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
            || b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8));

    /// <summary>
    /// Wide Steam artwork of the game (library_hero.jpg, else header.jpg) from the Steam CDN, cached 30 days in ~/.cache/ilmenyx/compat/art
    /// (a miss is remembered for 7 days). Call only after the user allowed online lookups. Bounded size, image signature checked, folder kept under 40 MB.
    /// </summary>
    public async Task<string?> ArtAsync(string appId, CancellationToken ct = default)
    {
        string id = Id(appId), file = Path.Combine(ArtFolder, id + ".img"), miss = Path.Combine(ArtFolder, id + ".none");
        try
        {
            if (CachedArt(id) is { } hit && utcNow() - File.GetLastWriteTimeUtc(hit) < ArtTtl) return hit;
            if (File.Exists(miss) && utcNow() - File.GetLastWriteTimeUtc(miss) < Ttl) return CachedArt(id);
        }
        catch (IOException) { }
        foreach (var name in new[] { "library_hero.jpg", "header.jpg" })
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(20));
            using var resp = await http.GetAsync($"{ArtHost}/{id}/{name}", HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) continue;
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException(L.T($"imagem: HTTP {(int)resp.StatusCode}"));
            if (resp.Content.Headers.ContentType?.MediaType is { } type && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;
            if (resp.Content.Headers.ContentLength is > MaxArtBytes) continue;
            await using var input = await resp.Content.ReadAsStreamAsync(limit.Token);
            using var ms = new MemoryStream(); var buffer = new byte[1 << 15]; int n;
            while ((n = await input.ReadAsync(buffer, limit.Token)) > 0) { if (ms.Length + n > MaxArtBytes) throw new HttpRequestException(L.T("imagem maior que o esperado")); ms.Write(buffer, 0, n); }
            if (!IsImage(ms.GetBuffer().AsSpan(0, (int)ms.Length))) continue;
            try
            {
                Directory.CreateDirectory(ArtFolder);
                string tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(tmp, ms.ToArray(), ct); File.Move(tmp, file, overwrite: true); File.Delete(miss);
                PruneArt(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return CachedArt(id);
        }
        Save(miss, "");
        return CachedArt(id); // an older copy (if any) stays usable
    }

    /// <summary>Oldest images go first once the folder passes <see cref="MaxArtFolderBytes"/>; the one just saved is kept.</summary>
    private void PruneArt(string keep)
    {
        var files = new DirectoryInfo(ArtFolder).GetFiles("*.img").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        long total = 0;
        foreach (var f in files)
        {
            total += f.Length;
            if (total > MaxArtFolderBytes && f.FullName != Path.GetFullPath(keep)) try { f.Delete(); } catch (IOException) { }
        }
    }

    /// <summary>Newest monthly export listed by the GitHub API (name like reports_nov1_2026.tar.gz), with the git blob SHA-1 used to check the download.</summary>
    public async Task<CompatDumpInfo> LatestDumpAsync(CancellationToken ct = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(20));
        using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApi); req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await http.SendAsync(req, limit.Token);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException(L.T($"GitHub respondeu HTTP {(int)resp.StatusCode}"));
        string body = await resp.Content.ReadAsStringAsync(limit.Token);
        return PickLatest(body) ?? throw new HttpRequestException(L.T("nenhum arquivo de relatos na listagem do GitHub"));
    }

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    public static CompatDumpInfo? PickLatest(string listingJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(listingJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var all = new List<CompatDumpInfo>();
            foreach (var f in doc.RootElement.EnumerateArray())
            {
                if (ProtonSummary.Str(f, "name") is not { } name || ProtonSummary.Str(f, "sha") is not { Length: 40 } sha || ProtonSummary.Str(f, "download_url") is not { } url
                    || !url.StartsWith("https://raw.githubusercontent.com/bdefore/protondb-data/", StringComparison.Ordinal) || !f.TryGetProperty("size", out var s) || !s.TryGetInt64(out long size)) continue;
                var m = Regex.Match(name, @"^reports_([a-z]{3})(\d{1,2})_(\d{4})\.tar\.gz$");
                if (!m.Success) continue;
                int month = Array.IndexOf(Months, m.Groups[1].Value) + 1;
                DateOnly? date = month > 0 && int.TryParse(m.Groups[2].Value, out int d) && int.TryParse(m.Groups[3].Value, out int y) && d is >= 1 and <= 31 ? new DateOnly(y, month, Math.Min(d, DateTime.DaysInMonth(y, month))) : null;
                all.Add(new(name, size, sha, url, date));
            }
            return all.Where(x => x.Date is not null).OrderByDescending(x => x.Date).FirstOrDefault();
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Downloads the export to <paramref name="destination"/> (a new file), refusing more than <see cref="MaxDumpBytes"/>; throws if the git blob SHA-1 does not match.</summary>
    public async Task DownloadDumpAsync(CompatDumpInfo dump, string destination, IProgress<(long Done, long Total)>? progress = null, CancellationToken ct = default)
    {
        if (dump.Size is <= 0 or > MaxDumpBytes) throw new HttpRequestException(L.T("tamanho do arquivo fora do limite"));
        using var resp = await http.GetAsync(dump.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException(L.T($"download respondeu HTTP {(int)resp.StatusCode}"));
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        sha.AppendData(Encoding.ASCII.GetBytes($"blob {dump.Size}\0"));
        long done = 0; var buffer = new byte[1 << 16];
        try
        {
            await using (var input = await resp.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, true))
            {
                int n;
                while ((n = await input.ReadAsync(buffer, ct)) > 0)
                {
                    done += n;
                    if (done > dump.Size) throw new HttpRequestException(L.T("o arquivo é maior que o anunciado"));
                    sha.AppendData(buffer, 0, n); await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    progress?.Report((done, dump.Size));
                }
            }
            if (done != dump.Size) throw new HttpRequestException(L.T("download incompleto"));
            if (!Convert.ToHexStringLower(sha.GetHashAndReset()).Equals(dump.GitSha, StringComparison.Ordinal)) throw new HttpRequestException(L.T("o arquivo não confere com o hash publicado pelo GitHub"));
        }
        catch { try { File.Delete(destination); } catch (IOException) { } throw; }
    }

    /// <summary>Latest export → verified download → index → the downloaded file is deleted.</summary>
    public async Task<CompatMeta> UpdateDatabaseAsync(CompatDatabase db, IProgress<string>? status = null, CancellationToken ct = default, CompatHardware? machine = null)
    {
        status?.Report(L.T("Procurando o arquivo de relatos mais recente…"));
        var dump = await LatestDumpAsync(ct);
        Directory.CreateDirectory(cacheFolder);
        string file = Path.Combine(cacheFolder, "download-" + Guid.NewGuid().ToString("N") + ".tar.gz");
        try
        {
            await DownloadDumpAsync(dump, file, new Progress<(long Done, long Total)>(p => status?.Report(L.T($"Baixando {dump.Name}: {p.Done >> 20} de {p.Total >> 20} MB"))), ct);
            status?.Report(L.T("Hash conferido. Montando o índice local (pode levar alguns minutos)…"));
            await using var stream = File.OpenRead(file);
            return await CompatDatabase.BuildAsync(stream, db.Folder, dump, new Progress<int>(n => status?.Report(L.T($"Montando o índice: {n:N0} relatos lidos"))), ct, machine);
        }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }
}
