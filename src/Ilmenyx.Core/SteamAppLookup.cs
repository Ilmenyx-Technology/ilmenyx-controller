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

    /// <summary>Picks the single exact match from a storesearch response.</summary>
    public static (string Id, string Name)? Parse(string json, string normalizedWant)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        (string, string)? found = null;
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var t) && t.GetString() is { } type && type != "app") continue;
            if (!item.TryGetProperty("name", out var n) || n.GetString() is not { } name || Normalize(name) != normalizedWant) continue;
            if (!item.TryGetProperty("id", out var idEl)) continue;
            string id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) : idEl.GetString() ?? "";
            if (id.Length == 0 || !id.All(char.IsAsciiDigit)) continue;
            if (found is { } f && f.Item1 != id) return null; // two different games with the same name: do not guess
            found = (id, name);
        }
        return found;
    }
}
