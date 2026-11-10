// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text;

namespace Ilmenyx.Core;

/// <summary>A "non-Steam game" the user added to Steam (userdata/&lt;id&gt;/config/shortcuts.vdf).</summary>
public sealed record SteamShortcut(uint AppId, string Name, string Exe, string StartDir, string LaunchOptions, bool Hidden)
{
    /// <summary>Steam opens a shortcut by its 64-bit game ID: the 32-bit app ID in the high half, 0x02000000 (shortcut type) in the low.</summary>
    public string LaunchUri => $"steam://rungameid/{((ulong)AppId << 32) | 0x02000000UL}";
}

/// <summary>
/// Reader for Steam's binary VDF (shortcuts.vdf): 0x00 opens a map, 0x01 is a string, 0x02 a 32-bit integer, 0x08 closes a map;
/// every key and string ends in a NUL. Bounded and read-only: a damaged or unexpected file yields what was read before the damage.
/// </summary>
public static class SteamShortcuts
{
    public const int MaxBytes = 1 << 20;
    private const int MaxEntries = 512, MaxText = 4096, MaxDepth = 8;

    public static IReadOnlyList<SteamShortcut> Parse(ReadOnlySpan<byte> data)
    {
        var list = new List<SteamShortcut>();
        if (data.Length > MaxBytes) return list;
        int pos = 0;
        // Root: 0x00 "shortcuts", then one map per entry ("0", "1", ...).
        if (!Byte(data, ref pos, out byte t) || t != 0x00 || Text(data, ref pos) is not { } root || !root.Equals("shortcuts", StringComparison.OrdinalIgnoreCase)) return list;
        while (list.Count < MaxEntries && Byte(data, ref pos, out t) && t == 0x00 && Text(data, ref pos) is not null)
        {
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ints = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            if (!Entry(data, ref pos, strings, ints, 1)) break;
            string name = strings.GetValueOrDefault("AppName") ?? strings.GetValueOrDefault("appname") ?? "";
            if (!ints.TryGetValue("appid", out uint id) || id == 0 || name.Trim().Length == 0) continue;
            list.Add(new(id, name.Trim(), Unquote(strings.GetValueOrDefault("Exe")), Unquote(strings.GetValueOrDefault("StartDir")),
                strings.GetValueOrDefault("LaunchOptions") ?? "", ints.GetValueOrDefault("IsHidden") != 0));
        }
        return list;
    }

    /// <summary>Reads one map up to its 0x08; nested maps (tags) are skipped. Only top-level fields of the entry are kept.</summary>
    private static bool Entry(ReadOnlySpan<byte> d, ref int pos, Dictionary<string, string> strings, Dictionary<string, uint> ints, int depth)
    {
        if (depth > MaxDepth) return false;
        while (Byte(d, ref pos, out byte t))
        {
            if (t == 0x08) return true;
            if (Text(d, ref pos) is not { } key) return false;
            switch (t)
            {
                case 0x00:
                    if (!Entry(d, ref pos, [], [], depth + 1)) return false;
                    break;
                case 0x01:
                    if (Text(d, ref pos) is not { } value) return false;
                    if (depth == 1) strings[key] = value;
                    break;
                case 0x02:
                    if (pos + 4 > d.Length) return false;
                    if (depth == 1) ints[key] = BitConverter.ToUInt32(d.Slice(pos, 4));
                    pos += 4;
                    break;
                default: return false; // other types (float, 64-bit) never appear in shortcuts.vdf
            }
        }
        return false;
    }

    private static bool Byte(ReadOnlySpan<byte> d, ref int pos, out byte b)
    {
        b = 0;
        if (pos >= d.Length) return false;
        b = d[pos++]; return true;
    }

    private static string? Text(ReadOnlySpan<byte> d, ref int pos)
    {
        int end = d[pos..].IndexOf((byte)0);
        if (end < 0 || end > MaxText) return null;
        string s = Encoding.UTF8.GetString(d.Slice(pos, end));
        pos += end + 1;
        return s;
    }

    private static string Unquote(string? s) => (s ?? "").Trim() is { Length: >= 2 } q && q[0] == '"' && q[^1] == '"' ? q[1..^1] : (s ?? "").Trim();
}
