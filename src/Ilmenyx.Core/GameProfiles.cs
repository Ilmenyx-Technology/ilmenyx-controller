// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
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
    /// <summary>Steam AppID of a Windows game run with Proton outside Steam (umu GAMEID umu-&lt;id&gt;, so the known per-game fixes apply). Digits only; null = generic.</summary>
    public string? ProtonGameId { get; set; }
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
    public string Subtitle => IsWindowsManual ? "Windows · Proton" : Store;
    /// <summary>A manually added Windows program (.exe): Ilmenyx runs it with Proton (umu-launcher).</summary>
    public bool IsWindowsManual => Store == "Manual" && (ExePath ?? Id).EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
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

/// <summary>
/// Summary of one game session measured by MangoHud on this PC (Launcher with MangoHud on). Only the game name from the library, the
/// date, FPS figures, duration, the GPU name MangoHud logged and the profile resolution are kept; nothing leaves the PC.
/// </summary>
public sealed record FpsSession(string Game, DateTimeOffset At, double AvgFps, double Low1Fps, double DurationSeconds, string? Gpu, string? Resolution)
{
    public const int MaxEntries = 50;
    public const double MinSeconds = 30;
    public bool IsValid => DurationSeconds >= MinSeconds && double.IsFinite(AvgFps) && AvgFps > 0 && AvgFps < 10000 && Game is { Length: > 0 };
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
    /// <summary>The first-run screen that offers every blocked sensor fix at once was already shown (accepted or not); later fixes stay in the sidebar button.</summary>
    public bool PermissionsAsked { get; set; }
    /// <summary>FPS measured in game sessions started by the Launcher with MangoHud (newest first, at most <see cref="FpsSession.MaxEntries"/>). Never sent anywhere.</summary>
    public List<FpsSession> FpsSessions { get; set; } = [];

    public void AddFpsSession(FpsSession session)
    {
        if (!session.IsValid) return;
        FpsSessions.Insert(0, session);
        if (FpsSessions.Count > FpsSession.MaxEntries) FpsSessions.RemoveRange(FpsSession.MaxEntries, FpsSessions.Count - FpsSession.MaxEntries);
    }

    public void AddBenchRun(BenchRunEntry entry)
    {
        BenchRuns.Insert(0, entry);
        if (BenchRuns.Count > BenchRunEntry.MaxEntries) BenchRuns.RemoveRange(BenchRunEntry.MaxEntries, BenchRuns.Count - BenchRunEntry.MaxEntries);
    }

    /// <summary>Reads the file (missing, too large or corrupt = defaults). Use it for reads; write with <see cref="Update"/>.</summary>
    public static IlmenyxSettings Load(string path) => LoadCore(path, out _, out _);

    private static IlmenyxSettings LoadCore(string path, out bool corrupt, out Exception? unreadable)
    {
        corrupt = false; unreadable = null;
        try
        {
            if (!File.Exists(path)) return new();
            if (new FileInfo(path).Length < 4 << 20 && JsonSerializer.Deserialize<IlmenyxSettings>(File.ReadAllText(path)) is { } loaded)
            {
                loaded.GameProfiles ??= []; loaded.CustomGames ??= []; loaded.TweakBackup ??= []; loaded.BenchRuns ??= []; // hand-edited files ("x": null)
                loaded.FpsSessions = (loaded.FpsSessions ?? []).Where(x => x is not null && x.IsValid).Take(FpsSession.MaxEntries).ToList();
                return loaded;
            }
            corrupt = true;
        }
        catch (JsonException) { corrupt = true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { unreadable = e; }
        return new();
    }

    /// <summary>Writes this whole copy over the file. Kept for compatibility: it overwrites what other pages or instances saved since this
    /// copy was loaded, so the app writes through <see cref="Update"/> instead.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tmp, path, overwrite: true);
        }
        catch { try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } throw; }
    }

    /// <summary>How long <see cref="Update"/> waits for another process holding the settings lock before going on without it.</summary>
    public static TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(2);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> pathLocks = new(StringComparer.Ordinal);

    /// <summary>Atomic read-modify-write: under an in-process lock (per path) and an inter-process lock (flock on "settings.json.lock",
    /// released by the kernel if the holder dies), re-reads the current file, applies only <paramref name="change"/> and writes it back
    /// atomically (temp file + rename, mode 0600). Returns the saved state, so callers can refresh their copy. A corrupt file is kept as
    /// "settings.json.corrupt" before being replaced. If the lock is not obtained within <see cref="LockTimeout"/> it goes on without it
    /// (still re-read + atomic rename), so a stuck process never blocks the app.</summary>
    public static IlmenyxSettings Update(string path, Action<IlmenyxSettings> change) => UpdateAndGet(path, s => { change(s); return s; });

    /// <summary><see cref="Update"/> returning a value computed from the fresh state inside the lock (separate name so an assignment
    /// lambda like <c>s =&gt; s.X = v</c> always binds to <see cref="Update"/>).</summary>
    public static T UpdateAndGet<T>(string path, Func<IlmenyxSettings, T> change)
    {
        string full = Path.GetFullPath(path);
        lock (pathLocks.GetOrAdd(full, _ => new object()))
        {
            using var crossProcess = AcquireFileLock(full + ".lock", LockTimeout);
            var current = LoadCore(full, out bool corrupt, out var unreadable);
            if (unreadable is not null) throw new IOException("Settings file exists but cannot be read; not overwriting it: " + unreadable.Message, unreadable);
            if (corrupt) { try { File.Copy(full, full + ".corrupt", overwrite: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
            T result = change(current);
            current.Save(full);
            return result;
        }
    }

    private static FileStream? AcquireFileLock(string lockPath, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
                var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
                if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                return new FileStream(lockPath, options); // .NET on Unix: FileShare.None = flock(LOCK_EX | LOCK_NB)
            }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(15); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; } // timeout or read-only dir: go on unlocked
        }
    }

    public static string DefaultPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string cfg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c && Path.IsPathRooted(c) ? c : Path.Combine(home, ".config");
        return Path.Combine(cfg, "ilmenyx", "settings.json");
    }
}
