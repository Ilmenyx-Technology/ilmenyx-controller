// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Parses the text of <c>dmidecode -t memory</c> (typically produced with root by the user and imported offline).
/// Never executes dmidecode. Serial numbers and asset tags are ignored.
/// </summary>
public static class DmiDecodeParser
{
    public const int MaxCharacters = 1 << 20, MaxModules = 64;

    public static MemoryInventory Parse(string text)
    {
        if (text.Length > MaxCharacters) throw new IOException(L.T("Saída do dmidecode excede o limite de 1 Mi caracteres."));
        int slots = 0; ulong maxBytes = 0; var modules = new List<MemoryModule>();
        foreach (var block in text.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToArray();
            int kind = lines.Any(l => l.Trim() == "Physical Memory Array") ? 1 : lines.Any(l => l.Trim() == "Memory Device") ? 2 : 0;
            if (kind == 0) continue;
            var f = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in lines)
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && char.IsWhiteSpace(line[0])) f.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
            }
            if (kind == 1)
            {
                if (f.TryGetValue("Number Of Devices", out var n) && int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count <= 256) slots += count;
                if (f.TryGetValue("Maximum Capacity", out var m) && Bytes(m) is { } max) maxBytes += max;
            }
            else if (modules.Count < MaxModules && f.TryGetValue("Size", out var size) && Bytes(size) is { } capacity && capacity > 0)
            {
                string Get(string k) => f.TryGetValue(k, out var v) && v is not ("" or "Unknown" or "Not Specified" or "None" or "Not Provided") ? v : "—";
                modules.Add(new(Get("Bank Locator"), Get("Locator"), capacity, Get("Type"), Get("Form Factor"),
                    Speed(f, "Speed"), Speed(f, f.ContainsKey("Configured Memory Speed") ? "Configured Memory Speed" : "Configured Clock Speed"),
                    Get("Manufacturer"), Get("Part Number").Trim(), Width(f, "Data Width"), Width(f, "Total Width"), Get("Rank"),
                    Volt(f, "Configured Voltage"), Volt(f, "Minimum Voltage"), Volt(f, "Maximum Voltage")));
            }
        }
        return new(new(slots, maxBytes), modules);
    }

    private static ulong? Bytes(string text)
    {
        var p = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 2 || !ulong.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong n)) return null;
        ulong unit = p[1] switch { "kB" or "KB" => 1UL << 10, "MB" => 1UL << 20, "GB" => 1UL << 30, "TB" => 1UL << 40, _ => 0 };
        return unit == 0 || n > ulong.MaxValue / unit ? null : n * unit;
    }
    private static int Speed(Dictionary<string, string> f, string key) =>
        f.TryGetValue(key, out var v) && v.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 1 } p && int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out int s) && s is > 0 and < 20000 ? s : 0;
    private static int Width(Dictionary<string, string> f, string key) =>
        f.TryGetValue(key, out var v) && v.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 1 } p && int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out int w) && w is > 0 and <= 512 ? w : 0;
    private static double? Volt(Dictionary<string, string> f, string key) =>
        f.TryGetValue(key, out var v) && v.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 1 } p
        && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double volts) && volts is > 0 and < 10 ? volts : null;
}
