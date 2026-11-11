// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Per-process facts that do not change while a process lives (owner, the app unit that started it) and the disk counters of
/// /proc/pid/io. Pure parsers here; the sampler caches the stable facts per (pid, start) so each file is read once per process.
/// </summary>
public static class ProcessDetails
{
    [DllImport("libc", EntryPoint = "getuid")] private static extern uint GetUid();
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)] private static extern int Kill(int pid, int signal);

    /// <summary>The user running Ilmenyx (same uid inside and outside the Flatpak sandbox).</summary>
    public static int CurrentUid { get; } = (int)GetUid();

    /// <summary>Real uid from the "Uid:" line of /proc/pid/status.</summary>
    internal static int? ParseUid(string status)
    {
        foreach (var line in status.Split('\n'))
            if (line.StartsWith("Uid:", StringComparison.Ordinal))
            {
                var parts = line[4..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int uid) ? uid : null;
            }
        return null;
    }

    /// <summary>
    /// The systemd app unit in a /proc/pid/cgroup text (cgroup v2 line "0::/path"): the last path segment starting with "app-",
    /// e.g. "app-firefox@cf5e….service" for the browser and its "tab(…).scope" children. Null for services, sessions and kernel threads.
    /// </summary>
    internal static string? ParseAppUnit(string cgroup)
    {
        foreach (var line in cgroup.Split('\n'))
        {
            int at = line.IndexOf("::", StringComparison.Ordinal);
            if (at < 0) continue;
            string? unit = null;
            foreach (var segment in line[(at + 2)..].Split('/'))
                if (segment.StartsWith("app-", StringComparison.Ordinal) && !segment.EndsWith(".slice", StringComparison.Ordinal)) unit = segment;
            if (unit is not null) return unit;
        }
        return null;
    }

    /// <summary>read_bytes and write_bytes of /proc/pid/io (what really reached the disk, not the page cache).</summary>
    internal static (long Read, long Write)? ParseIo(string io)
    {
        long? read = null, write = null;
        foreach (var line in io.Split('\n'))
        {
            if (line.StartsWith("read_bytes:", StringComparison.Ordinal) && long.TryParse(line[11..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long r)) read = r;
            else if (line.StartsWith("write_bytes:", StringComparison.Ordinal) && long.TryParse(line[12..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long w)) write = w;
        }
        return read is { } rr && write is { } ww ? (rr, ww) : null;
    }

    /// <summary>
    /// Flatpak: one host command for the stable facts of the given pids. Output, one line per pid: "pid TAB uid TAB cgroup-path".
    /// Pids are integers formatted here, never user text.
    /// </summary>
    internal static async Task<Dictionary<int, (int? Uid, string? AppUnit)>> HostFactsAsync(IReadOnlyCollection<int> pids, CancellationToken ct)
    {
        var result = new Dictionary<int, (int?, string?)>();
        if (pids.Count == 0) return result;
        string list = string.Join(' ', pids.Select(p => p.ToString(CultureInfo.InvariantCulture)));
        string script = "for p in " + list + "; do u=$(stat -c %u /proc/$p 2>/dev/null) || continue; c=$(cut -d: -f3- /proc/$p/cgroup 2>/dev/null | tail -n 1); printf '%s\\t%s\\t%s\\n' \"$p\" \"$u\" \"$c\"; done; exit 0";
        try
        {
            var (_, output) = await BoundedProcess.RunAsync("sh", ["-c", script], TimeSpan.FromSeconds(5), 4 * 1024 * 1024, ct);
            foreach (var line in output.Split('\n'))
            {
                var f = line.Split('\t');
                if (f.Length < 3 || !int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out int pid)) continue;
                result[pid] = (int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out int uid) ? uid : null, ParseAppUnit("0::" + f[2]));
            }
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { }
        return result;
    }

    /// <summary>Flatpak: /proc/pid/io of the given (own) pids in one host command, each block after a "@pid" line.</summary>
    internal static async Task<Dictionary<int, (long Read, long Write)>> HostIoAsync(IReadOnlyCollection<int> pids, CancellationToken ct)
    {
        var result = new Dictionary<int, (long, long)>();
        if (pids.Count == 0) return result;
        string list = string.Join(' ', pids.Select(p => p.ToString(CultureInfo.InvariantCulture)));
        try
        {
            var (_, output) = await BoundedProcess.RunAsync("sh", ["-c", "for p in " + list + "; do echo \"@$p\"; cat /proc/$p/io 2>/dev/null; done; exit 0"], TimeSpan.FromSeconds(5), 4 * 1024 * 1024, ct);
            foreach (var (pid, block) in SplitBlocks(output))
                if (ParseIo(block) is { } io) result[pid] = io;
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { }
        return result;
    }

    internal static IEnumerable<(int Pid, string Block)> SplitBlocks(string output)
    {
        int pid = -1; var block = new StringBuilder();
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith('@') && int.TryParse(line.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int next))
            {
                if (pid > 0) yield return (pid, block.ToString());
                pid = next; block.Clear();
            }
            else block.Append(line).Append('\n');
        }
        if (pid > 0) yield return (pid, block.ToString());
    }

    /// <summary>
    /// Asks a process of this user to close (SIGTERM) or ends it at once (SIGKILL). The start time is checked first, so a pid reused by a
    /// new process is never signalled. Returns null on success, else the reason in plain words.
    /// </summary>
    public static async Task<string?> SignalAsync(int pid, long startTicks, bool force, string procRoot = "/proc", CancellationToken ct = default)
    {
        if (pid <= 1 || pid == Environment.ProcessId) return Ilmenyx.Core.L.T("Este processo não pode ser encerrado por aqui.");
        int signal = force ? 9 : 15;
        if (HostProcesses.Active(procRoot))
        {
            // The sandbox has its own pid namespace: the host's kill gets the host pid. Same check of the start time, on the host.
            string p = pid.ToString(CultureInfo.InvariantCulture), start = startTicks.ToString(CultureInfo.InvariantCulture);
            string script = $"s=$(cut -d')' -f2- /proc/{p}/stat 2>/dev/null | awk '{{print $20}}'); [ \"$s\" = \"{start}\" ] || exit 3; kill -{signal} {p}";
            try
            {
                var (code, output) = await BoundedProcess.RunAsync("sh", ["-c", script], TimeSpan.FromSeconds(5), 4096, ct);
                return code switch { 0 => null, 3 => Ilmenyx.Core.L.T("O processo já tinha fechado."), _ => Ilmenyx.Core.L.T("Sem permissão para encerrar este processo (ele é de outro usuário ou do sistema).") };
            }
            catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { return e.Message; }
        }
        try
        {
            string stat = LinuxFiles.ReadSmall(Path.Combine(procRoot, pid.ToString(CultureInfo.InvariantCulture), "stat"));
            if (LinuxUsageProvider.ParseStat(stat) is not { } s || s.start != startTicks) return Ilmenyx.Core.L.T("O processo já tinha fechado.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Ilmenyx.Core.L.T("O processo já tinha fechado."); }
        if (Kill(pid, signal) == 0) return null;
        return Marshal.GetLastPInvokeError() switch
        {
            1 => Ilmenyx.Core.L.T("Sem permissão para encerrar este processo (ele é de outro usuário ou do sistema)."),
            3 => Ilmenyx.Core.L.T("O processo já tinha fechado."),
            int code => Ilmenyx.Core.L.T("Não foi possível encerrar (erro ") + code.ToString(CultureInfo.InvariantCulture) + ").",
        };
    }
}

/// <summary>User names for uids, from /etc/passwd (the host's copy inside the Flatpak). Unknown uids show as the number.</summary>
public static class UserNames
{
    private static Dictionary<int, string>? names;

    public static string Of(int uid)
    {
        names ??= Load();
        return names.TryGetValue(uid, out var n) ? n : uid.ToString(CultureInfo.InvariantCulture);
    }

    private static Dictionary<int, string> Load()
    {
        foreach (var file in HostSpawn.InFlatpak ? ["/run/host/etc/passwd", "/etc/passwd"] : new[] { "/etc/passwd" })
            try { return Parse(LinuxFiles.ReadSmall(file)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return [];
    }

    internal static Dictionary<int, string> Parse(string passwd)
    {
        var map = new Dictionary<int, string>();
        foreach (var line in passwd.Split('\n'))
        {
            var f = line.Split(':');
            if (f.Length >= 3 && int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out int uid)) map.TryAdd(uid, f[0]);
        }
        return map;
    }
}
