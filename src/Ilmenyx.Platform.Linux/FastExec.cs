// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Diagnostics;

namespace Ilmenyx.Platform.Linux;

/// <summary>What one disposable installer run needs: the umu command, its environment and the two folders involved.</summary>
public sealed record FastExecPlan(string Program, IReadOnlyList<string> Arguments, IReadOnlyList<KeyValuePair<string, string>> Environment, string TempPrefix, string Destination);

/// <summary>
/// "Fast exec": runs a Windows installer (setup.exe) once with Proton in a throwaway prefix, so nothing of the installer stays behind.
/// The chosen destination folder appears inside the installer as drive D: (the game must be installed there); the temporary prefix
/// ("C:" with the installer's registry, redistributables and shortcuts) is deleted afterwards. Wine's menu builder is disabled so no
/// .desktop entries or file associations are created in the user's session.
/// </summary>
public static class FastExec
{
    public const string PrefixPrefix = "fx-";

    public static string TempRoot()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d && Path.IsPathRooted(d) ? d : Path.Combine(home, ".local", "share");
        return Path.Combine(data, "ilmenyx", "fastexec");
    }

    public static string DefaultDestination() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Jogos");

    /// <summary>Command and environment for one run. Throws when the installer or the destination is not acceptable, or umu is missing.</summary>
    public static FastExecPlan Plan(string installer, string destination, Func<string, string?> find, string? tempRoot = null, string? id = null, bool geProton = false)
    {
        if (!LinuxGameLibrary.IsWindowsExe(installer)) throw new ArgumentException("Escolha o instalador do Windows (arquivo .exe).");
        if (!Path.IsPathFullyQualified(destination)) throw new ArgumentException("Escolha a pasta onde o jogo vai ficar.");
        string dest = Path.GetFullPath(destination).TrimEnd('/');
        string home = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd('/');
        if (dest.Length == 0 || dest == home || dest is "/" or "/usr" or "/etc" or "/bin" or "/boot" or "/proc" or "/sys" or "/dev" or "/var" || dest.StartsWith("/proc/", StringComparison.Ordinal) || dest.StartsWith("/sys/", StringComparison.Ordinal) || dest.StartsWith("/dev/", StringComparison.Ordinal))
            throw new ArgumentException("Escolha uma pasta própria para os jogos (por exemplo ~/Jogos), não a pasta pessoal inteira nem uma pasta do sistema.");
        string umu = find("umu-run") ?? throw new InvalidOperationException("O umu-launcher (Proton fora da Steam) não foi encontrado.");
        string prefix = Path.Combine(tempRoot ?? TempRoot(), PrefixPrefix + (id ?? Guid.NewGuid().ToString("N")[..12]));
        return new(umu, [installer], [
            new("WINEPREFIX", prefix), new("GAMEID", "umu-default"), new("STORE", "none"),
            new("WINEDLLOVERRIDES", "winemenubuilder.exe=d"), // no menu entries, icons or file associations from the installer
            // Installers need stability, not game performance: Proton's fsync/esync (and ntsync) shortcuts made a multi-threaded
            // installer fail its own CRC check (2026-11-08) while plain Wine worked; Xalia (gamepad UI helper) only burns CPU here.
            new("PROTON_NO_FSYNC", "1"), new("PROTON_NO_ESYNC", "1"), new("PROTON_NO_NTSYNC", "1"), new("PROTON_USE_XALIA", "0"),
            new("PROTON_LOG", "1"), new("PROTON_LOG_DIR", LogFolder(tempRoot)),
            .. (geProton ? new KeyValuePair<string, string>[] { new("PROTONPATH", "GE-Proton") } : []), // umu downloads the latest GE-Proton
        ], prefix, dest);
    }

    /// <summary>Where the Proton log of each installer run is kept (outside the throwaway prefix, so it survives the cleanup).</summary>
    public static string LogFolder(string? tempRoot = null) => Path.Combine(tempRoot ?? TempRoot(), "logs");

    /// <summary>Newest Proton log of an installer run, if any.</summary>
    public static string? LatestLog(string? tempRoot = null)
    {
        string dir = LogFolder(tempRoot);
        return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("steam-*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName : null;
    }

    /// <summary>Creates the temporary prefix with D: pointing at the destination (Wine keeps drive links that already exist).</summary>
    public static void Prepare(FastExecPlan plan)
    {
        Directory.CreateDirectory(plan.Destination);
        Directory.CreateDirectory(InstallFolder(plan)); // C:\Jogos, where the installer should put the game
        if (plan.Environment.FirstOrDefault(e => e.Key == "PROTON_LOG_DIR").Value is { Length: > 0 } logs) Directory.CreateDirectory(logs);
        string dosdevices = Path.Combine(plan.TempPrefix, "dosdevices");
        Directory.CreateDirectory(dosdevices);
        string d = Path.Combine(dosdevices, "d:");
        if (File.Exists(d) || Directory.Exists(d)) File.Delete(d);
        File.CreateSymbolicLink(d, plan.Destination);
    }

    /// <summary>Runs the installer and waits until it and its helpers end. Cancel kills the whole process tree.</summary>
    public static async Task<int> RunAsync(FastExecPlan plan, CancellationToken ct)
    {
        var psi = HostSpawn.Start(plan.Program, plan.Arguments, plan.Environment.ToDictionary(e => e.Key, e => e.Value), Path.GetDirectoryName(plan.Arguments[0]), redirect: false);
        using var process = Process.Start(psi) ?? throw new IOException("Não foi possível iniciar o instalador.");
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
        return process.ExitCode;
    }

    private static readonly string[] NotGames = ["unins", "setup", "install", "redist", "vcredist", "vc_redist", "dxsetup", "dxwebsetup", "dotnet", "ndp", "crash", "report", "update", "physx", "directx", "oalinst", "ue4prereq", "uninstall", "helper", "cef", "quicksfv", "unarc"];

    /// <summary>Game executables the installer created in the destination after <paramref name="since"/>, likeliest first (larger, shallower, not an uninstaller/redistributable).</summary>
    public static IReadOnlyList<string> NewExecutables(string destination, DateTime sinceUtc, int maxDepth = 6)
    {
        var found = new List<(string Path, long Size, int Depth)>();
        void Walk(string dir, int depth)
        {
            if (depth > maxDepth || found.Count > 2000) return;
            IEnumerable<string> files, dirs;
            try { files = Directory.EnumerateFiles(dir, "*.exe", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true }).ToList(); dirs = Directory.EnumerateDirectories(dir).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
            foreach (var f in files)
            {
                try
                {
                    var info = new FileInfo(f);
                    if (info.LinkTarget is not null || (info.LastWriteTimeUtc < sinceUtc && info.CreationTimeUtc < sinceUtc)) continue;
                    string name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (NotGames.Any(n => name.StartsWith(n, StringComparison.Ordinal) || name.Contains(n, StringComparison.Ordinal))) continue;
                    found.Add((f, info.Length, depth));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            foreach (var sub in dirs) { try { if (new DirectoryInfo(sub).LinkTarget is null) Walk(sub, depth + 1); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
        }
        if (Directory.Exists(destination)) Walk(destination, 0);
        return found.OrderByDescending(f => f.Size).ThenBy(f => f.Depth).Select(f => f.Path).Take(12).ToList();
    }

    /// <summary>Deletes the temporary prefix. The D: link is removed first, so the destination (the installed game) is never touched.</summary>
    public static void Cleanup(FastExecPlan plan)
    {
        string root = Path.GetFullPath(Path.GetDirectoryName(plan.TempPrefix)!);
        if (!Path.GetFileName(plan.TempPrefix).StartsWith(PrefixPrefix, StringComparison.Ordinal) || !Path.GetFullPath(plan.TempPrefix).StartsWith(root + "/", StringComparison.Ordinal)) return;
        DeleteTree(plan.TempPrefix);
    }

    /// <summary>Removes leftovers of runs that were interrupted (crash, power loss): only folders named fx-* inside the fastexec folder.</summary>
    public static int CleanupStale(string? tempRoot = null)
    {
        string root = tempRoot ?? TempRoot(); int removed = 0;
        if (!Directory.Exists(root)) return 0;
        foreach (var dir in Directory.EnumerateDirectories(root, PrefixPrefix + "*")) { DeleteTree(dir); removed++; }
        // Keep only the five newest installer logs.
        if (Directory.Exists(LogFolder(root)))
            foreach (var old in new DirectoryInfo(LogFolder(root)).EnumerateFiles("steam-*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(5))
                try { old.Delete(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return removed;
    }

    /// <summary>The folder offered to the installer inside the temporary Windows: C:\Jogos.</summary>
    public static string InstallFolder(FastExecPlan plan) => Path.Combine(plan.TempPrefix, "drive_c", "Jogos");

    /// <summary>Folders Wine itself creates in Program Files; anything else there came from the installer.</summary>
    private static readonly string[] WineDefaults = ["common files", "internet explorer", "windows media player", "windows nt", "windowspowershell", "microsoft.net", "msbuild", "reference assemblies", "uninstall information", "windows defender", "windows mail", "windows photo viewer", "windows portable devices", "windows sidebar", "steam"]; // Proton creates Program Files (x86)\\Steam itself

    /// <summary>
    /// What the installer put inside the temporary Windows: every folder in C:\Jogos and C:\Games, plus folders in Program Files that
    /// Wine does not create itself. These are moved to the destination before the temporary Windows is deleted.
    /// </summary>
    public static IReadOnlyList<string> InstalledFolders(FastExecPlan plan)
    {
        string c = Path.Combine(plan.TempPrefix, "drive_c"); var list = new List<string>();
        void Add(string parent, bool skipWine)
        {
            if (!Directory.Exists(parent)) return;
            foreach (var d in Directory.EnumerateDirectories(parent))
            {
                if (new DirectoryInfo(d).LinkTarget is not null) continue;
                if (skipWine && WineDefaults.Contains(Path.GetFileName(d).ToLowerInvariant())) continue;
                if (!Directory.EnumerateFileSystemEntries(d, "*", SearchOption.AllDirectories).Any(e => File.Exists(e))) continue; // empty: nothing installed
                list.Add(d);
            }
        }
        Add(Path.Combine(c, "Jogos"), false); Add(Path.Combine(c, "Games"), false);
        Add(Path.Combine(c, "Program Files"), true); Add(Path.Combine(c, "Program Files (x86)"), true);
        return list;
    }

    /// <summary>
    /// Moves each installed folder into the destination (rename on the same disk, copy + delete across disks). An existing name gets
    /// " (2)", " (3)"... Returns the new folders. Never follows symbolic links.
    /// </summary>
    public static IReadOnlyList<string> MoveToDestination(FastExecPlan plan, Action<string>? progress = null)
    {
        Directory.CreateDirectory(plan.Destination);
        var moved = new List<string>();
        foreach (var src in InstalledFolders(plan))
        {
            string name = Path.GetFileName(src), target = Path.Combine(plan.Destination, name);
            for (int i = 2; Directory.Exists(target) || File.Exists(target); i++) target = Path.Combine(plan.Destination, $"{name} ({i})");
            progress?.Invoke(name);
            try { Directory.Move(src, target); }
            catch (IOException) { CopyTree(src, target); DeleteTree(src); } // different disk: copy, then remove the source
            moved.Add(target);
        }
        return moved;
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var entry in new DirectoryInfo(src).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            string to = Path.Combine(dst, entry.Name);
            if (entry.LinkTarget is { } link) { if (entry is DirectoryInfo) Directory.CreateSymbolicLink(to, link); else File.CreateSymbolicLink(to, link); }
            else if (entry is DirectoryInfo) CopyTree(entry.FullName, to);
            else File.Copy(entry.FullName, to, overwrite: false);
        }
    }

    /// <summary>Recursive delete that never follows symbolic links (links are unlinked, their targets stay).</summary>
    private static void DeleteTree(string dir)
    {
        try
        {
            var info = new DirectoryInfo(dir);
            if (!info.Exists) return;
            if (info.LinkTarget is not null) { info.Delete(); return; }
            foreach (var entry in info.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }))
            {
                if (entry.LinkTarget is not null || entry is FileInfo) { try { File.Delete(entry.FullName); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { if (entry is DirectoryInfo d) d.Delete(); } }
                else DeleteTree(entry.FullName);
            }
            info.Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
