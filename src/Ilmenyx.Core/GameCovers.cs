// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Security.Cryptography;
using System.Text;

namespace Ilmenyx.Core;

/// <summary>A cover the app refuses; <see cref="Exception.Message"/> is the reason shown to the user.</summary>
public sealed class CoverException(string message) : Exception(message);

/// <summary>Custom game covers: the chosen image is always copied into the app's own cache (never referenced where the user keeps it).</summary>
public static class GameCovers
{
    public const long MaxBytes = 8L << 20;

    /// <summary>Folder of the copies, next to the art cache (~/.cache/ilmenyx/compat/covers).</summary>
    public static string Folder() => Path.Combine(CompatPaths.Folder(), "covers");

    /// <summary>Library entries to show: hidden ones only when asked (pure, for the filter and the count).</summary>
    public static IEnumerable<LibGame> Visible(IEnumerable<LibGame> games, ICollection<string> hidden, bool showHidden) =>
        showHidden || hidden.Count == 0 ? games : games.Where(g => !hidden.Contains(g.Key));

    private static string Target(string folder, string gameKey, string ext)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gameKey)))[..16].ToLowerInvariant();
        return Path.Combine(folder, hash + ext);
    }

    private static string Ext(string name) => Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" ? Path.GetExtension(name).ToLowerInvariant() : ".img";

    /// <summary>Copies a local image into <paramref name="folder"/> after checking size, symlink and signature; returns the copy.</summary>
    public static string CopyLocal(string source, string folder, string gameKey, long maxBytes = MaxBytes)
    {
        try
        {
            var info = new FileInfo(source);
            if (!info.Exists) throw new CoverException(L.T("o arquivo não existe."));
            if (info.LinkTarget is not null) throw new CoverException(L.T("o arquivo é um link simbólico."));
            if (info.Length > maxBytes) throw new CoverException(L.T("a imagem passa de 8 MB."));
            var bytes = File.ReadAllBytes(source);
            if (!CompatOnline.IsImage(bytes)) throw new CoverException(L.T("não é uma imagem PNG, JPEG ou WebP."));
            return Save(bytes, folder, gameKey, Ext(source));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new CoverException(e.Message); }
    }

    private static string Save(byte[] bytes, string folder, string gameKey, string ext)
    {
        Directory.CreateDirectory(folder);
        string dest = Target(folder, gameKey, ext), tmp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        foreach (var old in Directory.GetFiles(folder, Path.GetFileNameWithoutExtension(dest) + ".*").Where(f => f != tmp && f != dest)) File.Delete(old);
        File.Move(tmp, dest, overwrite: true);
        return dest;
    }

    /// <summary>https only, a host, no user:password.</summary>
    public static Uri ValidateUrl(string? text)
    {
        if (!Uri.TryCreate((text ?? "").Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0)
            throw new CoverException(L.T("use um link https://."));
        if (uri.UserInfo.Length > 0) throw new CoverException(L.T("o link não pode ter usuário e senha."));
        return uri;
    }

    /// <summary>Downloads the image of a link (only on the user's explicit click): 20 s, 3 redirects, 8 MB, signature checked.</summary>
    public static async Task<string> DownloadAsync(Uri url, string folder, string gameKey, CancellationToken ct, HttpMessageHandler? handler = null)
    {
        using var http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 3 }, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Ilmenyx");
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(20));
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (!resp.IsSuccessStatusCode) throw new CoverException($"HTTP {(int)resp.StatusCode}.");
            if (resp.Content.Headers.ContentLength is > MaxBytes) throw new CoverException(L.T("a imagem passa de 8 MB."));
            await using var input = await resp.Content.ReadAsStreamAsync(limit.Token);
            using var ms = new MemoryStream(); var buffer = new byte[1 << 15]; int n;
            while ((n = await input.ReadAsync(buffer, limit.Token)) > 0) { if (ms.Length + n > MaxBytes) throw new CoverException(L.T("a imagem passa de 8 MB.")); ms.Write(buffer, 0, n); }
            if (!CompatOnline.IsImage(ms.GetBuffer().AsSpan(0, (int)ms.Length))) throw new CoverException(L.T("o link não leva a uma imagem PNG, JPEG ou WebP."));
            return Save(ms.ToArray(), folder, gameKey, Ext(url.AbsolutePath));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { throw new CoverException(e is TaskCanceledException ? L.T("o servidor demorou demais.") : e.Message); }
    }
}
