// New Ilmenyx additions: see LICENSING.md. Fixtures sintéticos; nenhum hardware AMD/NVIDIA real foi usado.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class GpuTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-gpu-" + Guid.NewGuid().ToString("N"));
    public GpuTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static readonly PciIds Ids = PciIds.Parse("10de  NVIDIA Corporation\n\t2204  GA102 [GeForce RTX 3090]\n1002  Advanced Micro Devices, Inc. [AMD/ATI]\n\t744c  Navi 31 [Radeon RX 7900 XTX]\n8086  Intel Corporation\n\t9a49  TigerLake-LP GT2 [Iris Xe Graphics]\n");

    private string Gpu(string sys, string addr, string vendor, string device, string driver, Dictionary<string, string>? attrs = null)
    {
        string d = Path.Combine(sys, "devices/pci0000:00", addr); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "class"), "0x030000\n"); File.WriteAllText(Path.Combine(d, "vendor"), vendor + "\n"); File.WriteAllText(Path.Combine(d, "device"), device + "\n");
        File.WriteAllText(Path.Combine(d, "subsystem_vendor"), "0x1043\n"); File.WriteAllText(Path.Combine(d, "subsystem_device"), "0x87d5\n"); File.WriteAllText(Path.Combine(d, "revision"), "0xa1\n");
        File.WriteAllText(Path.Combine(d, "max_link_speed"), "16.0 GT/s PCIe\n"); File.WriteAllText(Path.Combine(d, "current_link_speed"), "2.5 GT/s PCIe\n");
        File.WriteAllText(Path.Combine(d, "max_link_width"), "16\n"); File.WriteAllText(Path.Combine(d, "current_link_width"), "16\n");
        foreach (var kv in attrs ?? []) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(d, kv.Key))!); File.WriteAllText(Path.Combine(d, kv.Key), kv.Value + "\n"); }
        Directory.CreateDirectory(Path.Combine(sys, "bus/pci/devices")); Directory.CreateSymbolicLink(Path.Combine(sys, "bus/pci/devices", addr), d);
        string drv = Path.Combine(sys, "bus/pci/drivers", driver); Directory.CreateDirectory(drv); Directory.CreateSymbolicLink(Path.Combine(d, "driver"), drv);
        return d;
    }

    private const string SmiCsv = "0, 00000000:01:00.0, NVIDIA GeForce RTX 3090, 550.54.14, 24576, 1200, 37, 12, 0, 0, 52, 60, 1695, 9751, 2100, 9751, 1950, 120.50, 350.00, 350.00, 100.00, 390.00, 41, P2, 0x0000000000000004, 4, 4, 16, 16, 94.02.4B.00.A4, 8.6";

    [Fact]
    public void SmiCsvIsParsedIntoNamedFieldsKeyedByBusIdSuffix()
    {
        var all = LinuxGpuCollector.ParseSmi(SmiCsv + "\n" + "garbage line\n" + "1, 00000000:02:00.0, X, [N/A], [Not Supported], N/A, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, P0, 0x0, 1, 1, 1, 1, v, c");
        Assert.Equal(2, all.Count);
        var a = all["01:00.0"]; Assert.Equal("NVIDIA GeForce RTX 3090", a["name"]); Assert.Equal("24576", a["memory.total"]); Assert.Equal("0x0000000000000004", a["{THROTTLE}"]); Assert.Equal("94.02.4B.00.A4", a["vbios_version"]);
        var b = all["02:00.0"]; Assert.False(b.ContainsKey("driver_version")); Assert.False(b.ContainsKey("memory.total")); Assert.Equal("P0", b["pstate"]); // [N/A] values are dropped, never zero
    }

    [Fact]
    public void NvidiaCardUsesSmiForVramClocksPowerVbiosAndSpecDbForChip()
    {
        string sys = Path.Combine(root, "sys"); Gpu(sys, "0000:01:00.0", "0x10de", "0x2204", "nvidia");
        Directory.CreateDirectory(Path.Combine(sys, "module/nvidia")); File.WriteAllText(Path.Combine(sys, "module/nvidia/taint"), "PO\n");
        var lib = Path.Combine(root, "lib"); Directory.CreateDirectory(lib); File.WriteAllText(Path.Combine(lib, "libvulkan.so.1"), ""); File.WriteAllText(Path.Combine(lib, "libcuda.so.1"), "");
        var (raw, card) = new LinuxGpuCollector(sys, Path.Combine(root, "proc"), [lib]).Collect(Ids, LinuxGpuCollector.ParseSmi(SmiCsv)).Single();
        Assert.Equal("NVIDIA", card.Vendor); Assert.Equal("NVIDIA GeForce RTX 3090", raw.Name); Assert.Equal(24576UL << 20, raw.VramBytes);
        string V(string section, string key) => card.Sections.Single(s => s.Header == section).Children.First(c => c.Header == key).Value;
        Assert.Equal("GA102", V("IDENTIFICAÇÃO", "Chip")); Assert.Equal("Ampere", V("IDENTIFICAÇÃO", "Arquitetura")); Assert.Equal("ASUS", V("IDENTIFICAÇÃO", "Fabricante da placa"));
        Assert.Equal("10DE 2204 · 1043 87D5", V("IDENTIFICAÇÃO", "Device ID")); Assert.Equal("A1", V("IDENTIFICAÇÃO", "Revisão"));
        Assert.Equal("10.496".Replace(".", ","), V("UNIDADES DE PROCESSAMENTO", "CUDA cores").Replace(".", ","));
        Assert.Contains("24 GB", V("MEMÓRIA", "Tamanho")); Assert.Contains("19.5 Gbps", V("MEMÓRIA", "Clock da memória").Replace("19,5", "19.5"));
        Assert.Equal("PCIe 4.0 x16", V("BARRAMENTO", "Interface")); Assert.Equal("PCIe 4.0 x16", V("BARRAMENTO", "Em uso agora")); // nvidia-smi overrides the sysfs link state
        Assert.Equal("350 W", V("ENERGIA", "Limite atual")); Assert.Equal("100 – 390 W", V("ENERGIA", "Faixa ajustável"));
        Assert.Equal("94.02.4B.00.A4", V("DRIVER E BIOS", "BIOS (VBIOS)")); Assert.Equal("550.54.14", V("DRIVER E BIOS", "Versão do driver")); Assert.Equal("8.6", V("DRIVER E BIOS", "Compute capability"));
        Assert.Contains("proprietário", V("DRIVER E BIOS", "Tipo do módulo")); Assert.Contains("CUDA", V("TECNOLOGIAS", "APIs (bibliotecas instaladas)")); Assert.Contains("Vulkan", V("TECNOLOGIAS", "APIs (bibliotecas instaladas)"));
        Assert.Contains("DLSS 2", V("TECNOLOGIAS", "Recursos da arquitetura"));
        Assert.Contains("24 GB GDDR6X", card.Subtitle); Assert.Contains("Ampere", card.Subtitle); Assert.Contains("ASUS", card.Subtitle); // subtitle follows the original: chip · arch · process · memory · board maker
    }

    [Fact]
    public void AmdCardReadsSysfsVramPowerCapAndOrdersDedicatedBeforeIntegrated()
    {
        string sys = Path.Combine(root, "sys2");
        string amd = Gpu(sys, "0000:03:00.0", "0x1002", "0x744c", "amdgpu", new() { ["mem_info_vram_total"] = (24L << 30).ToString(), ["mem_info_gtt_total"] = (32L << 30).ToString(), ["vbios_version"] = "113-FIXTURE-001" });
        Directory.CreateDirectory(Path.Combine(amd, "hwmon/hwmon5")); File.WriteAllText(Path.Combine(amd, "hwmon/hwmon5/power1_cap"), "355000000\n"); File.WriteAllText(Path.Combine(amd, "hwmon/hwmon5/power1_cap_max"), "400000000\n");
        Gpu(sys, "0000:00:02.0", "0x8086", "0x9a49", "i915");
        var cards = new LinuxGpuCollector(sys, Path.Combine(root, "p"), []).Collect(Ids);
        Assert.Equal(["0000:03:00.0", "0000:00:02.0"], cards.Select(c => c.Raw.PciAddress).ToArray());
        var (raw, card) = cards[0];
        string V(string s, string k) => card.Sections.Single(x => x.Header == s).Children.First(c => c.Header == k).Value;
        Assert.Contains("PCIe 1.1 x16", V("BARRAMENTO", "Em uso agora")); Assert.Contains("economia", V("BARRAMENTO", "Em uso agora")); // sysfs link: gen1 now, gen4 max
        Assert.Equal("Navi 31", V("IDENTIFICAÇÃO", "Chip")); Assert.Contains("24 GB", V("MEMÓRIA", "Tamanho")); Assert.Equal("113-FIXTURE-001", V("DRIVER E BIOS", "BIOS (VBIOS)")); Assert.Equal("355 W", V("ENERGIA", "Limite atual"));
        Assert.Contains("GTT", card.Sections.Single(s => s.Header == "MEMÓRIA").Children.Select(c => c.Header).First(h => h.Contains("GTT")));
        var intel = cards[1].Card; Assert.Equal("Intel", intel.Vendor);
        Assert.Contains("iGPU", intel.Sections.Single(s => s.Header == "MEMÓRIA").Children.First().Value);
        Assert.Equal("Sem dados", intel.Sections.Single(s => s.Header == "UNIDADES DE PROCESSAMENTO").Children[0].Header);
    }

    [Fact]
    public async Task AmdTelemetryReadsBusyPercentVramHwmonAndNeverInventsMissingValues()
    {
        string sys = Path.Combine(root, "sys3");
        string d = Gpu(sys, "0000:03:00.0", "0x1002", "0x744c", "amdgpu", new() { ["gpu_busy_percent"] = "73", ["mem_info_vram_used"] = (4L << 30).ToString(), ["mem_info_vram_total"] = (24L << 30).ToString() });
        string hw = Path.Combine(d, "hwmon/hwmon5"); Directory.CreateDirectory(hw);
        File.WriteAllText(Path.Combine(hw, "temp1_input"), "61000\n"); File.WriteAllText(Path.Combine(hw, "temp2_input"), "75000\n"); File.WriteAllText(Path.Combine(hw, "power1_average"), "180000000\n");
        File.WriteAllText(Path.Combine(hw, "fan1_input"), "1450\n"); File.WriteAllText(Path.Combine(hw, "freq1_input"), "2400000000\n");
        var collector = new LinuxGpuCollector(sys, Path.Combine(root, "p"), []);
        var raw = collector.Collect(Ids).Single().Raw;
        var t = await collector.TelemetryAsync(raw);
        Assert.Equal(73, t.LoadPercent); Assert.Equal(61, t.TempC); Assert.Equal(75, t.TempHotspotC); Assert.Equal(180, t.PowerW); Assert.Equal(1450, t.FanRpm); Assert.Equal(2400, t.CoreMHz);
        Assert.Equal(4UL << 30, t.VramUsed); Assert.Null(t.TempMemC); Assert.Null(t.MemMHz); Assert.Null(t.FanPercent);
        var rows = GpuLive.Rows(t); Assert.Contains(rows, r => r.Name == "Núcleo" && r.Value == "2400 MHz"); Assert.Contains(rows, r => r.Name == "Ocupação" && r.Value == "17 %");
        Assert.DoesNotContain(rows, r => r.Name == "Memória" && r.Category.StartsWith("Temperatura"));
    }

    // Values copied from the real sysfs of an ASUS Vivobook Go E1504FA (Radeon 610M, Mendocino, amdgpu, kernel 6.14) on 2026-11-06.
    private static readonly byte[] MendocinoGpuMetricsHeader = [120, 0, 2, 1];
    private const string MendocinoMclk = "0: 400Mhz \n1: 687Mhz \n2: 800Mhz *\n3: 687Mhz \n";

    private string Mendocino(string sys, string device = "0x1506")
    {
        string d = Gpu(sys, "0000:03:00.0", "0x1002", device, "amdgpu", new()
        {
            ["gpu_busy_percent"] = "0", ["mem_info_vram_total"] = "536870912", ["mem_info_vram_used"] = "218750976", ["mem_info_gtt_total"] = "3781308416",
            ["pp_dpm_mclk"] = MendocinoMclk.TrimEnd('\n'),
        });
        File.WriteAllBytes(Path.Combine(d, "gpu_metrics"), MendocinoGpuMetricsHeader.Concat(new byte[116]).ToArray());
        string hw = Path.Combine(d, "hwmon/hwmon6"); Directory.CreateDirectory(hw);
        foreach (var (f, v) in new[] { ("temp1_input", "41000"), ("temp1_label", "edge"), ("power1_input", "3167000"), ("power1_label", "PPT"), ("freq1_input", "1900000000"), ("freq1_label", "sclk"), ("in0_input", "810"), ("in0_label", "vddgfx") })
            File.WriteAllText(Path.Combine(hw, f), v + "\n");
        return d;
    }

    [Fact]
    public async Task MendocinoApuIsIntegratedWithPackagePowerMemoryClockAndReservedVram()
    {
        string sys = Path.Combine(root, "sys-apu"); Mendocino(sys);
        var ids = PciIds.Parse("1002  Advanced Micro Devices, Inc. [AMD/ATI]\n\t1506  Mendocino\n");
        var collector = new LinuxGpuCollector(sys, Path.Combine(root, "p"), []);
        var (raw, card) = collector.Collect(ids).Single();
        Assert.True(raw.Integrated);
        string V(string s, string k) => card.Sections.Single(x => x.Header == s).Children.First(c => c.Header == k).Value;
        Assert.Equal("Mendocino (iGPU · Radeon 610M)", V("IDENTIFICAÇÃO", "Chip")); Assert.Equal("RDNA 2", V("IDENTIFICAÇÃO", "Arquitetura"));
        Assert.Equal("—", V("IDENTIFICAÇÃO", "Transistores")); // not published: never "0 bilhões"
        Assert.Equal("512 MB", V("MEMÓRIA", "Reservada (dedicada)")); Assert.Contains("interna", V("BARRAMENTO", "Interface"));
        Assert.Equal("2", V("UNIDADES DE PROCESSAMENTO", "Compute units"));

        var t = await collector.TelemetryAsync(raw);
        Assert.True(t.Integrated); Assert.True(t.PowerIsPackage);
        Assert.Equal(3.167, t.PowerW); Assert.Equal(800, t.MemMHz); Assert.Equal(1900, t.CoreMHz); Assert.Equal(41, t.TempC); Assert.Equal(0, t.LoadPercent);
        Assert.Equal(0.81, t.CoreVoltageV);
        Assert.Null(t.TempHotspotC); Assert.Null(t.FanRpm); Assert.Null(t.FanPercent); // an APU has no GPU fan or junction sensor: unavailable, not zero
        var rows = GpuLive.Rows(t);
        Assert.Contains(rows, r => r.Name == "Pacote (CPU+GPU)" && r.Value == "3.2 W"); Assert.DoesNotContain(rows, r => r.Name == "Consumo");
        Assert.Contains(rows, r => r.Category == "VRAM reservada · " && r.Name == "Total" && r.Value == "512 MB");
        Assert.Contains(rows, r => r.Category == "Clock · " && r.Name == "Memória" && r.Value == "800 MHz");
    }

    [Fact]
    public void UnknownAmdDeviceIsIntegratedOnlyWhenGpuMetricsHasTheApuFormat()
    {
        var ids = PciIds.Parse("1002  Advanced Micro Devices, Inc. [AMD/ATI]\n");
        string apu = Path.Combine(root, "sys-apu2"); Mendocino(apu, "0x15bf"); // not in the reference table: decided by the gpu_metrics header
        Assert.True(new LinuxGpuCollector(apu, Path.Combine(root, "p"), []).Collect(ids).Single().Raw.Integrated);
        string dgpu = Path.Combine(root, "sys-dgpu"); string d = Mendocino(dgpu, "0x73ff");
        File.WriteAllBytes(Path.Combine(d, "gpu_metrics"), [100, 0, 1, 3]);
        Assert.False(new LinuxGpuCollector(dgpu, Path.Combine(root, "p"), []).Collect(ids).Single().Raw.Integrated);
        string none = Path.Combine(root, "sys-none"); File.Delete(Path.Combine(Mendocino(none, "0x73ef"), "gpu_metrics"));
        Assert.False(new LinuxGpuCollector(none, Path.Combine(root, "p"), []).Collect(ids).Single().Raw.Integrated); // unknown stays dedicated (previous behaviour)
    }

    [Theory]
    [InlineData(new byte[] { 120, 0, 2, 1 }, true)]
    [InlineData(new byte[] { 0, 1, 3, 0 }, true)]
    [InlineData(new byte[] { 100, 0, 1, 3 }, false)]
    [InlineData(new byte[] { 100, 0, 9, 0 }, null)]
    [InlineData(new byte[] { 120, 0 }, null)]
    public void GpuMetricsHeaderFormat(byte[] header, bool? apu) => Assert.Equal(apu, LinuxGpuCollector.GpuMetricsIsApu(header));

    [Theory]
    [InlineData(MendocinoMclk, 800.0)]
    [InlineData("0: 200Mhz \n1: 1900Mhz *\n2: 1899Mhz \n", 1900.0)]
    [InlineData("0: 96Mhz\n1: 456Mhz\n2: 1000Mhz\n", null)]
    [InlineData("0: 2.5GT/s, x8 *\n", null)]
    [InlineData("", null)]
    public void ActiveDpmLevel(string text, double? mhz) => Assert.Equal(mhz, LinuxGpuCollector.ActiveDpmMHz(text));

    [Fact]
    public void LiveExtrasUseThePerfCapRulesOfTheOriginal()
    {
        Assert.Equal("Energia", GpuCardBuilder.PerfCap(0x4)); Assert.Equal("Térmico", GpuCardBuilder.PerfCap(0x20)); Assert.Equal("Energia + Térmico + HW slowdown", GpuCardBuilder.PerfCap(0x2C));
        Assert.Equal("Ociosa", GpuCardBuilder.PerfCap(0x1)); Assert.Equal("Nenhum", GpuCardBuilder.PerfCap(0));
        var extras = GpuLive.Extras(new(50, 60, null, null, 1500, 9000, 1UL << 30, 8UL << 30, 175, 350, 40, null, "P2", 0x4, 12, 3, 1, 4, 16)).ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("P2", extras["Estado de desempenho"]); Assert.Equal("Energia", extras["PerfCap (limitador)"]); Assert.Equal("175.0 W · 50 %", extras["Consumo / limite"]); Assert.Equal("PCIe 4.0 x16", extras["PCIe em uso"]);
        Assert.Empty(GpuLive.Extras(new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)));
    }

    [Theory]
    [InlineData("2.5 GT/s PCIe", 1)]
    [InlineData("8.0 GT/s PCIe", 3)]
    [InlineData("16.0 GT/s PCIe", 4)]
    [InlineData("32.0 GT/s PCIe", 5)]
    [InlineData("Unknown", null)]
    [InlineData("", null)]
    public void LinkSpeedToGeneration(string text, int? gen) => Assert.Equal(gen, GpuCardBuilder.GenFromLinkSpeed(text));

    [Fact]
    public void SpecDbFindsVariantsByNameAndRejectsUnknownIds()
    {
        Assert.Equal("RDNA 4", GpuSpecDb.Find("1002", "7550", "AMD Radeon RX 9070 XT")!.Arch);
        Assert.Equal(3584, GpuSpecDb.Find("1002", "7550", "AMD Radeon RX 9070")!.Shaders);
        Assert.Null(GpuSpecDb.Find("8086", "9a49", "Iris Xe")); Assert.Equal("ASUS", GpuSpecDb.SubvendorName("1043")); Assert.Contains("Desconhecido", GpuSpecDb.SubvendorName("ffff"));
    }
}

public sealed class GameCatalogHardwareTests
{
    [Theory]
    [InlineData("GM108M [GeForce 940MX]", 4.5)]        // nouveau / pci.ids name on a Dell laptop
    [InlineData("NVIDIA GeForce 940MX", 4.5)]          // nvidia-smi name
    [InlineData("HD Graphics 620", 2.5)]
    [InlineData("Mendocino", 3)]                      // pci.ids name of the Radeon 610M
    [InlineData("AMD Radeon 610M (RADV RAPHAEL_MENDOCINO)", 3)]
    [InlineData("NVIDIA GeForce RTX 3060 Laptop GPU", 50)]
    [InlineData("NVIDIA GeForce RTX 3060", 60)]
    public void CommonLaptopAndIntegratedGpusAreInTheTable(string name, double index) => Assert.Equal(index, Ilmenyx.Core.GameCatalog.GpuPerf(name));

    [Theory]
    [InlineData("Intel(R) Core(TM) i7-7500U CPU @ 2.70GHz", 36)]
    [InlineData("AMD Ryzen 5 7520U with Radeon Graphics", 34)]
    [InlineData("AMD Ryzen 5 5600H with Radeon Graphics", 62)]
    [InlineData("AMD Ryzen 7 7700X 8-Core Processor", 100)]
    public void LaptopCpusAreInTheTable(string name, double index) =>
        Assert.Equal(index, Ilmenyx.Core.GameCatalog.CpuPerf(Ilmenyx.Core.GameCatalog.ShortCpuName(name)) ?? Ilmenyx.Core.GameCatalog.CpuPerf(name));

    [Fact]
    public void UnknownHardwareHasNoIndex() => Assert.Null(Ilmenyx.Core.GameCatalog.GpuPerf("Matrox G200eR2"));
}
