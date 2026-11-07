// New Ilmenyx additions, adapted from the data gathering of the original GpuInfoCollector (MIT, see NOTICE): see LICENSING.md.
using System.Globalization;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// GPU facts from sysfs (PCI ids, link, amdgpu/i915 attributes, hwmon) and, for NVIDIA, one read-only <c>nvidia-smi</c> CSV query.
/// No P/Invoke, no root, nothing written. AMD APU path checked on a real Radeon 610M (Mendocino, 2026-11-06); AMD dedicated and NVIDIA are fixtures only.
/// </summary>
public sealed class LinuxGpuCollector(string sysRoot = "/sys", string procRoot = "/proc", IEnumerable<string>? libraryDirs = null)
{
    private static readonly string[] DefaultLibraryDirs = ["/usr/lib", "/usr/lib64", "/usr/lib/x86_64-linux-gnu", "/lib64", "/usr/lib/aarch64-linux-gnu"];
    private readonly string[] libs = (libraryDirs ?? DefaultLibraryDirs).ToArray();
    private string Sys(string p) => Path.Combine(sysRoot, p);
    private static string Attr(string path, int max = 128) => SysfsText.Read(path, max).Replace("\n", " ").Trim();
    private static long? Num(string path) => long.TryParse(Attr(path, 32), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v) ? v : null;

    public static readonly string[] SmiFields =
    [
        "index", "pci.bus_id", "name", "driver_version", "memory.total", "memory.used", "utilization.gpu", "utilization.memory", "utilization.encoder", "utilization.decoder",
        "temperature.gpu", "temperature.memory", "clocks.current.graphics", "clocks.current.memory", "clocks.max.graphics", "clocks.max.memory", "clocks.max.video",
        "power.draw", "power.limit", "power.default_limit", "power.min_limit", "power.max_limit", "fan.speed", "pstate", "{THROTTLE}",
        "pcie.link.gen.current", "pcie.link.gen.max", "pcie.link.width.current", "pcie.link.width.max", "vbios_version", "compute_cap"
    ];

    /// <summary>Parses nvidia-smi --format=csv,noheader,nounits rows into per-GPU field maps keyed by the lowercase bus id suffix ("01:00.0").</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ParseSmi(string csv)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(16))
        {
            var cells = line.Split(", ", StringSplitOptions.None).Select(c => c.Trim()).ToArray();
            if (cells.Length != SmiFields.Length) continue;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].Length is > 0 and < 160 && !cells[i].StartsWith('[') && cells[i] != "N/A" && !cells[i].All(char.IsControl)) map[SmiFields[i]] = cells[i];
            if (map.TryGetValue("pci.bus_id", out var id) && id.IndexOf(':') is var c && c >= 0) result[id[(c + 1)..].ToLowerInvariant()] = map;
        }
        return result;
    }

    private static double? D(IReadOnlyDictionary<string, string> m, string k) => m.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) && d >= 0 ? d : null;
    private static int? I(IReadOnlyDictionary<string, string> m, string k) => D(m, k) is { } d && d < int.MaxValue ? (int)d : null;

    /// <summary>Runs the single read-only query; null when nvidia-smi is absent or fails (no driver, no device).</summary>
    public static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>?> QuerySmiAsync(CancellationToken ct = default)
    {
        foreach (var throttle in new[] { "clocks_throttle_reasons.active", "clocks_event_reasons.active" })
        {
            try
            {
                string fields = string.Join(",", SmiFields.Select(f => f == "{THROTTLE}" ? throttle : f));
                var (code, output) = await BoundedProcess.RunAsync("nvidia-smi", [$"--query-gpu={fields}", "--format=csv,noheader,nounits"], TimeSpan.FromSeconds(3), 65536, ct);
                if (code == 0 && ParseSmi(output) is { Count: > 0 } parsed) return parsed;
            }
            catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { return null; }
        }
        return null;
    }

    /// <summary>
    /// amdgpu gpu_metrics header (u16 size, u8 format_revision, u8 content_revision): format 2 and 3 are the APU tables, 1 the dedicated-GPU table.
    /// Null when the header is short or of an unknown format (no guess).
    /// </summary>
    public static bool? GpuMetricsIsApu(ReadOnlySpan<byte> header) => header.Length < 4 ? null : header[2] switch { 2 or 3 => true, 1 => false, _ => null };

    /// <summary>Active level ("1: 800Mhz *") of an amdgpu pp_dpm_* list, in MHz; null when no level is marked or the text is not in that format.</summary>
    public static double? ActiveDpmMHz(string text)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(32))
        {
            int colon = line.IndexOf(':');
            if (!line.EndsWith('*') || colon <= 0) continue;
            string value = line[(colon + 1)..^1].Trim();
            if (!value.EndsWith("mhz", StringComparison.OrdinalIgnoreCase)) return null;
            return double.TryParse(value[..^3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double mhz) && double.IsFinite(mhz) && mhz is > 0 and < 100_000 ? mhz : null;
        }
        return null;
    }

    /// <summary>
    /// AMD GPU on the CPU die. The reference table is checked first (no I/O); otherwise the gpu_metrics header, only for amdgpu-bound devices.
    /// Unknown stays false: the card is then shown as a dedicated GPU, exactly as before this check existed.
    /// </summary>
    private static bool AmdApu(string dir, string vendor, string device, string name, string driver)
    {
        if (vendor != "1002") return false;
        if (GpuSpecDb.Find(vendor, device, name) is { } spec) return spec.MemType.StartsWith("Memória do sistema", StringComparison.Ordinal);
        if (driver != "amdgpu") return false;
        try
        {
            using var file = new FileStream(Path.Combine(dir, "gpu_metrics"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, false);
            Span<byte> header = stackalloc byte[4];
            return file.ReadAtLeast(header, 4, throwOnEndOfStream: false) == 4 && GpuMetricsIsApu(header) == true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private string DriverName(string dir)
    {
        try { var l = new DirectoryInfo(Path.Combine(dir, "driver")).ResolveLinkTarget(true); return l is null ? "" : Path.GetFileName(l.FullName); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { return ""; }
    }

    private string DrmCard(string address)
    {
        try
        {
            string root = Sys("class/drm");
            if (!Directory.Exists(root)) return "";
            foreach (var card in Directory.EnumerateDirectories(root, "card*").Where(d => !Path.GetFileName(d).Contains('-')).OrderBy(d => d, StringComparer.Ordinal).Take(16))
                if (new DirectoryInfo(Path.Combine(card, "device")).ResolveLinkTarget(true)?.Name == address) return card;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { }
        return "";
    }

    private string Hwmon(string deviceDir)
    {
        try { return Directory.Exists(Path.Combine(deviceDir, "hwmon")) ? Directory.EnumerateDirectories(Path.Combine(deviceDir, "hwmon")).OrderBy(d => d, StringComparer.Ordinal).FirstOrDefault() ?? "" : ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    private IReadOnlyList<string> Apis(string vendor)
    {
        bool Has(string name) => libs.Any(d => { try { return Directory.Exists(d) && Directory.EnumerateFiles(d, name + "*").Any(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; } });
        var apis = new List<string>();
        if (vendor == "10de" && Has("libcuda.so")) apis.Add("CUDA");
        if (Has("libOpenCL.so")) apis.Add("OpenCL");
        if (Has("libvulkan.so")) apis.Add("Vulkan");
        if (Has("libGL.so")) apis.Add("OpenGL");
        if (Has("libEGL.so")) apis.Add("EGL");
        if (Has("libva.so")) apis.Add("VA-API");
        if (Has("libvdpau.so")) apis.Add("VDPAU");
        return apis;
    }

    public IReadOnlyList<(GpuRaw Raw, GpuCard Card)> Collect(PciIds ids, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? smi = null)
    {
        var list = new List<(GpuRaw, GpuCard)>();
        string pci = Sys("bus/pci/devices");
        if (!Directory.Exists(pci)) return list;
        string kernel = Attr(Path.Combine(procRoot, "sys/kernel/osrelease"), 64);
        foreach (var dir in Directory.EnumerateDirectories(pci).OrderBy(p => p, StringComparer.Ordinal).Take(512))
        {
            if (!Attr(Path.Combine(dir, "class"), 16).StartsWith("0x03", StringComparison.Ordinal)) continue;
            string address = Path.GetFileName(dir);
            string vendor = Attr(Path.Combine(dir, "vendor"), 8).ToLowerInvariant().Replace("0x", ""), device = Attr(Path.Combine(dir, "device"), 8).ToLowerInvariant().Replace("0x", "");
            string subV = Attr(Path.Combine(dir, "subsystem_vendor"), 8).Replace("0x", ""), subD = Attr(Path.Combine(dir, "subsystem_device"), 8).Replace("0x", "");
            string rev = Attr(Path.Combine(dir, "revision"), 8).Replace("0x", "").ToUpperInvariant();
            string drv = DriverName(dir);
            string name = ids.Device(vendor, device) ?? $"GPU {vendor}:{device}";
            string hw = Hwmon(dir), card = DrmCard(address);
            string taint = drv.Length > 0 ? Attr(Sys($"module/{drv}/taint"), 16) : "";

            ulong vram = Num(Path.Combine(dir, "mem_info_vram_total")) is { } vt and > 0 ? (ulong)vt : 0;
            ulong? gtt = Num(Path.Combine(dir, "mem_info_gtt_total")) is { } gt and > 0 ? (ulong)gt : null;
            int? maxCore = null, maxMem = null, maxVideo = null, plim = null, pdef = null, pmin = null, pmax = null;
            string? vbios = Attr(Path.Combine(dir, "vbios_version")) is { Length: > 0 } vb ? vb : null, cuda = null, cap = null, driverVersion = "";
            if (card.Length > 0)
            {
                maxCore = (int?)(Num(Path.Combine(card, "gt_max_freq_mhz")) ?? Num(Path.Combine(card, "gt/gt0/rps_max_freq_mhz")));
            }
            if (hw.Length > 0 && Num(Path.Combine(hw, "power1_cap")) is { } cap0 and > 0) { plim = (int)(cap0 / 1_000_000); pmax = Num(Path.Combine(hw, "power1_cap_max")) is { } cm and > 0 ? (int)(cm / 1_000_000) : null; pdef = Num(Path.Combine(hw, "power1_cap_default")) is { } cd and > 0 ? (int)(cd / 1_000_000) : null; }
            int? maxGen = GpuCardBuilder.GenFromLinkSpeed(Attr(Path.Combine(dir, "max_link_speed"))), curGen = GpuCardBuilder.GenFromLinkSpeed(Attr(Path.Combine(dir, "current_link_speed")));
            int? maxW = (int?)Num(Path.Combine(dir, "max_link_width")), curW = (int?)Num(Path.Combine(dir, "current_link_width"));

            string key = address[(address.IndexOf(':') + 1)..].ToLowerInvariant();
            if (vendor == "10de" && smi is not null && smi.TryGetValue(key, out var s))
            {
                if (s.TryGetValue("name", out var smiName) && smiName.Length > 0) name = smiName;
                if (D(s, "memory.total") is { } mt) vram = (ulong)mt * 1048576UL;
                maxCore = I(s, "clocks.max.graphics") ?? maxCore; maxMem = I(s, "clocks.max.memory"); maxVideo = I(s, "clocks.max.video");
                plim = I(s, "power.limit"); pdef = I(s, "power.default_limit"); pmin = I(s, "power.min_limit"); pmax = I(s, "power.max_limit");
                maxGen = I(s, "pcie.link.gen.max") ?? maxGen; curGen = I(s, "pcie.link.gen.current") ?? curGen; maxW = I(s, "pcie.link.width.max") ?? maxW; curW = I(s, "pcie.link.width.current") ?? curW;
                vbios = s.GetValueOrDefault("vbios_version") ?? vbios; cap = s.GetValueOrDefault("compute_cap"); driverVersion = s.GetValueOrDefault("driver_version") ?? "";
            }
            if (driverVersion.Length == 0)
                driverVersion = vendor == "10de" && drv.StartsWith("nvidia", StringComparison.Ordinal) && Attr(Sys("module/nvidia/version"), 64) is { Length: > 0 } nv ? nv : drv.Length > 0 ? $"módulo {drv} · kernel {kernel}" : "";
            bool integrated = vendor == "8086" || AmdApu(dir, vendor, device, name, drv);
            var raw = new GpuRaw(name, vendor, device, subV, subD, rev, address, drv, driverVersion, vbios, vram, gtt, integrated, maxGen, maxW, curGen, curW,
                maxCore, maxMem, maxVideo, plim, pdef, pmin, pmax, cap, cuda, Apis(vendor), drv.Length > 0 ? LinuxDriverInventory.Flags(taint) : "");
            list.Add((raw, GpuCardBuilder.Build(raw)));
        }
        // Dedicated first (more VRAM), like the original ordering.
        return list.OrderByDescending(x => x.Item1.VramBytes).ThenBy(x => x.Item1.PciAddress, StringComparer.Ordinal).ToList();
    }

    /// <summary>Live readings: sysfs for AMD/Intel, the same nvidia-smi query for NVIDIA. Absent sources stay null.</summary>
    public async Task<GpuTelemetry> TelemetryAsync(GpuRaw raw, CancellationToken ct = default)
    {
        string dir = Path.Combine(Sys("bus/pci/devices"), raw.PciAddress), hw = Hwmon(dir), card = DrmCard(raw.PciAddress);
        if (raw.VendorId == "10de")
        {
            var smi = await QuerySmiAsync(ct);
            string key = raw.PciAddress[(raw.PciAddress.IndexOf(':') + 1)..].ToLowerInvariant();
            if (smi is not null && smi.TryGetValue(key, out var s))
            {
                string? pstate = s.GetValueOrDefault("pstate");
                // The throttle column name depends on the driver generation; ParseSmi stores it under the literal placeholder key.
                ulong? mask = s.TryGetValue("{THROTTLE}", out var hex) && hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && ulong.TryParse(hex[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong m) ? m : null;
                return new(D(s, "utilization.gpu"), D(s, "temperature.gpu"), null, D(s, "temperature.memory"), D(s, "clocks.current.graphics"), D(s, "clocks.current.memory"),
                    D(s, "memory.used") is { } mu ? (ulong)mu * 1048576UL : null, D(s, "memory.total") is { } mt ? (ulong)mt * 1048576UL : null, D(s, "power.draw"), D(s, "power.limit"),
                    D(s, "fan.speed"), null, pstate, mask, D(s, "utilization.memory"), D(s, "utilization.encoder"), D(s, "utilization.decoder"), I(s, "pcie.link.gen.current"), I(s, "pcie.link.width.current"));
            }
            return new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        }
        double? Milli(string file) => hw.Length > 0 && Num(Path.Combine(hw, file)) is { } v and > -40000 and < 200000 ? v / 1000.0 : null;
        double? load = Num(Path.Combine(dir, "gpu_busy_percent")) is { } b and >= 0 and <= 100 ? b : null;
        ulong? used = Num(Path.Combine(dir, "mem_info_vram_used")) is { } vu and >= 0 ? (ulong)vu : null, total = Num(Path.Combine(dir, "mem_info_vram_total")) is { } vtt and > 0 ? (ulong)vtt : null;
        double? power = hw.Length > 0 && (Num(Path.Combine(hw, "power1_average")) ?? Num(Path.Combine(hw, "power1_input"))) is { } pw and >= 0 ? pw / 1_000_000.0 : null;
        double? fanRpm = hw.Length > 0 && Num(Path.Combine(hw, "fan1_input")) is { } fr and >= 0 ? fr : null;
        double? fanPct = hw.Length > 0 && Num(Path.Combine(hw, "pwm1")) is { } pwm and >= 0 and <= 255 ? Math.Round(pwm * 100.0 / 255) : null;
        double? sclk = hw.Length > 0 && Num(Path.Combine(hw, "freq1_input")) is { } f1 and > 0 ? f1 / 1_000_000.0 : card.Length > 0 && (Num(Path.Combine(card, "gt_cur_freq_mhz")) ?? Num(Path.Combine(card, "gt/gt0/rps_cur_freq_mhz"))) is { } ic and > 0 ? ic : null;
        // APUs have no freq2_input; amdgpu still marks the active memory clock level in pp_dpm_mclk (read-only).
        double? mclk = hw.Length > 0 && Num(Path.Combine(hw, "freq2_input")) is { } f2 and > 0 ? f2 / 1_000_000.0
            : raw.VendorId == "1002" ? ActiveDpmMHz(SysfsText.Read(Path.Combine(dir, "pp_dpm_mclk"), 1024)) : null;
        // amdgpu documents power1 on APUs as the SoC power including the CPU: flag it instead of calling it GPU power.
        bool package = raw.Integrated && raw.VendorId == "1002" && power is not null;
        // amdgpu in0 = vddgfx in mV (seen: in0_label "vddgfx", 810 on the Radeon 610M); only taken when the label says so.
        double? volt = hw.Length > 0 && Attr(Path.Combine(hw, "in0_label"), 16) == "vddgfx" && Num(Path.Combine(hw, "in0_input")) is { } mv and > 0 and < 2000 ? mv / 1000.0 : null;
        return new(load, Milli("temp1_input"), Milli("temp2_input"), Milli("temp3_input"), sclk, mclk, used, total, power, raw.PowerLimitW, fanPct, fanRpm, null, null, null, null, null, raw.CurGen, raw.CurWidth,
            raw.Integrated, package, volt);
    }
}
