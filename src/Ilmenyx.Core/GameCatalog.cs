// Reference data and estimation logic ported from the original GameCatalog (MIT, see NOTICE): see LICENSING.md.
namespace Ilmenyx.Core;

/// <summary>
/// Jogo do catálogo. Valores de referência APROXIMADOS (compilados de benchmarks públicos) usados só
/// pela estimativa local, quando a API da HowManyFPS não está configurada:
/// GpuRef = FPS médio em 1080p/Alto numa RTX 4070 (índice 100); CpuCap = teto de FPS limitado pela CPU
/// num Ryzen 7 7700X (índice 100); EngineCap = trava de FPS do próprio jogo (0 = sem trava).
/// </summary>
public record GameInfo(string Name, string Genre, double GpuRef, double CpuCap, double EngineCap = 0);

public enum Bottleneck { None, Gpu, Cpu, Engine }

public record FpsEstimate(double Avg, double Low1, Bottleneck Limiter);

public static class GameCatalog
{
    // O nome é enviado à API como está, então segue a grafia usada pela HowManyFPS.
    public static readonly GameInfo[] Games =
    {
        new("Counter-Strike 2", "FPS competitivo", 340, 420),
        new("Valorant", "FPS competitivo", 600, 650),
        new("Fortnite", "Battle royale", 160, 250),
        new("League of Legends", "MOBA", 420, 480),
        new("Dota 2", "MOBA", 260, 280),
        new("Apex Legends", "Battle royale", 220, 300),
        new("PUBG: Battlegrounds", "Battle royale", 180, 220),
        new("Call of Duty: Black Ops 6", "FPS", 140, 220),
        new("Call of Duty: Warzone", "Battle royale", 130, 200),
        new("Battlefield 6", "FPS", 120, 170),
        new("Rainbow Six Siege", "FPS tático", 350, 430),
        new("Overwatch 2", "FPS heroico", 280, 380),
        new("Marvel Rivals", "FPS heroico", 110, 160),
        new("Rocket League", "Esporte", 400, 450),
        new("EA Sports FC 25", "Esporte", 220, 260),
        new("Minecraft", "Sandbox", 300, 340),
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
        "2560x1440" => 0.72, "3840x2160" => 0.42, _ => 1.0
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

    /// <summary>FPS = menor entre o teto da GPU, o teto da CPU e a trava do jogo.</summary>
    public static FpsEstimate Estimate(GameInfo g, double gpuIdx, double cpuIdx, string res, string preset, string upscaling)
    {
        double gpuFps = g.GpuRef * gpuIdx / 100 * ResolutionFactor(res) * PresetFactor(preset) * UpscalingFactor(upscaling, res);
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
