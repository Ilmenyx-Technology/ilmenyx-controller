// New Ilmenyx additions, adapted from the profile model and argument builder of the original GameLauncher/AppSettings (MIT, see NOTICE): see LICENSING.md.
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

/// <summary>Program opened before the game and optionally closed with it. Started without a shell.</summary>
public sealed class HelperProgram
{
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
    public bool CloseWithGame { get; set; } = true;
    public string Display => System.IO.Path.GetFileName(Path) + (string.IsNullOrWhiteSpace(Args) ? "" : " " + Args);
}

/// <summary>Per-game profile. Linux: performance power profile, PRIME offload, GameMode, MangoHud, graphics arguments and helper programs.</summary>
public sealed class GameProfile
{
    public bool HighPerfPlan { get; set; }
    public bool GpuHigh { get; set; }
    public bool GameMode { get; set; }
    public bool MangoHud { get; set; }
    public string? ExePath { get; set; }
    public string WinMode { get; set; } = "default";   // default | full | borderless | window
    public string Res { get; set; } = "default";       // default | 1280x720 | 1920x1080 | 2560x1440 | 3840x2160
    public int Hz { get; set; }                        // 0 = do not set
    public string Style { get; set; } = "source";      // source | unity | unreal
    public string Extra { get; set; } = "";
    public List<HelperProgram> Helpers { get; set; } = [];
}

/// <summary>Manually added executable (ELF, script, AppImage).</summary>
public sealed class CustomGame
{
    public string Name { get; set; } = "";
    public string Exe { get; set; } = "";
    public string Args { get; set; } = "";
}

public sealed record LibGame(string Store, string Id, string Name, string InstallDir, string? ExePath = null, string? LaunchUri = null, string Args = "")
{
    public string Key => Store + ":" + Id;
    public string Subtitle => Store;
    public bool IsSteam => Store == "Steam";
}

public static class GameLaunchOptions
{
    private static readonly Regex Resolution = new(@"^(\d{3,5})x(\d{3,5})\z", RegexOptions.CultureInvariant);
    public static readonly string[] WinModes = ["default", "full", "borderless", "window"];
    public static readonly string[] Styles = ["source", "unity", "unreal"];

    /// <summary>Graphics arguments of the profile in the style of the chosen engine (same logic as the original BuildArgs).</summary>
    public static string BuildArgs(GameProfile p)
    {
        var a = new List<string>(); int w = 0, h = 0;
        var m = Resolution.Match(p.Res ?? "");
        if (m.Success) { w = int.Parse(m.Groups[1].Value); h = int.Parse(m.Groups[2].Value); }
        switch (p.Style)
        {
            case "unity":
                if (p.WinMode == "full") a.Add("-screen-fullscreen 1"); else if (p.WinMode == "window") a.Add("-screen-fullscreen 0"); else if (p.WinMode == "borderless") a.Add("-popupwindow");
                if (w > 0) a.Add($"-screen-width {w} -screen-height {h}");
                break;
            case "unreal":
                if (p.WinMode == "full") a.Add("-fullscreen"); else if (p.WinMode is "window" or "borderless") a.Add("-windowed");
                if (w > 0) a.Add($"-ResX={w} -ResY={h}");
                break;
            default:
                if (p.WinMode == "full") a.Add("-fullscreen"); else if (p.WinMode == "window") a.Add("-windowed"); else if (p.WinMode == "borderless") a.Add("-windowed -noborder");
                if (w > 0) a.Add($"-w {w} -h {h}");
                if (p.Hz > 0) a.Add($"-freq {p.Hz}");
                break;
        }
        if (!string.IsNullOrWhiteSpace(p.Extra)) a.Add(p.Extra.Trim());
        return string.Join(" ", a);
    }

    /// <summary>Environment variables selecting the discrete GPU through PRIME render offload (NVIDIA proprietary or Mesa).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> GpuEnvironment(string vendor) => vendor switch
    {
        "NVIDIA" => [new("__NV_PRIME_RENDER_OFFLOAD", "1"), new("__GLX_VENDOR_LIBRARY_NAME", "nvidia"), new("__VK_LAYER_NV_optimus", "NVIDIA_only")],
        "AMD" or "Intel" or "NVIDIA-Mesa" or "" => [new("DRI_PRIME", "1")], // NVIDIA-Mesa: NVIDIA GPU driven by nouveau/NVK
        _ => []
    };

    /// <summary>Launch options for Steam: <c>ENV=… wrappers %command% args</c>. No shell metacharacters beyond what the user typed in Extra.</summary>
    public static string BuildSteamOptions(GameProfile p, string gpuVendor, string? mangoConfig = null)
    {
        var parts = new List<string>();
        if (p.GpuHigh) parts.AddRange(GpuEnvironment(gpuVendor).Select(kv => $"{kv.Key}={kv.Value}"));
        if (p.MangoHud && mangoConfig is not null) parts.Add("MANGOHUD_CONFIGFILE=" + ShellQuote(mangoConfig));
        if (p.MangoHud) parts.Add("mangohud");
        if (p.GameMode) parts.Add("gamemoderun");
        string args = BuildArgs(p);
        if (parts.Count == 0 && args.Length == 0) return "";
        parts.Add("%command%"); if (args.Length > 0) parts.Add(args);
        return string.Join(" ", parts);
    }

    /// <summary>Single-quotes a value for the shell Steam uses to run launch options; plain safe paths stay unquoted.</summary>
    public static string ShellQuote(string value) =>
        value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '/' or '.' or '_' or '-' or '+' or ':' or '@' or ',') ? value : "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>Splits an argument string like a minimal shell would (quotes and backslash escapes only; no expansion). Throws on unterminated quotes.</summary>
    public static IReadOnlyList<string> SplitArguments(string text)
    {
        var result = new List<string>(); var cur = new System.Text.StringBuilder(); char quote = '\0'; bool any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; else if (c == '\\' && quote == '"' && i + 1 < text.Length && text[i + 1] is '"' or '\\') cur.Append(text[++i]); else cur.Append(c); }
            else if (c is '"' or '\'') { quote = c; any = true; }
            else if (c == '\\' && i + 1 < text.Length) { cur.Append(text[++i]); any = true; }
            else if (char.IsWhiteSpace(c)) { if (any || cur.Length > 0) { result.Add(cur.ToString()); cur.Clear(); any = false; } }
            else { cur.Append(c); any = true; }
        }
        if (quote != '\0') throw new FormatException("Aspas sem fechamento nos argumentos.");
        if (any || cur.Length > 0) result.Add(cur.ToString());
        return result;
    }
}

/// <summary>User settings (game profiles, manual games, later overlay and benchmark history) in one JSON file, written atomically.</summary>
/// <summary>One finished benchmark kept in the local history; <see cref="Score"/> is experimental and not comparable with Windows.</summary>
public sealed record BenchRunEntry(DateTimeOffset At, string Kind, string Summary, double? Score)
{
    public const int MaxEntries = 50;
}

public sealed class IlmenyxSettings
{
    public Dictionary<string, GameProfile> GameProfiles { get; set; } = [];
    public List<CustomGame> CustomGames { get; set; } = [];
    /// <summary>Original values of user-level tweaks before the first change ("&lt;absent&gt;" = unknown), so "Restaurar originais" can undo them.</summary>
    public Dictionary<string, string?> TweakBackup { get; set; } = [];
    /// <summary>HowManyFPS API key. Stored in this user-only (0600) file; never exported or logged.</summary>
    public string? HowManyFpsKey { get; set; }
    /// <summary>Local benchmark history (newest first, at most <see cref="BenchRunEntry.MaxEntries"/>). Never sent anywhere.</summary>
    public List<BenchRunEntry> BenchRuns { get; set; } = [];
    /// <summary>Overlay items (null = defaults), corner (top-left, top-right, bottom-left, bottom-right) and text scale.</summary>
    public List<string>? OverlayItems { get; set; }
    public string OverlayCorner { get; set; } = "top-left";
    public double OverlayScale { get; set; } = 1.0;
    /// <summary>Light theme (the default is dark). Applied at startup and when toggled.</summary>
    public bool ThemeLight { get; set; }
    /// <summary>Toast when a removable drive or USB device appears or disappears (local polling only).</summary>
    public bool DeviceToasts { get; set; } = true;

    public void AddBenchRun(BenchRunEntry entry)
    {
        BenchRuns.Insert(0, entry);
        if (BenchRuns.Count > BenchRunEntry.MaxEntries) BenchRuns.RemoveRange(BenchRunEntry.MaxEntries, BenchRuns.Count - BenchRunEntry.MaxEntries);
    }

    public static IlmenyxSettings Load(string path)
    {
        try { if (File.Exists(path) && new FileInfo(path).Length < 4 << 20) return JsonSerializer.Deserialize<IlmenyxSettings>(File.ReadAllText(path)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, path, overwrite: true);
    }

    public static string DefaultPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string cfg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c && Path.IsPathRooted(c) ? c : Path.Combine(home, ".config");
        return Path.Combine(cfg, "ilmenyx", "settings.json");
    }
}
