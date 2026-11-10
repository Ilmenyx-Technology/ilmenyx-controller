// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Net.Http;
using System.Text.Json;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Explicit-click helpers: NVIDIA latest-version lookup (network), fwupd device/update listing (read-only), opening an official page.</summary>
public static class DriverTools
{
    public sealed record DriverManager(string Program, IReadOnlyList<string> Arguments);

    public static DriverManager? FindDriverManager(string distroId, string idLike, Func<string, string?>? find = null)
    {
        find ??= BoundedProcess.FindSystem;
        if (distroId == "linuxmint")
            return find("mintdrivers") is { } mint ? new(mint, []) : null;
        if ((distroId + " " + idLike).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("ubuntu"))
            return find("software-properties-gtk") is { } ubuntu ? new(ubuntu, ["--open-tab=4"]) : null;
        return null;
    }

    /// <summary>Explicit click only. No shell, elevation, waiting, or termination of the distribution's GUI.</summary>
    public static void LaunchDriverManager(DriverManager manager)
    {
        // Re-resolve an allowlisted system tool rather than accepting arbitrary commands.
        string name = Path.GetFileName(manager.Program);
        if (name is not ("mintdrivers" or "software-properties-gtk")) throw new ArgumentException("Gerenciador não permitido.");
        string path = BoundedProcess.FindSystem(name) ?? throw new FileNotFoundException("Gerenciador de drivers não encontrado.");
        var start = HostSpawn.Start(path, name == "software-properties-gtk" ? ["--open-tab=4"] : [], redirect: false);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("Não foi possível abrir o gerenciador.");
    }

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

    /// <summary>Directory listing of NVIDIA's Linux x86_64 releases (one folder per version, e.g. "580.105.08/").</summary>
    public const string NvidiaReleasesUrl = "https://download.nvidia.com/XFree86/Linux-x86_64/";

    /// <summary>
    /// Newest release of <paramref name="branch"/> from the public listing (only on a click). A Maxwell card is compared with the newest 580.x,
    /// never with latest.txt (595+ dropped Maxwell, Pascal and Volta).
    /// </summary>
    public static async Task<string?> NvidiaBranchLatestAsync(string branch, CancellationToken ct = default)
    {
        if (!NvidiaScripts.ValidBranch(branch)) return null;
        using var response = await Http.GetAsync(NvidiaReleasesUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        var buffer = new char[1 << 20]; int total = 0, n;
        while (total < buffer.Length && (n = await reader.ReadAsync(buffer.AsMemory(total), ct)) > 0) total += n;
        return NvidiaGenerations.NewestInBranch(new string(buffer, 0, total), branch);
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
