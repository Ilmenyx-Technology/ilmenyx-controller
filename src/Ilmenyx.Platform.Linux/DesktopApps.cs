// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;

namespace Ilmenyx.Platform.Linux;

/// <summary>Name and icon of a desktop app, from its .desktop file and the icon theme (PNG only: the app draws no SVG).</summary>
public sealed record DesktopApp(string Id, string Name, string? IconPng);

/// <summary>
/// Finds the .desktop file of an app id ("firefox", "com.valvesoftware.Steam") in the usual folders, including Flatpak's exports, and a
/// PNG of its icon. Results are cached: the same few apps are asked about every second.
/// </summary>
public sealed class DesktopApps(IReadOnlyList<string>? dataDirs = null)
{
    private readonly Dictionary<string, DesktopApp?> cache = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<string> dirs = dataDirs ?? DefaultDataDirs();
    private static readonly string[] Sizes = ["48x48", "64x64", "32x32", "96x96", "128x128", "256x256", "24x24"];
    private static readonly string[] Themes = ["hicolor", "breeze", "Adwaita", "Papirus", "elementary"];

    public static IReadOnlyList<string> DefaultDataDirs()
    {
        string home = Environment.GetEnvironmentVariable("HOME") ?? "";
        var list = new List<string>
        {
            Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local/share"),
            Path.Combine(home, ".local/share/flatpak/exports/share"), "/var/lib/flatpak/exports/share",
        };
        foreach (var d in (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") ?? "/usr/local/share:/usr/share").Split(':', StringSplitOptions.RemoveEmptyEntries))
            list.Add(d);
        list.Add("/usr/share");
        // Inside the Flatpak the host's /usr is mounted at /run/host/usr (host-os permission).
        if (HostSpawn.InFlatpak) list.AddRange(["/run/host/usr/share", "/run/host/usr/local/share"]);
        return list.Distinct(StringComparer.Ordinal).ToArray();
    }

    private string[]? ids;

    /// <summary>
    /// The installed app a folder named <paramref name="folder"/> most likely belongs to (cache folders carry the program's name):
    /// a .desktop id equal to it or ending in ".folder", ignoring case ("Shelly" → com.shellyorg.shelly). Null when none.
    /// </summary>
    public DesktopApp? FindByFolder(string folder)
    {
        ids ??= dirs.SelectMany(d =>
        {
            try { string apps = Path.Combine(d, "applications"); return Directory.Exists(apps) ? Directory.EnumerateFiles(apps, "*.desktop").Select(f => Path.GetFileNameWithoutExtension(f)) : []; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        }).Distinct(StringComparer.Ordinal).ToArray();
        string? id = ids.FirstOrDefault(i => i.Equals(folder, StringComparison.OrdinalIgnoreCase)) ?? ids.FirstOrDefault(i => i.EndsWith("." + folder, StringComparison.OrdinalIgnoreCase));
        return id is null ? null : Find(id);
    }

    public DesktopApp? Find(string appId)
    {
        if (cache.TryGetValue(appId, out var known)) return known;
        DesktopApp? found = null;
        foreach (var dir in dirs)
        {
            string file = Path.Combine(dir, "applications", appId + ".desktop");
            try { if (!File.Exists(file)) continue; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            string text;
            try { text = LinuxFiles.ReadSmall(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            var (name, icon) = Parse(text, CultureInfo.CurrentUICulture.Name.Replace('-', '_'));
            if (name is null) continue;
            found = new(appId, name, icon is null ? null : IconPng(icon));
            break;
        }
        return cache[appId] = found;
    }

    /// <summary>Name (localized when the file has it) and Icon of the [Desktop Entry] group.</summary>
    internal static (string? Name, string? Icon) Parse(string text, string locale)
    {
        string? name = null, localized = null, language = null, icon = null; bool entry = false;
        string lang = locale.Split('_')[0];
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { entry = line == "[Desktop Entry]"; continue; }
            if (!entry) continue;
            if (line.StartsWith("Name=", StringComparison.Ordinal)) name ??= line[5..].Trim();
            else if (line.StartsWith("Name[" + locale + "]=", StringComparison.Ordinal)) localized = line[(locale.Length + 7)..].Trim();
            else if (lang.Length > 0 && line.StartsWith("Name[" + lang + "]=", StringComparison.Ordinal)) language = line[(lang.Length + 7)..].Trim();
            else if (line.StartsWith("Icon=", StringComparison.Ordinal)) icon ??= line[5..].Trim();
        }
        return (localized is { Length: > 0 } ? localized : language is { Length: > 0 } ? language : name, icon);
    }

    private string? IconPng(string icon)
    {
        if (icon.StartsWith('/')) return icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && Exists(icon) ? icon : null;
        foreach (var dir in dirs)
        {
            foreach (var theme in Themes)
                foreach (var size in Sizes)
                {
                    string path = Path.Combine(dir, "icons", theme, size, "apps", icon + ".png");
                    if (Exists(path)) return path;
                }
            string pixmap = Path.Combine(dir, "pixmaps", icon + ".png");
            if (Exists(pixmap)) return pixmap;
        }
        return null;
    }

    private static bool Exists(string path)
    {
        try { return File.Exists(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
