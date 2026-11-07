// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using System.Globalization;
using Ilmenyx.Contracts;
namespace Ilmenyx.Platform.Linux;

public static class LinuxParsers
{
    public static IReadOnlyList<KeyValuePair<string, string>> Fields(string text, char separator) => text.Split('\n')
        .Select(line => line.Split(separator, 2)).Where(p => p.Length == 2)
        .Select(p => new KeyValuePair<string, string>(p[0].Trim(), p[1].Trim())).ToArray();
    public static CpuInfo Cpu(string text)
    {
        var fields = Fields(text, ':');
        var name = Clean(fields.FirstOrDefault(p => p.Key == "model name").Value ?? "");
        var ids = fields.Where(p => p.Key == "processor").Select(p => p.Value).ToArray();
        var parsedIds = new HashSet<int>();
        var valid = ids.Length > 0 && ids.Length <= 65536 && ids.All(p =>
            int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 && parsedIds.Add(n));
        return new(string.IsNullOrWhiteSpace(name) ? Reading<string>.Missing("", "/proc/cpuinfo", "Nome não informado pelo kernel.") : Reading<string>.Available(Clean(name), "", "/proc/cpuinfo"),
            valid ? Reading<int?>.Available(ids.Length, "logical processors", "/proc/cpuinfo") : Reading<int?>.Missing("logical processors", "/proc/cpuinfo", "Lista de processadores ausente ou inválida."));
    }
    public static MemoryInfo Memory(string text)
    {
        var fields = Fields(text, ':');
        Reading<long?> Parse(string key)
        {
            var entries = fields.Where(p => p.Key == key).ToArray();
            if (entries.Length == 1)
            {
                var parts = entries[0].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[1] == "kB" && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long n) && n <= long.MaxValue / 1024 && (key != "MemTotal" || n > 0))
                    return Reading<long?>.Available(n * 1024, "B", "/proc/meminfo:" + key);
            }
            return Reading<long?>.Missing("B", "/proc/meminfo:" + key, "Campo ausente, duplicado, unidade ou valor inválido.");
        }
        var total = Parse("MemTotal"); var available = Parse("MemAvailable");
        if (available.Value > total.Value) available = Reading<long?>.Missing("B", "/proc/meminfo:MemAvailable", "Disponível maior que total.");
        return new(total, available);
    }
    public static Reading<long?> Capacity(string text, string source) => long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var sectors) && sectors <= long.MaxValue / 512
        ? Reading<long?>.Available(sectors * 512, "B", source)
        : Reading<long?>.Missing("B", source, "Quantidade de setores inválida ou excede Int64.");
    public static string Clean(string text)
    {
        var limited = new string(text.Trim().Where(c => !char.IsControl(c)).Take(513).ToArray());
        if (limited.Length > 512)
        {
            limited = limited[..512];
            int start = limited.Length;
            while (start > 0 && !char.IsWhiteSpace(limited[start - 1])) start--;
            // Do not publish a partial address after truncation makes its syntax
            // invalid. Conservatively drop the final colon-bearing token.
            if (limited.AsSpan(start).Contains(':')) limited = limited[..start] + "[texto truncado]";
        }
        return string.Concat(ReportTextPrivacy.Redact(limited).Take(512));
    }
    public static string? OsName(string text)
    {
        var matches = Fields(text, '=').Where(p => p.Key == "PRETTY_NAME").ToArray();
        if (matches.Length != 1) return null;
        var value = matches[0].Value;
        if (value.Length > 0 && value[0] is '\"' or '\'')
        {
            if (value.Length < 2 || value[^1] != value[0]) return null;
            value = value[1..^1];
        }
        value = Clean(value);
        return value.Length > 0 ? value : null;
    }

    public static Reading<string> DiskKind(string? text, string source) => text?.Trim() switch
    {
        "0" => Reading<string>.Available("Não rotacional (SSD, flash ou virtual)", "", source),
        "1" => Reading<string>.Available("Rotacional (HDD)", "", source),
        _ => Reading<string>.Missing("", source, "Valor de rotational ausente ou inválido.")
    };
    /// <summary>Kind with device-specific corrections: optical/legacy names, and a caveat on virtual disks whose rotational flag is not the physical media.</summary>
    public static Reading<string> DiskKindFor(string name, string? text, string source, Reading<string> bus)
    {
        static bool Numbered(string n, string prefix) => n.StartsWith(prefix, StringComparison.Ordinal) && n.Length > prefix.Length && n[prefix.Length..].All(char.IsAsciiDigit);
        if (Numbered(name, "sr")) return Reading<string>.Available("Unidade óptica", "", source);
        if (Numbered(name, "fd")) return Reading<string>.Available("Disquete/dispositivo legado", "", source);
        var kind = DiskKind(text, source);
        return kind.Status == "available" && (bus.Value is "VirtIO" or "Virtual/software")
            ? Reading<string>.Available(kind.Value + " — disco virtual: indicação do kernel pode não refletir a mídia física", "", source) : kind;
    }
    public static Reading<string> DiskBus(string name, string? resolved)
    {
        string source = "/sys/block/" + name;
        var segments = (resolved ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        bool Has(string prefix) => segments.Any(s => s.StartsWith(prefix, StringComparison.Ordinal));
        string? bus = name.StartsWith("nvme", StringComparison.Ordinal) ? "NVMe"
            : name.StartsWith("mmcblk", StringComparison.Ordinal) || Has("mmc") ? "MMC/SD"
            : name.StartsWith("loop", StringComparison.Ordinal) || name.StartsWith("zram", StringComparison.Ordinal) || name.StartsWith("ram", StringComparison.Ordinal) || name.StartsWith("dm-", StringComparison.Ordinal) || name.StartsWith("md", StringComparison.Ordinal) ? "Virtual/software"
            : Has("usb") ? "USB"
            : Has("ata") ? "SATA/ATA"
            : Has("virtio") ? "VirtIO"
            : null;
        return bus is null ? Reading<string>.Missing("", source, "Transporte não identificado pelo caminho do dispositivo.") : Reading<string>.Available(bus, "", source);
    }
}
