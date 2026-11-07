// New Ilmenyx additions: see LICENSING.md.
namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Flatpak: the sandbox has its own PID namespace, so its /proc lists only Ilmenyx. The host's process list comes from one host command
/// (<c>cat /proc/[0-9]*/stat</c>, the same "pid (comm) state ..." lines the native code reads file by file). Natively unused.
/// </summary>
public static class HostProcesses
{
    /// <summary>True when processes must be read on the host: Flatpak and the real /proc (tests pass their own roots).</summary>
    public static bool Active(string procRoot) => HostSpawn.InFlatpak && procRoot == "/proc";

    /// <summary>Every host process stat line; empty when the host cannot be asked.</summary>
    public static async Task<string> StatDumpAsync(CancellationToken ct = default)
    {
        try
        {
            var (_, output) = await BoundedProcess.RunAsync("sh", ["-c", "cat /proc/[0-9]*/stat 2>/dev/null; exit 0"], TimeSpan.FromSeconds(5), 16 * 1024 * 1024, ct);
            return output;
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { return ""; }
    }

    // Sync callers may be on the UI thread: run on the pool so the awaits inside never need the blocked UI context (deadlock seen in the Flatpak, 2026-11-07).
    public static string StatDump() => Task.Run(() => StatDumpAsync()).GetAwaiter().GetResult();

    /// <summary>Pure: (pid, comm, whole stat line) per line of a stat dump. comm is between the first '(' and the last ')' (it may contain spaces).</summary>
    public static IEnumerable<(int Pid, string Comm, string Line)> Parse(string dump)
    {
        foreach (var raw in dump.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int space = line.IndexOf(' '), open = line.IndexOf('('), close = line.LastIndexOf(')');
            if (space <= 0 || open < space || close < open || !int.TryParse(line.AsSpan(0, space), out int pid)) continue;
            yield return (pid, line[(open + 1)..close], line);
        }
    }

    /// <summary>Pids owned by <paramref name="uid"/> on the host (<c>ps -U uid -o pid=</c>); null when unknown.</summary>
    public static HashSet<int>? PidsOf(int uid)
    {
        try
        {
            var (code, output) = Task.Run(() => BoundedProcess.RunAsync("ps", ["-U", uid.ToString(System.Globalization.CultureInfo.InvariantCulture), "-o", "pid="], TimeSpan.FromSeconds(5), 1 << 20)).GetAwaiter().GetResult();
            if (code != 0) return null;
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t => int.TryParse(t, out int p) ? p : -1).Where(p => p > 0).ToHashSet();
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { return null; }
    }
}
