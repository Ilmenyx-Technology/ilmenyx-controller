// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
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
        "li-ion" or "lion" => L.T("Íon-lítio"), "li-poly" or "lip" or "li-polymer" => L.T("Polímero de lítio"), "nimh" => "NiMH", "nicd" => "NiCd", "life" => L.T("Fosfato de ferro-lítio"), "pb" or "lead" => L.T("Chumbo-ácido"), _ => "—"
    };
    public static string StatusOf(string status) => status.Trim() switch
    {
        "Charging" => L.T("Carregando"), "Discharging" => L.T("Descarregando"), "Full" => L.T("Totalmente carregada"), "Not charging" => L.T("Na tomada (sem carregar)"), _ => "—"
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
            var reasons = new List<string>(); var verdict = HealthVerdict.Good; string label = L.T("SAUDÁVEL");
            if (wear == null) { verdict = HealthVerdict.Unknown; label = L.T("SEM DADOS"); reasons.Add(L.T("O sistema não informou a capacidade de projeto ou a atual, então o desgaste não pôde ser calculado.")); }
            else if (wear >= 50) { verdict = HealthVerdict.Critical; label = L.T("DESGASTADA"); reasons.Add(L.T($"Perdeu {N0(wear.Value)}% da capacidade original: a autonomia é metade do que era. Vale trocar a bateria.")); }
            else if (wear >= 30) { verdict = HealthVerdict.Warning; label = L.T("ATENÇÃO"); reasons.Add(L.T($"Perdeu {N0(wear.Value)}% da capacidade original. Ainda funciona, mas a autonomia já caiu bastante.")); }
            else if (wear >= 15) reasons.Add(L.T($"Desgaste de {N0(wear.Value)}%, normal para uma bateria com uso."));
            else reasons.Add(L.T($"Desgaste de apenas {N0(wear.Value)}%: bateria em ótimo estado."));
            if (cycles is >= 1000 && verdict == HealthVerdict.Good) { verdict = HealthVerdict.Warning; label = L.T("ATENÇÃO"); }
            if (cycles is >= 800) reasons.Add(L.T($"{cycles} ciclos de carga: perto do fim da vida útil típica das baterias de íon-lítio (500 a 1000)."));
            if (b.Percent is < 20 && !onAc) reasons.Add(L.T("Carga baixa e fora da tomada."));

            var m = new List<KeyValuePair<string, string>>();
            void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) m.Add(new(k, v)); }
            Add(L.T("Carga atual"), b.Percent != null ? $"{b.Percent}%" : null);
            Add(L.T("Estado"), StatusOf(b.Status) is var s1 && s1 != "—" ? s1 : null);
            bool discharging = b.Status == "Discharging", charging = b.Status == "Charging";
            if (discharging && b.NowEnergyUwh is > 0 && b.PowerUw is > 0)
            {
                double minutes = (double)b.NowEnergyUwh.Value / b.PowerUw.Value * 60;
                if (minutes is > 0 and < 60 * 100) Add(L.T("Autonomia estimada"), $"{(int)(minutes / 60)}h {(int)(minutes % 60):00}min");
            }
            Add(L.T("Capacidade de projeto"), b.DesignEnergyUwh != null ? F1(b.DesignEnergyUwh.Value / 1_000_000.0) + " Wh" : null);
            Add(L.T("Capacidade máxima atual"), b.FullEnergyUwh != null ? F1(b.FullEnergyUwh.Value / 1_000_000.0) + " Wh" : null);
            Add(L.T("Desgaste"), wear != null ? F1(wear.Value) + "%" : null);
            Add(L.T("Ciclos de carga"), cycles?.ToString(CultureInfo.InvariantCulture));
            if (b.VoltageUv is > 0) Add(L.T("Tensão"), (b.VoltageUv.Value / 1_000_000.0).ToString("0.00", CultureInfo.InvariantCulture) + " V");
            if (charging && b.PowerUw is > 0) Add(L.T("Potência de carga"), F1(b.PowerUw.Value / 1_000_000.0) + " W");
            if (discharging && b.PowerUw is > 0) Add(L.T("Consumo atual"), F1(b.PowerUw.Value / 1_000_000.0) + " W");
            Add(L.T("Fabricante"), b.Manufacturer); Add(L.T("Modelo"), b.Model);
            Add(L.T("Química"), ChemistryOf(b.Technology) is var ch && ch != "—" ? ch : null);
            cards.Add(new HealthCard
            {
                Device = b.Name, Title = batteries.Count > 1 ? L.T($"Bateria {i + 1}") : L.T("Bateria"),
                Meta = (b.Manufacturer ?? "") + (b.Percent != null ? L.T($" · {b.Percent}% de carga") : ""),
                Verdict = verdict, Label = label, Reasons = string.Join(" ", reasons), Metrics = m
            });
        }
        // Power card (exists on every PC): source, active profile, sensor consumption.
        var em = new List<KeyValuePair<string, string>> { new(L.T("Fonte de energia"), batteries.Count == 0 ? L.T("Tomada (PC de mesa)") : onAc ? L.T("Tomada") : L.T("Bateria")) };
        if (plan != null) em.Add(new(L.T("Perfil de energia"), plan));
        foreach (var s in powerSensors.Take(6)) em.Add(s);
        bool high = plan != null && new[] { "alto", "desempenho", "performance", L.T("máximo") }.Any(w => plan.Contains(w, StringComparison.OrdinalIgnoreCase));
        string advice = plan != null && !high ? L.T(" Para jogos e benchmarks, o perfil Alto desempenho evita que o PC diminua o clock.") : "";
        cards.Add(new HealthCard
        {
            Device = "energia", Title = L.T("Energia"), Meta = batteries.Count == 0 ? L.T("Nenhuma bateria detectada") : L.T("Fonte, perfil e consumo"),
            Verdict = HealthVerdict.Good, Label = L.T("EM USO"),
            Reasons = (plan != null ? L.T($"Perfil de energia ativo: {plan}.") : L.T("Perfil de energia não identificado (power-profiles-daemon ausente ou inacessível).")) + advice
                      + (batteries.Count == 0 ? L.T(" O consumo da fonte (PSU) não é medido pelo sistema; os valores abaixo vêm de sensores de potência do kernel, quando existem.") : ""),
            Metrics = em
        });
        return cards;
    }
}
