// New Ilmenyx additions, adapted from the logic of the original BatteryInfo.Read (MIT, see NOTICE): see LICENSING.md.
// Wear and verdict are estimates from the readings the firmware reports, exactly as in the original.
using System.Globalization;

namespace Ilmenyx.Core;

/// <summary>One battery as read from the OS. Energies in µWh, voltage in µV, power in µW; null when not reported.</summary>
public sealed record BatteryInput(string Name, long? DesignEnergyUwh, long? FullEnergyUwh, long? NowEnergyUwh, long? Cycles, long? Percent, string Status,
    long? VoltageUv, long? PowerUw, string? Manufacturer, string? Model, string? Technology);

public static class BatteryEvaluator
{
    public static string ChemistryOf(string? technology) => technology?.Trim().ToLowerInvariant() switch
    {
        "li-ion" or "lion" => "Íon-lítio", "li-poly" or "lip" or "li-polymer" => "Polímero de lítio", "nimh" => "NiMH", "nicd" => "NiCd", "life" => "Fosfato de ferro-lítio", "pb" or "lead" => "Chumbo-ácido", _ => "—"
    };
    public static string StatusOf(string status) => status.Trim() switch
    {
        "Charging" => "Carregando", "Discharging" => "Descarregando", "Full" => "Totalmente carregada", "Not charging" => "Na tomada (sem carregar)", _ => "—"
    };

    public static IReadOnlyList<HealthCard> Evaluate(IReadOnlyList<BatteryInput> batteries, bool? mainsOnline, string? plan, IReadOnlyList<KeyValuePair<string, string>> powerSensors)
    {
        var cards = new List<HealthCard>();
        bool onAc = mainsOnline == true || (mainsOnline is null && batteries.Any(b => b.Status is "Charging" or "Full" or "Not charging"));
        string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        string N0(double v) => v.ToString("0", CultureInfo.InvariantCulture);
        for (int i = 0; i < batteries.Count; i++)
        {
            var b = batteries[i];
            long? cycles = b.Cycles == 0 ? null : b.Cycles; // many vendors report 0 for "unknown"
            double? wear = b.DesignEnergyUwh is > 0 && b.FullEnergyUwh is > 0 ? Math.Max(0, 100.0 * (1 - (double)b.FullEnergyUwh.Value / b.DesignEnergyUwh.Value)) : null;
            var reasons = new List<string>(); var verdict = HealthVerdict.Good; string label = "SAUDÁVEL";
            if (wear == null) { verdict = HealthVerdict.Unknown; label = "SEM DADOS"; reasons.Add("O sistema não informou a capacidade de projeto ou a atual, então o desgaste não pôde ser calculado."); }
            else if (wear >= 50) { verdict = HealthVerdict.Critical; label = "DESGASTADA"; reasons.Add($"Perdeu {N0(wear.Value)}% da capacidade original: a autonomia é metade do que era. Vale trocar a bateria."); }
            else if (wear >= 30) { verdict = HealthVerdict.Warning; label = "ATENÇÃO"; reasons.Add($"Perdeu {N0(wear.Value)}% da capacidade original. Ainda funciona, mas a autonomia já caiu bastante."); }
            else if (wear >= 15) reasons.Add($"Desgaste de {N0(wear.Value)}%, normal para uma bateria com uso.");
            else reasons.Add($"Desgaste de apenas {N0(wear.Value)}%: bateria em ótimo estado.");
            if (cycles is >= 1000 && verdict == HealthVerdict.Good) { verdict = HealthVerdict.Warning; label = "ATENÇÃO"; }
            if (cycles is >= 800) reasons.Add($"{cycles} ciclos de carga: perto do fim da vida útil típica das baterias de íon-lítio (500 a 1000).");
            if (b.Percent is < 20 && !onAc) reasons.Add("Carga baixa e fora da tomada.");

            var m = new List<KeyValuePair<string, string>>();
            void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) m.Add(new(k, v)); }
            Add("Carga atual", b.Percent != null ? $"{b.Percent}%" : null);
            Add("Estado", StatusOf(b.Status) is var s1 && s1 != "—" ? s1 : null);
            bool discharging = b.Status == "Discharging", charging = b.Status == "Charging";
            if (discharging && b.NowEnergyUwh is > 0 && b.PowerUw is > 0)
            {
                double minutes = (double)b.NowEnergyUwh.Value / b.PowerUw.Value * 60;
                if (minutes is > 0 and < 60 * 100) Add("Autonomia estimada", $"{(int)(minutes / 60)}h {(int)(minutes % 60):00}min");
            }
            Add("Capacidade de projeto", b.DesignEnergyUwh != null ? F1(b.DesignEnergyUwh.Value / 1_000_000.0) + " Wh" : null);
            Add("Capacidade máxima atual", b.FullEnergyUwh != null ? F1(b.FullEnergyUwh.Value / 1_000_000.0) + " Wh" : null);
            Add("Desgaste", wear != null ? F1(wear.Value) + "%" : null);
            Add("Ciclos de carga", cycles?.ToString(CultureInfo.InvariantCulture));
            if (b.VoltageUv is > 0) Add("Tensão", (b.VoltageUv.Value / 1_000_000.0).ToString("0.00", CultureInfo.InvariantCulture) + " V");
            if (charging && b.PowerUw is > 0) Add("Potência de carga", F1(b.PowerUw.Value / 1_000_000.0) + " W");
            if (discharging && b.PowerUw is > 0) Add("Consumo atual", F1(b.PowerUw.Value / 1_000_000.0) + " W");
            Add("Fabricante", b.Manufacturer); Add("Modelo", b.Model);
            Add("Química", ChemistryOf(b.Technology) is var ch && ch != "—" ? ch : null);
            cards.Add(new HealthCard
            {
                Device = b.Name, Title = batteries.Count > 1 ? $"Bateria {i + 1}" : "Bateria",
                Meta = (b.Manufacturer ?? "") + (b.Percent != null ? $" · {b.Percent}% de carga" : ""),
                Verdict = verdict, Label = label, Reasons = string.Join(" ", reasons), Metrics = m
            });
        }
        // Power card (exists on every PC): source, active profile, sensor consumption.
        var em = new List<KeyValuePair<string, string>> { new("Fonte de energia", batteries.Count == 0 ? "Tomada (PC de mesa)" : onAc ? "Tomada" : "Bateria") };
        if (plan != null) em.Add(new("Perfil de energia", plan));
        foreach (var s in powerSensors.Take(6)) em.Add(s);
        bool high = plan != null && new[] { "alto", "desempenho", "performance", "máximo" }.Any(w => plan.Contains(w, StringComparison.OrdinalIgnoreCase));
        string advice = plan != null && !high ? " Para jogos e benchmarks, o perfil Alto desempenho evita que o PC diminua o clock." : "";
        cards.Add(new HealthCard
        {
            Device = "energia", Title = "Energia", Meta = batteries.Count == 0 ? "Nenhuma bateria detectada" : "Fonte, perfil e consumo",
            Verdict = HealthVerdict.Good, Label = "EM USO",
            Reasons = (plan != null ? $"Perfil de energia ativo: {plan}." : "Perfil de energia não identificado (power-profiles-daemon ausente ou inacessível).") + advice
                      + (batteries.Count == 0 ? " O consumo da fonte (PSU) não é medido pelo sistema; os valores abaixo vêm de sensores de potência do kernel, quando existem." : ""),
            Metrics = em
        });
        return cards;
    }
}
