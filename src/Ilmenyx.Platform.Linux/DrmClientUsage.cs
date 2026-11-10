// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;

namespace Ilmenyx.Platform.Linux;

/// <summary>One DRM client from /proc/&lt;pid&gt;/fdinfo/&lt;fd&gt;: cumulative busy time per engine (ns) and engine capacity.</summary>
public sealed record DrmClient(string Pdev, string ClientId, IReadOnlyDictionary<string, ulong> EngineNs, IReadOnlyDictionary<string, int> Capacity);

/// <summary>
/// GPU load where the driver has no gpu_busy_percent (Intel i915/xe): the kernel's per-client engine time (DRM fdinfo, kernel 5.19+) summed over
/// the user's own processes, between two samples. Processes of other users and the compositor are not readable, so this is the load of the
/// user's programs (games included). Without root. A full /proc scan runs every 10 s; in between only the known GPU clients are read again.
/// </summary>
public static class DrmClientUsage
{
    private static readonly TimeSpan Rescan = TimeSpan.FromSeconds(10);
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, State> States = new(StringComparer.Ordinal);

    private sealed class State
    {
        public DateTime ScannedAt = DateTime.MinValue, SampledAt = DateTime.MinValue;
        public List<string> Files = [];
        public Dictionary<string, IReadOnlyDictionary<string, ulong>> Previous = new(StringComparer.Ordinal);
    }

    public static DrmClient? Parse(string text)
    {
        string? pdev = null, client = null;
        var ns = new Dictionary<string, ulong>(StringComparer.Ordinal); var cap = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon], value = line[(colon + 1)..].Trim();
            if (key == "drm-pdev") pdev = value;
            else if (key == "drm-client-id") client = value;
            else if (key.StartsWith("drm-engine-capacity-", StringComparison.Ordinal) && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int c) && c is > 0 and < 64)
                cap[key["drm-engine-capacity-".Length..]] = c;
            else if (key.StartsWith("drm-engine-", StringComparison.Ordinal) && value.EndsWith(" ns", StringComparison.Ordinal)
                && ulong.TryParse(value[..^3], NumberStyles.None, CultureInfo.InvariantCulture, out ulong v))
                ns[key["drm-engine-".Length..]] = v;
        }
        return pdev is null || client is null || ns.Count == 0 ? null : new(pdev, client, ns, cap);
    }

    /// <summary>Busiest engine in percent between two samples of the same clients (a client that appeared or vanished is left out).</summary>
    public static double? Busy(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ulong>> before, IReadOnlyDictionary<string, IReadOnlyDictionary<string, ulong>> now,
        IReadOnlyDictionary<string, int> capacity, double elapsedNs)
    {
        if (elapsedNs <= 0) return null;
        var sums = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (client, engines) in now)
        {
            if (!before.TryGetValue(client, out var old)) continue;
            foreach (var (engine, value) in engines)
                if (old.TryGetValue(engine, out ulong prev) && value >= prev) sums[engine] = sums.GetValueOrDefault(engine) + (value - prev);
        }
        if (sums.Count == 0) return before.Count > 0 || now.Count > 0 ? 0 : null;
        return Math.Clamp(sums.Max(s => s.Value / (elapsedNs * capacity.GetValueOrDefault(s.Key, 1)) * 100.0), 0, 100);
    }

    /// <summary>Load of the GPU at this PCI address, or null on the first sample, without DRM fdinfo, or when nothing is readable.</summary>
    public static double? Sample(string pdev, string procRoot = "/proc")
    {
        lock (Gate)
        {
            if (!States.TryGetValue(procRoot + "|" + pdev, out var st)) States[procRoot + "|" + pdev] = st = new State();
            var now = DateTime.UtcNow;
            if (now - st.ScannedAt > Rescan) { st.Files = Scan(procRoot, pdev); st.ScannedAt = now; }
            var current = new Dictionary<string, IReadOnlyDictionary<string, ulong>>(StringComparer.Ordinal);
            var capacity = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in st.Files)
            {
                if (Read(f) is not { } c || c.Pdev != pdev) continue;
                current.TryAdd(c.ClientId, c.EngineNs); // several fds of one client share the same counters
                foreach (var (k, v) in c.Capacity) capacity[k] = v;
            }
            double? load = st.SampledAt == DateTime.MinValue ? null : Busy(st.Previous, current, capacity, (now - st.SampledAt).TotalMilliseconds * 1_000_000);
            st.Previous = current; st.SampledAt = now;
            return load;
        }
    }

    private static DrmClient? Read(string fdinfo)
    {
        try { return Parse(SysfsText.Read(fdinfo, 4096)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>fdinfo files of the user's own processes that hold a DRM node of this GPU.</summary>
    private static List<string> Scan(string procRoot, string pdev)
    {
        var files = new List<string>();
        IEnumerable<string> pids;
        try { pids = Directory.EnumerateDirectories(procRoot).Where(d => Path.GetFileName(d).All(char.IsAsciiDigit)).Take(8192).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return files; }
        foreach (var pid in pids)
        {
            try
            {
                foreach (var fd in Directory.EnumerateFiles(Path.Combine(pid, "fd")).Take(4096))
                {
                    if (new FileInfo(fd).LinkTarget is not { } target || !target.StartsWith("/dev/dri/", StringComparison.Ordinal)) continue;
                    string info = Path.Combine(pid, "fdinfo", Path.GetFileName(fd));
                    if (Read(info) is { } c && c.Pdev == pdev) files.Add(info);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // other users' processes, or gone
        }
        return files;
    }
}
