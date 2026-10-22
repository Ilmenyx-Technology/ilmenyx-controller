// New Ilmenyx additions, adapted from GameLibrary/SteamInfo/GameLauncher of the original (MIT, see NOTICE): see LICENSING.md.
using System.Diagnostics;
using System.Text.Json;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Installed games from Steam (native and Proton), Heroic (Epic/GOG), Lutris and manually added executables. Read-only.</summary>
public sealed class LinuxGameLibrary(string? home = null, Func<IReadOnlyList<string>, CancellationToken, Task<(int Code, string Output)>>? lutris = null)
{
    private readonly string home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string[] IgnoredPrefixes = ["Proton", "Steam Linux Runtime", "Steamworks Common", "SteamVR"];

    public IReadOnlyList<string> SteamRoots() => new[] { ".local/share/Steam", ".steam/steam", ".steam/root", ".var/app/com.valvesoftware.Steam/.local/share/Steam" }
        .Select(p => Path.Combine(home, p)).Where(Directory.Exists).Select(p => { try { return new DirectoryInfo(p).ResolveLinkTarget(true)?.FullName ?? p; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return p; } }).Distinct().ToList();

    public static IReadOnlyList<string> LibraryFolders(string steam)
    {
        var list = new List<string> { steam };
        try
        {
            string f = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(f) && new FileInfo(f).Length < 1 << 20)
                foreach (var (_, c) in Vdf.Parse(File.ReadAllText(f)).Child("libraryfolders")?.Children ?? [])
                    if (c.Get("path") is { Length: > 0 } p && Path.IsPathRooted(p) && Directory.Exists(p) && !list.Contains(p, StringComparer.Ordinal)) list.Add(p);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list;
    }

    public IEnumerable<LibGame> Steam()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var steam in SteamRoots())
            foreach (var lib in LibraryFolders(steam))
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                IEnumerable<string> files; try { files = Directory.EnumerateFiles(apps, "appmanifest_*.acf").OrderBy(f => f, StringComparer.Ordinal).Take(2048).ToList(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                foreach (var acf in files)
                {
                    if (ParseManifest(acf, apps) is { } g && seen.Add(g.Id)) yield return g;
                }
            }
    }

    public static LibGame? ParseManifest(string acf, string steamapps)
    {
        try
        {
            if (new FileInfo(acf).Length > 1 << 20) return null;
            var st = Vdf.Parse(File.ReadAllText(acf)).Child("AppState");
            string? id = st?.Get("appid"), name = st?.Get("name"), dir = st?.Get("installdir");
            if (id is null || name is null || dir is null || id.Length > 12 || !id.All(char.IsAsciiDigit)) return null;
            if (long.TryParse(st!.Get("StateFlags"), out long flags) && (flags & 4) == 0) return null; // 4 = fully installed
            if (id == "228980" || IgnoredPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return null;
            string full = Path.GetFullPath(Path.Combine(steamapps, "common", dir));
            if (!full.StartsWith(Path.GetFullPath(Path.Combine(steamapps, "common")) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !Directory.Exists(full)) return null;
            return new LibGame("Steam", id, name, full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public async Task<IEnumerable<LibGame>> LutrisAsync(CancellationToken ct)
    {
        try
        {
            var run = lutris ?? ((args, token) => BoundedProcess.RunAsync("lutris", args, TimeSpan.FromSeconds(8), 1 << 20, token));
            var (code, output) = await run(["--list-games", "--json"], ct);
            return code == 0 ? ParseLutris(output) : [];
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception or JsonException) { return []; }
    }

    public static IReadOnlyList<LibGame> ParseLutris(string json)
    {
        var list = new List<LibGame>();
        int start = json.IndexOf('['); if (start < 0) return list;
        using var doc = JsonDocument.Parse(json[start..], new JsonDocumentOptions { MaxDepth = 16 });
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var g in doc.RootElement.EnumerateArray().Take(2048))
        {
            string? S(string k) => g.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string id = g.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt64(out long n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            string? name = S("name");
            if (id.Length == 0 || string.IsNullOrWhiteSpace(name) || name.Length > 200) continue;
            list.Add(new("Lutris", id, name, S("directory") ?? "", null, $"lutris:rungameid/{id}"));
        }
        return list;
    }

    /// <summary>Heroic: Epic via legendary (installed.json) and GOG (gog_store/installed.json + library.json titles). Formats are read defensively.</summary>
    public IEnumerable<LibGame> Heroic()
    {
        string cfg = Path.Combine(home, ".config/heroic"); var list = new List<LibGame>();
        try
        {
            string epic = Path.Combine(cfg, "legendaryConfig/legendary/installed.json");
            if (File.Exists(epic) && new FileInfo(epic).Length < 8 << 20) list.AddRange(ParseLegendary(File.ReadAllText(epic)));
            string gog = Path.Combine(cfg, "gog_store/installed.json"), lib = Path.Combine(cfg, "gog_store/library.json");
            if (File.Exists(gog) && new FileInfo(gog).Length < 8 << 20) list.AddRange(ParseHeroicGog(File.ReadAllText(gog), File.Exists(lib) && new FileInfo(lib).Length < 16 << 20 ? File.ReadAllText(lib) : ""));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return list;
    }

    public static IReadOnlyList<LibGame> ParseLegendary(string json)
    {
        var list = new List<LibGame>(); using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;
        foreach (var p in doc.RootElement.EnumerateObject().Take(2048))
        {
            string? S(string k) => p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string? title = S("title"), dir = S("install_path"), exe = S("executable");
            if (title is null || dir is null || !Path.IsPathRooted(dir) || p.Name.Length > 64) continue;
            list.Add(new("Epic Games", p.Name, title, dir, exe is { Length: > 0 } && !exe.Contains("..", StringComparison.Ordinal) ? Path.Combine(dir, exe) : null, $"heroic://launch/legendary/{Uri.EscapeDataString(p.Name)}"));
        }
        return list;
    }

    public static IReadOnlyList<LibGame> ParseHeroicGog(string installedJson, string libraryJson)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        if (libraryJson.Length > 0)
            try
            {
                using var lib = JsonDocument.Parse(libraryJson, new JsonDocumentOptions { MaxDepth = 16 });
                if (lib.RootElement.TryGetProperty("games", out var games) && games.ValueKind == JsonValueKind.Array)
                    foreach (var g in games.EnumerateArray().Take(4096))
                        if (g.TryGetProperty("app_name", out var a) && a.ValueKind == JsonValueKind.String && g.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String) titles[a.GetString()!] = t.GetString()!;
            }
            catch (JsonException) { }
        var list = new List<LibGame>(); using var doc = JsonDocument.Parse(installedJson, new JsonDocumentOptions { MaxDepth = 16 });
        if (!doc.RootElement.TryGetProperty("installed", out var installed) || installed.ValueKind != JsonValueKind.Array) return list;
        foreach (var g in installed.EnumerateArray().Take(2048))
        {
            string? S(string k) => g.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            string? app = S("appName"), dir = S("install_path");
            if (app is null || dir is null || !Path.IsPathRooted(dir) || app.Length > 64) continue;
            list.Add(new("GOG", app, titles.GetValueOrDefault(app) ?? app, dir, null, $"heroic://launch/gog/{Uri.EscapeDataString(app)}"));
        }
        return list;
    }

    public async Task<IReadOnlyList<LibGame>> ScanAsync(IEnumerable<CustomGame> custom, CancellationToken ct = default)
    {
        var all = new List<LibGame>();
        try { all.AddRange(Steam()); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        all.AddRange(Heroic()); all.AddRange(await LutrisAsync(ct));
        foreach (var c in custom)
            if (IsRunnableFile(c.Exe)) all.Add(new("Manual", c.Exe, string.IsNullOrWhiteSpace(c.Name) ? Path.GetFileNameWithoutExtension(c.Exe) : c.Name, Path.GetDirectoryName(c.Exe) ?? "", c.Exe, null, c.Args));
        return all.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Absolute path to an existing regular file the user can execute (never a link to a system path).</summary>
    public static bool IsRunnableFile(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            var info = new FileInfo(path);
            return info.Exists && info.LinkTarget is null && !path.StartsWith("/proc/", StringComparison.Ordinal) && !path.StartsWith("/sys/", StringComparison.Ordinal) && !path.StartsWith("/dev/", StringComparison.Ordinal)
                && (info.UnixFileMode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Best-effort main executable inside the install folder: name close to the game's, else the largest executable file. User can override.</summary>
    public static string? FindMainExe(string dir, string gameName)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            string[] ignored = ["unins", "crash", "redist", "setup", "uninstall", "launcher_helper", "notification", "dxsetup", "easyanticheat", "beservice", "cef"];
            var files = new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true })
                .Where(f => (f.Extension is ".exe" or ".sh" or ".AppImage" or "" ) && (f.UnixFileMode & UnixFileMode.UserExecute) != 0 && !ignored.Any(i => f.Name.Contains(i, StringComparison.OrdinalIgnoreCase)) && f.Length > 4096).Take(2000).ToList();
            if (files.Count == 0) return null;
            string norm = new(gameName.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
            var hit = files.Where(f => { string n = new(Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray()); return n.Length >= 3 && (norm.Contains(n, StringComparison.Ordinal) || n.Contains(norm, StringComparison.Ordinal)); }).OrderByDescending(f => f.Length).FirstOrDefault();
            return (hit ?? files.OrderByDescending(f => f.Length).First()).FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>Steam installation facts and the per-game "launch options" in localconfig.vdf (with a backup of the original file).</summary>
public sealed class SteamInfo(string home, string procRoot = "/proc")
{
    public string? Root() => new LinuxGameLibrary(home).SteamRoots().FirstOrDefault();

    public bool IsRunning
    {
        get
        {
            try { foreach (var d in Directory.EnumerateDirectories(procRoot).Where(d => char.IsAsciiDigit(Path.GetFileName(d)[0])).Take(4096)) if (SysfsText.Read(Path.Combine(d, "comm"), 32) == "steam") return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    /// <summary>Most recently used account's localconfig.vdf.</summary>
    public string? LocalConfig()
    {
        string? steam = Root(); if (steam is null) return null;
        string ud = Path.Combine(steam, "userdata"); if (!Directory.Exists(ud)) return null;
        return Directory.GetDirectories(ud).Select(d => Path.Combine(d, "config", "localconfig.vdf")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private static VdfNode? AppsNode(VdfNode root, bool create)
    {
        VdfNode? n = root.Child("UserLocalConfigStore");
        foreach (var key in new[] { "Software", "Valve", "Steam", "apps" }) { if (n is null) return null; n = create ? n.GetOrAddChild(key) : n.Child(key); }
        return n;
    }

    public string? GetLaunchOptions(string appId)
    {
        try { return LocalConfig() is { } f && new FileInfo(f).Length < 16 << 20 ? AppsNode(Vdf.Parse(File.ReadAllText(f)), false)?.Child(appId)?.Get("LaunchOptions") : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Returns null on success or the error text. Steam must be closed (it overwrites the file on exit); the original is copied first.</summary>
    public string? SetLaunchOptions(string appId, string options)
    {
        if (appId.Length is 0 or > 12 || !appId.All(char.IsAsciiDigit)) return "Identificador de jogo inválido.";
        if (IsRunning) return "A Steam está aberta. Feche-a para gravar as opções (ela sobrescreve o arquivo ao sair).";
        string? f = LocalConfig();
        if (f is null) return "Não achei o localconfig.vdf da Steam (entre na Steam ao menos uma vez neste PC).";
        try
        {
            var root = Vdf.Parse(File.ReadAllText(f)); var app = AppsNode(root, true)!.GetOrAddChild(appId);
            if (string.IsNullOrWhiteSpace(options)) app.Remove("LaunchOptions"); else app.Set("LaunchOptions", options.Trim());
            File.Copy(f, f + ".ilmenyx.bak", true);
            string tmp = f + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, Vdf.Serialize(root), new System.Text.UTF8Encoding(false)); File.Move(tmp, f, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "Não consegui gravar: " + e.Message; }
    }

    public async Task<bool> ShutdownAsync(CancellationToken ct = default)
    {
        try { await BoundedProcess.RunAsync("steam", ["-shutdown"], TimeSpan.FromSeconds(10), 4096, ct); } catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { }
        for (int i = 0; i < 50 && IsRunning; i++) await Task.Delay(500, ct);
        return !IsRunning;
    }
}

/// <summary>Finds a running game by environment (Steam: SteamGameId/SteamAppId) or by command line under the install folder. Own processes only.</summary>
public static class GameProcesses
{
    public static IReadOnlyList<int> Find(string procRoot, LibGame g, string? exe)
    {
        var pids = new List<int>();
        try
        {
            foreach (var d in Directory.EnumerateDirectories(procRoot).Where(d => char.IsAsciiDigit(Path.GetFileName(d)[0])).Take(4096))
            {
                if (!int.TryParse(Path.GetFileName(d), out int pid)) continue;
                bool hit = false;
                if (g.IsSteam)
                {
                    string env = ReadRaw(Path.Combine(d, "environ"), 65536);
                    hit = env.Split('\0').Any(e => e == $"SteamGameId={g.Id}" || e == $"SteamAppId={g.Id}");
                }
                else
                {
                    string cmd = ReadRaw(Path.Combine(d, "cmdline"), 16384).Replace('\0', ' ');
                    hit = (g.InstallDir.Length > 3 && cmd.Contains(g.InstallDir.TrimEnd('/') + "/", StringComparison.Ordinal)) || (exe is { Length: > 3 } && cmd.Contains(exe, StringComparison.Ordinal));
                }
                if (hit) pids.Add(pid);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return pids;
    }

    private static string ReadRaw(string path, int max)
    {
        try { using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, false); var b = new byte[max]; int n = s.Read(b, 0, b.Length); return System.Text.Encoding.UTF8.GetString(b, 0, n); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }
}

public sealed record LaunchCommand(string Program, IReadOnlyList<string> Arguments, IReadOnlyList<KeyValuePair<string, string>> Environment);

public sealed class LinuxGameLauncher(string procRoot = "/proc")
{
    private static readonly string[] AllowedProfiles = ["power-saver", "balanced", "performance"];

    /// <summary>Command for a manual executable: optional wrappers (mangohud, gamemoderun) before the executable, PRIME variables in the environment. Never a shell.</summary>
    public static LaunchCommand BuildManual(string exe, GameProfile p, string gameArgs, string gpuVendor, Func<string, string?> find)
    {
        var args = new List<string>(); string program = exe;
        var wrappers = new List<string>();
        if (p.MangoHud && find("mangohud") is { } mh) wrappers.Add(mh);
        if (p.GameMode && find("gamemoderun") is { } gm) wrappers.Add(gm);
        if (wrappers.Count > 0) { program = wrappers[0]; args.AddRange(wrappers.Skip(1)); args.Add(exe); }
        string graphics = GameLaunchOptions.BuildArgs(p);
        args.AddRange(GameLaunchOptions.SplitArguments((gameArgs + " " + graphics).Trim()));
        return new(program, args, p.GpuHigh ? GameLaunchOptions.GpuEnvironment(gpuVendor) : []);
    }

    public static async Task<string?> PowerProfileAsync(CancellationToken ct)
    {
        try { var (code, output) = await BoundedProcess.RunAsync("powerprofilesctl", ["get"], TimeSpan.FromSeconds(2), 128, ct); return code == 0 && AllowedProfiles.Contains(output.Trim()) ? output.Trim() : null; }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { return null; }
    }

    public static async Task<bool> SetPowerProfileAsync(string id, CancellationToken ct)
    {
        if (!AllowedProfiles.Contains(id)) throw new ArgumentException("Perfil de energia inválido.");
        try { var (code, _) = await BoundedProcess.RunAsync("powerprofilesctl", ["set", id], TimeSpan.FromSeconds(3), 256, ct); return code == 0; }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>Applies the profile, starts the game and follows it until it exits; temporary changes are always undone (finally).</summary>
    public async Task RunAsync(LibGame g, GameProfile p, string gpuVendor, Action<string> status, Action<string> learnedExe, CancellationToken ct)
    {
        string? oldPlan = null; var helpers = new List<(Process Proc, bool Close)>();
        try
        {
            status("Aplicando o perfil…");
            if (p.HighPerfPlan)
            {
                oldPlan = await PowerProfileAsync(ct);
                if (oldPlan is null) status("power-profiles-daemon indisponível: o perfil de energia não será alterado.");
                else if (oldPlan != "performance") { if (!await SetPowerProfileAsync("performance", ct)) { status("Não consegui ativar o perfil Alto desempenho."); oldPlan = null; } }
                else oldPlan = null;
            }
            string? exe = p.ExePath ?? g.ExePath ?? LinuxGameLibrary.FindMainExe(g.InstallDir, g.Name);
            foreach (var h in p.Helpers.Where(h => LinuxGameLibrary.IsRunnableFile(h.Path)))
            {
                status($"Abrindo {Path.GetFileName(h.Path)}…");
                try
                {
                    var psi = new ProcessStartInfo(h.Path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(h.Path) ?? "" };
                    foreach (var a in GameLaunchOptions.SplitArguments(h.Args)) psi.ArgumentList.Add(a);
                    if (Process.Start(psi) is { } proc) helpers.Add((proc, h.CloseWithGame));
                }
                catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or FormatException) { status("Não consegui abrir " + h.Path + ": " + e.Message); }
            }
            if (helpers.Count > 0) await Task.Delay(1500, ct);

            status($"Iniciando {g.Name}…");
            if (g.IsSteam) await StartAsync("steam", [$"steam://rungameid/{g.Id}"], [], ct);
            else if (g.LaunchUri is not null) await StartAsync("xdg-open", [g.LaunchUri], [], ct);
            else if (exe is not null && LinuxGameLibrary.IsRunnableFile(exe))
            {
                var cmd = BuildManual(exe, p, g.Args, gpuVendor, BoundedProcess.Find);
                await StartAsync(cmd.Program, cmd.Arguments, cmd.Environment, ct, Path.GetDirectoryName(exe));
            }
            else throw new InvalidOperationException("Não achei o executável deste jogo. Escolha o arquivo na seção Desempenho.");

            IReadOnlyList<int> running = [];
            for (int i = 0; i < 120 && running.Count == 0; i++) { await Task.Delay(1500, ct); running = GameProcesses.Find(procRoot, g, p.ExePath ?? g.ExePath); }
            if (running.Count == 0) { status("Não detectei o processo do jogo (ele pode ter demorado a abrir). Os ajustes continuam aplicados."); return; }
            status($"{g.Name} em execução" + (p.HighPerfPlan ? " · perfil Alto desempenho" : "") + ".");
            int quiet = 0;
            while (quiet < 6) { await Task.Delay(1000, ct); running = GameProcesses.Find(procRoot, g, p.ExePath ?? g.ExePath); quiet = running.Count == 0 ? quiet + 1 : 0; }
            status($"{g.Name} encerrado. Ajustes temporários desfeitos.");
        }
        catch (OperationCanceledException) { status("Acompanhamento cancelado."); }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception or FormatException or ArgumentException) { status("Falhou: " + e.Message); }
        finally
        {
            if (oldPlan is not null) await SetPowerProfileAsync(oldPlan, CancellationToken.None);
            foreach (var (proc, close) in helpers) if (close) await CloseAsync(proc);
        }
    }

    private static async Task StartAsync(string program, IReadOnlyList<string> args, IReadOnlyList<KeyValuePair<string, string>> env, CancellationToken ct, string? workDir = null)
    {
        string path = program.StartsWith('/') ? program : BoundedProcess.Find(program) ?? throw new FileNotFoundException($"{program} não encontrado.");
        var psi = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = workDir ?? "" };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in env) psi.Environment[k] = v;
        using var process = Process.Start(psi) ?? throw new IOException("Não foi possível iniciar " + program);
        await Task.Delay(200, ct);
    }

    private static async Task CloseAsync(Process proc)
    {
        try
        {
            if (proc.HasExited) return;
            try { await BoundedProcess.RunAsync("kill", ["-TERM", proc.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)], TimeSpan.FromSeconds(2), 128); } catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { }
            if (!proc.WaitForExit(1500)) proc.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
