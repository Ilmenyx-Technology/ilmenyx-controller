// New Ilmenyx additions, adapted from the logic of the original SystemInfoCollector.CollectRam (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;
using System.Text.RegularExpressions;
using Ilmenyx.Contracts;

namespace Ilmenyx.Core;

/// <summary>Builds the RAM section (summary, EXPO/XMP and channel estimates, modules). Pure: no I/O, no Windows or Linux dependency.</summary>
public static partial class RamInfoBuilder
{
    [GeneratedRegex(@"(?<!\d)(2133|2400|2666|2933|3000|3200|3466|3600|3733|3800|4000|4133|4266|4400|4800|5200|5600|5800|6000|6200|6400|6600|6800|7000|7200|7400|7600|7800|8000|8200|8400|8800)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SpeedInPart();
    [GeneratedRegex(@"(?i)(?:channel|chan|ch)[\s_-]*([A-H0-9])|dimm[\s_-]*([A-H])\d|(?:^|[^A-Za-z])([A-H])[12](?:$|[^0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelInLocator();

    public static string Bytes(double b)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"]; int i = 0;
        while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
        return b.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[i];
    }
    private static string? ChannelOf(string locator)
    {
        var m = ChannelInLocator().Match(locator);
        if (!m.Success) return null;
        for (int g = 1; g <= 3; g++) if (m.Groups[g].Success) return m.Groups[g].Value.ToUpperInvariant();
        return null;
    }
    private static string Volt(double? v) => v is { } x ? x.ToString("0.00#", CultureInfo.InvariantCulture) + " V" : "—";

    /// <param name="usedBytes">Currently used RAM, or null. <param name="inventory">null when SMBIOS data was not provided.</param>
    public static InfoNode Build(long? totalBytes, long? usedBytes, long? swapTotal, long? swapUsed, MemoryInventory? inventory)
    {
        var n = new InfoNode { Header = "Memória RAM" };
        var modules = inventory?.Modules ?? [];
        ulong installed = 0; foreach (var m in modules) installed += m.Capacity;
        n.Add("Total visível ao sistema", totalBytes is { } t ? Bytes(t) : "—");
        if (installed > 0) n.Add("Total instalado (SMBIOS)", Bytes(installed));
        var sum = n.Add("Resumo");
        if (usedBytes is { } u && totalBytes is > 0) sum.Add("Em uso (no momento da leitura)", $"{Bytes(u)} de {Bytes(totalBytes.Value)} ({((double)u / totalBytes.Value * 100).ToString("0", CultureInfo.InvariantCulture)}%)");
        if (swapTotal is > 0 && swapUsed is { } su) sum.Add("Swap em uso", $"{Bytes(su)} de {Bytes(swapTotal.Value)}");
        if (inventory is null || modules.Count == 0)
        {
            sum.Add("Módulos, velocidade, canais e perfil EXPO/XMP",
                "O Linux só expõe esses dados do SMBIOS ao root. Rode `sudo dmidecode -t memory > arquivo.txt` e importe o arquivo abaixo; o Ilmenyx não executa dmidecode nem pede privilégios.");
            return n;
        }
        sum.Add("Módulos instalados", inventory.Array.Slots > 0 ? $"{modules.Count} de {inventory.Array.Slots} slots" : modules.Count.ToString(CultureInfo.InvariantCulture));
        if (inventory.Array.MaxBytes > 0) sum.Add("Capacidade máxima suportada pela placa", Bytes(inventory.Array.MaxBytes));
        sum.Add("Tipo", string.Join(" / ", modules.Select(m => m.Type).Distinct()));
        int conf = modules.Where(m => m.ConfiguredSpeed > 0).Select(m => m.ConfiguredSpeed).DefaultIfEmpty(0).Min();
        int rated = modules.Select(m => m.RatedSpeed).DefaultIfEmpty(0).Max();
        sum.Add("Velocidade em uso", conf > 0 ? $"{conf} MT/s" : "—");
        sum.Add("Velocidade nominal reportada pelo módulo", rated > 0 ? $"{rated} MT/s" : "—");
        int label = 0;
        foreach (var m in modules) { var mt = SpeedInPart().Match(m.Part); if (mt.Success) label = Math.Max(label, int.Parse(mt.Value, CultureInfo.InvariantCulture)); }
        string profile = conf <= 0 ? "Não foi possível ler a velocidade em uso."
            : label > 0 && conf < label * 0.95 ? $"Provavelmente DESLIGADO: a RAM roda a {conf} MT/s, mas o part number indica {label} MT/s. Ative EXPO/XMP na BIOS."
            : label > 0 ? $"Provavelmente ATIVO: roda a {conf} MT/s (part number indica {label} MT/s)."
            : rated > 0 && conf < rated ? $"Roda abaixo da velocidade nominal ({conf} de {rated} MT/s). Confira EXPO/XMP na BIOS."
            : "Sem como estimar (o part number não traz a velocidade). Confira na BIOS.";
        sum.Add("Perfil EXPO/XMP (estimado)", profile);
        var channels = modules.Select(m => ChannelOf(m.Locator) ?? ChannelOf(m.Bank)).ToList();
        sum.Add("Canais (estimado)", modules.Count == 1 ? "Canal único (1 módulo). Com 2 módulos iguais a RAM roda em dual channel e fica bem mais rápida."
            : channels.All(c => c != null) ? (channels.Distinct().Count() >= 2 ? $"{channels.Distinct().Count()} canais (dual channel ou mais)" : "Canal único: os módulos estão no mesmo canal. Mude para os slots recomendados no manual da placa.")
            : $"Não deu para identificar pelos nomes dos slots ({string.Join(", ", modules.Select(m => m.Locator))}). Veja o manual da placa.");
        sum.Add("ECC", modules.Any(m => m.TotalWidth > m.DataWidth && m.DataWidth > 0) ? "Sim (correção de erros)" : "Não");
        var volts = modules.Select(m => Volt(m.ConfiguredVolt)).Where(v => v != "—").Distinct().ToList();
        if (volts.Count > 0) sum.Add("Tensão em uso", string.Join(" / ", volts));
        sum.Add("Timings (CL, tRCD, tRP...)", "O SMBIOS não expõe. Veja na BIOS ou no SPD com ferramentas dedicadas.");
        foreach (var m in modules)
        {
            var slot = n.Add($"Módulo — {m.Bank} / {m.Locator}");
            slot.Add("Capacidade", Bytes(m.Capacity)); slot.Add("Tipo", m.Type); slot.Add("Formato", m.Form); slot.Add("Ranks", m.Rank);
            slot.Add("Velocidade em uso", m.ConfiguredSpeed > 0 ? $"{m.ConfiguredSpeed} MT/s" : "—");
            slot.Add("Velocidade nominal", m.RatedSpeed > 0 ? $"{m.RatedSpeed} MT/s" : "—");
            slot.Add("Tensão em uso", Volt(m.ConfiguredVolt));
            slot.Add("Faixa de tensão do módulo", m.MinVolt is not null && m.MaxVolt is not null ? $"{Volt(m.MinVolt)} – {Volt(m.MaxVolt)}" : "—");
            slot.Add("Largura de dados / total", m.DataWidth > 0 ? $"{m.DataWidth} / {m.TotalWidth} bits" : "—");
            slot.Add("Fabricante", m.Maker); slot.Add("Part number", m.Part);
            slot.Add("Nº de série", "omitido por privacidade");
        }
        return n;
    }
}
