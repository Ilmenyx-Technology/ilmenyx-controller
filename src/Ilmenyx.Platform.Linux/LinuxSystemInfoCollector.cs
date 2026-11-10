// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions, adapted from the sections of the original SystemInfoCollector (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;
using System.Runtime.InteropServices;
using Ilmenyx.Contracts;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Static system information for the System page from /proc, /sys/class/dmi/id and /etc. Read-only; serials and tags are never shown.
/// Roots are injectable for fixtures. Every section is isolated: one failure becomes an "Erro ao ler" line, not an exception.
/// </summary>
public sealed class LinuxSystemInfoCollector(string procRoot = "/proc", string sysRoot = "/sys", string etcRoot = "/etc")
{
    private readonly string etcRoot = HostSpawn.HostPath(etcRoot); // Flatpak: the host's /etc (/run/host/etc)

    private string Proc(string p) => Path.Combine(procRoot, p);
    private string Sys(string p) => Path.Combine(sysRoot, p);
    private static string? Text(string path)
    {
        try { var f = new FileInfo(path); return f.Exists && f.Length <= LinuxFiles.MaxCharacters ? File.ReadAllText(path) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
    private static string One(string path) => Text(path)?.Trim() is { Length: > 0 } t ? new string(t.Where(c => !char.IsControl(c)).Take(160).ToArray()) : "—";

    public IReadOnlyList<InfoNode> Collect(MemoryInventory? ram, GpuSnapshot? gpus, PciIds? pciIds)
    {
        var roots = new List<InfoNode>();
        Safe(roots, L.T("Sistema operacional"), Os);
        Safe(roots, L.T("Placa-mãe e BIOS"), Board);
        Safe(roots, L.T("Processador"), Cpu);
        Safe(roots, L.T("Memória RAM"), n => { var r = Memory(ram); n.Children.AddRange(r.Children); n.Value = r.Value; });
        Safe(roots, L.T("Placa de vídeo"), n => Gpu(n, gpus, pciIds ?? new PciIds()));
        Safe(roots, L.T("Armazenamento"), Disks);
        return roots;
    }

    private static void Safe(List<InfoNode> roots, string title, Action<InfoNode> fill)
    {
        var node = new InfoNode { Header = title };
        try { fill(node); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException) { node.Add(L.T("Erro ao ler"), e.Message); }
        roots.Add(node);
    }

    private void Os(InfoNode n)
    {
        var os = Text(Path.Combine(etcRoot, "os-release")) ?? "";
        string? Field(string key) => os.Split('\n').Where(l => l.StartsWith(key + "=", StringComparison.Ordinal)).Select(l => l[(key.Length + 1)..].Trim().Trim('"')).FirstOrDefault();
        n.Add(L.T("Nome"), Field("PRETTY_NAME") ?? "—");
        n.Add(L.T("Versão"), Field("VERSION_ID") ?? Field("VERSION") ?? "(rolling)");
        n.Add("Kernel", One(Proc("sys/kernel/osrelease")));
        n.Add(L.T("Arquitetura"), RuntimeInformation.OSArchitecture.ToString());
        n.Add(L.T("Nome do computador (local; omitido dos relatórios)"), Environment.MachineName);
    }

    private void Board(InfoNode n)
    {
        string D(string f) => One(Sys("class/dmi/id/" + f));
        var board = n.Add(L.T("Placa-mãe"));
        board.Add(L.T("Fabricante"), D("board_vendor")); board.Add(L.T("Modelo"), D("board_name")); board.Add(L.T("Versão"), D("board_version"));
        board.Add(L.T("Nº de série"), L.T("omitido por privacidade"));
        var bios = n.Add("BIOS");
        bios.Add(L.T("Fabricante"), D("bios_vendor")); bios.Add(L.T("Versão"), D("bios_version"));
        string date = D("bios_date");
        bios.Add("Data", DateTime.TryParseExact(date, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : date);
        var system = n.Add(L.T("Sistema (fabricante do equipamento)"));
        system.Add(L.T("Fabricante"), D("sys_vendor")); system.Add(L.T("Produto"), D("product_name"));
    }

    private void Cpu(InfoNode n)
    {
        string text = Text(Proc("cpuinfo")) ?? throw new IOException("/proc/cpuinfo indisponível.");
        var blocks = text.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Where(b => b.Contains("processor", StringComparison.Ordinal)).ToList();
        if (blocks.Count == 0) throw new IOException(L.T("Nenhum processador listado."));
        string Get(string block, string key) => block.Split('\n').Select(l => l.Split(':', 2)).Where(p => p.Length == 2 && p[0].Trim() == key).Select(p => p[1].Trim()).FirstOrDefault() ?? "";
        var first = blocks[0];
        var cpu = n.Add(Get(first, "model name") is { Length: > 0 } name ? name : L.T("Processador"));
        string vendor = Get(first, "vendor_id");
        cpu.Add(L.T("Fabricante"), vendor switch { "GenuineIntel" => "Intel", "AuthenticAMD" => "AMD", "" => "—", _ => vendor });
        var pairs = blocks.Select(b => (Get(b, "physical id"), Get(b, "core id"))).Where(p => p.Item2.Length > 0).Distinct().Count();
        int sockets = Math.Max(1, blocks.Select(b => Get(b, "physical id")).Where(s => s.Length > 0).Distinct().Count());
        cpu.Add(L.T("Soquetes"), sockets.ToString(CultureInfo.InvariantCulture));
        cpu.Add(L.T("Núcleos físicos"), pairs > 0 ? pairs.ToString(CultureInfo.InvariantCulture) : "—");
        cpu.Add(L.T("Threads (lógicos)"), blocks.Count.ToString(CultureInfo.InvariantCulture));
        string max = One(Sys("devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq"));
        cpu.Add(L.T("Clock máximo declarado"), long.TryParse(max, NumberStyles.None, CultureInfo.InvariantCulture, out long khz) ? $"{khz / 1000} MHz" : "—");
        foreach (int level in new[] { 1, 2, 3 })
            cpu.Add($"Cache L{level}", CacheOf(level));
        var flags = Get(first, "flags").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        cpu.Add(L.T("Virtualização"), flags.Contains("vmx") ? L.T("Intel VT-x suportado") + KvmNote() : flags.Contains("svm") ? L.T("AMD-V suportado") + KvmNote() : L.T("Não anunciada nas flags (pode estar desligada na BIOS)"));
        string[] wanted = ["sse4_2", "aes", "avx", "avx2", "avx512f", "fma"];
        cpu.Add(L.T("Extensões relevantes"), string.Join(" ", wanted.Where(flags.Contains)) is { Length: > 0 } list ? list : "—");
    }
    private string KvmNote() => File.Exists("/dev/kvm") && procRoot == "/proc" ? L.T(" · /dev/kvm presente") : "";
    private string CacheOf(int level)
    {
        try
        {
            string root = Sys("devices/system/cpu/cpu0/cache");
            if (!Directory.Exists(root)) return "—";
            var sizes = Directory.EnumerateDirectories(root, "index*").OrderBy(p => p, StringComparer.Ordinal).Take(16)
                .Where(d => One(Path.Combine(d, "level")) == level.ToString(CultureInfo.InvariantCulture) && One(Path.Combine(d, "type")) is "Data" or "Unified")
                .Select(d => One(Path.Combine(d, "size"))).Where(s => s != "—").ToList();
            return sizes.Count > 0 ? string.Join(" + ", sizes) + " (cpu0)" : "—";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "—"; }
    }

    private InfoNode Memory(MemoryInventory? ram)
    {
        long? Kb(string key)
        {
            var line = (Text(Proc("meminfo")) ?? "").Split('\n').FirstOrDefault(l => l.StartsWith(key + ":", StringComparison.Ordinal));
            var p = line?[(key.Length + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return p is { Length: 2 } && p[1] == "kB" && long.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out long v) && v <= long.MaxValue / 1024 ? v * 1024 : null;
        }
        long? total = Kb("MemTotal"), avail = Kb("MemAvailable"), swapT = Kb("SwapTotal"), swapF = Kb("SwapFree");
        return RamInfoBuilder.Build(total, total is { } t && avail is { } a && a <= t ? t - a : null, swapT, swapT is { } st && swapF is { } sf && sf <= st ? st - sf : null, ram);
    }

    private void Gpu(InfoNode n, GpuSnapshot? gpus, PciIds ids)
    {
        if (gpus is null || gpus.Devices.Count == 0) { n.Add(L.T("Nenhuma GPU DRM/PCI identificada"), gpus?.Reason ?? L.T("Inventário não coletado.")); return; }
        foreach (var g in gpus.Devices)
        {
            string vendor = g.VendorId.Value ?? "", device = g.DeviceId.Value ?? "";
            string title = ids.Device(vendor, device) ?? (ids.Vendor(vendor) is { } v ? L.T($"{v} (dispositivo {device})") : L.T($"Dispositivo PCI {vendor}:{device}"));
            var node = n.Add(title);
            node.Add(L.T("Entrada DRM"), g.Device);
            node.Add(L.T("Fabricante"), ids.Vendor(vendor) ?? L.T("não encontrado em pci.ids"));
            node.Add("ID PCI (vendor:device)", $"{vendor}:{device}");
            node.Add(L.T("Classe PCI"), g.PciClass.Value ?? "—");
            node.Add(L.T("Driver do kernel"), DriverOf(g.Device));
            node.Add(L.T("Memória de vídeo"), g.VramTotal.Value is { } vram ? RamInfoBuilder.Bytes(vram) : L.T("indisponível (VRAM de iGPU não é inferida)"));
            node.Add(L.T("Saída/resolução (primeiro modo anunciado)"), Output(g.Device));
        }
    }
    private string DriverOf(string card)
    {
        try { var link = new DirectoryInfo(Sys($"class/drm/{card}/device/driver")).ResolveLinkTarget(true); return link is null ? "—" : Path.GetFileName(link.FullName); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { return "—"; }
    }
    private string Output(string card)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Sys("class/drm"), card + "-*").OrderBy(p => p, StringComparer.Ordinal).Take(16))
                if (One(Path.Combine(dir, "status")) == "connected")
                    return $"{Path.GetFileName(dir)[(card.Length + 1)..]}: {(Text(Path.Combine(dir, "modes"))?.Split('\n').FirstOrDefault(l => l.Length > 0) ?? "—")}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return "—";
    }

    public void Disks(InfoNode n)
    {
        n.Children.Clear();
        string root = Sys("block");
        if (!Directory.Exists(root)) throw new IOException("/sys/block indisponível.");
        foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(p => p, StringComparer.Ordinal).Take(257))
        {
            string name = Path.GetFileName(dir);
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^(sd[a-z]+|vd[a-z]+|xvd[a-z]+|nvme[0-9]+n[0-9]+|mmcblk[0-9]+)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) continue;
            string model = One(Path.Combine(dir, "device/model"));
            var disk = n.Add(model == "—" ? name : model);
            disk.Add(L.T("Dispositivo"), "/dev/" + name);
            disk.Add(L.T("Capacidade"), long.TryParse(One(Path.Combine(dir, "size")), NumberStyles.None, CultureInfo.InvariantCulture, out long sectors) && sectors <= long.MaxValue / 512 ? MountedDrives.Bytes(sectors * 512) : "—");
            string resolved = new DirectoryInfo(dir).ResolveLinkTarget(true)?.FullName ?? "";
            disk.Add("Interface", LinuxParsers.DiskBus(name, resolved).Value ?? L.T("não identificada"));
            disk.Add(L.T("Tipo"), LinuxParsers.DiskKindFor(name, Text(Path.Combine(dir, "queue/rotational")), "", LinuxParsers.DiskBus(name, resolved)).Value ?? "—");
            string fw = One(Path.Combine(dir, "device/firmware_rev")); if (fw == "—") fw = One(Path.Combine(dir, "device/rev"));
            disk.Add("Firmware", fw);
            disk.Add(L.T("Nº de série"), L.T("omitido por privacidade"));
        }
        if (n.Children.Count == 0) n.Add(L.T("Nenhum disco físico visível"), L.T("Somente SATA/NVMe/virtio/MMC são listados."));
    }
}
