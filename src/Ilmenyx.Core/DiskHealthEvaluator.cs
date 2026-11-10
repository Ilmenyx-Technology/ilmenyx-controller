// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions, adapted from the heuristics of the original DiskHealth.ToCard (MIT, see NOTICE): see LICENSING.md.
// The verdict is an estimate from the available readings, exactly as in the original; it is not a guarantee of health.
using System.Globalization;

namespace Ilmenyx.Core;

public sealed record DiskHealthInput(string Device, string Model, string Firmware, string Bus, string Media, long SizeBytes, string Mounts,
    bool? ReportedPassed, double? Temp, double? Wear, long? PowerOnHours, long? StartStop, long? UncorrectedErrors, long? ReallocatedSectors,
    double? WrittenBytes, double? ReadBytes, long? SpareErrorsNote = null);

public static class DiskHealthEvaluator
{
    public static HealthCard Evaluate(DiskHealthInput r)
    {
        bool isSsd = r.Media == "SSD" || r.Bus == "NVMe", isUsb = r.Bus == "USB";
        var crit = new List<string>(); var warn = new List<string>();
        bool anyData = r.ReportedPassed != null || r.Wear != null || r.Temp != null || r.PowerOnHours != null;
        string N0(double v) => v.ToString("0", CultureInfo.InvariantCulture);

        if (r.ReportedPassed == false) crit.Add("O próprio disco avisa (S.M.A.R.T.) que deve falhar em breve. Faça backup agora.");
        if (r.Wear is double w)
        {
            if (w >= 95) crit.Add($"Desgaste de {N0(w)}%: o SSD está no fim da vida útil. Troque e faça backup.");
            else if (w >= 80) warn.Add($"Desgaste de {N0(w)}%: o SSD já gastou boa parte da vida útil. Planeje a troca.");
        }
        if (r.Temp is double t)
        {
            double okMax = isSsd ? 70 : 55, critMax = isSsd ? 85 : 65;
            if (t >= critMax) crit.Add($"Temperatura de {N0(t)} °C é muito alta para este disco.");
            else if (t >= okMax) warn.Add($"Temperatura de {N0(t)} °C está alta. Melhore a ventilação.");
        }
        if (r.UncorrectedErrors > 0) warn.Add($"Erros não corrigidos/de mídia: {r.UncorrectedErrors:N0}.".Replace(",", "."));

        var v = crit.Count > 0 ? HealthVerdict.Critical : warn.Count > 0 ? HealthVerdict.Warning : anyData ? HealthVerdict.Good : HealthVerdict.Unknown;
        var reasons = crit.Concat(warn).ToList();
        if (reasons.Count == 0)
            reasons.Add(v == HealthVerdict.Good ? "Nenhum problema encontrado nas leituras disponíveis."
                : "Este disco não informa dados de saúde sem SMART (comum em pendrives e alguns HDs externos). Use \"Consultar SMART\" com permissão ou importe o JSON do smartctl.");

        string kind = r.Media.Length > 0 ? r.Media : isSsd ? "SSD" : "";
        var meta = new[] { kind, r.Bus, FmtSize(r.SizeBytes), r.Mounts.Length > 0 ? "Montado em: " + r.Mounts : "" }.Where(x => !string.IsNullOrEmpty(x));

        var metrics = new List<KeyValuePair<string, string>>();
        void M(string key, string? value) { if (value != null) metrics.Add(new(key, value)); }
        M("Temperatura", r.Temp is double tt ? N0(tt) + " °C" : null);
        if (r.Wear is double wr) { M("Desgaste", N0(wr) + "%"); M("Vida útil restante", N0(Math.Max(0, 100 - wr)) + "%"); }
        M("Horas ligado", r.PowerOnHours is long h ? $"{h:N0} h ({h / 24.0:N0} dias)".Replace(",", ".") : null);
        M("Ciclos liga/desliga", r.StartStop is long ss ? ss.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".") : null);
        M("Erros não corrigidos/de mídia", r.UncorrectedErrors is long ue ? ue.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".") : null);
        M("Setores realocados", r.ReallocatedSectors is long rs ? rs.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".") : null);
        M("Total gravado", r.WrittenBytes is double wg && wg > 0 ? FmtSize((long)wg) : null);
        M("Total lido", r.ReadBytes is double rg && rg > 0 ? FmtSize((long)rg) : null);
        M("Falha prevista (S.M.A.R.T.)", r.ReportedPassed is bool ok ? (ok ? "Não" : "SIM") : null);
        M("Firmware", r.Firmware.Length > 0 && r.Firmware != "—" ? r.Firmware : null);

        return new()
        {
            Device = r.Device, Title = r.Model, Meta = string.Join(" · ", meta),
            Verdict = isUsb && !anyData ? HealthVerdict.Unknown : v, Reasons = string.Join(" ", reasons), Metrics = metrics
        };
    }

    /// <summary>Decimal units, as the original FmtSize (disk marketing sizes).</summary>
    public static string FmtSize(long bytes)
    {
        if (bytes <= 0) return "";
        double v = bytes; string[] u = ["B", "KB", "MB", "GB", "TB"]; int i = 0;
        while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
        return v.ToString("0.#", CultureInfo.InvariantCulture) + " " + u[i];
    }
}
