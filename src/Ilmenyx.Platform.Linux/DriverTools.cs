// New Ilmenyx additions: see LICENSING.md.
using System.Net.Http;
using System.Text.Json;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Explicit-click helpers: NVIDIA latest-version lookup (network), fwupd device/update listing (read-only), opening an official page.</summary>
public static class DriverTools
{
    public const string NvidiaLatestUrl = "https://download.nvidia.com/XFree86/Linux-x86_64/latest.txt";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>First token of NVIDIA's public latest.txt ("570.133.07 570.133.07/NVIDIA-Linux-...run"); null when malformed.</summary>
    public static string? ParseNvidiaLatest(string text)
    {
        string first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length is > 0 and <= 24 && first.All(c => char.IsAsciiDigit(c) || c == '.') && first.Contains('.') ? first : null;
    }

    public static async Task<string?> NvidiaLatestAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(NvidiaLatestUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var buffer = new char[512]; using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        int n = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
        return ParseNvidiaLatest(new string(buffer, 0, n));
    }

    public sealed record FirmwareItem(string Name, string Version, string Update);

    /// <summary>Parses <c>fwupdmgr get-devices --json</c>: updatable devices with current version. No serials or device ids are kept.</summary>
    public static IReadOnlyList<FirmwareItem> ParseFwupd(string json)
    {
        var items = new List<FirmwareItem>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        if (!doc.RootElement.TryGetProperty("Devices", out var devices) || devices.ValueKind != JsonValueKind.Array) return items;
        foreach (var d in devices.EnumerateArray().Take(128))
        {
            string name = d.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "";
            if (name.Length == 0 || name.Length > 120) continue;
            string version = d.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "—";
            bool hasUpdate = d.TryGetProperty("Releases", out var r) && r.ValueKind == JsonValueKind.Array && r.GetArrayLength() > 0;
            string update = hasUpdate && r.EnumerateArray().First().TryGetProperty("Version", out var rv) && rv.ValueKind == JsonValueKind.String ? rv.GetString()! : "";
            items.Add(new(name, version.Length > 64 ? version[..64] : version, update.Length > 64 ? update[..64] : update));
        }
        return items;
    }

    /// <summary>Read-only listing; never installs. The daemon may require network/polkit for updates, in which case this fails with a message.</summary>
    public static async Task<IReadOnlyList<FirmwareItem>> FwupdDevicesAsync(CancellationToken ct = default)
    {
        var (code, output) = await BoundedProcess.RunAsync("fwupdmgr", ["get-devices", "--json"], TimeSpan.FromSeconds(10), 1 << 20, ct);
        if (code != 0) throw new IOException($"fwupdmgr retornou código {code}.");
        return ParseFwupd(output);
    }

    /// <summary>Opens an official page with the desktop's handler. Only https URLs; never a shell.</summary>
    public static async Task OpenUrlAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || url.Length > 512) throw new ArgumentException("Somente URLs https são abertas.");
        await BoundedProcess.RunAsync("xdg-open", [uri.AbsoluteUri], TimeSpan.FromSeconds(5), 4096, ct);
    }
}
