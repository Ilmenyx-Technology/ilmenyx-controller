// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Net.Http.Headers;
using System.Text.Json;

namespace Ilmenyx.Core;

public sealed record ReleaseInfo(Version Version, string Tag, string Url);

/// <summary>
/// Opt-in "new version available": reads the public release list of the GitHub repository (no account, nothing sent besides the
/// request itself), at most once per <see cref="Interval"/>. Pre-releases count, since every Ilmenyx release is one for now.
/// </summary>
public static class ReleaseCheck
{
    public const string Api = "https://api.github.com/repos/Ilmenyx-Technology/ilmenyx-controller/releases?per_page=10";
    public const string ReleasesPage = "https://github.com/Ilmenyx-Technology/ilmenyx-controller/releases";
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private const int MaxBytes = 1 << 20;

    /// <summary>Major.minor.patch of the running app (assembly versions carry a fourth part).</summary>
    public static Version Current => Normalize(typeof(ReleaseCheck).Assembly.GetName().Version ?? new Version(0, 0, 0));

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>Newest published (not draft) release in the GitHub API answer; tags look like "v0.31.1". Null when none parses.</summary>
    public static ReleaseInfo? Newest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            ReleaseInfo? best = null;
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
                if (!r.TryGetProperty("tag_name", out var tagValue) || tagValue.GetString() is not { } tag) continue;
                string number = tag.TrimStart('v', 'V').Split('-', '+')[0];
                if (!Version.TryParse(number, out var version)) continue;
                version = Normalize(version);
                string url = r.TryGetProperty("html_url", out var u) && u.GetString() is { } s && s.StartsWith("https://github.com/", StringComparison.Ordinal) ? s : ReleasesPage;
                if (best is null || version > best.Version) best = new(version, tag, url);
            }
            return best;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The newest release when it is newer than <paramref name="current"/>, else null.</summary>
    public static ReleaseInfo? NewerThan(string json, Version current) => Newest(json) is { } r && r.Version > Normalize(current) ? r : null;

    /// <summary>Asks GitHub (15 s timeout, 1 MiB answer at most). Throws <see cref="HttpRequestException"/>/<see cref="IOException"/> on failure.</summary>
    public static async Task<ReleaseInfo?> FetchNewerAsync(Version current, CancellationToken ct, HttpMessageHandler? handler = null)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(15);
        using var request = new HttpRequestMessage(HttpMethod.Get, Api);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Ilmenyx-Controller", Normalize(current).ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes) throw new IOException(L.T("Resposta do GitHub grande demais."));
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384]; int n;
        while ((n = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + n > MaxBytes) throw new IOException(L.T("Resposta do GitHub grande demais."));
            buffer.Write(chunk, 0, n);
        }
        return NewerThan(System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), current);
    }
}
