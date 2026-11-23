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
            if (info.Length == 0) throw new CoverException(L.T("o arquivo está vazio ou não é um arquivo comum.")); // a FIFO or device reports 0 and would block the read
            if (info.Length > maxBytes) throw new CoverException(L.T("a imagem passa de 8 MB."));
            var bytes = File.ReadAllBytes(source);
            if (!CompatOnline.IsImage(bytes)) throw new CoverException(L.T("não é uma imagem PNG, JPEG ou WebP."));
            return Save(bytes, folder, gameKey, Ext(source));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new CoverException(e.Message); }
    }

    public const int MaxSide = 8192;

    /// <summary>Width and height from the header of a PNG, JPEG or WebP (VP8, VP8L, VP8X) without decoding it; null when the header is not understood.</summary>
    public static (int Width, int Height)? ImageSize(ReadOnlySpan<byte> b)
    {
        static int Be16(ReadOnlySpan<byte> x, int i) => (x[i] << 8) | x[i + 1];
        static int Le16(ReadOnlySpan<byte> x, int i) => x[i] | (x[i + 1] << 8);
        if (b.Length >= 24 && b[0] == 0x89 && b[1] == 'P' && b[12] == 'I' && b[13] == 'H' && b[14] == 'D' && b[15] == 'R')
            return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b[16..]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b[20..]));
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            for (int i = 2; i + 9 < b.Length;)
            {
                if (b[i] != 0xFF) { i++; continue; }
                byte m = b[i + 1];
                if (m is 0xFF) { i++; continue; }
                if (m is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC)) return (Be16(b, i + 7), Be16(b, i + 5));
                if (m is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
                i += 2 + Be16(b, i + 2);
            }
            return null;
        }
        if (b.Length >= 30 && b[0] == 'R' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
        {
            if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == ' ' && b[23] == 0x9D) return (Le16(b, 26) & 0x3FFF, Le16(b, 28) & 0x3FFF);
            if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'L' && b[20] == 0x2F)
            {
                uint v = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(b[21..]);
                return ((int)(v & 0x3FFF) + 1, (int)((v >> 14) & 0x3FFF) + 1);
            }
            if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'X')
                return ((b[24] | (b[25] << 8) | (b[26] << 16)) + 1, (b[27] | (b[28] << 8) | (b[29] << 16)) + 1);
        }
        return null;
    }

    private static string Save(byte[] bytes, string folder, string gameKey, string ext)
    {
        if (ImageSize(bytes) is { } size && (size.Width > MaxSide || size.Height > MaxSide)) throw new CoverException(L.T($"a imagem passa de {MaxSide} pixels de lado."));
        Directory.CreateDirectory(folder);
        string dest = Target(folder, gameKey, ext), tmp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, dest, overwrite: true); // the new cover is in place before the old ones (other extensions) go
        foreach (var old in Directory.GetFiles(folder, Path.GetFileNameWithoutExtension(dest) + ".*").Where(f => f != dest)) File.Delete(old);
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
