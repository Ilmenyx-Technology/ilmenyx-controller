// New Ilmenyx additions: see LICENSING.md.
using System.Globalization;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

public sealed record SerialRow(SerialPortInfo Port, SerialChip? Chip, SerialDiagnosisResult Diagnosis);

/// <summary>
/// USB-serial adapters from sysfs (/sys/class/tty/ttyUSB* and ttyACM*), never opening the port (opening can reset boards such as Arduino).
/// Access is computed from the node's owner/group/mode and the user's groups; kernel modules are checked by file presence.
/// </summary>
public sealed class LinuxSerialPorts(string sysRoot = "/sys", string procRoot = "/proc", string devRoot = "/dev", string? modulesRoot = null,
    Func<string, CancellationToken, Task<(int Uid, int Gid, int Mode)?>>? stat = null)
{
    private static string Attr(string path, int max = 80) => SysfsText.Read(path, max).Replace("\n", " ").Trim();

    private static async Task<(int, int, int)?> StatNode(string node, CancellationToken ct)
    {
        try
        {
            var (code, output) = await BoundedProcess.RunAsync("stat", ["-c", "%u %g %a", "--", node], TimeSpan.FromSeconds(2), 128, ct);
            var p = output.Trim().Split(' ');
            return code == 0 && p.Length == 3 && int.TryParse(p[0], out int u) && int.TryParse(p[1], out int g) && int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out int octal)
                ? (u, g, Convert.ToInt32(octal.ToString(CultureInfo.InvariantCulture), 8)) : null;
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { return null; }
    }

    public static (int Uid, int[] Gids) Identity(string procRoot)
    {
        // Flatpak: inside the user namespace the supplementary groups read as 65534 (nobody); the host's `id` gives the real ones,
        // matching the device nodes, which are also stat'ed on the host (StatNode runs stat through HostSpawn).
        if (HostProcesses.Active(procRoot) && HostIdentity() is { } host) return host;
        string status = SysfsText.Read(Path.Combine(procRoot, "self/status"), 4096); // small, line based
        int uid = -1; var gids = new List<int>();
        foreach (var line in status.Split('\n'))
        {
            if (line.StartsWith("Uid:", StringComparison.Ordinal)) { var p = line[4..].Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries); if (p.Length > 0 && int.TryParse(p[0], out int u)) uid = u; }
            else if (line.StartsWith("Groups:", StringComparison.Ordinal)) foreach (var g in line[7..].Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries)) { if (int.TryParse(g, out int supplementary)) gids.Add(supplementary); }
            else if (line.StartsWith("Gid:", StringComparison.Ordinal)) { var p = line[4..].Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries); if (p.Length > 0 && int.TryParse(p[0], out int primary)) gids.Add(primary); }
        }
        return (uid, gids.Distinct().ToArray());
    }

    private static (int Uid, int[] Gids)? HostIdentity()
    {
        try
        {
            var (c1, u) = Task.Run(() => BoundedProcess.RunAsync("id", ["-u"], TimeSpan.FromSeconds(3), 64)).GetAwaiter().GetResult();
            var (c2, g) = Task.Run(() => BoundedProcess.RunAsync("id", ["-G"], TimeSpan.FromSeconds(3), 4096)).GetAwaiter().GetResult();
            if (c1 != 0 || c2 != 0 || !int.TryParse(u.Trim(), out int uid)) return null;
            var gids = g.Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(t => int.TryParse(t, out int x) ? x : -1).Where(x => x >= 0).Distinct().ToArray();
            return gids.Length == 0 ? null : (uid, gids);
        }
        catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { return null; }
    }

    /// <summary>rw access for (uid, gids) from owner/group/other bits of a file mode.</summary>
    public static bool CanReadWrite(int uid, IReadOnlyCollection<int> gids, int nodeUid, int nodeGid, int mode)
    {
        if (uid == 0) return true;
        int bits = uid == nodeUid ? (mode >> 6) & 7 : gids.Contains(nodeGid) ? (mode >> 3) & 7 : mode & 7;
        return (bits & 6) == 6;
    }

    private bool Running(string name)
    {
        if (HostProcesses.Active(procRoot)) return HostProcesses.Parse(HostProcesses.StatDump()).Any(p => p.Comm == name);
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(procRoot).Where(d => char.IsAsciiDigit(Path.GetFileName(d)[0])).Take(4096))
                if (Attr(Path.Combine(dir, "comm"), 32) == name) return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return false;
    }

    private (bool Available, bool Loaded) Module(string name)
    {
        string moduleDir = Path.Combine(sysRoot, "module", name);
        bool loaded = HostSysfs.Hidden(moduleDir) ? HostSysfs.DirectoryExists(moduleDir) : Directory.Exists(moduleDir);
        string release = Attr(Path.Combine(procRoot, "sys/kernel/osrelease"), 64);
        bool available = loaded || (new[] { modulesRoot, HostSpawn.HostPath("/usr/lib/modules"), HostSpawn.HostPath("/lib/modules") }.Where(r => !string.IsNullOrEmpty(r)).Any(r =>
        {
            try { string root = Path.Combine(r!, release, "kernel/drivers/usb/serial"); string acm = Path.Combine(r!, release, "kernel/drivers/usb/class"); return (Directory.Exists(root) && Directory.EnumerateFiles(root, name + ".ko*").Any()) || (Directory.Exists(acm) && Directory.EnumerateFiles(acm, name + ".ko*").Any()); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }));
        return (available, loaded);
    }

    public async Task<IReadOnlyList<SerialRow>> ReadAsync(CancellationToken ct = default)
    {
        var rows = new List<SerialRow>();
        string tty = Path.Combine(sysRoot, "class/tty");
        if (!Directory.Exists(tty)) return rows;
        var (uid, gids) = Identity(procRoot);
        string serialGroup = File.Exists(HostSpawn.HostPath("/etc/group")) && File.ReadAllText(HostSpawn.HostPath("/etc/group")).Contains("\nuucp:", StringComparison.Ordinal) && !File.ReadAllText(HostSpawn.HostPath("/etc/group")).Contains("\ndialout:", StringComparison.Ordinal) ? "uucp" : "dialout";
        var statFn = stat ?? StatNode;
        // Group membership: any of the user's gids that belongs to the serial group is resolved through /etc/group names only for the advice text.
        foreach (var dir in Directory.EnumerateDirectories(tty).Where(d => Path.GetFileName(d).StartsWith("ttyUSB", StringComparison.Ordinal) || Path.GetFileName(d).StartsWith("ttyACM", StringComparison.Ordinal)).OrderBy(d => d, StringComparer.Ordinal).Take(64))
        {
            ct.ThrowIfCancellationRequested();
            string name = Path.GetFileName(dir), node = Path.Combine(devRoot, name);
            // Walk up from the tty's device to the USB device directory that has idVendor.
            string? usb = null;
            try
            {
                var link = new DirectoryInfo(Path.Combine(dir, "device")).ResolveLinkTarget(true);
                string? cur = link?.FullName ?? Path.GetFullPath(Path.Combine(dir, "device"));
                for (int i = 0; i < 6 && cur is not null; i++, cur = Path.GetDirectoryName(cur)) if (File.Exists(Path.Combine(cur, "idVendor"))) { usb = cur; break; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { }
            if (usb is null) continue;
            string vid = Attr(Path.Combine(usb, "idVendor"), 8).ToUpperInvariant(), pid = Attr(Path.Combine(usb, "idProduct"), 8).ToUpperInvariant();
            string driver = "";
            try { driver = new DirectoryInfo(Path.Combine(dir, "device", "driver")).ResolveLinkTarget(true) is { } l ? l.Name : ""; } catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { }
            var st = await statFn(node, ct);
            bool? accessible = st is { } s ? CanReadWrite(uid, gids, s.Uid, s.Gid, s.Mode) : null;
            string? group = null;
            if (st is { } s2 && File.Exists(HostSpawn.HostPath("/etc/group"))) group = File.ReadLines(HostSpawn.HostPath("/etc/group")).Take(2048).Select(l => l.Split(':')).Where(p => p.Length > 2 && p[2] == s2.Gid.ToString(CultureInfo.InvariantCulture)).Select(p => p[0]).FirstOrDefault();
            var port = new SerialPortInfo(node, vid, pid, Attr(Path.Combine(usb, "product")), Attr(Path.Combine(usb, "manufacturer")), driver, accessible, group);
            bool inGroup = st is { } s3 && gids.Contains(s3.Gid);
            var env = new SerialEnvironment(Running("brltty"), Running("ModemManager"), inGroup, serialGroup, Module);
            rows.Add(new(port, SerialDiagnosis.ChipByVid(vid), SerialDiagnosis.Diagnose(port, env)));
        }
        return rows;
    }

    /// <summary>Environment summary for the drivers page.</summary>
    public SerialEnvironment Environment() { var (_, gids) = Identity(procRoot); return new(Running("brltty"), Running("ModemManager"), false, "dialout", Module); }
    public (bool Available, bool Loaded) ModuleState(string name) => Module(name);
}
