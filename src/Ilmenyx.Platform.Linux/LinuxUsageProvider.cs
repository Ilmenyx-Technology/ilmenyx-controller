// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Read-only live usage from /proc and hwmon. Stateful: rates need two samples (the first reports them as null).
/// Process CPU is normalized to total machine capacity (sum across processes is at most 100 %). USER_HZ is assumed 100.
/// Only the kernel comm name is read per process; command lines, environments and other users' I/O are never touched.
/// </summary>
public sealed partial class LinuxUsageProvider(string procRoot = "/proc", string sysRoot = "/sys") : IUsageProvider
{
    public const int MaxProcesses = 4096, TicksPerSecond = 100, MaxFileCharacters = 1 << 20;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private (double At, long Total, long Idle)? cpuBefore;
    private readonly Dictionary<string, (long Total, long Idle)> coreBefore = new(StringComparer.Ordinal);
    private (double At, long Read, long Written)? diskBefore;
    private (double At, long Rx, long Tx)? netBefore;
    private (double At, Dictionary<(int, long), long> Ticks) processBefore = (0, []);
    private bool hasProcessBefore;
    // Facts that never change while a process lives, read once per (pid, start): owner, app unit, full name of a cut comm.
    private Dictionary<(int, long), (int? Uid, string? AppUnit, string? FullName)> facts = [];
    private (double At, Dictionary<(int, long), (long Read, long Write)> Bytes) ioBefore = (0, []);

    /// <summary>
    /// When true (the Processes page is open), each sample also reads the disk counters of the user's own processes. Off by default:
    /// the overview does not need them and it saves hundreds of reads per second.
    /// </summary>
    public bool DetailedProcesses { get; set; }
    private readonly RaplPowerReader rapl = new(sysRoot);
    /// <summary>Why CPU power is (not) available: RAPL present, missing or root-only (the kernel default).</summary>
    public RaplState CpuPowerState => rapl.State;
    public string? CpuPowerZone => rapl.ZonePath ?? rapl.FindPackageZone();

    [GeneratedRegex("^(sd[a-z]+|vd[a-z]+|xvd[a-z]+|nvme[0-9]+n[0-9]+|mmcblk[0-9]+)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex WholeDisk();
    [GeneratedRegex("^(lo|veth.*|docker.*|br-.*|virbr.*)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IgnoredInterface();
    [GeneratedRegex("^[0-9]{1,8}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    public async Task<UsageSample> SampleAsync(CancellationToken ct = default)
    {
        double now = clock.Elapsed.TotalSeconds;
        string stat = Read("stat", ct), meminfo = Read("meminfo", ct), cpuinfo = Read("cpuinfo", ct);
        string diskstats = Read("diskstats", ct), netdev = Read("net/dev", ct);
        var (cpu, logical) = Cpu(stat, now);
        var (maxCore, busiest) = Cores(stat);
        var memory = Memory(meminfo);
        var (read, write) = Disk(diskstats, now);
        var (rx, tx, iface) = Net(netdev, now);
        var interfaces = Interfaces(netdev, now);
        var (swapTotal, swapUsed) = Swap(meminfo);
        var (processes, count) = await Processes(now, Math.Max(1, logical), ct);
        return new(DateTimeOffset.UtcNow, Field(cpuinfo, "model name"), cpu, AverageMhz(cpuinfo), Temperature(),
            memory.total, memory.used, read, write, rx, tx, iface, count, processes, maxCore, busiest, MaxMhz(cpuinfo), rapl.Sample(now), swapTotal, swapUsed, interfaces);
    }

    // The sampler runs off the UI thread (UsageViewModel), so the lean synchronous read is safe here.
    private string Read(string relative, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { return LinuxFiles.ReadSmall(Path.Combine(procRoot, relative)); }
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
                if (best is null || load > best) { best = load; name = L.T("núcleo ") + p[0][3..]; }
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

    /// <summary>Swap from /proc/meminfo (SwapTotal, SwapFree); zram counts as swap. Null when the system has none.</summary>
    internal static (long? Total, long? Used) Swap(string meminfo)
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
        long? total = Kb("SwapTotal"), free = Kb("SwapFree");
        return total is > 0 && free is { } f && f <= total ? (total, total - f) : (null, null);
    }

    private readonly Dictionary<string, (double At, long Rx, long Tx)> ifaceBefore = new(StringComparer.Ordinal);
    private (double At, Dictionary<string, string[]> Map) addresses = (-100, []);

    /// <summary>Each active interface (same filter as the total), with its own rates and IP addresses.</summary>
    private IReadOnlyList<NetInterfaceUsage> Interfaces(string netdev, double now)
    {
        if (now - addresses.At >= 10) addresses = (now, Addresses());
        var list = new List<NetInterfaceUsage>();
        foreach (var line in netdev.Split('\n').Skip(2))
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string iface = line[..colon].Trim();
            var p = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (iface.Length == 0 || IgnoredInterface().IsMatch(iface) || p.Length < 9 || Num(p[0]) is not { } r || Num(p[8]) is not { } t) continue;
            double? rxRate = null, txRate = null;
            if (ifaceBefore.TryGetValue(iface, out var b) && now > b.At && r >= b.Rx && t >= b.Tx) { rxRate = (r - b.Rx) / (now - b.At); txRate = (t - b.Tx) / (now - b.At); }
            ifaceBefore[iface] = (now, r, t);
            var ips = addresses.Map.TryGetValue(iface, out var a) ? a : [];
            // Interfaces that are down and never moved a byte (unused Ethernet ports, idle tunnels) stay out of the list.
            if (ips.Length == 0 && r == 0 && t == 0) continue;
            list.Add(new(iface, rxRate, txRate, ips, Directory.Exists(Path.Combine(sysRoot, "class/net", iface, "wireless"))));
        }
        return list;
    }

    private static Dictionary<string, string[]> Addresses()
    {
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        try
        {
            foreach (var n in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                map[n.Name] = n.GetIPProperties().UnicastAddresses
                    .Where(u => !u.Address.IsIPv6LinkLocal)
                    .OrderBy(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 0 : 1)
                    .Select(u => u.Address.ToString()).ToArray();
        }
        catch (Exception e) when (e is System.Net.NetworkInformation.NetworkInformationException or PlatformNotSupportedException) { }
        return map;
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

    internal static double? AverageMhz(string cpuinfo) => CoreMhz(cpuinfo) is { Length: > 0 } v ? v.Average() : null;

    /// <summary>Fastest core right now ("Clock máximo (núcleo)" of the original overlay); null when cpuinfo has no MHz lines.</summary>
    internal static double? MaxMhz(string cpuinfo) => CoreMhz(cpuinfo) is { Length: > 0 } v ? v.Max() : null;

    private static double[] CoreMhz(string cpuinfo) =>
        cpuinfo.Split('\n').Where(l => l.StartsWith("cpu MHz", StringComparison.Ordinal)).Select(l => l[(l.IndexOf(':') + 1)..].Trim())
            .Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0 && d < 20000 ? d : (double?)null)
            .Where(v => v is not null).Select(v => v!.Value).ToArray();

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
        bool host = HostProcesses.Active(procRoot);
        // Natively one stat file per process; in Flatpak the host's stat lines in one dump (the sandbox /proc only lists the sandbox).
        var stats = new List<(int Pid, string? Dir, string? Line)>();
        if (host) stats.AddRange(HostProcesses.Parse(await HostProcesses.StatDumpAsync(ct)).Take(MaxProcesses).Select(h => (h.Pid, (string?)null, (string?)h.Line)));
        else
        {
            try { stats.AddRange(Directory.EnumerateDirectories(procRoot).Where(p => Digits().IsMatch(Path.GetFileName(p))).Take(MaxProcesses).Select(d => (int.TryParse(Path.GetFileName(d), NumberStyles.None, CultureInfo.InvariantCulture, out int p) ? p : -1, (string?)d, (string?)null))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ([], 0); }
        }
        var parsed = new List<(int Pid, (string name, long ticks, long start, long? rssPages, int ppid) S)>();
        foreach (var (pid, dir, line) in stats)
        {
            ct.ThrowIfCancellationRequested();
            string text;
            if (line is not null) text = line;
            else try { text = LinuxFiles.ReadSmall(Path.Combine(dir!, "stat")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (ParseStat(text) is not { } s || pid < 0) continue;
            count++;
            current[(pid, s.start)] = s.ticks;
            if (s.rssPages is 0) continue; // kernel threads have no resident pages; counted above, hidden from the table
            parsed.Add((pid, s));
        }
        await LearnFacts(parsed.Select(p => (p.Pid, p.S.start, p.S.name)).ToList(), host, ct);
        var io = DetailedProcesses ? await ReadIo(parsed.Where(p => facts.TryGetValue((p.Pid, p.S.start), out var f) && f.Uid == ProcessDetails.CurrentUid).Select(p => (p.Pid, p.S.start)).ToList(), host, ct) : [];
        double ioDt = now - ioBefore.At;
        foreach (var (pid, s) in parsed)
        {
            double? percent = null;
            if (hasProcessBefore && dt > 0 && processBefore.Ticks.TryGetValue((pid, s.start), out long before) && s.ticks >= before)
                percent = Math.Clamp((s.ticks - before) / (double)TicksPerSecond / dt * 100.0 / logical, 0, 100);
            facts.TryGetValue((pid, s.start), out var f);
            double? readBps = null, writeBps = null;
            if (io.TryGetValue((pid, s.start), out var now2) && ioBefore.Bytes.TryGetValue((pid, s.start), out var then) && ioDt > 0 && now2.Read >= then.Read && now2.Write >= then.Write)
            { readBps = (now2.Read - then.Read) / ioDt; writeBps = (now2.Write - then.Write) / ioDt; }
            rows.Add(new(pid, f.FullName ?? s.name, percent, s.rssPages is { } pages ? pages * Environment.SystemPageSize : null, f.Uid, s.ppid, s.start, f.AppUnit, readBps, writeBps));
        }
        processBefore = (now, current); hasProcessBefore = true;
        ioBefore = (now, io);
        // Forget the facts of processes that ended (the dictionary would otherwise grow for as long as the app runs).
        if (facts.Count > current.Count + 256) facts = facts.Where(kv => current.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        return (rows.OrderByDescending(r => r.CpuPercent ?? -1).ThenByDescending(r => r.ResidentBytes ?? -1).ToArray(), count);
    }

    private async Task LearnFacts(List<(int Pid, long Start, string Comm)> seen, bool host, CancellationToken ct)
    {
        var unknown = seen.Where(p => !facts.ContainsKey((p.Pid, p.Start))).ToList();
        if (unknown.Count == 0) return;
        if (host)
        {
            // The sandbox cannot read the host's status/cgroup/cmdline: one host command for all new processes; names stay as the kernel cut them.
            var hostFacts = await ProcessDetails.HostFactsAsync(unknown.Select(u => u.Pid).ToList(), ct);
            foreach (var (pid, start, _) in unknown)
                facts[(pid, start)] = hostFacts.TryGetValue(pid, out var h) ? (h.Uid, h.AppUnit, null) : (null, null, null);
            return;
        }
        foreach (var (pid, start, comm) in unknown)
        {
            string dir = Path.Combine(procRoot, pid.ToString(CultureInfo.InvariantCulture));
            int? uid = null; string? unit = null, full = null;
            try { uid = ProcessDetails.ParseUid(LinuxFiles.ReadSmall(Path.Combine(dir, "status"))); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            try { unit = ProcessDetails.ParseAppUnit(LinuxFiles.ReadSmall(Path.Combine(dir, "cgroup"))); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (comm.Length == CommLength)
                try { full = FullName(comm, LinuxFiles.ReadSmall(Path.Combine(dir, "cmdline"))); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            facts[(pid, start)] = (uid, unit, full);
        }
    }

    private async Task<Dictionary<(int, long), (long Read, long Write)>> ReadIo(List<(int Pid, long Start)> own, bool host, CancellationToken ct)
    {
        var result = new Dictionary<(int, long), (long, long)>();
        if (host)
        {
            var dump = await ProcessDetails.HostIoAsync(own.Select(o => o.Pid).ToList(), ct);
            foreach (var (pid, start) in own) if (dump.TryGetValue(pid, out var v)) result[(pid, start)] = v;
            return result;
        }
        foreach (var (pid, start) in own)
            try { if (ProcessDetails.ParseIo(LinuxFiles.ReadSmall(Path.Combine(procRoot, pid.ToString(CultureInfo.InvariantCulture), "io"))) is { } v) result[(pid, start)] = v; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return result;
    }

    /// <summary>The kernel keeps only the first 15 bytes of a program name (comm).</summary>
    internal const int CommLength = 15;

    /// <summary>
    /// Full program name for a cut comm: the file name of the first command-line argument, used only when it starts with the comm
    /// (so a renamed process title or an interpreter running a script keeps what the kernel reported).
    /// </summary>
    internal static string FullName(string comm, string cmdline)
    {
        string first = cmdline.Split('\0', 2)[0];
        string name = new(Path.GetFileName(first).Where(c => !char.IsControl(c)).Take(64).ToArray());
        return name.Length > comm.Length && name.StartsWith(comm, StringComparison.Ordinal) ? name : comm;
    }

    internal static (string name, long ticks, long start, long? rssPages, int ppid)? ParseStat(string text)
    {
        int open = text.IndexOf('('), close = text.LastIndexOf(')');
        if (open < 0 || close < open) return null;
        // Fields after the name, counted from 0 = field 3 (state); read in place, ~300 times per second, without splitting the line.
        var rest = text.AsSpan(close + 1);
        long utime = 0, stime = 0, start = 0; long? rss = null; int field = 0, ppid = 0;
        foreach (var range in rest.Split(' '))
        {
            var value = rest[range];
            if (value.IsEmpty) continue;
            bool ok = field switch
            {
                1 => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ppid),
                11 => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out utime),
                12 => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out stime),
                19 => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out start),
                21 => true,
                _ => true
            };
            if (!ok) return null;
            if (field == 21) { rss = long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long r) && r >= 0 ? r : null; field++; break; }
            field++;
        }
        if (field < 22) return null;
        string name = new(text[(open + 1)..close].Where(c => !char.IsControl(c)).Take(64).ToArray());
        return (name, utime + stime, start, rss, ppid);
    }
}
