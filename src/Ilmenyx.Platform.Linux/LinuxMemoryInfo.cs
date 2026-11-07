// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using System.Globalization;

namespace Ilmenyx.Platform.Linux;

public static class LinuxMemoryInfo
{
    /// <summary>MemAvailable from /proc/meminfo in bytes; 0 when unreadable (callers then refuse to allocate).</summary>
    public static long AvailableBytes(string path = "/proc/meminfo")
    {
        try
        {
            foreach (string line in File.ReadLines(path).Take(64))
            {
                if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal)) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long kb) && kb is >= 0 and < long.MaxValue / 1024 ? kb * 1024 : 0;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return 0;
    }
}
