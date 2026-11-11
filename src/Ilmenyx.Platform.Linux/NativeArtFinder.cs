// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Artwork of a Linux game in the library: its icon from the icon theme (offline), and its AppStream screenshot (metainfo of the app, else the
/// catalog of the Flatpak remotes or the distribution), downloaded once into ~/.cache/ilmenyx/art-native. Bounded size, image signature checked.
/// </summary>
public sealed class NativeArtFinder(string? home = null, HttpClient? http = null)
{
    private readonly string home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly HttpClient Shared = CreateClient();
    private readonly HttpClient http = http ?? Shared;
    private static readonly string[] Sizes = ["512x512", "256x256", "192x192", "128x128", "96x96", "64x64", "48x48"];
    private static readonly TimeSpan MissTtl = TimeSpan.FromDays(7);

    private string Cache => Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".cache"), "ilmenyx", "art-native");
    private string[] FlatpakRoots => [Path.Combine(home, ".local/share/flatpak"), "/var/lib/flatpak"];

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("IlmenyxController/0.30 (+https://github.com/Ilmenyx-Technology/ilmenyx-controller)");
        return c;
    }

    /// <summary>Largest PNG of the icon in the hicolor theme or pixmaps (SVG is left out: the library shows bitmaps).</summary>
    public string? Icon(string? name)
    {
        if (name is not { Length: > 0 and <= 255 }) return null;
        if (Path.IsPathFullyQualified(name)) return name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(name) ? name : null;
        if (name.Contains('/') || name.Contains("..")) return null;
        string[] roots = [Path.Combine(home, ".local/share/icons"), .. FlatpakRoots.Select(r => Path.Combine(r, "exports/share/icons")), "/usr/local/share/icons", "/usr/share/icons"];
        foreach (var size in Sizes)
            foreach (var root in roots)
                if (Real(Path.Combine(root, "hicolor", size, "apps", name + ".png")) is { } f) return f;
        return Real($"/usr/share/pixmaps/{name}.png");
    }

    /// <summary>The file itself: Flatpak exports are links into the app folder, and the image loader refuses links.</summary>
    private static string? Real(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            return info.LinkTarget is null ? path : info.ResolveLinkTarget(true) is FileInfo target && target.Exists ? target.FullName : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Screenshot already downloaded for this game, if any (UI thread safe: no network, no catalog reading).</summary>
    public string? Cached(string id) => Valid(Path.Combine(Cache, Name(id) + ".img"));

    /// <summary>Downloads the screenshot once (a miss is remembered for 7 days). Run off the UI thread: the Flathub catalog has tens of MB.</summary>
    public async Task<string?> ScreenshotAsync(string id, bool flatpak, CancellationToken ct)
    {
        string file = Path.Combine(Cache, Name(id) + ".img"), miss = Path.Combine(Cache, Name(id) + ".none");
        if (Valid(file) is { } hit) return hit;
        try { if (File.Exists(miss) && DateTime.UtcNow - File.GetLastWriteTimeUtc(miss) < MissTtl) return null; } catch (IOException) { }
        string? url = FindUrl(id, flatpak);
        byte[]? image = url is null ? null : await DownloadAsync(url, ct);
        try
        {
            Directory.CreateDirectory(Cache);
            if (image is null) { await File.WriteAllTextAsync(miss, url ?? "", ct); return null; }
            string tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tmp, image, ct); File.Move(tmp, file, overwrite: true); File.Delete(miss);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        return Valid(file);
    }

    /// <summary>Where the screenshot address comes from: the app's own metainfo first, then the catalogs (Flatpak remotes, distribution).</summary>
    public string? FindUrl(string id, bool flatpak)
    {
        var metainfo = new List<string>();
        foreach (var root in FlatpakRoots)
            foreach (var sub in new[] { "metainfo", "appdata" })
                metainfo.AddRange(Files(Path.Combine(root, "app", id, "current/active/files/share", sub), "*.xml"));
        foreach (var sub in new[] { "metainfo", "appdata" })
            foreach (var ext in new[] { ".metainfo.xml", ".appdata.xml" })
                if (Path.Combine("/usr/share", sub, id + ext) is var f && File.Exists(f)) metainfo.Add(f);
        foreach (var f in metainfo)
            if (Read(f, id) is { } url) return url;
        var catalogs = new List<string>();
        if (flatpak)
            foreach (var root in FlatpakRoots)
                foreach (var remote in Dirs(Path.Combine(root, "appstream")))
                    catalogs.AddRange(Files(Path.Combine(remote, "x86_64/active"), "appstream.xml"));
        else catalogs.AddRange(Files("/usr/share/swcatalog/xml", "*.xml.gz").Concat(Files("/usr/share/app-info/xmls", "*.xml.gz")));
        foreach (var f in catalogs)
            if (Read(f, id) is { } url) return url;
        return null;
    }

    private static string? Read(string file, string id)
    {
        try
        {
            using var s = File.OpenRead(file);
            using Stream xml = file.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(s, CompressionMode.Decompress) : s;
            return NativeArt.ScreenshotUrl(xml, id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException) { return null; }
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromSeconds(20));
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength is > CompatOnline.MaxArtBytes) return null;
            await using var input = await resp.Content.ReadAsStreamAsync(limit.Token);
            using var ms = new MemoryStream(); var buffer = new byte[1 << 15]; int n;
            while ((n = await input.ReadAsync(buffer, limit.Token)) > 0) { if (ms.Length + n > CompatOnline.MaxArtBytes) return null; ms.Write(buffer, 0, n); }
            return CompatOnline.IsImage(ms.GetBuffer().AsSpan(0, (int)ms.Length)) ? ms.ToArray() : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { return null; }
    }

    private static string? Valid(string file)
    {
        try { var info = new FileInfo(file); return info.Exists && info.Length is > 16 and <= CompatOnline.MaxArtBytes ? file : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string Name(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..20].ToLowerInvariant();
    private static IEnumerable<string> Files(string dir, string pattern) { try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern).Take(64).ToList() : []; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; } }
    private static IEnumerable<string> Dirs(string dir) { try { return Directory.Exists(dir) ? Directory.EnumerateDirectories(dir).Take(32).ToList() : []; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; } }
}
