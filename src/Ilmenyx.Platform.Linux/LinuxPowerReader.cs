// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions, adapted from the data gathering of the original BatteryInfo (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

public sealed record PowerReadout(IReadOnlyList<BatteryInput> Batteries, bool? MainsOnline);

/// <summary>Batteries and mains from /sys/class/power_supply. Read-only; units converted from µWh/µAh/µV/µW; absent attributes stay null.</summary>
public sealed class LinuxPowerReader(string sysRoot = "/sys")
{
    private static string Text(string dir, string name) => SysfsText.Read(Path.Combine(dir, name), 80).Replace("\n", " ").Trim();
    private static long? Num(string dir, string name) => long.TryParse(Text(dir, name), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v) ? v : null;

    public PowerReadout Read()
    {
        string root = Path.Combine(sysRoot, "class/power_supply");
        var batteries = new List<BatteryInput>(); bool? mains = null;
        if (!Directory.Exists(root)) return new(batteries, mains);
        foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(p => p, StringComparer.Ordinal).Take(16))
        {
            string type = Text(dir, "type");
            if (type == "Mains") { if (Num(dir, "online") is { } online) mains = (mains ?? false) || online == 1; continue; }
            if (type != "Battery" || Num(dir, "present") == 0) continue;
            long? vmin = Num(dir, "voltage_min_design") is > 0 and var v1 ? v1 : Num(dir, "voltage_now") is > 0 and var v2 ? v2 : null;
            long? Energy(string energy, string charge) => Num(dir, energy) is >= 0 and var e ? e : Num(dir, charge) is >= 0 and var c && vmin is { } v ? (long)(c * (double)v / 1_000_000) : null;
            long? power = Num(dir, "power_now") is { } p ? Math.Abs(p) : Num(dir, "current_now") is { } cur && Num(dir, "voltage_now") is { } vn ? (long)(Math.Abs(cur) * (double)vn / 1_000_000) : null;
            string Str(string n) => Text(dir, n);
            long? pct = Num(dir, "capacity") is >= 0 and <= 100 and var c0 ? c0 : null;
            batteries.Add(new(Path.GetFileName(dir), Energy("energy_full_design", "charge_full_design"), Energy("energy_full", "charge_full"), Energy("energy_now", "charge_now"),
                Num(dir, "cycle_count"), pct, Str("status"), Num(dir, "voltage_now") is > 0 and var vn2 ? vn2 : null, power,
                Str("manufacturer") is { Length: > 0 } mf ? mf : null, Str("model_name") is { Length: > 0 } mn ? mn : null, Str("technology") is { Length: > 0 } tech ? tech : null));
        }
        return new(batteries, mains);
    }

    public static string ProfileName(string id) => id switch { "power-saver" => "Economia de energia", "balanced" => "Equilibrado", "performance" => "Alto desempenho", _ => id };

    /// <summary>Active profile from power-profiles-daemon (read-only <c>powerprofilesctl get</c>); null when the tool is absent or fails.</summary>
    public static async Task<string?> ActiveProfileAsync(CancellationToken ct = default)
    {
        try
        {
            var (code, output) = await BoundedProcess.RunAsync("powerprofilesctl", ["get"], TimeSpan.FromSeconds(2), 512, ct);
            return code == 0 && output.Trim() is { Length: > 0 and <= 32 } id && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? ProfileName(id) : null;
        }
        catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception) { return null; }
    }
}
