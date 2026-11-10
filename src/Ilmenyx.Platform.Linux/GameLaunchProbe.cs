// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Read-only probes for the "Jogo não abre?" checklist and for ZRAM: files under /proc, /sys, /etc and /usr, plus read-only programs
// (flatpak remotes/info/override --show, vulkaninfo --summary, zramctl). Never as root and never changing anything. The only writer here
// is GameUserFixRunner, which runs the whitelisted user-level flatpak calls of GameLaunchChecks after the user's click.
using System.Globalization;
using System.Text.Json;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

public sealed class GameLaunchProbe
{
    private readonly string root, home;
    private readonly ReadOnlyRunner run;
    private readonly Func<string, bool> hasProgram;

    /// <param name="root">"/" on a real system; a fixture tree in tests (etc/, proc/, sys/, usr/, mnt/…).</param>
    public GameLaunchProbe(string root = "/", ReadOnlyRunner? runner = null, Func<string, bool>? hasProgram = null, string? home = null)
    {
        this.root = root;
        this.home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        run = runner ?? ((p, a, t, ct) => BoundedProcess.FindSystem(p) is null ? throw new FileNotFoundException(p) : BoundedProcess.RunAsync(p, a, t, 1 << 20, ct));
        this.hasProgram = hasProgram ?? (p => BoundedProcess.FindSystem(p) is not null);
    }

    /// <summary>Path of a host file: /etc and /usr come from /run/host inside Flatpak; /proc and /sys are the sandbox's (shared kernel).</summary>
    private string P(string path)
    {
        string full = Path.Combine(root, path.TrimStart('/'));
        return root == "/" && (path.StartsWith("/etc", StringComparison.Ordinal) || path.StartsWith("/usr", StringComparison.Ordinal)) ? HostSpawn.HostPath(full) : full;
    }

    private static string Read(string path, int max = 4096)
    {
        try { return File.Exists(path) || HostSysfs.Hidden(path) ? SysfsText.Read(path, max) : ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static long? Number(string text) => long.TryParse(text.Trim().Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v) ? v : null;

    private long? Sysctl(string name) => File.Exists(Path.Combine(root, "proc/sys", name.Replace('.', '/'))) ? Number(Read(Path.Combine(root, "proc/sys", name.Replace('.', '/')), 64)) : null;

    private async Task<(int Code, string Output)?> TryRun(string program, IReadOnlyList<string> args, int seconds, CancellationToken ct)
    {
        if (!hasProgram(program)) return null;
        try { return await run(program, args, TimeSpan.FromSeconds(seconds), ct); }
        catch (Exception e) when (e is FileNotFoundException or IOException or TimeoutException or System.ComponentModel.Win32Exception) { return null; }
    }

    public DistroInfo Distro()
    {
        string text = Read(P("/etc/os-release"), 8192);
        if (text.Length == 0) text = Read(P("/usr/lib/os-release"), 8192);
        return DistroInfo.Parse(text);
    }

    public async Task<GameCheckFacts> CollectAsync(CancellationToken ct = default)
    {
        var distro = Distro();
        bool flatpak = hasProgram("flatpak");
        IReadOnlyList<FlatpakRemote>? sys = null, user = null;
        bool steamFlatpak = false; var steamFs = new List<string>();
        if (flatpak)
        {
            if (await TryRun("flatpak", ["remotes", "--system", "--columns=name,url,options"], 15, ct) is { Code: 0 } s) sys = GameLaunchChecks.ParseRemotes(s.Output);
            if (await TryRun("flatpak", ["remotes", "--user", "--columns=name,url,options"], 15, ct) is { Code: 0 } u) user = GameLaunchChecks.ParseRemotes(u.Output);
            if (await TryRun("flatpak", ["info", "--show-permissions", GameLaunchChecks.SteamAppId], 15, ct) is { Code: 0 } info)
            {
                steamFlatpak = true;
                steamFs.AddRange(GameLaunchChecks.ParseFilesystems(info.Output));
                if (await TryRun("flatpak", ["override", "--show", GameLaunchChecks.SteamAppId], 15, ct) is { Code: 0 } so) steamFs.AddRange(GameLaunchChecks.ParseFilesystems(so.Output));
                if (await TryRun("flatpak", ["override", "--user", "--show", GameLaunchChecks.SteamAppId], 15, ct) is { Code: 0 } uo) steamFs.AddRange(GameLaunchChecks.ParseFilesystems(uo.Output));
            }
        }
        var drivers = GpuDrivers();
        string? vulkan = null; bool vulkaninfo = hasProgram("vulkaninfo");
        if (vulkaninfo && await TryRun("vulkaninfo", ["--summary"], 20, ct) is { Code: 0 } vk) vulkan = vk.Output;

        string? pacman = distro.Family == DistroFamily.Arch ? Read(P("/etc/pacman.conf"), 65536) is { Length: > 0 } pc ? pc : null : null;
        bool? free = null, nonFree = null;
        if (distro.Family == DistroFamily.Fedora && Directory.Exists(P("/etc/yum.repos.d")))
        {
            string f = Read(P("/etc/yum.repos.d/rpmfusion-free.repo"), 16384), n = Read(P("/etc/yum.repos.d/rpmfusion-nonfree.repo"), 16384);
            free = f.Length > 0 && GameLaunchChecks.RepoEnabled(f); nonFree = n.Length > 0 && GameLaunchChecks.RepoEnabled(n);
        }
        string selinux = Path.Combine(root, "sys/fs/selinux");
        long? enforce = File.Exists(Path.Combine(selinux, "enforce")) ? Number(Read(Path.Combine(selinux, "enforce"), 16)) : null;
        bool? execmod = File.Exists(Path.Combine(selinux, "booleans/selinuxuser_execmod")) ? Number(Read(Path.Combine(selinux, "booleans/selinuxuser_execmod"), 16)) == 1 : null;

        return new GameCheckFacts
        {
            Distro = distro,
            AppArmorRestrictUserns = Sysctl("kernel.apparmor_restrict_unprivileged_userns"),
            UsernsProfiles = UsernsProfiles(),
            UnprivilegedUsernsClone = Sysctl("kernel.unprivileged_userns_clone"),
            MaxUserNamespaces = Sysctl("user.max_user_namespaces"),
            SelinuxEnforce = enforce, SelinuxExecmod = execmod,
            PacmanConf = pacman, RpmFusionFree = free, RpmFusionNonFree = nonFree,
            FlatpakInstalled = flatpak, FlatpakInstallHint = LinuxAppInstaller.FlatpakInstallHint(distro.Id, distro.IdLike),
            SystemRemotes = sys, UserRemotes = user,
            SteamFlatpak = steamFlatpak, SteamFilesystems = steamFs, Home = home, Drives = Drives(),
            SteamNative = hasProgram("steam"),
            GpuDrivers = drivers, IcdLibraries = IcdLibraries(), VulkanSummary = vulkan, VulkaninfoInstalled = vulkaninfo,
            Lib32Present = GameLaunchChecks.Lib32Needed(drivers).Where(l => File.Exists(P(GameLaunchChecks.Lib32Folder(distro.Family) + "/" + l))).ToList(),
            MaxMapCount = Sysctl("vm.max_map_count"),
            SplitLockMitigate = Sysctl("kernel.split_lock_mitigate"),
        };
    }

    /// <summary>Top-level AppArmor profiles for bwrap/Flatpak/Steam: by file name, or a small file that mentions bwrap and userns.</summary>
    public IReadOnlyList<string> UsernsProfiles()
    {
        var list = new List<string>();
        try
        {
            string dir = P("/etc/apparmor.d");
            if (!Directory.Exists(dir)) return list;
            foreach (var file in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.Ordinal).Take(800))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith('.') || name.EndsWith(".dpkg-old", StringComparison.Ordinal) || name.EndsWith(".dpkg-dist", StringComparison.Ordinal)) continue;
                bool byName = new[] { "bwrap", "flatpak", "steam", "pressure-vessel" }.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
                bool byContent = false;
                if (!byName)
                {
                    try { if (new FileInfo(file).Length < 65536) { string t = File.ReadAllText(file); byContent = t.Contains("bwrap", StringComparison.Ordinal) && t.Contains("userns", StringComparison.Ordinal); } }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
                if (byName || byContent) list.Add(name);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list.Take(12).ToList();
    }

    public IReadOnlyList<string> GpuDrivers()
    {
        var list = new List<string>();
        try
        {
            string drm = Path.Combine(root, "sys/class/drm");
            if (!Directory.Exists(drm)) return list;
            foreach (var card in Directory.EnumerateFileSystemEntries(drm, "card*").Where(c => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(c), @"^card\d+$")).OrderBy(c => c, StringComparer.Ordinal))
            {
                string link = Path.Combine(card, "device", "driver");
                string? target = null;
                try { target = new DirectoryInfo(link).LinkTarget ?? (Directory.Exists(link) ? link : null); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                if (target is { Length: > 0 } && Path.GetFileName(target.TrimEnd('/')) is { Length: > 0 } d && !list.Contains(d)) list.Add(d);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list;
    }

    public IReadOnlyList<string> IcdLibraries()
    {
        var libs = new List<string>();
        foreach (var dir in new[] { "/usr/share/vulkan/icd.d", "/etc/vulkan/icd.d", "/usr/local/share/vulkan/icd.d" }.Select(P))
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.json").Take(64))
                {
                    try
                    {
                        if (new FileInfo(file).Length > 65536) continue;
                        using var doc = JsonDocument.Parse(File.ReadAllText(file));
                        if (doc.RootElement.TryGetProperty("ICD", out var icd) && icd.TryGetProperty("library_path", out var lp) && lp.GetString() is { Length: > 0 } path && !libs.Contains(Path.GetFileName(path)))
                            libs.Add(Path.GetFileName(path));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return libs;
    }

    /// <summary>Real filesystems mounted under /mnt, /media or /run/media, plus Steam library folders outside the home folder.</summary>
    public IReadOnlyList<GameDrive> Drives()
    {
        var real = new HashSet<string>(StringComparer.Ordinal) { "ext4", "ext3", "btrfs", "xfs", "f2fs", "ntfs", "ntfs3", "fuseblk", "exfat", "vfat", "zfs", "bcachefs" };
        var list = new List<GameDrive>();
        foreach (var line in Read(Path.Combine(root, "proc/mounts"), 1 << 20).Split('\n'))
        {
            var p = line.Split(' ');
            if (p.Length < 3 || !real.Contains(p[2])) continue;
            string mount = MountedDrives.Unescape(p[1]);
            if (!(mount.StartsWith("/mnt/", StringComparison.Ordinal) || mount.StartsWith("/media/", StringComparison.Ordinal) || mount.StartsWith("/run/media/", StringComparison.Ordinal))) continue;
            if (list.Any(d => d.MountPoint == mount) || list.Count >= 16) continue;
            list.Add(new(mount, HasSteamLibrary(Path.Combine(root, mount.TrimStart('/')))));
        }
        foreach (var steam in new[] { ".var/app/com.valvesoftware.Steam/.local/share/Steam", ".var/app/com.valvesoftware.Steam/data/Steam" }.Select(s => Path.Combine(home, s)).Where(Directory.Exists))
            foreach (var lib in LinuxGameLibrary.LibraryFolders(steam).Skip(1))
            {
                if (lib.StartsWith(home.TrimEnd('/') + "/", StringComparison.Ordinal)) continue;
                int i = list.FindIndex(d => lib == d.MountPoint || lib.StartsWith(d.MountPoint.TrimEnd('/') + "/", StringComparison.Ordinal));
                if (i >= 0) list[i] = list[i] with { HasSteamLibrary = true };
                else if (list.Count < 16) list.Add(new(lib, true));
            }
        return list;
    }

    private static bool HasSteamLibrary(string mount)
    {
        try
        {
            if (Directory.Exists(Path.Combine(mount, "steamapps")) || Directory.Exists(Path.Combine(mount, "SteamLibrary", "steamapps"))) return true;
            return Directory.EnumerateDirectories(mount).Take(64).Any(d => Directory.Exists(Path.Combine(d, "steamapps")));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}

/// <summary>Runs one of the whitelisted user-level flatpak calls (<see cref="GameLaunchChecks.IsAllowedFix"/>): no shell, no root, after a click.</summary>
public static class GameUserFixRunner
{
    public static async Task<(bool Ok, string Message)> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct = default)
    {
        if (!GameLaunchChecks.IsAllowedFix(arguments)) return (false, "Recusado: comando fora da lista permitida.");
        if (BoundedProcess.FindSystem("flatpak") is null) return (false, "O Flatpak não está instalado.");
        try
        {
            var (code, output) = await BoundedProcess.RunAsync("flatpak", arguments, TimeSpan.FromMinutes(2), 65536, ct);
            return code == 0 ? (true, "Pronto.") : (false, "Falhou (código " + code + ")" + (output.Trim().Length > 0 ? ": " + output.Trim() : "."));
        }
        catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or System.ComponentModel.Win32Exception) { return (false, "Falhou: " + e.Message); }
    }
}

public sealed class ZramProbe
{
    private readonly string root;
    private readonly ReadOnlyRunner run;
    private readonly Func<string, bool> hasProgram;

    public ZramProbe(string root = "/", ReadOnlyRunner? runner = null, Func<string, bool>? hasProgram = null)
    {
        this.root = root;
        run = runner ?? ((p, a, t, ct) => BoundedProcess.FindSystem(p) is null ? throw new FileNotFoundException(p) : BoundedProcess.RunAsync(p, a, t, 65536, ct));
        this.hasProgram = hasProgram ?? (p => BoundedProcess.FindSystem(p) is not null);
    }

    private string P(string path)
    {
        string full = Path.Combine(root, path.TrimStart('/'));
        return root == "/" && (path.StartsWith("/etc", StringComparison.Ordinal) || path.StartsWith("/usr", StringComparison.Ordinal)) ? HostSpawn.HostPath(full) : full;
    }

    private static string? Text(string path, int max = 16384)
    {
        try { return File.Exists(path) ? SysfsText.Read(path, max) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private bool Any(params string[] paths) => paths.Any(p => File.Exists(P(p)));

    public async Task<ZramFacts> CollectAsync(CancellationToken ct = default)
    {
        string os = Text(P("/etc/os-release"), 8192) ?? Text(P("/usr/lib/os-release"), 8192) ?? "";
        var devices = new List<ZramDevice>();
        try
        {
            string block = Path.Combine(root, "sys/block");
            if (Directory.Exists(block))
                foreach (var dir in Directory.EnumerateFileSystemEntries(block, "zram*").OrderBy(d => d, StringComparer.Ordinal).Take(16))
                {
                    long.TryParse(Text(Path.Combine(dir, "disksize"), 64), NumberStyles.None, CultureInfo.InvariantCulture, out long size);
                    devices.Add(new(Path.GetFileName(dir), size, ZramPlanner.ActiveAlgorithm(Text(Path.Combine(dir, "comp_algorithm"), 256) ?? "")));
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        // zramctl (util-linux) is only a cross-check for when /sys/block is not visible; sysfs above is the main source.
        if (devices.Count == 0 && hasProgram("zramctl"))
        {
            try
            {
                var (code, output) = await run("zramctl", ["--raw", "--noheadings", "--bytes", "--output", "NAME,DISKSIZE,ALGORITHM"], TimeSpan.FromSeconds(10), ct);
                if (code == 0)
                    foreach (var line in output.Split('\n'))
                        if (line.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var n, var sz, var alg] && long.TryParse(sz, NumberStyles.None, CultureInfo.InvariantCulture, out long b))
                            devices.Add(new(Path.GetFileName(n), b, alg));
            }
            catch (Exception e) when (e is FileNotFoundException or IOException or TimeoutException or System.ComponentModel.Win32Exception) { }
        }
        var dropIns = new List<string>();
        foreach (var d in new[] { "/etc/systemd/zram-generator.conf.d", "/usr/lib/systemd/zram-generator.conf.d", "/run/systemd/zram-generator.conf.d" })
        {
            try { if (Directory.Exists(P(d))) dropIns.AddRange(Directory.EnumerateFiles(P(d), "*.conf").Take(16).Select(f => d + "/" + Path.GetFileName(f))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new ZramFacts
        {
            Distro = DistroInfo.Parse(os),
            MemTotalBytes = ZramPlanner.ParseMemTotal(Text(Path.Combine(root, "proc/meminfo"), 16384) ?? ""),
            Swaps = ZramPlanner.ParseSwaps(Text(Path.Combine(root, "proc/swaps"), 65536) ?? ""),
            Devices = devices,
            EtcGeneratorConf = Text(P(ZramPlanner.GeneratorConf)),
            VendorGeneratorConf = Text(P("/usr/lib/systemd/zram-generator.conf")),
            GeneratorDropIns = dropIns,
            ZramswapDefaults = Text(P(ZramPlanner.ZramswapConf)),
            GeneratorInstalled = Any("/usr/lib/systemd/system-generators/zram-generator", "/lib/systemd/system-generators/zram-generator"),
            ZramToolsInstalled = Any("/usr/sbin/zramswap", "/usr/lib/systemd/system/zramswap.service", "/lib/systemd/system/zramswap.service"),
            ZramConfigInstalled = Any("/usr/bin/init-zram-swapping", "/usr/lib/systemd/system/zram-config.service", "/lib/systemd/system/zram-config.service"),
            State = Text(Path.Combine(root, ZramPlanner.StateFile.TrimStart('/'))),
        };
    }
}
