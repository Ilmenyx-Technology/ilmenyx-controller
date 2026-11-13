// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <param name="Name">Release tag, also the folder name ("GE-Proton11-12").</param>
public sealed record RunnerRelease(string Name, string Url, long Size, string Sha512Url);

/// <summary>
/// "Baixar versão…": the Proton builds published on GitHub by the two projects that offer ready archives, listed and downloaded only
/// after a click. GE-Proton (GloriousEggroll) and UMU-Proton (Valve's Proton as packaged by the umu project; Valve itself publishes no
/// archive, only through Steam). Each archive is checked against the SHA-512 file of its own release, then extracted into Ilmenyx's
/// runners folder, where <see cref="GameRunners.Find"/> picks it up.
/// </summary>
public static partial class RunnerCatalog
{
    public static readonly (string Label, string Repo)[] Sources = [("GE-Proton", "GloriousEggroll/proton-ge-custom"), ("Proton (UMU-Proton)", "Open-Wine-Components/umu-proton")];

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,59}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();

    /// <summary>
    /// Releases of one repository from the GitHub API answer: stable ones with an x86-64 .tar.gz and its .sha512sum, newest first.
    /// Names become folder names and URLs are fetched, so anything outside the repository's own download path is dropped.
    /// </summary>
    internal static IReadOnlyList<RunnerRelease> Parse(string json, string repo)
    {
        string prefix = "https://github.com/" + repo + "/releases/download/";
        var found = new List<RunnerRelease>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return found;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } name || !SafeName().IsMatch(name)) continue;
            if (release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) continue;
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            var files = new Dictionary<string, (string Url, long Size)>(StringComparer.Ordinal);
            foreach (var a in assets.EnumerateArray())
                if (a.TryGetProperty("name", out var n) && n.GetString() is { } file && a.TryGetProperty("browser_download_url", out var u) && u.GetString() is { } url
                    && url.StartsWith(prefix, StringComparison.Ordinal) && a.TryGetProperty("size", out var s) && s.TryGetInt64(out long size))
                    files[file] = (url, size);
            foreach (string stem in new[] { name + "-x86_64", name })
                if (files.TryGetValue(stem + ".tar.gz", out var tar) && files.TryGetValue(stem + ".sha512sum", out var sum) && tar.Size is > 0 and < 2L << 30)
                { found.Add(new(name, tar.Url, tar.Size, sum.Url)); break; }
        }
        return found;
    }

    public static async Task<IReadOnlyList<RunnerRelease>> ListAsync(HttpClient http, string repo, CancellationToken ct)
    {
        if (Sources.All(s => s.Repo != repo)) throw new ArgumentException(L.T("Fonte de Proton desconhecida."));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + repo + "/releases?per_page=30");
        request.Headers.UserAgent.ParseAdd("Ilmenyx-Controller/" + ReleaseCheck.Current); request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new IOException(L.T($"O GitHub não respondeu a lista de versões (HTTP {(int)response.StatusCode}). Tente de novo em alguns minutos."));
        return Parse(await response.Content.ReadAsStringAsync(ct), repo);
    }

    /// <summary>The hash in a sha512sum file ("hex  name"); null when malformed.</summary>
    internal static string? ParseSha512(string text) =>
        text.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { Length: 128 } hex && hex.All(Uri.IsHexDigit) ? hex : null;

    /// <summary>Downloads, checks (SHA-512 of the release) and extracts one build; returns its folder. Already there: returned as is.</summary>
    public static async Task<string> InstallAsync(HttpClient http, RunnerRelease release, IProgress<double>? progress, CancellationToken ct, string? root = null)
    {
        string folder = Path.Combine(root ?? ClassicProton.Root(), release.Name);
        if (GameRunners.IsProton(folder)) return folder;
        string hash = ParseSha512(await http.GetStringAsync(release.Sha512Url, ct)) ?? throw new IOException(L.T("A soma de verificação desta versão não pôde ser lida."));
        await RunnerDownload.FetchAsync(http, release.Url, release.Size, HashAlgorithmName.SHA512, hash, folder, release.Name, progress, ct, stripTopFolder: true);
        if (!GameRunners.IsProton(folder)) { try { Directory.Delete(folder, true); } catch (IOException) { } throw new IOException(L.T("O arquivo baixado não é um Proton utilizável.")); }
        return folder;
    }
}
