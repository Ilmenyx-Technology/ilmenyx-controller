// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core;

public enum SensorGroup { Cpu, Gpu, Storage, Battery, Fans, Board }
public enum SensorLevel { None, Normal, Hot, Critical }

/// <summary>Where a temperature sits: normal below <paramref name="Hot"/>, hot until <paramref name="Critical"/>, critical above; the scale ends at <paramref name="Max"/>.</summary>
public sealed record SensorRange(double Hot, double Critical, double Max);

/// <param name="Name">Plain name ("Temperatura geral").</param>
/// <param name="Technical">Kernel name ("coretemp · Package id 0 (temp1)"), shown only on request.</param>
/// <param name="Help">One sentence for "o que é?"; empty when the name says it all.</param>
/// <param name="Range">Reference ruler for temperatures; null for other readings.</param>
public sealed record FriendlyReading(SensorGroup Group, string Name, string Technical, string Value, double? Number, SensorLevel Level, string Verdict, string Help, SensorRange? Range, int Order);

/// <summary>A card of the Sensors page: one part of the PC with its readings.</summary>
public sealed record FriendlyCard(SensorGroup Group, string Title, string Subtitle, IReadOnlyList<FriendlyReading> Readings)
{
    public SensorLevel Level => Readings.Count == 0 ? SensorLevel.None : Readings.Max(r => r.Level);
}

/// <summary>
/// Turns raw kernel readings (hwmon, thermal zones, power_supply, GPU) into cards a beginner understands: plain names, a ruler with what is
/// normal for that kind of part, a one-line verdict, and the technical name kept for whoever wants it. Pure: the page only draws the result.
/// Duplicates of the same value (thermal zones that mirror the CPU package, the battery voltage read twice) are folded into "Outros sensores".
/// </summary>
public static class SensorFriendly
{
    // Typical limits: laptop and desktop CPUs/GPUs throttle near 95-100 °C; consumer SSDs slow down near 70-80 °C.
    public static readonly SensorRange CpuRange = new(80, 95, 105), GpuRange = new(80, 95, 105), StorageRange = new(70, 80, 90), BoardRange = new(70, 85, 100);

    public static IReadOnlyList<FriendlyCard> Build(SensorSnapshot snapshot, bool includeUnavailable = false)
    {
        var items = new List<FriendlyReading>();
        foreach (var m in snapshot.Readings)
        {
            if (!includeUnavailable && (m.Reading.Status != "available" || m.Reading.Value is null)) continue;
            if (Classify(m) is { } f) items.Add(f);
        }
        if (snapshot.PowerSupplies is { } supplies)
            foreach (var p in supplies.Where(p => p.Type.Value == "Mains" && p.Present.Value is not null))
                items.Add(new(SensorGroup.Battery, L.T("Carregador"), "power_supply · " + p.Device, p.Present.Value > 0 ? L.T("presente") : L.T("ausente"), null, SensorLevel.None, "", "", null, 0));
        Fold(items);
        var order = new[] { SensorGroup.Cpu, SensorGroup.Gpu, SensorGroup.Storage, SensorGroup.Battery, SensorGroup.Fans, SensorGroup.Board };
        return order.Select(g => (g, list: items.Where(i => i.Group == g).OrderBy(i => i.Order).ThenBy(i => i.Name, StringComparer.Ordinal).ToList()))
            .Where(x => x.list.Count > 0)
            .Select(x => new FriendlyCard(x.g, Title(x.g), Subtitle(x.g, snapshot), x.list)).ToArray();
    }

    /// <summary>Worst level over every card, for the banner at the top of the page.</summary>
    public static SensorLevel Overall(IReadOnlyList<FriendlyCard> cards) => cards.Count == 0 ? SensorLevel.None : cards.Max(c => c.Level);

    public static string Title(SensorGroup g) => g switch
    {
        SensorGroup.Cpu => L.T("Processador"), SensorGroup.Gpu => L.T("Placa de vídeo"), SensorGroup.Storage => L.T("Armazenamento"),
        SensorGroup.Battery => L.T("Bateria e energia"), SensorGroup.Fans => L.T("Ventoinhas"), _ => L.T("Placa-mãe e outros sensores"),
    };

    private static string Subtitle(SensorGroup g, SensorSnapshot s) => g switch
    {
        SensorGroup.Gpu => s.Readings.FirstOrDefault(r => r.Device.StartsWith("gpu:", StringComparison.Ordinal))?.Chip ?? "",
        _ => "",
    };

    private static bool IsStorage(string chip) => chip is "nvme" or "drivetemp";
    private static bool IsCpu(string chip) => chip is "coretemp" or "k10temp" or "zenpower";

    internal static FriendlyReading? Classify(SensorMeasurement m)
    {
        double? v = m.Reading.Value;
        string unit = m.Reading.Unit;
        string tech = m.Device.StartsWith("Thermal/", StringComparison.Ordinal) || m.Device.StartsWith("PowerSupply/", StringComparison.Ordinal) || m.Device.StartsWith("gpu:", StringComparison.Ordinal)
            ? m.Chip + " · " + m.Label : $"{m.Chip} · {m.Label} ({m.Channel})";
        string value = Format(v, unit);
        if (IsCpu(m.Chip) && unit == "°C")
        {
            bool package = m.Label is "Package id 0" or "Tctl" or "Tdie";
            if (package) return Temp(SensorGroup.Cpu, L.T("Temperatura geral"), tech, v, CpuRange, L.T("A temperatura do chip inteiro. É a que mais importa: perto de 95 °C o processador reduz a velocidade para se proteger."), 0);
            if (m.Label.StartsWith("Core ", StringComparison.Ordinal) && int.TryParse(m.Label.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out int core))
                return Temp(SensorGroup.Cpu, L.T($"Núcleo {core + 1}"), tech, v, CpuRange, "", 10 + core);
            return Temp(SensorGroup.Cpu, m.Label, tech, v, CpuRange, "", 50);
        }
        if (IsStorage(m.Chip) && unit == "°C")
        {
            bool main = m.Label is "Composite" or "temp1" || m.Chip == "drivetemp";
            return Temp(SensorGroup.Storage, main ? L.T("Temperatura do SSD/disco") : L.T("Sensor interno do SSD"), tech, v, StorageRange,
                main ? L.T("SSDs funcionam bem até uns 70 °C; acima disso alguns ficam mais lentos para esfriar.") : "", main ? 0 : 5);
        }
        if (m.Device.StartsWith("gpu:", StringComparison.Ordinal))
        {
            return m.Channel switch
            {
                "load" => Plain(SensorGroup.Gpu, L.T("Uso"), tech, value, v, L.T("Quanto da placa de vídeo está ocupado agora."), 0),
                "temp1" => Temp(SensorGroup.Gpu, L.T("Temperatura"), tech, v, GpuRange, L.T("Placas de vídeo costumam passar de 70 °C em jogos; perto de 95 °C reduzem a velocidade."), 1),
                "temp2" => Temp(SensorGroup.Gpu, L.T("Ponto mais quente"), tech, v, GpuRange, L.T("O ponto mais quente do chip; normalmente 10 a 20 °C acima da temperatura geral."), 2),
                "temp3" => Temp(SensorGroup.Gpu, L.T("Temperatura da memória de vídeo"), tech, v, GpuRange, "", 3),
                "freq1" => Plain(SensorGroup.Gpu, L.T("Velocidade (clock)"), tech, value, v, L.T("Sobe quando há trabalho e cai em repouso para economizar energia."), 4),
                "freq2" => Plain(SensorGroup.Gpu, L.T("Velocidade da memória"), tech, value, v, "", 5),
                "power1" => Plain(SensorGroup.Gpu, L.T("Consumo"), tech, value, v, "", 6),
                "fan1" or "fan_pct" => Plain(SensorGroup.Gpu, L.T("Ventoinha da placa"), tech, v is 0 ? L.T("parada") : value, v, "", 7),
                "vram_used" => Plain(SensorGroup.Gpu, L.T("Memória de vídeo em uso"), tech, value, v, "", 8),
                "vram_total" => Plain(SensorGroup.Gpu, L.T("Memória de vídeo total"), tech, value, v, "", 9),
                _ => Plain(SensorGroup.Gpu, m.Label, tech, value, v, "", 20),
            };
        }
        if (unit == "RPM" || m.Channel.StartsWith("fan", StringComparison.Ordinal))
        {
            string n = m.Channel.Length > 3 ? m.Channel[3..] : "";
            return Plain(SensorGroup.Fans, L.T("Ventoinha ") + n, tech, v is 0 ? L.T("parada") : value, v,
                v is 0 ? L.T("Normal com o PC frio: muitas ventoinhas só ligam quando a temperatura sobe.") : "", 0);
        }
        if (m.Device.StartsWith("PowerSupply/", StringComparison.Ordinal))
        {
            return m.Channel switch
            {
                "capacity" => Plain(SensorGroup.Battery, L.T("Carga da bateria"), tech, value, v, "", 0),
                "voltage_now" => Plain(SensorGroup.Battery, L.T("Tensão da bateria"), tech, value, v, "", 3),
                "power_now" => Plain(SensorGroup.Battery, L.T("Consumo agora"), tech, value, v, L.T("Quanto o notebook está gastando da bateria neste momento."), 2),
                "cycle_count" => Plain(SensorGroup.Battery, L.T("Ciclos de carga"), tech, value, v, L.T("Quantas cargas completas a bateria já fez. Muitos notebooks informam 0 mesmo com uso."), 6),
                "energy_now" => Plain(SensorGroup.Battery, L.T("Energia guardada agora"), tech, value, v, "", 4),
                "energy_full" => Plain(SensorGroup.Battery, L.T("Capacidade atual (cheia)"), tech, value, v, "", 5),
                "energy_full_design" => Plain(SensorGroup.Battery, L.T("Capacidade de fábrica"), tech, value, v, "", 7),
                "online" => null, // the charger comes from the power supply list
                _ => Plain(SensorGroup.Battery, m.Label, tech, value, v, "", 20),
            };
        }
        if (m.Chip.StartsWith("BAT", StringComparison.Ordinal))
            return m.Channel.StartsWith("in", StringComparison.Ordinal) ? Plain(SensorGroup.Battery, L.T("Tensão da bateria"), tech, value, v, "", 3)
                : Plain(SensorGroup.Battery, m.Label, tech, value, v, "", 20);
        if (unit == "°C")
        {
            // ACPI and thermal zones: board or chipset sensors; some report placeholder values (0 °C, 10 °C) that mean "not wired".
            string name = m.Chip switch
            {
                "acpitz" => L.T("Sensor da placa-mãe (ACPI)"),
                "TCPU" or "x86_pkg_temp" or "cpu_thermal" => L.T("Processador (outra leitura)"),
                "pch_skylake" or "pch_cannonlake" or "pch_tigerlake" or "pch_cometlake" => L.T("Chipset da placa-mãe"),
                "iwlwifi_1" => L.T("Placa Wi-Fi"),
                _ => L.T("Sensor de temperatura ") + m.Chip,
            };
            if (v is < 15) return Plain(SensorGroup.Board, name, tech, value, v, L.T("Valor muito baixo: provavelmente este sensor não está ligado a nada."), 40);
            return Temp(SensorGroup.Board, name, tech, v, BoardRange, "", 10);
        }
        return Plain(SensorGroup.Board, m.Label, tech, value, v, "", 50);
    }

    /// <summary>The battery's health (current full capacity ÷ factory capacity) and duplicate voltage rows.</summary>
    private static void Fold(List<FriendlyReading> items)
    {
        var full = items.FirstOrDefault(i => i.Technical.EndsWith("energy_full", StringComparison.Ordinal));
        var design = items.FirstOrDefault(i => i.Technical.EndsWith("energy_full_design", StringComparison.Ordinal));
        if (full?.Number is { } f && design?.Number is > 0 and { } d)
        {
            double health = Math.Min(100, 100 * f / d);
            items.Add(new(SensorGroup.Battery, L.T("Saúde da bateria"), L.T("energy_full ÷ energy_full_design"), health.ToString("0", CultureInfo.InvariantCulture) + "%", health,
                health < 60 ? SensorLevel.Hot : SensorLevel.Normal,
                health >= 80 ? L.T("Boa: a bateria ainda guarda quase tudo o que guardava nova.") : health >= 60 ? L.T("Já perdeu parte da capacidade, o que é normal com o tempo.") : L.T("Gasta: a bateria dura bem menos que nova. Considere trocar."),
                L.T("Quanto a bateria ainda guarda comparado a quando era nova."), null, 1));
        }
        // The battery voltage may come twice (hwmon and power_supply): keep one.
        var volts = items.Where(i => i.Name == L.T("Tensão da bateria")).ToList();
        foreach (var extra in volts.Skip(1)) items.Remove(extra);
    }

    private static FriendlyReading Temp(SensorGroup g, string name, string tech, double? v, SensorRange range, string help, int order)
    {
        var level = v is not { } t ? SensorLevel.None : t >= range.Critical ? SensorLevel.Critical : t >= range.Hot ? SensorLevel.Hot : SensorLevel.Normal;
        string verdict = level switch
        {
            SensorLevel.Normal => L.T("Normal."),
            SensorLevel.Hot => L.T("Quente: normal só sob carga pesada (jogo, compilação). Se estiver parado, confira a ventilação."),
            SensorLevel.Critical => L.T("Muito quente: a peça já reduz a velocidade para se proteger. Limpe as saídas de ar e evite carga pesada."),
            _ => "",
        };
        return new(g, name, tech, Format(v, "°C"), v, level, verdict, help, range, order);
    }

    private static FriendlyReading Plain(SensorGroup g, string name, string tech, string value, double? v, string help, int order) =>
        new(g, name, tech, value, v, SensorLevel.None, "", help, null, order);

    internal static string Format(double? v, string unit)
    {
        if (v is not { } x) return "—";
        string n = unit switch
        {
            "°C" or "V" => x.ToString("0.0", CultureInfo.CurrentCulture),
            "Wh" or "W" => x.ToString("0.0", CultureInfo.CurrentCulture),
            _ => x.ToString("0", CultureInfo.CurrentCulture),
        };
        return unit switch { "%" => n + "%", "cycles" => n, "boolean" => x > 0 ? L.T("sim") : L.T("não"), "" => n, _ => n + " " + unit };
    }
}
