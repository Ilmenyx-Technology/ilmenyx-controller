// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using System.Text.RegularExpressions;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>What the game's own settings file says (read-only; nothing is written to the game). Preset is null when the level has no reliable mapping.</summary>
/// <param name="Engine">"Unreal" or "Unity".</param>
/// <param name="Level">Raw value for the screen ("Alto", "nível 2", "Personalizado").</param>
/// <param name="File">Settings file or registry that was read.</param>
public sealed record DetectedQuality(string Engine, string? Preset, string Level, string? Resolution, int? FpsLimit, string File);

/// <summary>
/// Best-effort detector of the graphics quality used inside the game, from the files the engine itself writes: Unreal GameUserSettings.ini
/// (Proton prefix or ~/.config/Epic) and Unity PlayerPrefs (user.reg of the prefix or ~/.config/unity3d). Bounded in depth, count and size.
/// </summary>
public static class GameQualityDetector
{
    private const long MaxFile = 4L << 20;
    private static readonly string[] UnrealKeys = ["ViewDistance", "AntiAliasing", "Shadow", "PostProcess", "Texture", "Effects", "Foliage"];

    /// <summary>Wine prefixes of the game (the chosen one, Ilmenyx's for the exe, Steam's compatdata); each may hold the "C:" drive directly or in pfx/.</summary>
    public static List<string> Prefixes(LibGame g, GameProfile p, string? exe, string? prefixRoot = null)
    {
        var l = new List<string>();
        if (p.WinePrefix is { Length: > 0 } own && Path.IsPathFullyQualified(own)) l.Add(own);
        else if (g.IsSteam && g.Id.All(char.IsAsciiDigit) && g.InstallDir.Length > 0 && Path.GetDirectoryName(Path.GetDirectoryName(g.InstallDir.TrimEnd('/'))) is { Length: > 0 } steamapps)
            l.Add(Path.Combine(steamapps, "compatdata", g.Id));
        else if (g.IsWindowsManual && exe is not null) l.Add(LinuxGameLauncher.ProtonEnvironment(exe, p.ProtonGameId, prefixRoot ?? LinuxGameLauncher.DefaultPrefixRoot())[0].Value);
        return l.SelectMany(x => new[] { x, Path.Combine(x, "pfx") }).Where(Directory.Exists).ToList();
    }

    /// <summary>Runs <see cref="Detect"/> off the calling thread with a short deadline; null on timeout or failure.</summary>
    public static async Task<DetectedQuality?> DetectAsync(string gameName, string? exe, IReadOnlyList<string> prefixes, TimeSpan timeout, string? configHome = null)
    {
        try { return await Task.Run(() => Detect(gameName, exe, prefixes, configHome)).WaitAsync(timeout); }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { return null; }
    }

    public static DetectedQuality? Detect(string gameName, string? exe, IReadOnlyList<string> prefixes, string? configHome = null)
    {
        try
        {
            configHome ??= Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x && Path.IsPathRooted(x) ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            string stem = exe is null ? "" : Path.GetFileNameWithoutExtension(exe);
            return DetectUnreal(gameName, stem, prefixes, configHome) ?? DetectUnity(exe, prefixes, configHome);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    // ---- Unreal ----
    private static DetectedQuality? DetectUnreal(string game, string stem, IReadOnlyList<string> prefixes, string configHome)
    {
        var projects = new List<string>(); // folders that hold Saved/Config
        foreach (string prefix in prefixes)
        {
            string users = Path.Combine(prefix, "drive_c", "users");
            if (!Directory.Exists(users)) continue;
            foreach (string user in Directory.EnumerateDirectories(users).Take(4))
            {
                string local = Path.Combine(user, "AppData", "Local");
                if (Directory.Exists(local)) projects.AddRange(Directory.EnumerateDirectories(local).Take(64).Where(d => Directory.Exists(Path.Combine(d, "Saved", "Config"))));
            }
        }
        string epic = Path.Combine(configHome, "Epic");
        if (Directory.Exists(epic)) projects.AddRange(Directory.EnumerateDirectories(epic).Take(64).Where(d => Directory.Exists(Path.Combine(d, "Saved", "Config"))));
        if (projects.Count == 0) return null;
        string ng = Norm(game), ns = Norm(stem);
        int Score(string d) { string n = Norm(Path.GetFileName(d)); return n.Length < 3 ? 0 : n == ng || n == ns ? 3 : ng.Contains(n) || n.Contains(ng) || (ns.Length >= 3 && (ns.Contains(n) || n.Contains(ns))) ? 2 : 0; }
        var pick = projects.Select(d => (d, Score(d))).Where(t => t.Item2 > 0 || projects.Count == 1).OrderByDescending(t => t.Item2).Select(t => t.d);
        foreach (string proj in pick)
        {
            string config = Path.Combine(proj, "Saved", "Config");
            foreach (string platform in new[] { "Windows", "WindowsNoEditor", "WinGDK" }.Concat(Directory.EnumerateDirectories(config, "Linux*").Take(4).Select(d => Path.GetFileName(d))))
            {
                string ini = Path.Combine(config, platform, "GameUserSettings.ini");
                if (ReadSmall(ini) is { } text) return ParseUnreal(text, ini);
            }
        }
        return null;
    }

    public static DetectedQuality? ParseUnreal(string text, string file)
    {
        var values = new List<int>(); int? w = null, h = null; double? limit = null;
        foreach (string raw in text.Split('\n').Take(4000))
        {
            int eq = raw.IndexOf('='); if (eq <= 0) continue;
            string key = raw[..eq].Trim(), val = raw[(eq + 1)..].Trim();
            if (key.StartsWith("sg.", StringComparison.OrdinalIgnoreCase) && key.EndsWith("Quality", StringComparison.OrdinalIgnoreCase) && UnrealKeys.Any(k => key.Equals("sg." + k + "Quality", StringComparison.OrdinalIgnoreCase))
                && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int q) && q is >= 0 and <= 4) values.Add(q);
            else if (key.Equals("ResolutionSizeX", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rx)) w = rx;
            else if (key.Equals("ResolutionSizeY", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ry)) h = ry;
            else if (key.Equals("FrameRateLimit", StringComparison.OrdinalIgnoreCase) && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double fl)) limit = fl;
        }
        string? res = w is > 0 and < 20000 && h is > 0 and < 20000 ? $"{w}x{h}" : null;
        int? cap = limit is > 0 and < 10000 ? (int)Math.Round(limit.Value) : null;
        if (values.Count == 0 && res is null) return null;
        string? preset = null, level = "Personalizado";
        if (values.Count > 0 && values.GroupBy(v => v).OrderByDescending(g => g.Count()).First() is { } top && top.Count() * 2 > values.Count)
        {
            preset = top.Key switch { 0 => "Baixo", 1 => "Médio", 2 => "Alto", _ => "Ultra" };
            level = top.Key == 4 ? "Cinemático" : preset;
        }
        if (values.Count == 0) level = "";
        return new("Unreal", preset, level, res, cap, file);
    }

    // ---- Unity ----
    private static DetectedQuality? DetectUnity(string? exe, IReadOnlyList<string> prefixes, string configHome)
    {
        if (exe is null || ReadAppInfo(exe) is not (string company, string product)) return null;
        string section = "[Software\\\\" + company + "\\\\" + product + "]";
        foreach (string prefix in prefixes)
            if (ReadSmall(Path.Combine(prefix, "user.reg")) is { } reg && ParseUnityReg(reg, section, Path.Combine(prefix, "user.reg")) is { } r) return r;
        string prefs = Path.Combine(configHome, "unity3d", company, product, "prefs");
        return ReadSmall(prefs) is { } xml ? ParseUnityPrefs(xml, prefs) : null;
    }

    /// <summary>Company and product from &lt;exe&gt;_Data/app.info (two lines), or null.</summary>
    public static (string, string)? ReadAppInfo(string exe)
    {
        string dir = Path.GetDirectoryName(exe) ?? "";
        string data = Path.Combine(dir, Path.GetFileNameWithoutExtension(exe) + "_Data");
        if (!Directory.Exists(data)) data = Directory.Exists(dir) ? Directory.EnumerateDirectories(dir, "*_Data").Take(2).FirstOrDefault() ?? "" : "";
        if (data.Length == 0 || ReadSmall(Path.Combine(data, "app.info")) is not { } t) return null;
        var lines = t.Split('\n').Select(x => x.Trim()).Where(x => x.Length is > 0 and <= 120 && !x.Any(char.IsControl)).Take(2).ToList();
        return lines.Count == 2 ? (lines[0], lines[1]) : null;
    }

    public static DetectedQuality? ParseUnityReg(string reg, string section, string file)
    {
        bool inside = false; int? level = null, w = null, h = null;
        foreach (string raw in reg.Split('\n').Take(200000))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith('[')) { inside = line.StartsWith(section, StringComparison.OrdinalIgnoreCase); continue; }
            if (!inside) continue;
            var m = Regex.Match(line, "^\"(UnityGraphicsQuality|Screenmanager Resolution Width|Screenmanager Resolution Height)_h\\d+\"=dword:([0-9a-fA-F]{1,8})$");
            if (!m.Success) continue;
            int v = (int)Convert.ToUInt32(m.Groups[2].Value, 16);
            switch (m.Groups[1].Value) { case "UnityGraphicsQuality": level = v; break; case "Screenmanager Resolution Width": w = v; break; default: h = v; break; }
        }
        return Unity(level, w, h, file);
    }

    public static DetectedQuality? ParseUnityPrefs(string xml, string file)
    {
        int? level = null, w = null, h = null;
        foreach (Match m in Regex.Matches(xml.Length > MaxFile ? xml[..(int)MaxFile] : xml, "name=\"(UnityGraphicsQuality|Screenmanager Resolution Width|Screenmanager Resolution Height)[^\"]*\" type=\"int\">(-?\\d+)<").Take(64))
        {
            if (!int.TryParse(m.Groups[2].Value, out int v)) continue;
            switch (m.Groups[1].Value) { case "UnityGraphicsQuality": level = v; break; case "Screenmanager Resolution Width": w = v; break; default: h = v; break; }
        }
        return Unity(level, w, h, file);
    }

    // The Unity level is an index into the game's own list of names: shown as "nível N", never mapped to a preset.
    private static DetectedQuality? Unity(int? level, int? w, int? h, string file)
    {
        string? res = w is > 0 and < 20000 && h is > 0 and < 20000 ? $"{w}x{h}" : null;
        return level is null && res is null ? null : new("Unity", null, level is { } l and >= 0 and < 100 ? $"nível {l}" : "", res, null, file);
    }

    private static string? ReadSmall(string path)
    {
        try { var f = new FileInfo(path); return f.Exists && f.Length <= MaxFile ? File.ReadAllText(path) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
