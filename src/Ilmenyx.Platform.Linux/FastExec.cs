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
    public static FastExecPlan Plan(string installer, string destination, Func<string, string?> find, string? tempRoot = null, string? id = null)
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
        ], prefix, dest);
    }

    /// <summary>Creates the temporary prefix with D: pointing at the destination (Wine keeps drive links that already exist).</summary>
    public static void Prepare(FastExecPlan plan)
    {
        Directory.CreateDirectory(plan.Destination);
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
        return removed;
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
