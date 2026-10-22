// New Ilmenyx additions, adapted from the Optimizer/WindowsTweaks of the original (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

public sealed class LinuxOptimizer(string procRoot = "/proc", string sysRoot = "/sys", string? home = null, Func<IReadOnlyList<string>, CancellationToken, Task<(int Code, string Output)>>? gsettings = null, Func<IReadOnlyList<int>, CancellationToken, Task>? signalTerm = null)
{
    private readonly string home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Executable base name → desktop entry Name, from the .desktop files of installed applications (NoDisplay and non-applications skipped).</summary>
    public IReadOnlyDictionary<string, string> DesktopApps(IEnumerable<string>? dirs = null)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dir in dirs ?? ["/usr/share/applications", Path.Combine(home, ".local/share/applications"), "/var/lib/flatpak/exports/share/applications", Path.Combine(home, ".local/share/flatpak/exports/share/applications")])
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.desktop").Take(2048))
                {
                    try
                    {
                        if (new FileInfo(file).Length > 65536) continue;
                        var d = DesktopEntry.Parse(File.ReadAllText(file));
                        if (d.GetValueOrDefault("Type") != "Application" || string.Equals(d.GetValueOrDefault("NoDisplay"), "true", StringComparison.OrdinalIgnoreCase) || !d.TryGetValue("Name", out var name) || !d.TryGetValue("Exec", out var exec)) continue;
                        string? exe = ExecName(exec); if (exe is null) continue;
                        map.TryAdd(exe.Length > 15 ? exe[..15] : exe, name); map.TryAdd(exe, name); // comm is truncated to 15 characters
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return map;
    }

    /// <summary>Program name from an Exec line: skips env/flatpak wrappers and VAR=value, takes the base name; for "flatpak run app.id" the app id's last segment.</summary>
    public static string? ExecName(string exec)
    {
        List<string> parts;
        try { parts = GameLaunchOptions.SplitArguments(DesktopEntry.CleanExec(exec)).ToList(); } catch (FormatException) { return null; } // quoted paths may contain spaces
        while (parts.Count > 0 && (parts[0] == "env" || parts[0].Contains('=') && !parts[0].StartsWith('/'))) parts.RemoveAt(0);
        if (parts.Count == 0) return null;
        string first = parts[0];
        if (Path.GetFileName(first) == "flatpak" && parts.Contains("run")) { var id = parts.Skip(parts.IndexOf("run") + 1).FirstOrDefault(p => !p.StartsWith('-')); return id?.Split('.').LastOrDefault()?.ToLowerInvariant(); }
        string name = Path.GetFileName(first);
        return name.Length > 0 ? name : null;
    }

    private static string Raw(string path, int max)
    {
        try { using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, false); var b = new byte[max]; int n = s.Read(b, 0, b.Length); return System.Text.Encoding.UTF8.GetString(b, 0, n); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    public IReadOnlyList<BackgroundApp> ListApps(IReadOnlyDictionary<string, string>? desktop = null, int? selfPid = null)
    {
        desktop ??= DesktopApps();
        var (uid, _) = LinuxSerialPorts.Identity(procRoot);
        var list = new List<RunningProcess>(); long page = Environment.SystemPageSize;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(procRoot).Where(d => char.IsAsciiDigit(Path.GetFileName(d)[0])).Take(4096))
            {
                if (!int.TryParse(Path.GetFileName(dir), out int pid)) continue;
                string status = SysfsText.Read(Path.Combine(dir, "status"), 4096);
                var uidLine = status.Split('\n').FirstOrDefault(l => l.StartsWith("Uid:", StringComparison.Ordinal));
                if (uid >= 0 && (uidLine is null || !int.TryParse(uidLine[4..].Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out int pu) || pu != uid)) continue;
                string comm = SysfsText.Read(Path.Combine(dir, "comm"), 32).Trim(); if (comm.Length == 0) continue;
                string argv0 = Raw(Path.Combine(dir, "cmdline"), 512).Split('\0')[0];
                string? app = desktop.GetValueOrDefault(comm) ?? (argv0.Length > 0 ? desktop.GetValueOrDefault(Path.GetFileName(argv0)) : null);
                var statm = SysfsText.Read(Path.Combine(dir, "statm"), 128).Split(' ');
                long rss = statm.Length > 1 && long.TryParse(statm[1], NumberStyles.None, CultureInfo.InvariantCulture, out long pages) ? pages * page : 0;
                list.Add(new(pid, comm, rss, app));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return OptimizerLogic.Build(list, selfPid ?? Environment.ProcessId);
    }

    /// <summary>Asks each process to terminate (SIGTERM, never SIGKILL) and waits up to four seconds. Only the PIDs of the listed (own-user) apps are signalled.</summary>
    public async Task<CloseResult> CloseAsync(IEnumerable<BackgroundApp> apps, CancellationToken ct = default)
    {
        var closed = new List<string>(); var notClosed = new List<string>();
        foreach (var app in apps)
        {
            var pids = app.Pids.Where(p => p > 1 && Directory.Exists(Path.Combine(procRoot, p.ToString(CultureInfo.InvariantCulture)))).ToList();
            if (pids.Count > 0)
                try { await (signalTerm ?? SendTerm)(pids, ct); }
                catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 4 && pids.Any(p => Directory.Exists(Path.Combine(procRoot, p.ToString(CultureInfo.InvariantCulture))))) await Task.Delay(200, ct);
            (pids.Any(p => Directory.Exists(Path.Combine(procRoot, p.ToString(CultureInfo.InvariantCulture)))) ? notClosed : closed).Add(app.Title);
        }
        return new(closed, notClosed);
    }

    private static Task SendTerm(IReadOnlyList<int> pids, CancellationToken ct) =>
        BoundedProcess.RunAsync("kill", ["-TERM", "--", .. pids.Select(p => p.ToString(CultureInfo.InvariantCulture))], TimeSpan.FromSeconds(3), 512, ct);

    public long? SysctlLong(string name)
    {
        string text = SysfsText.Read(Path.Combine(procRoot, "sys", name.Replace('.', '/')), 32);
        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v) ? v : null;
    }

    public IReadOnlyList<GameTweak> ReadOnlyTweaks()
    {
        string governor = SysfsText.Read(Path.Combine(sysRoot, "devices/system/cpu/cpu0/cpufreq/scaling_governor"), 32);
        return [GameTweakRules.MaxMapCount(SysctlLong("vm.max_map_count")), GameTweakRules.Swappiness(SysctlLong("vm.swappiness")), GameTweakRules.Governor(governor.Length > 0 ? governor : null),
            GameTweakRules.OpenFiles(GameTweakRules.ParseOpenFiles(SysfsText.Read(Path.Combine(procRoot, "self/limits"), 4096)))];
    }

    // ---- reversible user-level tweaks ----

    public static bool IsGnome() => (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("GNOME", StringComparison.OrdinalIgnoreCase);
    private Task<(int, string)> Gsettings(IReadOnlyList<string> args, CancellationToken ct) => gsettings is not null ? gsettings(args, ct) : BoundedProcess.RunAsync("gsettings", args, TimeSpan.FromSeconds(3), 256, ct);

    public async Task<string?> MouseAccelProfileAsync(CancellationToken ct = default)
    {
        try { var (code, output) = await Gsettings(["get", "org.gnome.desktop.peripherals.mouse", "accel-profile"], ct); return code == 0 ? output.Trim().Trim('\'') : null; }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { return null; }
    }

    public async Task<bool> SetMouseAccelProfileAsync(string profile, CancellationToken ct = default)
    {
        if (profile is not ("flat" or "adaptive" or "default")) throw new ArgumentException("Perfil inválido.");
        try { var (code, _) = await Gsettings(["set", "org.gnome.desktop.peripherals.mouse", "accel-profile", profile], ct); return code == 0; }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { return false; }
    }
}
