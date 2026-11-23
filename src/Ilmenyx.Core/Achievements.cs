// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ilmenyx.Core;

/// <summary>One achievement of a game: Steam's internal id, localized name and description, icon files (Steam CDN names) and global rate.</summary>
public sealed record AchievementDef(string Id, string Name, string Description, string Icon, string IconGray, bool Hidden, double? GlobalPercent = null);

/// <summary>An achievement the player has; <paramref name="At"/> is null when the source does not record the time.</summary>
public sealed record AchievementUnlock(string Id, DateTimeOffset? At);

/// <summary>
/// Readers for the places achievements live on Linux: Steam's public list (names, icons), Steam's local stats cache (official games) and
/// the files written by the Steam emulators inside game copies that run without Steam (CODEX/RUNE ini, Goldberg/GSE json).
/// All of them are read-only; every parser tolerates broken input by returning what it could read.
/// </summary>
public static class Achievements
{
    public const int MaxFileBytes = 4 << 20;
    public const string IconHost = "https://shared.akamai.steamstatic.com/community_assets/images/apps";

    public static string IconUrl(string appId, string file) => $"{IconHost}/{appId}/{file}";

    /// <summary>Steam's language name for the interface language (Steam has its own list: "brazilian", "latam"...).</summary>
    public static string SteamLanguage(string uiCode) => uiCode switch { "pt-BR" => "brazilian", "es" => "latam", _ => "english" };

    /// <summary>IPlayerService/GetGameAchievements (public, no key): the game's list in Steam's order.</summary>
    public static IReadOnlyList<AchievementDef> ParseSteamList(string json)
    {
        var list = new List<AchievementDef>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var r) || !r.TryGetProperty("achievements", out var a) || a.ValueKind != JsonValueKind.Array) return list;
            foreach (var x in a.EnumerateArray())
            {
                if (Str(x, "internal_name") is not { Length: > 0 } id) continue;
                double? pct = Str(x, "player_percent_unlocked") is { } p && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
                list.Add(new(id, Str(x, "localized_name") ?? id, Str(x, "localized_desc") ?? "", SafeFile(Str(x, "icon")), SafeFile(Str(x, "icon_gray")),
                    x.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True, pct));
            }
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>CODEX and RUNE emulators: one [ID] section per achievement with Achieved=1 and UnlockTime (Unix seconds).</summary>
    public static IReadOnlyDictionary<string, AchievementUnlock> ParseCodexIni(string text)
    {
        var result = new Dictionary<string, AchievementUnlock>(StringComparer.Ordinal);
        string? section = null; bool achieved = false; long time = 0;
        void Flush() { if (section is not null && achieved) result[section] = new(section, time > 0 ? FromUnix(time) : null); }
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { Flush(); section = line[1..^1].Trim(); achieved = false; time = 0; if (section is "SteamAchievements" or "Stats") section = null; continue; }
            int eq = line.IndexOf('='); if (section is null || eq <= 0) continue;
            string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
            if (key.Equals("Achieved", StringComparison.OrdinalIgnoreCase) || key.Equals("State", StringComparison.OrdinalIgnoreCase)) achieved = value is "1" or "true" or "True";
            else if (key.Equals("UnlockTime", StringComparison.OrdinalIgnoreCase) || key.Equals("Time", StringComparison.OrdinalIgnoreCase)) long.TryParse(value, out time);
        }
        Flush();
        return result;
    }

    /// <summary>Goldberg and its fork GSE: {"ID": {"earned": true, "earned_time": unix}}.</summary>
    public static IReadOnlyDictionary<string, AchievementUnlock> ParseGoldbergJson(string json)
    {
        var result = new Dictionary<string, AchievementUnlock>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("earned", out var e) && e.ValueKind == JsonValueKind.True)
                    result[p.Name] = new(p.Name, p.Value.TryGetProperty("earned_time", out var t) && t.TryGetInt64(out long s) && s > 0 ? FromUnix(s) : null);
        }
        catch (JsonException) { }
        return result;
    }

    /// <summary>The Steam AppID written in an emulator settings file (steam_emu.ini "AppId=", or steam_appid.txt holding only the number).</summary>
    public static string? ParseAppId(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('#') || line.StartsWith(';')) continue;
            string value = line.StartsWith("AppId=", StringComparison.OrdinalIgnoreCase) ? line[6..].Trim() : line;
            if (value.Length is > 0 and <= 10 && value.All(char.IsAsciiDigit) && value != "0") return value;
        }
        return null;
    }

    /// <summary>Steam's local schema (appcache/stats/UserGameStatsSchema_ID.bin): achievements with names in the chosen language, and where each one's bit is.</summary>
    public static (IReadOnlyList<AchievementDef> Defs, IReadOnlyDictionary<string, (string Stat, int Bit)> Bits) ParseSteamSchema(byte[] data, string language)
    {
        var defs = new List<AchievementDef>(); var bits = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        if (BinaryKv.Parse(data) is not { Count: 1 } root || root.Values.First() is not Dictionary<string, object> game || game.GetValueOrDefault("stats") is not Dictionary<string, object> stats) return (defs, bits);
        foreach (var (statId, statObj) in stats)
        {
            if (statObj is not Dictionary<string, object> stat || stat.GetValueOrDefault("type")?.ToString() is not ("ACHIEVEMENTS" or "4" or "5") || stat.GetValueOrDefault("bits") is not Dictionary<string, object> bitList) continue;
            foreach (var (bitId, bitObj) in bitList.OrderBy(b => int.TryParse(b.Key, out int n) ? n : int.MaxValue))
            {
                if (bitObj is not Dictionary<string, object> b || b.GetValueOrDefault("name") is not string id || !int.TryParse(bitId, out int bit)) continue;
                var display = b.GetValueOrDefault("display") as Dictionary<string, object>;
                string Local(string key) => display?.GetValueOrDefault(key) switch
                {
                    Dictionary<string, object> langs => (langs.GetValueOrDefault(language) ?? langs.GetValueOrDefault("english") ?? langs.Values.FirstOrDefault(v => v is string && v.ToString() != ""))?.ToString() ?? "",
                    string s => s, _ => "",
                };
                defs.Add(new(id, Local("name") is { Length: > 0 } n ? n : id, Local("desc"), SafeFile(display?.GetValueOrDefault("icon") as string), SafeFile(display?.GetValueOrDefault("icon_gray") as string),
                    display?.GetValueOrDefault("hidden")?.ToString() is "1" or "true"));
                bits[id] = (statId, bit);
            }
        }
        return (defs, bits);
    }

    /// <summary>
    /// Steam's local player stats (appcache/stats/UserGameStats_ACCOUNT_ID.bin): under "cache", each achievement stat holds "data" (one bit per
    /// achievement) and "AchievementTimes" (bit → Unix time).
    /// </summary>
    public static IReadOnlyDictionary<string, AchievementUnlock> ParseSteamStats(byte[] data, IReadOnlyDictionary<string, (string Stat, int Bit)> bits)
    {
        var result = new Dictionary<string, AchievementUnlock>(StringComparer.Ordinal);
        if (BinaryKv.Parse(data).GetValueOrDefault("cache") is not Dictionary<string, object> cache) return result;
        foreach (var (id, (statId, bit)) in bits)
        {
            if (cache.GetValueOrDefault(statId) is not Dictionary<string, object> stat || stat.GetValueOrDefault("data") is not int mask || bit is < 0 or > 31 || (mask & (1 << bit)) == 0) continue;
            long? time = (stat.GetValueOrDefault("AchievementTimes") as Dictionary<string, object>)?.GetValueOrDefault(bit.ToString(CultureInfo.InvariantCulture)) is int t && t > 0 ? t : null;
            result[id] = new(id, time is { } s ? FromUnix(s) : null);
        }
        return result;
    }

    /// <summary>Readable name from an internal id when Steam's list is not available ("NAILSMITH_KILL" → "Nailsmith kill").</summary>
    public static string Readable(string id)
    {
        string s = id.Replace('_', ' ').Trim();
        if (s.StartsWith("ACH ", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        return s.Length == 0 ? id : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
    }

    private static DateTimeOffset? FromUnix(long s) => s is > 946684800 and < 4102444800 ? DateTimeOffset.FromUnixTimeSeconds(s) : null; // 2000..2100, else not a time
    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    /// <summary>Icon names come from the network: only a plain file name (hash.jpg) may become part of a URL or a cache path.</summary>
    private static string SafeFile(string? f) => f is { Length: > 4 and < 80 } && f.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') && !f.StartsWith('.') ? f : "";
}

/// <summary>Valve's binary KeyValues (appcache files): nested maps of strings, ints, floats and 64-bit numbers. Stops quietly at broken data.</summary>
public static class BinaryKv
{
    public static Dictionary<string, object> Parse(byte[] data)
    {
        int i = 0;
        try { return Map(data, ref i, 0); }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException) { return []; }
    }

    private static Dictionary<string, object> Map(byte[] b, ref int i, int depth)
    {
        if (depth > 32) throw new InvalidDataException();
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        while (i < b.Length)
        {
            byte type = b[i++];
            if (type is 8 or 11) return map;
            string key = Text(b, ref i);
            map[key] = type switch
            {
                0 => Map(b, ref i, depth + 1),
                1 => Text(b, ref i),
                2 or 4 or 6 => BitConverter.ToInt32(Take(b, ref i, 4)),
                3 => BitConverter.ToSingle(Take(b, ref i, 4)),
                7 or 10 => BitConverter.ToUInt64(Take(b, ref i, 8)),
                _ => throw new InvalidDataException(),
            };
        }
        return map;
    }

    private static ReadOnlySpan<byte> Take(byte[] b, ref int i, int size) { if (i + size > b.Length) throw new InvalidDataException(); i += size; return b.AsSpan(i - size, size); }

    private static string Text(byte[] b, ref int i)
    {
        int end = Array.IndexOf(b, (byte)0, i);
        if (end < 0) throw new InvalidDataException();
        string s = Encoding.UTF8.GetString(b, i, end - i); i = end + 1; return s;
    }
}
