// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// Fixtures são sintéticos; nenhum serial, hostname ou dado real.
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class SystemInfoTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-sysinfo-" + Guid.NewGuid().ToString("N"));
    public SystemInfoTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private const string Dmi = """
        # dmidecode 3.5
        Handle 0x0040, DMI type 16, 23 bytes
        Physical Memory Array
        	Location: System Board Or Motherboard
        	Maximum Capacity: 64 GB
        	Number Of Devices: 4

        Handle 0x0041, DMI type 17, 40 bytes
        Memory Device
        	Total Width: 72 bits
        	Data Width: 64 bits
        	Size: 16 GB
        	Form Factor: DIMM
        	Locator: DIMM_A2
        	Bank Locator: BANK 0
        	Type: DDR5
        	Speed: 4800 MT/s
        	Manufacturer: FIXTURE-MAKER
        	Serial Number: FIXTURE-SERIAL-NOT-REAL
        	Part Number: F5-6000J3038F16GX2
        	Rank: 1
        	Configured Memory Speed: 4800 MT/s
        	Configured Voltage: 1.1 V
        	Minimum Voltage: 1.1 V
        	Maximum Voltage: 1.35 V

        Handle 0x0042, DMI type 17, 40 bytes
        Memory Device
        	Size: No Module Installed
        	Locator: DIMM_A1

        Handle 0x0043, DMI type 17, 40 bytes
        Memory Device
        	Total Width: 72 bits
        	Data Width: 64 bits
        	Size: 16384 MB
        	Locator: DIMM_B2
        	Bank Locator: BANK 1
        	Type: DDR5
        	Speed: 4800 MT/s
        	Part Number: F5-6000J3038F16GX2
        	Configured Memory Speed: 4800 MT/s
        """;

    [Fact]
    public void DmiDecodeParsesModulesAndSkipsEmptySlotsAndSerials()
    {
        var inv = DmiDecodeParser.Parse(Dmi);
        Assert.Equal(4, inv.Array.Slots); Assert.Equal(64UL << 30, inv.Array.MaxBytes);
        Assert.Equal(2, inv.Modules.Count);
        var m = inv.Modules[0];
        Assert.Equal(16UL << 30, m.Capacity); Assert.Equal("DDR5", m.Type); Assert.Equal(4800, m.ConfiguredSpeed); Assert.Equal(1.1, m.ConfiguredVolt);
        Assert.Equal(16UL << 30, inv.Modules[1].Capacity); // "16384 MB"
        Assert.DoesNotContain("FIXTURE-SERIAL", System.Text.Json.JsonSerializer.Serialize(inv));
    }

    [Fact]
    public void DmiDecodeRejectsOversizedInputAndIgnoresGarbage()
    {
        Assert.Throws<IOException>(() => DmiDecodeParser.Parse(new string('x', DmiDecodeParser.MaxCharacters + 1)));
        var empty = DmiDecodeParser.Parse("garbage\n\nnot a block");
        Assert.Empty(empty.Modules);
    }

    [Fact]
    public void RamSummaryEstimatesProfileChannelsAndEcc()
    {
        var n = RamInfoBuilder.Build(32L << 30, 8L << 30, 8L << 30, 1L << 30, DmiDecodeParser.Parse(Dmi));
        string Find(string key) => Flatten(n).First(x => x.Header == key).Value;
        Assert.Contains("DESLIGADO", Find("Perfil EXPO/XMP (estimado)"));   // 4800 vs 6000 in part number
        Assert.Contains("2 canais", Find("Canais (estimado)"));            // A2 + B2
        Assert.Equal("Sim (correção de erros)", Find("ECC"));              // 72 > 64
        Assert.Equal("2 de 4 slots", Find("Módulos instalados"));
        Assert.Contains(n.Children, c => c.Header.StartsWith("Módulo — BANK 0 / DIMM_A2"));
        Assert.DoesNotContain("FIXTURE-SERIAL", string.Join("|", Flatten(n).Select(x => x.Value)));
    }

    [Fact]
    public void RamWithoutInventoryExplainsHowToImportWithoutExecutingAnything()
    {
        var n = RamInfoBuilder.Build(8L << 30, 2L << 30, null, null, null);
        Assert.Contains(Flatten(n), x => x.Value.Contains("dmidecode") && x.Value.Contains("não executa"));
        Assert.Contains("25%", Flatten(n).First(x => x.Header.StartsWith("Em uso")).Value);
    }

    private static IEnumerable<InfoNode> Flatten(InfoNode n) => n.Children.SelectMany(c => new[] { c }.Concat(Flatten(c)));

    [Fact]
    public void PciIdsResolvesVendorAndDeviceAndStopsAtClassSection()
    {
        var ids = PciIds.Parse("# comment\n8086  Intel Corporation\n\t9a49  TigerLake-LP GT2 [Iris Xe Graphics]\n\t\t8086 1234  Subsystem\n10de  NVIDIA Corporation\n\t2204  GA102 [GeForce RTX 3090]\nC 03  Display controller\n\t00  VGA\n");
        Assert.Equal("Intel Corporation", ids.Vendor("0x8086"));
        Assert.Equal("TigerLake-LP GT2 [Iris Xe Graphics]", ids.Device("8086", "0x9A49"));
        Assert.Equal("GA102 [GeForce RTX 3090]", ids.Device("10DE", "2204"));
        Assert.Null(ids.Device("8086", "ffff")); Assert.Null(ids.Vendor("zzzz"));
        Assert.True(PciIds.Load(["/nonexistent/pci.ids"]).IsEmpty);
    }

    [Fact]
    public void MountedDrivesKeepRealFilesystemsDedupeBySourceAndComputeUsage()
    {
        string mounts = "proc /proc proc rw 0 0\n/dev/nvme0n1p2 / btrfs rw 0 0\n/dev/nvme0n1p2 /home btrfs rw 0 0\n/dev/sda1 /mnt/HDD ext4 rw 0 0\n/dev/sdb1 /run/media/u/USB\\040STICK vfat rw 0 0\ntmpfs /tmp tmpfs rw 0 0\nserver:/x /net nfs rw 0 0\n/dev/loop0 /snap/core squashfs ro 0 0\n";
        var cards = MountedDrives.Read(mounts, m => m == "/" ? (1000L << 30, 250L << 30) : m == "/mnt/HDD" ? (500L << 30, 100L << 30) : null);
        Assert.Equal(2, cards.Count); // USB has no stat → skipped; nfs/tmpfs/squashfs ignored
        Assert.Equal("/", cards[0].Title); Assert.Equal("75%", cards[0].UsedPercent); Assert.Contains("+1 ponto", cards[0].Meta);
        Assert.Contains("250 GiB livres de 1000 GiB", cards[0].Sizes);
        Assert.Equal("/mnt/HDD", cards[1].Title);
        Assert.Equal("a b", MountedDrives.Unescape("a\\040b"));
    }

    [Fact]
    public void CollectorReadsFixtureTreeAndHidesIdentifiers()
    {
        string proc = Path.Combine(root, "proc"), sys = Path.Combine(root, "sys"), etc = Path.Combine(root, "etc");
        Directory.CreateDirectory(Path.Combine(proc, "sys/kernel")); Directory.CreateDirectory(etc);
        File.WriteAllText(Path.Combine(etc, "os-release"), "PRETTY_NAME=\"Fixture Linux\"\nVERSION_ID=\"1\"\n");
        File.WriteAllText(Path.Combine(proc, "sys/kernel/osrelease"), "9.9.9-fixture\n");
        File.WriteAllText(Path.Combine(proc, "meminfo"), "MemTotal: 8000000 kB\nMemAvailable: 6000000 kB\nSwapTotal: 1000 kB\nSwapFree: 500 kB\n");
        File.WriteAllText(Path.Combine(proc, "cpuinfo"), "processor\t: 0\nvendor_id\t: GenuineIntel\nmodel name\t: Fixture CPU\nphysical id\t: 0\ncore id\t: 0\nflags\t\t: fpu vmx sse4_2 avx avx2\n\nprocessor\t: 1\nvendor_id\t: GenuineIntel\nmodel name\t: Fixture CPU\nphysical id\t: 0\ncore id\t: 0\nflags\t\t: fpu vmx sse4_2 avx avx2\n");
        string dmi = Path.Combine(sys, "class/dmi/id"); Directory.CreateDirectory(dmi);
        File.WriteAllText(Path.Combine(dmi, "board_vendor"), "FixtureBoards\n"); File.WriteAllText(Path.Combine(dmi, "board_name"), "FB-1\n");
        File.WriteAllText(Path.Combine(dmi, "board_serial"), "FIXTURE-SERIAL-NOT-REAL\n"); File.WriteAllText(Path.Combine(dmi, "bios_date"), "03/15/2024\n");
        string disk = Path.Combine(sys, "block/nvme0n1"); Directory.CreateDirectory(Path.Combine(disk, "device")); Directory.CreateDirectory(Path.Combine(disk, "queue"));
        File.WriteAllText(Path.Combine(disk, "size"), "2000\n"); File.WriteAllText(Path.Combine(disk, "device/model"), "Fixture NVMe\n");
        File.WriteAllText(Path.Combine(disk, "device/serial"), "FIXTURE-SERIAL-NOT-REAL\n"); File.WriteAllText(Path.Combine(disk, "queue/rotational"), "0\n");
        Directory.CreateDirectory(Path.Combine(sys, "block/loop0")); // must be ignored

        var nodes = new LinuxSystemInfoCollector(proc, sys, etc).Collect(null, null, null);
        Assert.Equal(6, nodes.Count);
        var all = nodes.SelectMany(n => new[] { n }.Concat(Flatten(n))).ToList();
        string Value(string header) => all.First(x => x.Header == header).Value;
        Assert.Equal("Fixture Linux", Value("Nome")); Assert.Equal("9.9.9-fixture", Value("Kernel"));
        Assert.Equal("FixtureBoards", Value("Fabricante")); Assert.Equal("15/03/2024", Value("Data"));
        Assert.Equal("1", Value("Núcleos físicos")); Assert.Equal("2", Value("Threads (lógicos)")); Assert.Contains("Intel VT-x", Value("Virtualização"));
        Assert.Contains("avx2", Value("Extensões relevantes"));
        Assert.Contains(all, x => x.Header == "Fixture NVMe"); Assert.DoesNotContain(all, x => x.Header.Contains("loop"));
        Assert.DoesNotContain("FIXTURE-SERIAL", string.Join("|", all.Select(x => x.Header + x.Value)));
        Assert.Contains(all, x => x.Header == "Nenhuma GPU DRM/PCI identificada");
    }

    [Fact]
    public void CollectorSurvivesAnEmptyRoot()
    {
        var nodes = new LinuxSystemInfoCollector(Path.Combine(root, "p"), Path.Combine(root, "s"), Path.Combine(root, "e")).Collect(null, null, null);
        Assert.Equal(6, nodes.Count);
        Assert.Contains(nodes.SelectMany(n => n.Children), c => c.Header == "Erro ao ler");
    }
}
