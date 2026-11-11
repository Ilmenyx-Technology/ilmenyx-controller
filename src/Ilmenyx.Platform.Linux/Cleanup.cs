// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using System.Text.RegularExpressions;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

public enum CleanupKind { Trash, UserCache, PackageCache, Journal, FlatpakUnused }

/// <param name="Bytes">What the cleanup frees, as measured now; null when it can only be known by doing it (Flatpak).</param>
/// <param name="Recommended">Safe for everyone: comes ticked.</param>
/// <param name="NeedsPassword">Runs as root through polkit (pkexec).</param>
/// <param name="Command">What will run, shown before anything happens; empty for in-app deletions.</param>
public sealed record CleanupItem(CleanupKind Kind, string Title, string Explanation, long? Bytes, bool Recommended, bool NeedsPassword, string Command, string? Unavailable = null);

/// <summary>A big file in the home folder that nobody opened or changed for a long time.</summary>
public sealed record StaleFile(string Path, long Bytes, int DaysUnused);

/// <summary>
/// "Liberar espaço": measures what can be removed safely (trash, program caches, downloaded packages of the distribution, old system
/// logs, Flatpak runtimes no app uses) and lists big files unused for a long time. Nothing is deleted here without an explicit call from
/// the page, which only happens after the user ticks items and confirms. Caches rebuild themselves; package caches keep the last two
/// versions; logs keep four weeks.
/// </summary>
public static partial class Cleanup
{
    public const int StaleDays = 50;
    public const long StaleMinBytes = 50L << 20;
    private const int MaxStale = 200;

    public static string Home => Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } h ? h : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string TrashDir => Path.Combine(Home, ".local/share/Trash");
    public static string CacheDir => Path.Combine(Home, ".cache");

    [GeneratedRegex(@"disk space saved:\s*([0-9.]+)\s*([KMGT]i?B)", RegexOptions.CultureInvariant)]
    private static partial Regex PaccacheSaved();
    [GeneratedRegex(@"take up\s*([0-9.]+)\s*([KMGT])", RegexOptions.CultureInvariant)]
    private static partial Regex JournalUsage();

    public static async Task<IReadOnlyList<CleanupItem>> ScanAsync(string distroId, string idLike, CancellationToken ct = default)
    {
        var items = new List<CleanupItem>
        {
            new(CleanupKind.Trash, L.T("Lixeira"), L.T("Arquivos que você já apagou."), await Task.Run(() => DirectorySize(TrashDir, ct), ct), true, false, ""),
            new(CleanupKind.UserCache, L.T("Caches de programas"), L.T("Miniaturas, navegador e afins em ~/.cache. Se refazem quando preciso (o primeiro uso fica um pouco mais lento). Feche os programas antes."), await Task.Run(() => DirectorySize(CacheDir, ct, skip: "ilmenyx"), ct), true, false, ""),
        };
        items.Add(await PackageCacheAsync(distroId, idLike, ct));
        items.Add(await JournalAsync(ct));
        if (BoundedProcess.FindSystem("flatpak") is not null)
            items.Add(new(CleanupKind.FlatpakUnused, L.T("Flatpak sem uso"), L.T("Bibliotecas (runtimes) que nenhum app instalado usa mais. O tamanho só é conhecido ao remover."), null, false, false, "flatpak uninstall --unused -y --noninteractive"));
        return items;
    }

    /// <summary>Total size of the files under a folder (symbolic links not followed); 0 when it does not exist.</summary>
    public static long DirectorySize(string root, CancellationToken ct, string? skip = null)
    {
        long total = 0;
        if (!Directory.Exists(root)) return 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
        foreach (var info in new DirectoryInfo(root).EnumerateFiles("*", options))
        {
            ct.ThrowIfCancellationRequested();
            if (skip is not null && info.FullName.StartsWith(Path.Combine(root, skip) + "/", StringComparison.Ordinal)) continue;
            try { total += info.Length; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }

    /// <summary>Cleaning command of the distribution's package cache. Arch keeps the last two versions (paccache) so a downgrade stays possible.</summary>
    public static (string Command, string Folder) PackagePlan(string distroId, string idLike, bool paccache)
    {
        string all = (distroId + " " + idLike).ToLowerInvariant();
        if (all.Contains("arch")) return (paccache ? "paccache -rk2" : "pacman -Sc --noconfirm", "/var/cache/pacman/pkg");
        if (all.Contains("fedora") || all.Contains("rhel")) return ("dnf clean packages", "/var/cache/libdnf5");
        if (all.Contains("suse")) return ("zypper clean --all", "/var/cache/zypp/packages");
        return ("apt-get clean", "/var/cache/apt/archives");
    }

    private static async Task<CleanupItem> PackageCacheAsync(string distroId, string idLike, CancellationToken ct)
    {
        bool paccache = BoundedProcess.FindSystem("paccache") is not null;
        var (command, folder) = PackagePlan(distroId, idLike, paccache);
        string title = L.T("Pacotes baixados do sistema");
        string text = command.StartsWith("paccache", StringComparison.Ordinal)
            ? L.T("Instaladores antigos dos programas do sistema. Mantém as 2 últimas versões de cada um, para poder voltar. Pede senha.")
            : L.T("Instaladores dos programas do sistema que já foram instalados. Pede senha.");
        string tool = command.Split(' ')[0];
        if (BoundedProcess.FindSystem(tool) is null) return new(CleanupKind.PackageCache, title, text, null, false, true, command, L.T("Ferramenta não encontrada neste sistema: ") + tool);
        long? bytes = null;
        try
        {
            if (paccache && tool == "paccache")
            {
                var (_, output) = await BoundedProcess.RunAsync("paccache", ["-dk2"], TimeSpan.FromSeconds(60), 1 << 20, ct);
                bytes = ParsePaccache(output);
            }
            if (bytes is null)
            {
                var (code, output) = await BoundedProcess.RunAsync("du", ["-sb", folder], TimeSpan.FromSeconds(60), 4096, ct);
                if (code == 0 && long.TryParse(output.Split('\t')[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long b)) bytes = b;
            }
        }
        catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception) { }
        return new(CleanupKind.PackageCache, title, text, bytes, true, true, command);
    }

    private static async Task<CleanupItem> JournalAsync(CancellationToken ct)
    {
        string title = L.T("Registros antigos do sistema"), text = L.T("Logs com mais de 4 semanas. Os recentes ficam para diagnóstico. Pede senha.");
        const string command = "journalctl --vacuum-time=4weeks";
        if (BoundedProcess.FindSystem("journalctl") is null) return new(CleanupKind.Journal, title, text, null, false, true, command, L.T("Este sistema não usa o journal do systemd."));
        long? bytes = null;
        try { var (_, output) = await BoundedProcess.RunAsync("journalctl", ["--disk-usage"], TimeSpan.FromSeconds(20), 4096, ct); bytes = ParseJournal(output); }
        catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception) { }
        return new(CleanupKind.Journal, title, text, bytes, false, true, command);
    }

    internal static long? ParsePaccache(string output) =>
        PaccacheSaved().Match(output) is { Success: true } m && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? (long)(v * Unit(m.Groups[2].Value)) : output.Contains("no candidate packages found", StringComparison.Ordinal) ? 0 : null;

    internal static long? ParseJournal(string output) =>
        JournalUsage().Match(output) is { Success: true } m && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? (long)(v * Unit(m.Groups[2].Value)) : null;

    private static double Unit(string u) => u[0] switch { 'K' => 1024d, 'M' => 1024d * 1024, 'G' => 1024d * 1024 * 1024, 'T' => 1024d * 1024 * 1024 * 1024, _ => 1 };

    /// <summary>
    /// Big files (≥ 50 MB) in the home folder not opened nor changed for <see cref="StaleDays"/> days, biggest first. Hidden folders
    /// (settings, caches, game libraries, Flatpak data) are not scanned. Access time counts: with relatime the kernel still updates it once a day.
    /// </summary>
    public static IReadOnlyList<StaleFile> StaleFiles(string root, DateTime nowUtc, CancellationToken ct)
    {
        var found = new List<StaleFile>();
        var limit = nowUtc.AddDays(-StaleDays);
        var stack = new Stack<(string Dir, int Depth)>(); stack.Push((root, 0));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                if (entry.Name.StartsWith('.')) continue;
                if (entry is DirectoryInfo d) { if (depth < 8) stack.Push((d.FullName, depth + 1)); continue; }
                if (entry is not FileInfo f) continue;
                try
                {
                    if (f.Length < StaleMinBytes) continue;
                    var last = f.LastAccessTimeUtc > f.LastWriteTimeUtc ? f.LastAccessTimeUtc : f.LastWriteTimeUtc;
                    if (last > limit) continue;
                    found.Add(new(f.FullName, f.Length, (int)(nowUtc - last).TotalDays));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return found.OrderByDescending(f => f.Bytes).Take(MaxStale).ToArray();
    }

    /// <summary>Deletes the contents of a folder (not the folder), skipping files in use or without permission. Returns bytes freed.</summary>
    public static long EmptyDirectory(string root, CancellationToken ct, string? skip = null)
    {
        long freed = 0;
        if (!Directory.Exists(root)) return 0;
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if (skip is not null && entry.Name == skip) continue;
            try
            {
                if (entry is DirectoryInfo d && (d.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    long size = DirectorySize(d.FullName, ct);
                    d.Delete(recursive: true); freed += size;
                }
                else { long size = entry is FileInfo f ? f.Length : 0; entry.Delete(); freed += size; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return freed;
    }

    /// <summary>Deletes chosen stale files; only regular files inside the home folder, never through links. Returns bytes freed.</summary>
    public static long DeleteFiles(IEnumerable<StaleFile> files, string home, CancellationToken ct)
    {
        long freed = 0;
        string prefix = Path.GetFullPath(home).TrimEnd('/') + "/";
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            string full = Path.GetFullPath(f.Path);
            if (!full.StartsWith(prefix, StringComparison.Ordinal)) continue;
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                long size = info.Length; info.Delete(); freed += size;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return freed;
    }

    /// <summary>Runs one cleanup item. User items run in the app; root items through polkit with the fixed command shown before.</summary>
    public static async Task<(bool Ok, string Message, long Freed)> RunAsync(CleanupItem item, CancellationToken ct)
    {
        switch (item.Kind)
        {
            case CleanupKind.Trash:
                return (true, "", await Task.Run(() => EmptyDirectory(Path.Combine(TrashDir, "files"), ct) + EmptyDirectory(Path.Combine(TrashDir, "info"), ct) + EmptyDirectory(Path.Combine(TrashDir, "expunged"), ct), ct));
            case CleanupKind.UserCache:
                return (true, "", await Task.Run(() => EmptyDirectory(CacheDir, ct, skip: "ilmenyx"), ct));
            case CleanupKind.FlatpakUnused:
                try
                {
                    var (code, output, errors) = await BoundedProcess.RunWithErrorsAsync("flatpak", ["uninstall", "--unused", "-y", "--noninteractive"], TimeSpan.FromMinutes(10), 1 << 20, ct);
                    return code == 0 ? (true, output.Contains("Nothing unused", StringComparison.OrdinalIgnoreCase) ? L.T("Nada sem uso para remover.") : "", 0) : (false, (errors + output).Trim(), 0);
                }
                catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception) { return (false, e.Message, 0); }
            default:
                // Fixed commands from PackagePlan/JournalAsync only: the item's Command never comes from user text.
                if (item.Command is not ("paccache -rk2" or "pacman -Sc --noconfirm" or "dnf clean packages" or "zypper clean --all" or "apt-get clean" or "journalctl --vacuum-time=4weeks"))
                    return (false, L.T("Comando não permitido."), 0);
                var (ok, message) = await PrivilegedFix.RunAsync(item.Command, ct);
                return (ok, ok ? "" : message, ok ? item.Bytes ?? 0 : 0);
        }
    }
}
