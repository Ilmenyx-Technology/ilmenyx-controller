// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Where a game's achievements come from: Steam itself, or the Steam emulator inside a copy that runs without Steam.</summary>
public enum AchievementOrigin { Steam, Emulator }

/// <summary>A game with achievements on this PC: its Steam AppID, where the unlocks are read and the files that hold them (may not exist yet).</summary>
public sealed record AchievementSource(LibGame Game, string AppId, AchievementOrigin Origin, IReadOnlyList<string> Files);

/// <summary>What the Conquistas page shows for one game: the full list (Steam's order) and what the player has.</summary>
public sealed record AchievementView(AchievementSource Source, IReadOnlyList<AchievementDef> Defs, IReadOnlyDictionary<string, AchievementUnlock> Unlocks)
{
    public int Unlocked => Defs.Count(d => Unlocks.ContainsKey(d.Id));
}

/// <summary>
/// Finds and reads achievement files on this PC, read-only: Steam's own stats cache for Steam games, and for Windows games run by Ilmenyx,
/// the save files of the Steam emulators that copies without Steam carry (CODEX/RUNE and Goldberg/GSE), inside the game's Wine prefix.
/// </summary>
public static class AchievementFiles
{
    private static readonly string[] EmulatorMarks = ["steam_emu.ini", "steam_api.cdx", "steam_api64.cdx", "steam_appid.txt", "steam_settings", "steam_interfaces.txt", "SteamConfig.ini", "cream_api.ini"];

    /// <summary>The source for a library game, or null when it has no achievements Ilmenyx can read.</summary>
    public static AchievementSource? Find(LibGame g, GameProfile? p, IReadOnlyList<string> steamRoots, string? prefixRoot = null)
    {
        if (g.IsSteam && g.Id.All(char.IsAsciiDigit))
        {
            var files = steamRoots.SelectMany(r => StatsFiles(r, g.Id)).ToList();
            return files.Count > 0 ? new(g, g.Id, AchievementOrigin.Steam, files) : null;
        }
        if (!g.IsWindowsManual || g.ExePath is not { } exe) return null;
        string gameDir = GameFolder(exe);
        bool emulator = HasEmulator(Path.GetDirectoryName(exe) ?? gameDir, gameDir);
        string? appId = EmulatorAppId(gameDir) ?? (emulator ? p?.ProtonGameId : null);
        if (!emulator || appId is null) return null;
        string prefix = p?.WinePrefix is { Length: > 0 } own && Path.IsPathFullyQualified(own) ? own
            : LinuxGameLauncher.ProtonEnvironment(exe, p?.ProtonGameId, prefixRoot ?? LinuxGameLauncher.DefaultPrefixRoot())[0].Value;
        return new(g, appId, AchievementOrigin.Emulator, SaveFiles(prefix, appId));
    }

    /// <summary>
    /// The list and the unlocks of one game. Names come from Steam's own cache when it has the game (no network), else from Steam's public
    /// list (<paramref name="online"/>); without either, the emulator's ids are shown in a readable form.
    /// </summary>
    public static async Task<AchievementView> LoadAsync(AchievementSource s, IReadOnlyList<string> steamRoots, string steamLanguage,
        Func<string, string, CancellationToken, Task<IReadOnlyList<AchievementDef>>>? online, CancellationToken ct)
    {
        var local = SteamSchema(steamRoots, s.AppId, steamLanguage);
        IReadOnlyList<AchievementDef> defs = local?.Defs ?? [];
        if (defs.Count == 0 && online is not null)
            try { defs = await online(s.AppId, steamLanguage, ct); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or ArgumentException) { } // offline: ids below
        var unlocks = ReadUnlocks(s, local?.Bits);
        var known = defs.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var extra = unlocks.Keys.Where(id => !known.Contains(id)).Select(id => new AchievementDef(id, Achievements.Readable(id), "", "", "", false)).ToList();
        return new(s, [.. defs, .. extra], unlocks);
    }

    /// <summary>
    /// While a game runs: reads the emulator's save every few seconds and reports each achievement that appears (the ones already there at
    /// the start are not repeated). Steam games are skipped: Steam shows its own notice.
    /// </summary>
    public static async Task WatchAsync(AchievementSource s, Func<IReadOnlyList<AchievementDef>> defs, Func<AchievementDef, Task> unlocked, CancellationToken ct, TimeSpan? every = null)
    {
        if (s.Origin != AchievementOrigin.Emulator) return;
        var seen = ReadUnlocks(s).Keys.ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            try { await Task.Delay(every ?? TimeSpan.FromSeconds(3), ct); } catch (OperationCanceledException) { return; }
            foreach (var id in ReadUnlocks(s).Keys.Where(seen.Add).ToList())
                await unlocked(defs().FirstOrDefault(d => d.Id == id) ?? new AchievementDef(id, Achievements.Readable(id), "", "", "", false));
        }
    }

    /// <summary>Unlocks read now from the source's files (newest file wins on a tie); empty when nothing was saved yet.</summary>
    public static IReadOnlyDictionary<string, AchievementUnlock> ReadUnlocks(AchievementSource s, IReadOnlyDictionary<string, (string Stat, int Bit)>? steamBits = null)
    {
        var all = new Dictionary<string, AchievementUnlock>(StringComparer.Ordinal);
        foreach (var f in s.Files)
        {
            byte[]? data = ReadSmall(f);
            if (data is null) continue;
            var found = s.Origin == AchievementOrigin.Steam ? steamBits is null ? new Dictionary<string, AchievementUnlock>() : Achievements.ParseSteamStats(data, steamBits)
                : f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? Achievements.ParseGoldbergJson(System.Text.Encoding.UTF8.GetString(data))
                : Achievements.ParseCodexIni(System.Text.Encoding.UTF8.GetString(data));
            foreach (var (id, u) in found) if (!all.TryGetValue(id, out var had) || had.At is null) all[id] = u;
        }
        return all;
    }

    /// <summary>Steam's local list for a Steam game (no network), in the chosen Steam language; null when Steam has not cached it.</summary>
    public static (IReadOnlyList<AchievementDef> Defs, IReadOnlyDictionary<string, (string Stat, int Bit)> Bits)? SteamSchema(IReadOnlyList<string> steamRoots, string appId, string language)
    {
        foreach (var root in steamRoots)
            if (ReadSmall(Path.Combine(root, "appcache", "stats", $"UserGameStatsSchema_{appId}.bin")) is { } data && Achievements.ParseSteamSchema(data, language) is { Defs.Count: > 0 } schema)
                return schema;
        return null;
    }

    /// <summary>Emulator save files inside a Wine prefix (umu keeps drive_c in the prefix itself; Proton layouts add "pfx").</summary>
    public static IReadOnlyList<string> SaveFiles(string prefix, string appId)
    {
        var list = new List<string>();
        foreach (var c in new[] { Path.Combine(prefix, "drive_c"), Path.Combine(prefix, "pfx", "drive_c") })
        {
            string users = Path.Combine(c, "users");
            list.Add(Path.Combine(users, "Public", "Documents", "Steam", "CODEX", appId, "achievements.ini"));
            list.Add(Path.Combine(users, "Public", "Documents", "Steam", "RUNE", appId, "achievements.ini"));
            list.Add(Path.Combine(users, "steamuser", "AppData", "Roaming", "Steam", "CODEX", appId, "achievements.ini"));
            list.Add(Path.Combine(users, "steamuser", "AppData", "Roaming", "Goldberg SteamEmu Saves", appId, "achievements.json"));
            list.Add(Path.Combine(users, "steamuser", "AppData", "Roaming", "GSE Saves", appId, "achievements.json"));
        }
        return list;
    }

    /// <summary>A Steam emulator next to the executable or in the game folder (steam_emu.ini, steam_settings, *.cdx...).</summary>
    public static bool HasEmulator(params string[] folders) => folders.Distinct().Any(d => Search(d, 4, n => EmulatorMarks.Contains(n, StringComparer.OrdinalIgnoreCase)) is not null);

    /// <summary>AppID written by the emulator settings in the game folder (steam_emu.ini, steam_appid.txt, steam_settings/steam_appid.txt).</summary>
    public static string? EmulatorAppId(string gameDir)
    {
        foreach (var name in new[] { "steam_emu.ini", "steam_appid.txt" })
            if (Search(gameDir, 4, n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } file && ReadSmall(file) is { } data && Achievements.ParseAppId(System.Text.Encoding.UTF8.GetString(data)) is { } id)
                return id;
        return null;
    }

    /// <summary>The game's top folder (".../Hollow Knight/hollow_knight.exe" → ".../Hollow Knight"), skipping Binaries/Win64-style folders.</summary>
    public static string GameFolder(string exe)
    {
        string? dir = Path.GetDirectoryName(exe);
        string[] generic = ["binaries", "retail", "win64", "win32", "bin", "bin64", "x64", "x86", "game"];
        for (int i = 0; i < 5 && dir is { Length: > 1 } && generic.Contains(Path.GetFileName(dir).ToLowerInvariant()); i++) dir = Path.GetDirectoryName(dir);
        return dir ?? Path.GetDirectoryName(exe) ?? "/";
    }

    private static IEnumerable<string> StatsFiles(string steamRoot, string appId)
    {
        string dir = Path.Combine(steamRoot, "appcache", "stats");
        if (!File.Exists(Path.Combine(dir, $"UserGameStatsSchema_{appId}.bin"))) yield break;
        string[] files;
        try { files = Directory.GetFiles(dir, $"UserGameStats_*_{appId}.bin"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var f in files) yield return f;
        if (files.Length == 0) yield return Path.Combine(dir, $"UserGameStats_0_{appId}.bin"); // list without unlocks yet: still a game with achievements
    }

    /// <summary>Breadth-first search for a file or folder name, a few levels deep, never following links; null when absent.</summary>
    private static string? Search(string root, int depth, Func<string, bool> match)
    {
        var level = new List<string> { root };
        for (int d = 0; d <= depth && level.Count > 0; d++)
        {
            var next = new List<string>();
            foreach (var dir in level.Take(200))
            {
                try
                {
                    foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                    {
                        if (match(e.Name)) return e.FullName;
                        if (e is DirectoryInfo { LinkTarget: null } sub) next.Add(sub.FullName);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
            level = next;
        }
        return null;
    }

    private static byte[]? ReadSmall(string file)
    {
        try { var info = new FileInfo(file); return info.Exists && info.LinkTarget is null && info.Length is > 0 and <= Achievements.MaxFileBytes ? File.ReadAllBytes(file) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
