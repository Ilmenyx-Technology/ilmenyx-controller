// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Ilmenyx.Core;

/// <summary>
/// Finds a game's Steam ID from its name with the Steam store's public search (store.steampowered.com/api/storesearch; only the
/// name is sent). Accepts a result only when its name matches the game's name exactly once punctuation and case are ignored
/// ("Hollow Knight - Silksong" = "Hollow Knight: Silksong"): a wrong ID would make Proton apply another game's fixes, so no guessing.
/// </summary>
public sealed class SteamAppLookup(HttpMessageHandler? handler = null)
{
    private readonly HttpClient http = CreateClient(handler);

    private static HttpClient CreateClient(HttpMessageHandler? handler)
    {
        var c = handler is null ? new HttpClient() : new HttpClient(handler);
        c.Timeout = TimeSpan.FromSeconds(10);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("IlmenyxController/0.30 (+https://github.com/Ilmenyx-Technology/ilmenyx-controller)");
        return c;
    }

    /// <summary>Lowercase letters and digits only; trademark signs and punctuation dropped.</summary>
    public static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char ch in name.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(ch) && ch < 0x2100) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>Only the words of the name: the store search finds nothing for "Hollow Knight - Silksong" but finds "Hollow Knight Silksong".</summary>
    public static string SearchTerm(string name) =>
        string.Join(' ', new string(name.Select(c => char.IsLetterOrDigit(c) || c == '\'' ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The Steam ID and the store's name for <paramref name="gameName"/>, or null (no exact match, offline, error).</summary>
    public async Task<(string Id, string Name)?> FindAsync(string gameName, CancellationToken ct = default)
    {
        string want = Normalize(gameName);
        if (want.Length < 2) return null;
        try
        {
            string url = "https://store.steampowered.com/api/storesearch/?l=english&cc=US&term=" + Uri.EscapeDataString(SearchTerm(gameName));
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentLength is > 512 * 1024) return null;
            string body = await response.Content.ReadAsStringAsync(ct);
            return Parse(body, want);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or IOException) { return null; }
    }

    /// <summary>Apps of a storesearch response: type "app" (or untyped), digits-only id.</summary>
    private static List<(string Id, string Name)> Items(string json)
    {
        var list = new List<(string, string)>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var t) && t.GetString() is { } type && type != "app") continue;
            if (!item.TryGetProperty("name", out var n) || n.GetString() is not { } name) continue;
            if (!item.TryGetProperty("id", out var idEl)) continue;
            string id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) : idEl.GetString() ?? "";
            if (id.Length == 0 || !id.All(char.IsAsciiDigit)) continue;
            list.Add((id, name));
        }
        return list;
    }

    /// <summary>Picks the single exact match from a storesearch response.</summary>
    public static (string Id, string Name)? Parse(string json, string normalizedWant)
    {
        (string, string)? found = null;
        foreach (var (id, name) in Items(json))
        {
            if (Normalize(name) != normalizedWant) continue;
            if (found is { } f && f.Item1 != id) return null; // two different games with the same name: do not guess
            found = (id, name);
        }
        return found;
    }

    private async Task<List<(string Id, string Name)>> SearchAsync(string term, CancellationToken ct)
    {
        string url = "https://store.steampowered.com/api/storesearch/?l=english&cc=US&term=" + Uri.EscapeDataString(term);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 512 * 1024) return [];
        return Items(await response.Content.ReadAsStringAsync(ct));
    }

    private const int MaxExtraSearches = 3;
    private static readonly string[] NotAGame = ["soundtrack", "ost", "dlc", "pack", "season pass", "artbook"];

    /// <summary>Up to 3 games that look like the typed name ("Hollow Knigth" → "Hollow Knight"), never the exact match (that is FindAsync).
    /// First the whole name is searched; with no good candidate, up to 3 more searches by single words (the store finds nothing for a misspelled
    /// whole name). Offline/errors: empty. <paramref name="exclude"/> = ids the user already refused.</summary>
    public async Task<IReadOnlyList<(string Id, string Name)>> SuggestAsync(string gameName, CancellationToken ct = default, IReadOnlySet<string>? exclude = null)
    {
        string term = SearchTerm(gameName);
        if (Normalize(gameName).Length < 2) return [];
        var pool = new List<(string Id, string Name)>();
        IReadOnlyList<(string Id, string Name)> ranked = [];
        try
        {
            var words = term.Split(' ').Where(w => w.Length >= 3 && !w.All(char.IsAsciiDigit) && !IsRoman(w)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(w => w.Length).Take(MaxExtraSearches).ToList();
            foreach (var t in new[] { term }.Concat(words.Where(w => !w.Equals(term, StringComparison.OrdinalIgnoreCase))))
            {
                pool.AddRange(await SearchAsync(t, ct));
                ranked = RankSuggestions(gameName, pool.Where(c => exclude is null || !exclude.Contains(c.Id)));
                if (ranked.Count > 0) break;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or IOException) { return ranked; }
        return ranked;
    }

    private static bool IsRoman(string w) => w.All(c => "ivxIVX".Contains(c));

    /// <summary>Pure ranking: drops duplicates, DLC/soundtrack names and the exact match; keeps similarity &gt;= 0.6, best first, shorter name on ties.</summary>
    public static IReadOnlyList<(string Id, string Name)> RankSuggestions(string typed, IEnumerable<(string Id, string Name)> candidates, int max = 3)
    {
        string want = Normalize(typed);
        return candidates.DistinctBy(c => c.Id)
            .Where(c => Normalize(c.Name) != want && !NotAGame.Any(w => ContainsWord(c.Name, w)))
            .Select(c => (c, score: Similarity(typed, c.Name))).Where(x => x.score >= 0.6)
            .OrderByDescending(x => x.score).ThenBy(x => x.c.Name.Length).Take(max).Select(x => x.c).ToList();
    }

    private static bool ContainsWord(string name, string word) =>
        word.Contains(' ') ? name.Contains(word, StringComparison.OrdinalIgnoreCase)
        : name.Split([' ', ':', '-', '(', ')', '[', ']', ',', '–'], StringSplitOptions.RemoveEmptyEntries).Any(w => w.Equals(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>The larger of: 1 - Levenshtein/maxLen over the whole normalized names, and the mean over typed words of the best match among the other name's words.</summary>
    public static double Similarity(string a, string b)
    {
        double whole = Ratio(Normalize(a), Normalize(b));
        var wa = Words(a); var wb = Words(b);
        double words = wa.Count == 0 || wb.Count == 0 ? 0 : wa.Average(x => wb.Max(y => Ratio(x, y)));
        return Math.Max(whole, words);
    }

    private static List<string> Words(string s) => SearchTerm(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Normalize).Where(w => w.Length > 0).ToList();

    private static double Ratio(string a, string b)
    {
        int max = Math.Max(a.Length, b.Length);
        if (max == 0) return 0;
        var prev = Enumerable.Range(0, b.Length + 1).ToArray(); var cur = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1], prev[j]) + 1, prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return 1.0 - (double)prev[b.Length] / max;
    }
}
