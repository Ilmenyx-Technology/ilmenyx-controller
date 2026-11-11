// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core;

/// <summary>An app and all of its processes (a browser with its tabs, a game with its helpers).</summary>
public sealed record AppGroup(string AppId, IReadOnlyList<ProcessUsage> Processes)
{
    public double? CpuPercent => Processes.Any(p => p.CpuPercent is not null) ? Processes.Sum(p => p.CpuPercent ?? 0) : null;
    public long ResidentBytes => Processes.Sum(p => p.ResidentBytes ?? 0);
    public double? ReadBps => Processes.Any(p => p.ReadBps is not null) ? Processes.Sum(p => p.ReadBps ?? 0) : null;
    public double? WriteBps => Processes.Any(p => p.WriteBps is not null) ? Processes.Sum(p => p.WriteBps ?? 0) : null;
}

/// <summary>
/// Groups processes by the app that started them, the way desktop system monitors do: systemd puts every app launched from the menu,
/// the dock or a .desktop file in a unit named after it ("app-firefox@….service", "app-flatpak-com.valvesoftware.Steam-123.scope",
/// "app-gnome-org.gnome.Nautilus-456.scope"), and its child processes inherit that unit.
/// </summary>
public static class ProcessGroups
{
    private static readonly string[] Launchers = ["flatpak-", "gnome-", "kde-", "dbus-:"];

    /// <summary>
    /// Desktop app id of a systemd app unit: "app[-launcher]-ID@RANDOM.service" or "app[-launcher]-ID-RANDOM.scope" (systemd's
    /// desktop environment conventions). Null when the unit does not follow them.
    /// </summary>
    public static string? AppId(string? unit)
    {
        if (unit is null || !unit.StartsWith("app-", StringComparison.Ordinal)) return null;
        string name = Unescape(unit[4..]);
        bool scope = name.EndsWith(".scope", StringComparison.Ordinal);
        if (scope) name = name[..^6]; else if (name.EndsWith(".service", StringComparison.Ordinal)) name = name[..^8]; else return null;
        int at = name.IndexOf('@');
        if (at >= 0) name = name[..at];
        else if (scope && name.LastIndexOf('-') is > 0 and int dash && name[(dash + 1)..].All(char.IsAsciiHexDigit)) name = name[..dash];
        foreach (var launcher in Launchers)
            if (name.StartsWith(launcher, StringComparison.Ordinal) && name.Length > launcher.Length) { name = name[launcher.Length..]; break; }
        return name.Length > 0 ? name : null;
    }

    /// <summary>systemd escapes "-" inside names as "\x2d" (and other bytes the same way).</summary>
    internal static string Unescape(string text)
    {
        if (!text.Contains("\\x", StringComparison.Ordinal)) return text;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\\' && i + 3 < text.Length && text[i + 1] == 'x' && int.TryParse(text.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b)) { sb.Append((char)b); i += 3; }
            else sb.Append(text[i]);
        return sb.ToString();
    }

    /// <summary>Apps with their processes, heaviest first; processes without an app unit (services, the desktop itself) are left out.</summary>
    public static IReadOnlyList<AppGroup> Build(IEnumerable<ProcessUsage> processes) =>
        processes.Select(p => (Id: AppId(p.AppUnit), P: p)).Where(x => x.Id is not null)
            .GroupBy(x => x.Id!, StringComparer.Ordinal)
            .Select(g => new AppGroup(g.Key, g.Select(x => x.P).ToArray()))
            .OrderByDescending(g => g.CpuPercent ?? -1).ThenByDescending(g => g.ResidentBytes).ToArray();

    /// <summary>Processes that belong to the desktop session itself: ending them can close the session or freeze the screen.</summary>
    public static bool IsSessionCritical(string name) => name is "systemd" or "plasmashell" or "kwin_wayland" or "kwin_x11" or "ksmserver" or "gnome-shell"
        or "Xorg" or "Xwayland" or "gnome-session-binary" or "cinnamon" or "muffin" or "xfce4-session" or "xfwm4" or "dbus-daemon" or "dbus-broker"
        or "pipewire" or "wireplumber" or "sddm" or "gdm" or "lightdm" or "kded6" or "kded5";
}
