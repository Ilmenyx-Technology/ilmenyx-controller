// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

/// <summary>A game installed on Linux outside the stores (system package, Flatpak, AppImage with a menu entry).</summary>
/// <param name="Program">First word of Exec: a name looked up in PATH or an absolute path.</param>
public sealed record NativeGame(string Id, string Name, string Program, IReadOnlyList<string> Arguments, bool Flatpak, string? Icon = null)
{
    public string Kind => Flatpak ? "Linux · Flatpak" : "Linux";
}

/// <summary>
/// Reads freedesktop menu entries (.desktop) and keeps the games: Categories with "Game", visible, with a command. Leaves out store and
/// tool entries (Steam, Lutris, Heroic, Bottles, Proton managers, emulators): their games are listed by their own store, or they are not games.
/// </summary>
public static class NativeGames
{
    public const int MaxBytes = 64 * 1024;

    private static readonly HashSet<string> Tools = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "com.valvesoftware.Steam", "lutris", "net.lutris.Lutris", "heroic", "com.heroicgameslauncher.hgl", "com.usebottles.bottles",
        "com.vysp3r.ProtonPlus", "net.davidotek.pupgui2", "io.github.benjamimgois.goverlay", "com.github.Matoking.protontricks",
        "page.kramo.Cartridges", "io.itch.itch", "io.github.sharkwouter.Minigalaxy", "com.github.mtkennerly.ludusavi", "steam-native", "steam-runtime",
    };
    private static readonly string[] ToolCategories = ["Emulator", "PackageManager", "Settings", "System"];
    private static readonly string[] StoreSchemes = ["steam://", "heroic://", "lutris:", "itch://"];
    private static readonly HashSet<string> DroppedCodes = ["%f", "%F", "%u", "%U", "%d", "%D", "%n", "%N", "%v", "%m", "%i", "%c", "%k"];

    /// <param name="id">Desktop file ID (file name without ".desktop").</param>
    /// <param name="flatpakExport">The entry comes from a Flatpak exports folder.</param>
    public static NativeGame? FromEntry(string id, string text, bool flatpakExport)
    {
        if (text.Length > MaxBytes || Tools.Contains(id)) return null;
        var e = Group(text);
        if (e.GetValueOrDefault("Type") != "Application" || Flag(e, "NoDisplay") || Flag(e, "Hidden")) return null;
        var categories = (e.GetValueOrDefault("Categories") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!categories.Contains("Game") || categories.Any(c => ToolCategories.Contains(c))) return null;
        string exec = e.GetValueOrDefault("Exec") ?? "";
        if (exec.Length == 0 || StoreSchemes.Any(s => exec.Contains(s, StringComparison.OrdinalIgnoreCase))) return null;
        IReadOnlyList<string> words;
        try { words = GameLaunchOptions.SplitArguments(exec.Replace("%%", "\u0001")); } catch (FormatException) { return null; }
        var args = words.Where(w => !DroppedCodes.Contains(w)).Select(w => w.Replace('\u0001', '%')).ToList();
        if (args.Count == 0) return null;
        string name = e.GetValueOrDefault("Name[pt_BR]") ?? e.GetValueOrDefault("Name[pt]") ?? e.GetValueOrDefault("Name") ?? id;
        bool flatpak = flatpakExport || Path.GetFileName(args[0]) == "flatpak";
        return new(id, name.Trim(), args[0], args.Skip(1).ToList(), flatpak, e.GetValueOrDefault("Icon"));
    }

    /// <summary>Joins arguments so that <see cref="GameLaunchOptions.SplitArguments"/> gives them back unchanged.</summary>
    public static string JoinArguments(IEnumerable<string> args) =>
        string.Join(' ', args.Select(a => a.Length > 0 && a.All(c => !char.IsWhiteSpace(c) && c is not ('"' or '\'' or '\\')) ? a : "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));

    /// <summary>Keys of the [Desktop Entry] group only (actions and other groups are ignored).</summary>
    private static Dictionary<string, string> Group(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inGroup = false;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { inGroup = line == "[Desktop Entry]"; continue; }
            if (!inGroup || line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq > 0) d.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
        return d;
    }

    private static bool Flag(Dictionary<string, string> e, string key) => string.Equals(e.GetValueOrDefault(key), "true", StringComparison.OrdinalIgnoreCase);
}
