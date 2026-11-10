// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Reference data and estimation logic ported from the original GameCatalog (MIT, see NOTICE): see LICENSING.md.
namespace Ilmenyx.Core;

/// <summary>
/// Jogo do catálogo. Valores de referência APROXIMADOS (compilados de benchmarks públicos) usados só
/// pela estimativa local (calculada no PC, sem internet):
/// GpuRef = FPS médio em 1080p/Alto numa RTX 4070 (índice 100); CpuCap = teto de FPS limitado pela CPU
/// num Ryzen 7 7700X (índice 100); EngineCap = trava de FPS do próprio jogo (0 = sem trava).
/// GpuExp = sensibilidade do jogo à força da GPU (Ilmenyx, 2026-11-08): FPS da GPU = GpuRef × (índice/100)^GpuExp. 1 = proporcional
/// (jogos pesados); menor que 1 = jogo leve, que cai bem menos que o índice em GPUs fracas. Fontes e contas: docs/fps-estimativa.md.
/// </summary>
public record GameInfo(string Name, string Genre, double GpuRef, double CpuCap, double EngineCap = 0, double GpuExp = 1.0);

/// <summary>
/// GPU-limited FPS of one game at 1920×1080 per preset, fitted to measured benchmarks (Notebookcheck's per-game laptop and desktop
/// tables, 2026-11-08): FPS = A × (índice/100)^b for Baixo/Médio/Alto/Ultra. Fitted only on points below 80 % of each column's top
/// (the CPU/engine plateau); 720p/768p columns of older pages converted to 1080p with the resolution curve. Over 2 775 measured
/// points the median error went from 51 % (one reference × preset factor) to 17 %. Method and data: docs/fps-estimativa.md.
/// </summary>
public sealed record PresetCurves(double Low, double LowExp, double Med, double MedExp, double High, double HighExp, double Ultra, double UltraExp, string Source)
{
    /// <summary>GPU FPS at 1080p for <paramref name="preset"/>; a lower preset is never slower than a higher one.</summary>
    public double Fps(double gpuIdx, string preset)
    {
        double x = Math.Max(gpuIdx, 0.01) / 100;
        double ultra = Ultra * Math.Pow(x, UltraExp);
        double high = Math.Max(High * Math.Pow(x, HighExp), ultra);
        double med = Math.Max(Med * Math.Pow(x, MedExp), high);
        double low = Math.Max(Low * Math.Pow(x, LowExp), med);
        return preset switch { "Baixo" => low, "Médio" => med, "Ultra" => ultra, _ => high };
    }
}

public enum Bottleneck { None, Gpu, Cpu, Engine }

public record FpsEstimate(double Avg, double Low1, Bottleneck Limiter);

public static class GameCatalog
{
    // Os nomes seguem a grafia das lojas; sessões medidas casam com eles pelo nome normalizado (FpsAccuracy.LatestSession).
    public static readonly GameInfo[] Games =
    {
        new("Counter-Strike 2", "FPS competitivo", 340, 420),
        // GpuExp 0,73: ajuste aos três níveis oficiais da Riot (30 FPS Intel HD 4000, 60 FPS GT 730, 144 FPS GTX 1050 Ti;
        // support.riotgames.com/en-us/valorant/support-tools/minimum-recommended-pc-specs, lido em 2026-11-08), supondo Alto/1080p.
        new("Valorant", "FPS competitivo", 600, 650, 0, LightValorant),
        new("Fortnite", "Battle royale", 160, 250),
        // GpuExp 0,55: a Riot recomenda Intel UHD 630 para Alto/1920x1080 (support.riotgames.com/en-us/league-of-legends/performance/
        // minimum-and-recommended-system-requirements-league-of-legends, lido em 2026-11-08); supondo "recomendado" = 60 FPS.
        // Antes (expoente 1) a 940MX dava 16–44 FPS no Baixo; o mantenedor observa > 100 FPS nesse hardware.
        new("League of Legends", "MOBA", 420, 480, 0, LightLol),
        new("Dota 2", "MOBA", 260, 280, 0, LightClass), // expoente da classe "leve" (sem fonte oficial com FPS para o jogo)
        new("Apex Legends", "Battle royale", 220, 300),
        new("PUBG: Battlegrounds", "Battle royale", 180, 220),
        new("Call of Duty: Black Ops 6", "FPS", 140, 220),
        new("Call of Duty: Warzone", "Battle royale", 130, 200),
        new("Battlefield 6", "FPS", 120, 170),
        new("Rainbow Six Siege", "FPS tático", 350, 430),
        new("Overwatch 2", "FPS heroico", 280, 380),
        new("Marvel Rivals", "FPS heroico", 110, 160),
        new("Rocket League", "Esporte", 400, 450, 0, LightClass), // idem; requisitos da Epic (GTX 760 / GTX 1060) não dizem FPS
        new("EA Sports FC 25", "Esporte", 220, 260),
        new("Minecraft", "Sandbox", 300, 340, 0, LightClass), // idem
        new("GTA V", "Mundo aberto", 160, 190),
        new("Red Dead Redemption 2", "Mundo aberto", 95, 160),
        new("Cyberpunk 2077", "RPG mundo aberto", 95, 150),
        new("The Witcher 3: Wild Hunt", "RPG", 120, 160),
        new("Elden Ring", "RPG de ação", 110, 140, 60),
        new("Baldur's Gate 3", "RPG", 110, 120),
        new("Hogwarts Legacy", "RPG de ação", 90, 120),
        new("Starfield", "RPG espacial", 65, 90),
        new("Black Myth: Wukong", "Ação", 70, 140),
        new("Alan Wake 2", "Terror / ação", 60, 140),
        new("God of War Ragnarök", "Ação", 110, 170),
        new("Forza Horizon 5", "Corrida", 140, 200),
        new("Helldivers 2", "Cooperativo", 90, 110),
        new("Monster Hunter Wilds", "Ação", 60, 85),
        new("Assassin's Creed Shadows", "Ação mundo aberto", 65, 110),
        new("Borderlands 4", "Looter shooter", 55, 110),
    };

    /// <summary>Expoentes de GPU dos jogos leves (ver GameInfo.GpuExp e docs/fps-estimativa.md, seção "Jogos leves").</summary>
    public const double LightValorant = 0.73, LightLol = 0.55, LightClass = 0.64;

    // Desempenho relativo em jogos (raster), RTX 4070 = 100. Aproximado.
    private static readonly (string Key, double Index)[] GpuIndex =
    {
        ("RTX 5090", 265), ("RTX 5080", 175), ("RTX 5070 Ti", 150), ("RTX 5070", 118), ("RTX 5060 Ti", 95), ("RTX 5060", 85),
        ("RTX 4090", 210), ("RTX 4080 SUPER", 160), ("RTX 4080", 157), ("RTX 4070 Ti SUPER", 136), ("RTX 4070 Ti", 124),
        ("RTX 4070 SUPER", 116), ("RTX 4070", 100), ("RTX 4060 Ti", 82), ("RTX 4060", 70),
        ("RTX 3090 Ti", 135), ("RTX 3090", 125), ("RTX 3080 Ti", 122), ("RTX 3080", 112), ("RTX 3070 Ti", 92), ("RTX 3070", 86),
        ("RTX 3060 Ti", 78), ("RTX 3060", 60), ("RTX 3050", 40), ("GTX 1660", 38), ("GTX 1650", 28),
        ("RX 9070 XT", 160), ("RX 9070", 140), ("RX 7900 XTX", 170), ("RX 7900 XT", 150), ("RX 7900 GRE", 128),
        ("RX 7800 XT", 115), ("RX 7700 XT", 98), ("RX 7600 XT", 72), ("RX 7600", 70),
        ("RX 6950 XT", 125), ("RX 6800 XT", 112), ("RX 6700 XT", 82), ("RX 6600", 58),
        ("Arc B580", 80), ("Arc A770", 68),
        ("Radeon(TM) Graphics", 6), ("Radeon Graphics", 6),
        // Ilmenyx additions (2026-11-07): older desktop, laptop and integrated GPUs, so common Linux machines are not left out
        // (approximate relative gaming performance, same scale: RTX 4070 = 100).
        ("RTX 2080 Ti", 75), ("RTX 2080 SUPER", 65), ("RTX 2080", 61), ("RTX 2070 SUPER", 58), ("RTX 2070", 51), ("RTX 2060 SUPER", 48), ("RTX 2060", 42),
        ("GTX 1660 Ti", 42), ("GTX 1660 SUPER", 41), ("GTX 1080 Ti", 66), ("GTX 1080", 52), ("GTX 1070 Ti", 47), ("GTX 1070", 43),
        ("GTX 1060", 30), ("GTX 1050 Ti", 18), ("GTX 1050", 14), ("GT 1030", 7), ("GTX 980 Ti", 40), ("GTX 980", 32), ("GTX 970", 28), ("GTX 960", 18),
        ("RTX 4060 Laptop", 58), ("RTX 4050 Laptop", 48), ("RTX 3060 Laptop", 50), ("RTX 3050 Laptop", 30), ("RTX 3050 Ti Laptop", 33), ("GTX 1650 Ti", 26),
        ("MX570", 15), ("MX550", 13), ("MX450", 11), ("MX350", 8), ("MX330", 7.5), ("MX250", 7.5), ("MX230", 6), ("MX150", 7), ("MX130", 4.5), ("MX110", 3.5),
        ("940MX", 4.5), ("940M", 4), ("930MX", 3.5), ("930M", 3.5), ("920MX", 3), ("GTX 950M", 9), ("GTX 960M", 11), ("GTX 1050 Mobile", 13),
        ("RX 6800", 98), ("RX 6750 XT", 86), ("RX 6650 XT", 66), ("RX 6600 XT", 64), ("RX 6500 XT", 26), ("RX 6400", 20),
        ("RX 5700 XT", 55), ("RX 5700", 50), ("RX 5600 XT", 46), ("RX 5500 XT", 33), ("RX 590", 32), ("RX 580", 30), ("RX 570", 26), ("RX 560", 13), ("RX 550", 9),
        ("Arc A750", 62), ("Arc A580", 52), ("Arc A380", 22), ("Iris Xe", 7), ("Iris Plus", 4), ("UHD Graphics 770", 4), ("UHD Graphics 630", 3), ("UHD Graphics 620", 2.5),
        ("HD Graphics 630", 3), ("HD Graphics 620", 2.5), ("HD Graphics 520", 2), ("UHD Graphics", 2.5),
        ("Radeon 890M", 30), ("Radeon 880M", 26), ("Radeon 780M", 21), ("Radeon 760M", 17), ("Radeon 680M", 18), ("Radeon 660M", 12), ("Radeon 610M", 3), ("Mendocino", 3),
        ("Vega 8", 6), ("Vega 7", 6), ("Vega 6", 5), ("Vega 3", 2.5), ("Steam Deck", 9), ("VanGogh", 9), ("Van Gogh", 9),
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
        ("5700U", 50), ("5500U", 45), ("4700U", 44), ("4500U", 40), ("3700U", 32), ("3500U", 30), ("Custom APU 0405", 40),
    };

    public static double? GpuPerf(string gpuName) => GpuMatch(gpuName)?.Index;
    public static double? CpuPerf(string cpuName) => Lookup(CpuIndex, cpuName)?.Index;

    /// <summary>Table entry that matched the GPU name (the key tells how specific the match was: see <see cref="FpsAccuracy"/>).</summary>
    public static (string Key, double Index)? GpuMatch(string gpuName) => Lookup(GpuIndex, gpuName) ?? Lookup(GpuIndex, ShortGpuName(gpuName));
    public static (string Key, double Index)? CpuMatch(string cpuName) => Lookup(CpuIndex, ShortCpuName(cpuName)) ?? Lookup(CpuIndex, cpuName);

    /// <summary>A chave mais longa contida no nome vence ("RTX 5070 Ti" antes de "RTX 5070").</summary>
    private static (string Key, double Index)? Lookup((string Key, double Index)[] table, string name)
    {
        var hit = table.Where(t => name.Contains(t.Key, StringComparison.OrdinalIgnoreCase))
                       .OrderByDescending(t => t.Key.Length).FirstOrDefault();
        return hit.Key == null ? null : hit;
    }

    public static double ResolutionFactor(string res) => res switch
    {
        // 720p (Ilmenyx, 2026-11-08): mesma curva do 1440p (0,72 para 0,5625× os pixels → expoente 0,57), 2,25× os pixels → ×1,59.
        "1280x720" => 1.59, "2560x1440" => 0.72, "3840x2160" => 0.42, _ => 1.0
    };

    public static double PresetFactor(string preset) => preset switch
    {
        "Baixo" => 1.6, "Médio" => 1.3, "Ultra" => 0.82, _ => 1.0
    };

    /// <summary>Upscaling (DLSS/FSR) só alivia a GPU; o ganho cresce com a resolução.</summary>
    public static double UpscalingFactor(string upscaling, string res) => upscaling switch
    {
        "Quality" => res == "1920x1080" ? 1.25 : 1.4,
        "Performance" => res == "1920x1080" ? 1.45 : 1.75,
        _ => 1.0
    };

    /// <summary>Measured per-preset curves (see <see cref="PresetCurves"/>). Games not listed (League of Legends, Marvel Rivals, Minecraft:
    /// no usable benchmark table) keep GpuRef × índice^GpuExp × preset factor. Fortnite: the table is from the 2018 build, so only its shape
    /// is used, anchored at the catalog's High level; Warzone uses the Black Ops 6 curves (same engine) scaled 130/140.</summary>
    public static readonly IReadOnlyDictionary<string, PresetCurves> Measured = new Dictionary<string, PresetCurves>(StringComparer.Ordinal)
    {
        ["Counter-Strike 2"] = new(386, 0.73, 343, 0.79, 284, 0.96, 210, 0.99, "cs2"),
        ["Valorant"] = new(161, 0.25, 318, 0.43, 315, 0.48, 258, 0.48, "valorant"),
        ["Fortnite"] = new(142, 0.58, 238, 0.87, 160, 0.94, 124, 0.96, "fortnite"),
        ["Dota 2"] = new(143, 0.29, 143, 0.35, 240, 0.57, 210, 0.56, "dota2"),
        ["Apex Legends"] = new(337, 0.81, 294, 0.90, 245, 0.90, 241, 0.92, "apex"),
        ["PUBG: Battlegrounds"] = new(155, 0.54, 230, 0.81, 226, 0.85, 200, 0.94, "pubg"),
        ["Call of Duty: Black Ops 6"] = new(159, 0.82, 145, 0.83, 104, 0.87, 97, 0.86, "bo6"),
        ["Battlefield 6"] = new(170, 1.02, 157, 1.09, 136, 1.19, 87, 1.05, "bf6"),
        ["Rainbow Six Siege"] = new(378, 0.69, 493, 0.88, 500, 0.96, 430, 0.99, "r6"),
        ["Overwatch 2"] = new(548, 0.74, 804, 0.96, 547, 0.91, 306, 0.88, "overwatch"),
        ["Rocket League"] = new(580, 0.70, 572, 0.81, 411, 0.82, 337, 0.82, "rocketleague"),
        ["EA Sports FC 25"] = new(319, 0.84, 259, 0.84, 268, 1.21, 203, 1.05, "fc25"),
        ["GTA V"] = new(188, 0.50, 217, 0.54, 296, 0.90, 151, 0.94, "gtav"),
        ["Red Dead Redemption 2"] = new(166, 0.81, 177, 0.90, 122, 0.92, 83, 0.97, "rdr2"),
        ["Cyberpunk 2077"] = new(181, 0.96, 149, 1.00, 130, 1.06, 115, 1.08, "cyberpunk"),
        ["The Witcher 3: Wild Hunt"] = new(360, 0.88, 311, 0.94, 277, 1.00, 142, 0.94, "witcher3"),
        ["Elden Ring"] = new(374, 1.04, 113, 0.62, 93, 0.57, 93, 0.60, "eldenring"),
        ["Baldur's Gate 3"] = new(194, 0.97, 156, 0.98, 137, 1.03, 136, 1.04, "bg3"),
        ["Hogwarts Legacy"] = new(189, 0.95, 154, 1.03, 118, 1.00, 101, 1.15, "hogwarts"),
        ["Starfield"] = new(100, 0.88, 93, 0.97, 77, 1.02, 72, 1.08, "starfield"),
        ["Black Myth: Wukong"] = new(111, 0.87, 90, 0.99, 73, 1.11, 36, 0.90, "wukong"),
        ["Alan Wake 2"] = new(95, 1.02, 86, 1.05, 85, 1.11, 70, 1.11, "alanwake2"),
        ["God of War Ragnarök"] = new(181, 0.95, 153, 0.95, 156, 1.22, 145, 1.28, "gowr"),
        ["Forza Horizon 5"] = new(231, 0.70, 205, 0.76, 187, 0.82, 107, 0.88, "fh5"),
        ["Helldivers 2"] = new(183, 0.95, 164, 1.16, 149, 1.17, 149, 1.21, "hd2"),
        ["Monster Hunter Wilds"] = new(100, 1.05, 84, 1.10, 65, 1.10, 61, 1.20, "mhwilds"),
        ["Assassin's Creed Shadows"] = new(75, 0.85, 66, 0.81, 55, 0.85, 41, 1.07, "acshadows"),
        ["Borderlands 4"] = new(97, 1.16, 84, 1.19, 67, 1.21, 39, 1.16, "borderlands4"),
        ["Call of Duty: Warzone"] = new(148, 0.82, 135, 0.83, 96, 0.87, 90, 0.86, "bo6 (proxy)"),
    };

    /// <summary>FPS = menor entre o teto da GPU, o teto da CPU e a trava do jogo.</summary>
    public static FpsEstimate Estimate(GameInfo g, double gpuIdx, double cpuIdx, string res, string preset, string upscaling)
    {
        double atPreset = Measured.TryGetValue(g.Name, out var curves) ? curves.Fps(gpuIdx, preset)
            : g.GpuRef * Math.Pow(Math.Max(gpuIdx, 0.01) / 100, g.GpuExp) * PresetFactor(preset);
        double gpuFps = atPreset * ResolutionFactor(res) * UpscalingFactor(upscaling, res);
        double cpuFps = g.CpuCap * cpuIdx / 100 * (preset == "Baixo" ? 1.1 : preset == "Ultra" ? 0.95 : 1.0);

        double fps = Math.Min(gpuFps, cpuFps);
        var limiter = gpuFps <= cpuFps ? Bottleneck.Gpu : Bottleneck.Cpu;
        if (g.EngineCap > 0 && g.EngineCap < fps) { fps = g.EngineCap; limiter = Bottleneck.Engine; }
        return new FpsEstimate(Math.Round(fps), Math.Round(fps * 0.75), limiter);
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
