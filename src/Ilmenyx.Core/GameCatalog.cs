// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Relative GPU/CPU performance tables ported from the original GameCatalog (MIT, see NOTICE): see LICENSING.md.
// The FPS estimate (games, curves, Estimate) was removed on 2026-11-23; the tables now feed "Meu PC roda isso?" (docs/meu-pc-roda.md).
namespace Ilmenyx.Core;

public static class GameCatalog
{
    // Desempenho relativo em jogos (raster), RTX 4070 = 100. Aproximado. Reajustado em 2026-11-10 junto com as curvas por jogo (removidas em 2026-11-23):
    // GPUs com 8+ pontos de benchmark limitados pela GPU recebem o índice que melhor explica esses pontos; as com menos (RTX 4090/4080,
    // RX 7900 XTX: em 1080p a CPU limita) ficam onde estavam em relação à RTX 4070; as sem dados seguem a irmã da mesma família.
    // Método e números: docs/meu-pc-roda.md (histórico no git).
    private static readonly (string Key, double Index)[] GpuIndex =
    {
        (L.T("RTX 5090"), 265), (L.T("RTX 5080"), 175), (L.T("RTX 5070 Ti"), 150), (L.T("RTX 5070"), 118), (L.T("RTX 5060 Ti"), 80), (L.T("RTX 5060"), 74),
        (L.T("RTX 4090"), 210), (L.T("RTX 4080 SUPER"), 160), (L.T("RTX 4080"), 157), (L.T("RTX 4070 Ti SUPER"), 136), (L.T("RTX 4070 Ti"), 124),
        (L.T("RTX 4070 SUPER"), 116), (L.T("RTX 4070"), 100), (L.T("RTX 4060 Ti"), 69), (L.T("RTX 4060"), 61),
        (L.T("RTX 3090 Ti"), 109), (L.T("RTX 3090"), 101), (L.T("RTX 3080 Ti"), 99), (L.T("RTX 3080"), 91), (L.T("RTX 3070 Ti"), 75), (L.T("RTX 3070"), 70),
        (L.T("RTX 3060 Ti"), 65), (L.T("RTX 3060"), 51), (L.T("RTX 3050"), 34), (L.T("GTX 1660"), 31), (L.T("GTX 1650"), 21),
        ("RX 9070 XT", 133), ("RX 9070", 116), ("RX 7900 XTX", 170), ("RX 7900 XT", 150), ("RX 7900 GRE", 106),
        ("RX 7800 XT", 95), ("RX 7700 XT", 81), ("RX 7600 XT", 64), ("RX 7600", 62),
        ("RX 6950 XT", 101), ("RX 6800 XT", 90), ("RX 6700 XT", 66), ("RX 6600", 46),
        ("Arc B580", 57), ("Arc A770", 49),
        ("Radeon(TM) Graphics", 5.3), ("Radeon Graphics", 5.3),
        // Ilmenyx additions (2026-11-07): older desktop, laptop and integrated GPUs, so common Linux machines are not left out
        // (approximate relative gaming performance, same scale: RTX 4070 = 100).
        (L.T("RTX 2080 Ti"), 75), (L.T("RTX 2080 SUPER"), 58), (L.T("RTX 2080"), 54), (L.T("RTX 2070 SUPER"), 52), (L.T("RTX 2070"), 45), (L.T("RTX 2060 SUPER"), 45), (L.T("RTX 2060"), 40),
        (L.T("GTX 1660 Ti"), 35), (L.T("GTX 1660 SUPER"), 33), (L.T("GTX 1080 Ti"), 44), (L.T("GTX 1080"), 39), (L.T("GTX 1070 Ti"), 36), (L.T("GTX 1070"), 33),
        (L.T("GTX 1060"), 24), (L.T("GTX 1050 Ti"), 14.1), (L.T("GTX 1050"), 11.7), ("GT 1030", 5.8), (L.T("GTX 980 Ti"), 30), (L.T("GTX 980"), 24), (L.T("GTX 970"), 22), (L.T("GTX 960"), 14.6),
        (L.T("RTX 4060 Laptop"), 52), (L.T("RTX 4050 Laptop"), 45), (L.T("RTX 3060 Laptop"), 44), (L.T("RTX 3050 Laptop"), 24), (L.T("RTX 3050 Ti Laptop"), 30), (L.T("GTX 1650 Ti"), 22),
        ("MX570", 14.9), ("MX550", 16.5), ("MX450", 9.4), ("MX350", 8.1), ("MX330", 6.1), ("MX250", 5.9), ("MX230", 4.1), ("MX150", 5.8), ("MX130", 3.7), ("MX110", 2.9),
        ("940MX", 3.3), ("940M", 2.8), ("930MX", 2.9), ("930M", 2.9), ("920MX", 2.4), (L.T("GTX 950M"), 5.9), (L.T("GTX 960M"), 8.1), (L.T("GTX 1050 Mobile"), 11.1),
        ("RX 6800", 79), ("RX 6750 XT", 69), ("RX 6650 XT", 55), ("RX 6600 XT", 51), ("RX 6500 XT", 29), ("RX 6400", 22),
        ("RX 5700 XT", 47), ("RX 5700", 43), ("RX 5600 XT", 39), ("RX 5500 XT", 26), ("RX 590", 25), ("RX 580", 24), ("RX 570", 25), ("RX 560", 12.5), ("RX 550", 8.7),
        ("Arc A750", 48), ("Arc A580", 44), ("Arc A380", 19.5), ("Iris Xe", 8.1), ("Iris Plus", 3.5), ("UHD Graphics 770", 2.7), ("UHD Graphics 630", 2), ("UHD Graphics 620", 1.4),
        ("HD Graphics 630", 2), ("HD Graphics 620", 1.4), ("HD Graphics 520", 1.1), ("UHD Graphics", 1.4),
        ("Radeon 890M", 19.3), ("Radeon 880M", 14.7), ("Radeon 780M", 16), ("Radeon 760M", 12.7), ("Radeon 680M", 14), ("Radeon 660M", 8.9), ("Radeon 610M", 3.9), (L.T("Mendocino"), 3.9),
        ("Vega 8", 6.9), ("Vega 7", 5.3), ("Vega 6", 4.7), ("Vega 3", 1.7), ("Steam Deck", 11.4), ("VanGogh", 11.4), ("Van Gogh", 11.4),
    };

    // Desempenho relativo em jogos (limitado pela CPU), Ryzen 7 7700X = 100. Aproximado.
    private static readonly (string Key, double Index)[] CpuIndex =
    {
        ("9950X3D", 138), ("9800X3D", 140), ("7950X3D", 126), ("7800X3D", 128), ("5800X3D", 95), ("5700X3D", 90),
        ("9950X", 112), ("9900X", 110), ("9700X", 108), ("9600X", 104), ("7950X", 104), ("7900X", 102), ("7700X", 100),
        ("7700", 97), ("7600X", 96), ("7600", 93), ("5950X", 82), ("5900X", 81), ("5800X", 80), ("5700X", 77), ("5600X", 76), ("5600", 73),
        ("285K", 112), ("265K", 108), ("245K", 100), ("14900K", 116), ("14700K", 112), ("14600K", 104), ("13900K", 113),
        ("13700K", 108), ("13600K", 101), ("13400", 84), ("12900K", 98), ("12700K", 95), ("12600K", 90), ("12400", 78),
        // Ilmenyx additions (2026-11-07): older desktop and laptop CPUs (approximate, same scale: Ryzen 7 7700X = 100).
        ("11700K", 78), ("11400", 70), ("10700K", 76), ("10400", 62), ("9900K", 78), ("9700K", 74), ("9600K", 66), ("9400F", 58), ("9400", 58),
        ("8700K", 68), ("8700", 66), ("8400", 58), ("7700K", 60), ("6700K", 55), ("4790K", 45),
        ("13900H", 95), ("13700H", 90), ("13500H", 86), ("12900H", 92), ("12700H", 85), ("12650H", 80), ("12500H", 78), ("12450H", 72),
        ("1360P", 66), ("1260P", 64), ("1255U", 63), ("1235U", 60), ("1165G7", 58), ("1135G7", 55), ("1065G7", 46), ("10750H", 58), ("10510U", 45),
        ("9750H", 55), ("8750H", 52), ("8565U", 42), ("8550U", 42), ("8250U", 40), ("7700HQ", 45), ("7500U", 36), ("7200U", 33), ("6500U", 30),
        ("3900X", 67), ("3700X", 65), ("3600", 62), ("2700X", 54), ("2600", 50), ("1600", 45), ("5600G", 66), ("5700G", 70), ("5500", 70), ("4600G", 58),
        ("7945HX", 115), ("7940HS", 86), ("7840HS", 85), ("7840U", 78), ("7735HS", 74), ("7640HS", 76), ("7535HS", 66), ("6900HS", 74), ("6800H", 72),
        ("5900HX", 70), ("5800H", 66), ("5600H", 62), ("4800H", 58), ("4600H", 55), ("7730U", 52), ("7530U", 48), ("7520U", 34), ("7320U", 30),
        ("5700U", 50), ("5500U", 45), ("4700U", 44), ("4500U", 40), ("3700U", 32), ("3500U", 30), (L.T("Custom APU 0405"), 40),
    };

    public static double? GpuPerf(string gpuName) => GpuMatch(gpuName)?.Index;
    public static double? CpuPerf(string cpuName) => Lookup(CpuIndex, cpuName)?.Index;

    /// <summary>Table entry that matched the GPU name (the key tells how specific the match was: see <see cref="FpsAccuracy"/>).</summary>
    public static (string Key, double Index)? GpuMatch(string gpuName) => Lookup(GpuIndex, gpuName) ?? Lookup(GpuIndex, ShortGpuName(gpuName));
    public static (string Key, double Index)? CpuMatch(string cpuName) => Lookup(CpuIndex, ShortCpuName(cpuName)) ?? Lookup(CpuIndex, cpuName);

    // Nomes genéricos (vários chips diferentes) nunca servem para comparar requisitos.
    private static readonly string[] AmbiguousKeys = ["Radeon(TM) Graphics", "Radeon Graphics"];

    /// <summary>
    /// Casamento estrito para comparar com requisitos de jogos: a chave precisa estar inteira ("1600" não casa em "i5-11600K"), sem sufixo que
    /// muda o chip (Ti, SUPER, XT, Laptop, Mobile) que a tabela não conhece; nome genérico de iGPU nunca casa. Sem entrada = null, nunca um palpite.
    /// </summary>
    public static (string Key, double Index)? GpuMatchWhole(string name) => LookupWhole(GpuIndex, name);
    public static (string Key, double Index)? CpuMatchWhole(string name) => LookupWhole(CpuIndex, ShortCpuName(name));

    private static (string Key, double Index)? LookupWhole((string Key, double Index)[] table, string name)
    {
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\b(GTX|RTX|GT|RX)(?=\d)", "$1 ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var hit = table.Where(t => !AmbiguousKeys.Contains(t.Key) && System.Text.RegularExpressions.Regex.IsMatch(name,
                    @"(?<![A-Za-z0-9])" + System.Text.RegularExpressions.Regex.Escape(t.Key) + @"(?![0-9])(?!\s?(?:Ti|SUPER|XTX|XT|GRE|Laptop|Mobile|Max-Q)\b)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            .OrderByDescending(t => t.Key.Length).FirstOrDefault();
        return hit.Key == null ? null : hit;
    }

    /// <summary>A chave mais longa contida no nome vence ("RTX 5070 Ti" antes de "RTX 5070").</summary>
    private static (string Key, double Index)? Lookup((string Key, double Index)[] table, string name)
    {
        var hit = table.Where(t => name.Contains(t.Key, StringComparison.OrdinalIgnoreCase))
                       .OrderByDescending(t => t.Key.Length).FirstOrDefault();
        return hit.Key == null ? null : hit;
    }

    /// <summary>Nomes curtos como a API espera: "RTX 5070", "Ryzen 7 7700X".</summary>
    public static string ShortGpuName(string name)
    {
        string s = name.Replace("(TM)", "").Replace("(R)", "");
        foreach (var w in new[] { "NVIDIA", "GeForce", "AMD", "Intel" }) s = s.Replace(w, "");
        if (s.Contains("Radeon RX")) s = s.Replace("Radeon", "");             // "Radeon RX 9070 XT" → "RX 9070 XT"
        s = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (s.StartsWith("Arc") && s.EndsWith(" Graphics")) s = s[..^9];        // "Arc B580 Graphics" → "Arc B580"
        return s;                                                              // iGPU AMD fica "Radeon Graphics"
    }

    public static string ShortCpuName(string name)
    {
        string s = name.Replace("AMD", "").Replace("Intel(R)", "").Replace("(R)", "").Replace("(TM)", "").Replace("Core(TM)", "Core");
        int i = s.IndexOf("-Core", StringComparison.OrdinalIgnoreCase);
        if (i > 0) s = s[..s.LastIndexOf(' ', i)];       // "Ryzen 7 7700X 8-Core Processor" → "Ryzen 7 7700X"
        s = s.Replace("Processor", "").Replace("CPU", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\d+th Gen\s*", ""); // "13th Gen Core i7-13700K"
        int at = s.IndexOf('@');
        if (at > 0) s = s[..at];                          // "Core i5-12400F CPU @ 2.50GHz"
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
