// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions, adapted from the behaviour of the original StartupManager (MIT, see NOTICE): see LICENSING.md.
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Program that starts with the user session (XDG autostart .desktop file or systemd user service).</summary>
public sealed record StartupEntry(string Id, string Name, string Command, string Source, bool System, bool Enabled, string Detail, bool FileMissing, string Kind, string? FilePath)
{
    /// <summary>Plain explanation and recommendation (system/desktop piece or the user's app), filled by <see cref="LinuxStartupManager.ListAsync"/>.</summary>
    public StartupAdvice Advice { get; init; } = new(StartupGroup.User, StartupRole.Unknown, "", "", StartupCatalog.RecommendationFor(StartupRole.Unknown, null), true, "");
    internal IReadOnlyDictionary<string, string> Keys { get; init; } = new Dictionary<string, string>();
    internal bool OverridesSystem { get; init; }

    public string State => FileMissing ? L.T("ARQUIVO NÃO EXISTE") : Enabled ? L.T("ATIVADO") : L.T("DESATIVADO");
}

/// <summary>Removed or overridden item kept so "Restaurar removidos" can undo it.</summary>
public sealed record StartupBackup(string Kind, string Name, string? Backup, string? Original, string? Unit);

public static class DesktopEntry
{
    public static Dictionary<string, string> Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal); bool inGroup = false;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inGroup = line == "[Desktop Entry]"; continue; }
            if (!inGroup || line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq > 0) map.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
        return map;
    }

    /// <summary>Sets or removes a key inside [Desktop Entry], preserving every other line (other groups, comments, order).</summary>
    public static string WithKey(string text, string key, string? value)
    {
        var lines = text.Replace("\r", "").Split('\n').ToList();
        int start = lines.FindIndex(l => l.Trim() == "[Desktop Entry]");
        if (start < 0) { lines.Insert(0, "[Desktop Entry]"); start = 0; }
        int end = lines.FindIndex(start + 1, l => l.Trim().StartsWith('[')); if (end < 0) end = lines.Count;
        int found = -1;
        for (int i = start + 1; i < end; i++) if (lines[i].Trim().StartsWith(key + "=", StringComparison.Ordinal)) { found = i; break; }
        if (found >= 0) { if (value is null) lines.RemoveAt(found); else lines[found] = $"{key}={value}"; }
        else if (value is not null)
        {
            int at = end; while (at > start + 1 && lines[at - 1].Trim().Length == 0) at--; // before trailing blank lines of the group
            lines.Insert(at, $"{key}={value}");
        }
        return string.Join("\n", lines);
    }

    private static readonly Regex FieldCode = new(@"\s?%[fFuUdDnNickvm]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static string CleanExec(string exec) => FieldCode.Replace(exec, "").Trim();

    public static string? ExeOf(string exec)
    {
        string c = CleanExec(exec);
        if (c.Length == 0) return null;
        string first = c[0] == '"' && c.IndexOf('"', 1) is > 1 and var end ? c[1..end] : c.Split(' ', 2)[0];
        if (first == "env" || first == "sh" || first == "bash") return first; // wrapper: cannot know the real target
        return first;
    }

    public static bool ExistsOnPathOrAbsolute(string exe, string? pathVariable = null)
    {
        if (exe.StartsWith('/')) return File.Exists(exe);
        foreach (var dir in (pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(Path.Combine(dir, exe))) return true;
        return false;
    }
}

/// <param name="owners">Optional package-ownership lookup for system files (path → package); failures and timeouts mean "unknown".</param>
/// <param name="currentDesktop">XDG_CURRENT_DESKTOP, used to tell entries that do not run in this desktop (OnlyShowIn/NotShowIn).</param>
public sealed class LinuxStartupManager(string configHome, string dataHome, IReadOnlyList<string> systemDirs, Func<IReadOnlyList<string>, CancellationToken, Task<(int Code, string Output)>>? systemctl = null,
    Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, string>>>? owners = null, string? currentDesktop = null)
{
    public static readonly Regex UnitName = new(@"^[A-Za-z0-9:_.@-]{1,120}\.service\z", RegexOptions.CultureInvariant);
    private string UserDir => Path.Combine(configHome, "autostart");
    private string BackupDir => Path.Combine(dataHome, "ilmenyx", "startup");
    private string StorePath => Path.Combine(configHome, "ilmenyx", "startup-removed.json");

    public static LinuxStartupManager CreateDefault()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string cfg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c && Path.IsPathRooted(c) ? c : Path.Combine(home, ".config");
        string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d && Path.IsPathRooted(d) ? d : Path.Combine(home, ".local/share");
        var dirs = (Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS") is { Length: > 0 } x ? x : "/etc/xdg").Split(':', StringSplitOptions.RemoveEmptyEntries).Where(Path.IsPathRooted).Select(p => Path.Combine(p, "autostart")).ToList();
        return new(cfg, data, dirs, SystemctlAsync, PackageOwnersAsync, Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));
    }

    /// <summary>Which package installed each file: one bounded call to dpkg, rpm or pacman (4 s). Read-only; any failure returns an empty map.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> PackageOwnersAsync(IReadOnlyList<string> files, CancellationToken ct)
    {
        var list = files.Where(f => f.StartsWith('/') && !f.Contains('\n')).Take(256).ToList();
        if (list.Count == 0) return new Dictionary<string, string>();
        var timeout = TimeSpan.FromSeconds(4);
        try
        {
            if (BoundedProcess.FindSystem("dpkg") is not null)
                return StartupCatalog.ParseDpkgOwners((await BoundedProcess.RunAsync("dpkg", ["-S", .. list], timeout, 262144, ct)).Output);
            if (BoundedProcess.FindSystem("rpm") is not null)
                return StartupCatalog.ParseRpmOwners(list, (await BoundedProcess.RunAsync("rpm", ["-qf", "--queryformat", "%{NAME}\\n", .. list], timeout, 262144, ct)).Output);
            if (BoundedProcess.FindSystem("pacman") is not null)
                return StartupCatalog.ParsePacmanOwners((await BoundedProcess.RunAsync("pacman", ["-Qo", .. list], timeout, 262144, ct)).Output);
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or OperationCanceledException or System.ComponentModel.Win32Exception) { }
        return new Dictionary<string, string>();
    }

    private static async Task<(int, string)> SystemctlAsync(IReadOnlyList<string> args, CancellationToken ct) => await BoundedProcess.RunAsync("systemctl", args, TimeSpan.FromSeconds(5), 131072, ct);

    public IReadOnlyList<StartupBackup> Removed()
    {
        try { return File.Exists(StorePath) && new FileInfo(StorePath).Length < 1 << 20 ? JsonSerializer.Deserialize<List<StartupBackup>>(File.ReadAllText(StorePath)) ?? [] : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    private void SaveRemoved(IReadOnlyList<StartupBackup> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        string tmp = StorePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list)); File.Move(tmp, StorePath, overwrite: true);
    }

    private static StartupEntry FromDesktop(string file, string source, bool system, string? overrideNote = null)
    {
        string text = File.ReadAllText(file); var d = DesktopEntry.Parse(text);
        string name = d.GetValueOrDefault("Name") is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(file);
        string exec = DesktopEntry.CleanExec(d.GetValueOrDefault("Exec") ?? "");
        string? exe = DesktopEntry.ExeOf(exec);
        bool enabled = !string.Equals(d.GetValueOrDefault("Hidden"), "true", StringComparison.OrdinalIgnoreCase) && !string.Equals(d.GetValueOrDefault(L.T("X-GNOME-Autostart-enabled")), "false", StringComparison.OrdinalIgnoreCase);
        string detail = string.Join(" · ", new[] { d.GetValueOrDefault("Comment"), d.GetValueOrDefault("GenericName") }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
        return new(Path.GetFileName(file), name, exec, overrideNote is null ? source : source + " · " + overrideNote, system, enabled, detail, exe is not null && exe is not ("env" or "sh" or "bash") && !DesktopEntry.ExistsOnPathOrAbsolute(exe), "Desktop", file)
        { Keys = d, OverridesSystem = overrideNote is not null };
    }

    public async Task<IReadOnlyList<StartupEntry>> ListAsync(CancellationToken ct = default)
    {
        var list = new List<StartupEntry>(); var userFiles = new HashSet<string>(StringComparer.Ordinal);
        void ReadDir(string dir, string source, bool system)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var file in Directory.EnumerateFiles(dir, "*.desktop").OrderBy(f => f, StringComparer.Ordinal).Take(512))
                {
                    string id = Path.GetFileName(file);
                    if (system && userFiles.Contains(id)) continue; // overridden by the user's copy
                    try
                    {
                        if (new FileInfo(file).Length > 65536) continue;
                        if (!system) userFiles.Add(id);
                        list.Add(FromDesktop(file, source, system, !system && systemDirs.Any(s => File.Exists(Path.Combine(s, id))) ? L.T("substitui o do sistema") : null));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        ReadDir(UserDir, L.T("Autostart · usuário"), false);
        foreach (var dir in systemDirs) ReadDir(dir, L.T("Autostart · todos os usuários (") + Path.GetDirectoryName(dir) + ")", true);

        if (systemctl is not null)
        {
            try
            {
                var (code, output) = await systemctl(["--user", "list-unit-files", "--type=service", "--state=enabled", "--no-legend", "--no-pager"], ct);
                if (code == 0) foreach (var unit in ParseUnits(output)) list.Add(new(unit, unit[..^8], "systemctl --user start " + unit, L.T("Serviço systemd · usuário"), false, true, "", false, "Service", null));
            }
            catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { }
            foreach (var b in Removed().Where(b => b.Kind == "Service" && b.Unit is not null && !list.Any(e => e.Id == b.Unit)))
                list.Add(new(b.Unit!, b.Unit![..^8], "systemctl --user start " + b.Unit, L.T("Serviço systemd · usuário (desativado pelo Ilmenyx)"), false, false, "", false, "Service", null));
        }
        return (await AdviseAsync(list, ct)).OrderBy(e => e.Advice.Group).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private async Task<List<StartupEntry>> AdviseAsync(List<StartupEntry> list, CancellationToken ct)
    {
        string? SystemFile(StartupEntry e) => e.Kind != "Desktop" ? null : e.System ? e.FilePath : e.OverridesSystem ? systemDirs.Select(s => Path.Combine(s, e.Id)).FirstOrDefault(File.Exists) : null;
        IReadOnlyDictionary<string, string> packages = new Dictionary<string, string>();
        var files = list.Select(SystemFile).OfType<string>().Distinct().ToList();
        if (owners is not null && files.Count > 0)
        {
            try { packages = await owners(files, ct); }
            catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception) { }
        }
        return list.Select(e =>
        {
            string? pkg = SystemFile(e) is { } f ? packages.GetValueOrDefault(f) : null;
            // A user unit lives in ~/.config/systemd/user; anything else enabled there was shipped by a package (/usr/lib/systemd/user).
            bool fromSystem = e.Kind == "Service" ? !File.Exists(Path.Combine(configHome, "systemd", "user", e.Id)) : e.System;
            var facts = new StartupFacts(e.Id, e.Kind, e.Command, e.Detail, fromSystem, e.Keys, pkg, currentDesktop, e.OverridesSystem);
            return e with { Advice = StartupCatalog.Classify(facts) };
        }).ToList();
    }

    public static IReadOnlyList<string> ParseUnits(string output) => output.Split('\n').Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length >= 2 && p[1] == "enabled" && UnitName.IsMatch(p[0])).Select(p => p[0]).Take(256).ToList();

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false)); File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Toggle. System entries are disabled through a per-user override (Hidden=true): no root, reversible, the system file is untouched.</summary>
    public async Task SetEnabledAsync(StartupEntry e, bool enabled, CancellationToken ct = default)
    {
        if (e.Kind == "Service")
        {
            if (!UnitName.IsMatch(e.Id) || systemctl is null) throw new ArgumentException(L.T("Unidade inválida."));
            var (code, _) = await systemctl(["--user", enabled ? "enable" : "disable", "--", e.Id], ct);
            if (code != 0) throw new IOException($"systemctl retornou código {code}.");
            var list = Removed().Where(b => !(b.Kind == "Service" && b.Unit == e.Id)).ToList();
            if (!enabled) list.Add(new("Service", e.Name, null, null, e.Id));
            SaveRemoved(list); return;
        }
        if (e.FilePath is null) throw new ArgumentException(L.T("Entrada sem arquivo."));
        string target = Path.Combine(UserDir, e.Id);
        string text = File.ReadAllText(e.FilePath);
        if (e.System && !File.Exists(target)) text = DesktopEntry.WithKey(text, L.T("X-Ilmenyx-Override"), "true");
        text = DesktopEntry.WithKey(text, "Hidden", enabled ? null : "true");
        text = DesktopEntry.WithKey(text, L.T("X-GNOME-Autostart-enabled"), enabled ? null : "false");
        if (enabled && DesktopEntry.Parse(text).ContainsKey("X-Ilmenyx-Override")) { if (File.Exists(target)) File.Delete(target); return; } // drop our override: the system entry applies again
        WriteAtomic(target, text);
    }

    /// <summary>Removes a user autostart file (moved to the backup folder). System entries cannot be removed, only disabled.</summary>
    public StartupBackup Remove(StartupEntry e)
    {
        if (e.Kind != "Desktop" || e.System || e.FilePath is null || Path.GetDirectoryName(e.FilePath) != UserDir) throw new InvalidOperationException(L.T("Só entradas do próprio usuário podem ser removidas; desative as do sistema."));
        if (DesktopEntry.Parse(File.ReadAllText(e.FilePath)).ContainsKey("X-Ilmenyx-Override")) throw new InvalidOperationException(L.T("Esta entrada é um override do Ilmenyx sobre uma entrada do sistema; reative-a em vez de removê-la."));
        Directory.CreateDirectory(BackupDir);
        string copy = Path.Combine(BackupDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{e.Id}");
        File.Move(e.FilePath, copy);
        var b = new StartupBackup("Desktop", e.Name, copy, e.FilePath, null);
        SaveRemoved(Removed().Append(b).ToList()); return b;
    }

    public void Restore(StartupBackup b)
    {
        if (b.Kind == "Service") { SaveRemoved(Removed().Where(x => x != b).ToList()); return; }
        if (b.Backup is null || b.Original is null || !File.Exists(b.Backup) || Path.GetDirectoryName(b.Original) != UserDir) throw new IOException(L.T("Backup indisponível."));
        Directory.CreateDirectory(UserDir); File.Move(b.Backup, b.Original, overwrite: false);
        SaveRemoved(Removed().Where(x => x != b).ToList());
    }

    public async Task RestoreAllAsync(CancellationToken ct = default)
    {
        foreach (var b in Removed().ToList())
        {
            if (b.Kind == "Service" && b.Unit is not null && systemctl is not null && UnitName.IsMatch(b.Unit)) { await systemctl(["--user", "enable", "--", b.Unit], ct); Restore(b); }
            else Restore(b);
        }
    }
}
