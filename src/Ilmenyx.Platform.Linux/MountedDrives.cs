// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions, adapted from the behaviour of the original DriveMap (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;

namespace Ilmenyx.Platform.Linux;

public sealed record DriveCard(string Title, string Sizes, string Meta, double UsedFraction)
{
    public string UsedPercent => (UsedFraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}

/// <summary>Mounted real filesystems with occupancy. Pseudo and network filesystems are skipped; one card per source device.</summary>
public static class MountedDrives
{
    private static readonly HashSet<string> RealFileSystems = new(StringComparer.Ordinal)
    { "ext2", "ext3", "ext4", "btrfs", "xfs", "f2fs", "vfat", "exfat", "ntfs", "ntfs3", "fuseblk", "zfs", "jfs", "reiserfs", "bcachefs" };
    public const int MaxCards = 32;

    public static string Unescape(string s)
    {
        var r = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 3 < s.Length && s[(i + 1)..(i + 4)].All(c => c is >= '0' and <= '7')) { r.Append((char)Convert.ToInt32(s[(i + 1)..(i + 4)], 8)); i += 3; }
            else r.Append(s[i]);
        }
        return r.ToString();
    }

    public static string Bytes(long b)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"]; double v = b; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return v.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[i];
    }

    /// <param name="stat">(total, available) bytes for a mount point, or null when unreadable.</param>
    public static IReadOnlyList<DriveCard> Read(string mountsText, Func<string, (long Total, long Free)?>? stat = null)
    {
        stat ??= DefaultStat;
        var bySource = new Dictionary<string, List<(string Mount, string Fs)>>(StringComparer.Ordinal);
        foreach (var line in mountsText.Split('\n'))
        {
            var p = line.Split(' ');
            if (p.Length < 3 || !RealFileSystems.Contains(p[2]) || !(p[0].StartsWith('/') || p[2] == "zfs")) continue;
            string source = Unescape(p[0]), mount = Unescape(p[1]);
            if (!bySource.TryGetValue(source, out var list)) bySource[source] = list = [];
            if (list.All(m => m.Mount != mount)) list.Add((mount, p[2]));
        }
        var cards = new List<DriveCard>();
        foreach (var (source, mounts) in bySource.OrderBy(kv => kv.Value.Min(m => m.Mount.Length)).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(MaxCards))
        {
            var main = mounts.OrderBy(m => m.Mount.Length).ThenBy(m => m.Mount, StringComparer.Ordinal).First();
            if (stat(main.Mount) is not { Total: > 0 } s) continue;
            double used = Math.Clamp((double)(s.Total - s.Free) / s.Total, 0, 1);
            cards.Add(new(main.Mount, $"{Bytes(s.Free)} livres de {Bytes(s.Total)}", $"{main.Fs} · {source}" + (mounts.Count > 1 ? $" · +{mounts.Count - 1} ponto(s) de montagem" : ""), used));
        }
        return cards;
    }

    private static (long, long)? DefaultStat(string mount)
    {
        try { var d = new DriveInfo(mount); return d.IsReady ? (d.TotalSize, d.AvailableFreeSpace) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}
