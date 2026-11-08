// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Globalization;
using System.Text.RegularExpressions;
using Ilmenyx.Contracts;

namespace Ilmenyx.Platform.Linux;

public static partial class HwmonParser
{
    [GeneratedRegex("^hwmon[0-9]{1,5}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex DevicePattern();
    [GeneratedRegex("^(temp|fan|in|curr|power)([0-9]{1,4})_input\\z", RegexOptions.CultureInvariant)]
    private static partial Regex InputPattern();
    public static bool ValidDevice(string device) => DevicePattern().IsMatch(device);
    public static bool TryChannel(string filename, out string channel, out string kind)
    {
        var match = InputPattern().Match(filename);
        channel = ""; kind = "";
        if (!match.Success) return false;
        kind = match.Groups[1].Value;
        if (int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) == 0 && kind != "in") return false;
        channel = filename[..^6];
        return true;
    }
    public static string Unit(string kind) => kind switch { "temp" => "°C", "fan" => "RPM", "in" => "V", "curr" => "A", "power" => "W", _ => "" };
    public static bool KnownTemperatureDriver(string chip) => chip is "coretemp" or "k10temp" or "acpitz" or "nvme" or "amdgpu";
    public static Reading<double?> Parse(string raw, string kind, string source)
    {
        string unit = Unit(kind);
        if (unit.Length == 0 || !long.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)
            || (kind == "fan" && value < 0) || (kind == "temp" && (value <= -273150 || value > 1000000)))
            return Reading<double?>.Missing(unit, source, "Valor inválido, fora do limite de representação ou sem conversão suportada.");
        double divisor = kind == "power" ? 1000000 : kind == "fan" ? 1 : 1000;
        return Reading<double?>.Available(value / divisor, unit, source);
    }
}
