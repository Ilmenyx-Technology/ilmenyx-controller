// New Ilmenyx additions: see LICENSING.md.
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Ilmenyx.Contracts;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Read-only live usage from /proc and hwmon. Stateful: rates need two samples (the first reports them as null).
/// Process CPU is normalized to total machine capacity (sum across processes is at most 100 %). USER_HZ is assumed 100.
/// Only the kernel comm name is read per process; command lines, environments and other users' I/O are never touched.
/// </summary>
public sealed partial class LinuxUsageProvider(string procRoot = "/proc", string sysRoot = "/sys") : IUsageProvider
{
    public const int MaxProcesses = 4096, MaxRows = 64, TicksPerSecond = 100, MaxFileCharacters = 1 << 20;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private (double At, long Total, long Idle)? cpuBefore;
    private readonly Dictionary<string, (long Total, long Idle)> coreBefore = new(StringComparer.Ordinal);
    private (double At, long Read, long Written)? diskBefore;
    private (double At, long Rx, long Tx)? netBefore;
    private (double At, Dictionary<(int, long), long> Ticks) processBefore = (0, []);
    private bool hasProcessBefore;

    [GeneratedRegex("^(sd[a-z]+|vd[a-z]+|xvd[a-z]+|nvme[0-9]+n[0-9]+|mmcblk[0-9]+)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex WholeDisk();
    [GeneratedRegex("^(lo|veth.*|docker.*|br-.*|virbr.*)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IgnoredInterface();
    [GeneratedRegex("^[0-9]{1,8}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    public async Task<UsageSample> SampleAsync(CancellationToken ct = default)
    {
        double now = clock.Elapsed.TotalSeconds;
        string stat = await Read("stat", ct), meminfo = await Read("meminfo", ct), cpuinfo = await Read("cpuinfo", ct);
        string diskstats = await Read("diskstats", ct), netdev = await Read("net/dev", ct);
        var (cpu, logical) = Cpu(stat, now);
        var (maxCore, busiest) = Cores(stat);
        var memory = Memory(meminfo);
        var (read, write) = Disk(diskstats, now);
        var (rx, tx, iface) = Net(netdev, now);
        var (processes, count) = await Processes(now, Math.Max(1, logical), ct);
        return new(DateTimeOffset.UtcNow, Field(cpuinfo, "model name"), cpu, AverageMhz(cpuinfo), Temperature(),
            memory.total, memory.used, read, write, rx, tx, iface, count, processes, maxCore, busiest);
    }

    private async Task<string> Read(string relative, CancellationToken ct)
    {
        try { return await new LinuxFiles().ReadAsync(Path.Combine(procRoot, relative), ct); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    internal static long? Num(string? text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long n) ? n : null;

    private (double?, int) Cpu(string stat, double now)
    {
        int logical = 0; long total = 0, idle = 0; bool found = false;
        foreach (var line in stat.Split('\n'))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length > 0 && p[0].StartsWith("cpu", StringComparison.Ordinal) && p[0].Length > 3 && p[0][3..].All(char.IsAsciiDigit)) logical++;
            if (p.Length >= 5 && p[0] == "cpu" && !found)
            {
                var values = p.Skip(1).Take(8).Select(Num).ToArray();
                if (values.Any(v => v is null)) continue;
                total = values.Sum(v => v!.Value); idle = values[3]!.Value + (values.Length > 4 ? values[4]!.Value : 0); found = true;
            }
        }
        if (!found) return (null, logical);
        double? percent = null;
        if (cpuBefore is { } b && total > b.Total && idle >= b.Idle)
            percent = Math.Clamp(100.0 * (1.0 - (double)(idle - b.Idle) / (total - b.Total)), 0, 100);
        cpuBefore = (now, total, idle);
        return (percent, logical);
    }

    /// <summary>Busiest logical processor since the previous sample (per-core deltas of /proc/stat); null on the first sample.</summary>
    private (double?, string?) Cores(string stat)
    {
        double? best = null; string? name = null;
        foreach (var line in stat.Split('\n'))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5 || !p[0].StartsWith("cpu", StringComparison.Ordinal) || p[0].Length <= 3 || !p[0][3..].All(char.IsAsciiDigit)) continue;
            var values = p.Skip(1).Take(8).Select(Num).ToArray();
            if (values.Any(v => v is null)) continue;
            long total = values.Sum(v => v!.Value), idle = values[3]!.Value + (values.Length > 4 ? values[4]!.Value : 0);
            if (coreBefore.TryGetValue(p[0], out var b) && total > b.Total && idle >= b.Idle)
            {
                double load = Math.Clamp(100.0 * (1.0 - (double)(idle - b.Idle) / (total - b.Total)), 0, 100);
                if (best is null || load > best) { best = load; name = "núcleo " + p[0][3..]; }
            }
            coreBefore[p[0]] = (total, idle);
        }
        return (best, name);
    }

    internal static (long? total, long? used) Memory(string meminfo)
    {
        long? Kb(string key)
        {
            foreach (var line in meminfo.Split('\n'))
            {
                if (!line.StartsWith(key + ":", StringComparison.Ordinal)) continue;
                var p = line[(key.Length + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 2 && p[1] == "kB" && Num(p[0]) is { } n && n <= long.MaxValue / 1024) return n * 1024;
            }
            return null;
        }
        long? total = Kb("MemTotal"), available = Kb("MemAvailable");
        return total is > 0 && available is { } a && a <= total ? (total, total - a) : (total, null);
    }

    private (double?, double?) Disk(string diskstats, double now)
    {
        long read = 0, written = 0; bool any = false;
        foreach (var line in diskstats.Split('\n'))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 10 || !WholeDisk().IsMatch(p[2]) || Num(p[5]) is not { } r || Num(p[9]) is not { } w) continue;
            read += r * 512; written += w * 512; any = true;
        }
        if (!any) return (null, null);
        (double?, double?) rate = (null, null);
        if (diskBefore is { } b && now > b.At && read >= b.Read && written >= b.Written)
            rate = ((read - b.Read) / (now - b.At), (written - b.Written) / (now - b.At));
        diskBefore = (now, read, written);
        return rate;
    }

    private (double?, double?, string?) Net(string netdev, double now)
    {
        long rx = 0, tx = 0, best = -1; string? name = null; bool any = false;
        foreach (var line in netdev.Split('\n').Skip(2))
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string iface = line[..colon].Trim();
            var p = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (iface.Length == 0 || IgnoredInterface().IsMatch(iface) || p.Length < 9 || Num(p[0]) is not { } r || Num(p[8]) is not { } t) continue;
            rx += r; tx += t; any = true;
            if (r + t > best) { best = r + t; name = iface; }
        }
        if (!any) return (null, null, null);
        (double?, double?) rate = (null, null);
        if (netBefore is { } b && now > b.At && rx >= b.Rx && tx >= b.Tx)
            rate = ((rx - b.Rx) / (now - b.At), (tx - b.Tx) / (now - b.At));
        netBefore = (now, rx, tx);
        return (rate.Item1, rate.Item2, name);
    }

    internal static string? Field(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim() == key) return line[(colon + 1)..].Trim() is { Length: > 0 } v ? v : null;
        }
        return null;
    }

    internal static double? AverageMhz(string cpuinfo)
    {
        var values = cpuinfo.Split('\n').Where(l => l.StartsWith("cpu MHz", StringComparison.Ordinal)).Select(l => l[(l.IndexOf(':') + 1)..].Trim())
            .Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0 && d < 20000 ? d : (double?)null)
            .Where(v => v is not null).Select(v => v!.Value).ToArray();
        return values.Length == 0 ? null : values.Average();
    }

    private double? Temperature()
    {
        try
        {
            string root = Path.Combine(sysRoot, "class/hwmon");
            if (!Directory.Exists(root)) return null;
            foreach (var chip in Directory.EnumerateDirectories(root).Take(64).OrderBy(p => p, StringComparer.Ordinal))
            {
                string name = File.Exists(Path.Combine(chip, "name")) ? File.ReadAllText(Path.Combine(chip, "name")).Trim() : "";
                if (name is not ("coretemp" or "k10temp" or "zenpower")) continue;
                double? first = null, preferred = null;
                foreach (var input in Directory.EnumerateFiles(chip, "temp*_input").Take(64).OrderBy(p => p, StringComparer.Ordinal))
                {
                    if (!long.TryParse(File.ReadAllText(input).Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long milli) || milli is < -50000 or > 150000) continue;
                    string labelFile = input.Replace("_input", "_label");
                    string label = File.Exists(labelFile) ? File.ReadAllText(labelFile).Trim() : "";
                    first ??= milli / 1000.0;
                    if (label is "Package id 0" or "Tctl" or "Tdie") preferred ??= milli / 1000.0;
                }
                if ((preferred ?? first) is { } found) return found;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private async Task<(IReadOnlyList<ProcessUsage>, int)> Processes(double now, int logical, CancellationToken ct)
    {
        var current = new Dictionary<(int, long), long>();
        var rows = new List<ProcessUsage>();
        int count = 0;
        double dt = now - processBefore.At;
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateDirectories(procRoot).Where(p => Digits().IsMatch(Path.GetFileName(p))).Take(MaxProcesses).ToArray(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ([], 0); }
        foreach (var dir in entries)
        {
            ct.ThrowIfCancellationRequested();
            string text;
            try { text = await new LinuxFiles().ReadAsync(Path.Combine(dir, "stat"), ct); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (ParseStat(text) is not { } s || !int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out int pid)) continue;
            count++;
            current[(pid, s.start)] = s.ticks;
            double? percent = null;
            if (hasProcessBefore && dt > 0 && processBefore.Ticks.TryGetValue((pid, s.start), out long before) && s.ticks >= before)
                percent = Math.Clamp((s.ticks - before) / (double)TicksPerSecond / dt * 100.0 / logical, 0, 100);
            if (s.rssPages is 0) continue; // kernel threads have no resident pages; counted above, hidden from the table
            rows.Add(new(pid, s.name, percent, s.rssPages is { } pages ? pages * Environment.SystemPageSize : null));
        }
        processBefore = (now, current); hasProcessBefore = true;
        return (rows.OrderByDescending(r => r.CpuPercent ?? -1).ThenByDescending(r => r.ResidentBytes ?? -1).Take(MaxRows).ToArray(), count);
    }

    internal static (string name, long ticks, long start, long? rssPages)? ParseStat(string text)
    {
        int open = text.IndexOf('('), close = text.LastIndexOf(')');
        if (open < 0 || close < open) return null;
        var rest = text[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries); // rest[0] is field 3 (state)
        if (rest.Length < 22 || Num(rest[11]) is not { } utime || Num(rest[12]) is not { } stime || Num(rest[19]) is not { } start) return null;
        long? rss = long.TryParse(rest[21], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long r) && r >= 0 ? r : null;
        string name = new(text[(open + 1)..close].Where(c => !char.IsControl(c)).Take(64).ToArray());
        return (name, utime + stime, start, rss);
    }
}
