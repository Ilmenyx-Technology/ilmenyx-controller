// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Página Drivers em linguagem simples: categorias, situação e ação recomendada (fixtures sintéticos).
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class DriverOverviewTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-drvo-" + Guid.NewGuid().ToString("N"));
    public DriverOverviewTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static DriverRow Pci(string name, string cls, string code, string id, string driver, string flags = "módulo do kernel", bool wireless = false) =>
        new(name, cls, "Vendor", "kernel 6.14", driver, flags, "PCI", id, code, wireless);
    private static DriverRow Usb(string name, string driver, string classes, string id = "1234:5678") => new(name, "USB", "Vendor", "kernel 6.14", driver, driver == "sem driver" ? "sem driver carregado" : "módulo do kernel", "USB", id, classes);

    [Theory]
    [InlineData("HD Graphics 620", "DISPLAY", "0x030000", "i915", DriverCategory.Video)]
    [InlineData("Wireless 8265 / 8275", "NET", "0x028000", "iwlwifi", DriverCategory.Network)]
    [InlineData("RTL8111/8168 Gigabit Ethernet", "NET", "0x020000", "r8169", DriverCategory.Network)]
    [InlineData("Sunrise Point-LP HD Audio", "MEDIA", "0x040300", "snd_hda_intel", DriverCategory.Audio)]
    [InlineData("Capture card", "MEDIA", "0x048000", "saa7134", DriverCategory.Other)]
    [InlineData("NVMe SSD", "STORAGE", "0x010802", "nvme", DriverCategory.Other)]
    [InlineData("Bluetooth PCI", "NET", "0x0d1100", "btintel", DriverCategory.Bluetooth)]
    public void PciDevicesFallInTheRightCategory(string name, string cls, string code, string driver, DriverCategory expected) =>
        Assert.Equal(expected, DriverOverview.CategoryOf(Pci(name, cls, code, "8086:0001", driver)));

    [Theory]
    [InlineData("Bluetooth Radio", "btusb", "e0", DriverCategory.Bluetooth)]
    [InlineData("USB2.0 Camera", "uvcvideo", "0e", DriverCategory.Other)]
    [InlineData("HyperX Cloud", "snd-usb-audio, usbhid", "01,03", DriverCategory.Audio)]
    [InlineData("HP LaserJet 1020", "usblp", "07", DriverCategory.Printing)]
    [InlineData("CanoScan LiDE 300", "sem driver", "ff", DriverCategory.Printing)]
    [InlineData("802.11n NIC", "rtl8xxxu", "ff", DriverCategory.Network)]
    [InlineData("USB 10/100/1000 LAN", "r8152", "ff,02", DriverCategory.Network)]
    [InlineData("USB Receiver", "usbhid", "03", DriverCategory.Other)]
    public void UsbDevicesFallInTheRightCategory(string name, string driver, string classes, DriverCategory expected) =>
        Assert.Equal(expected, DriverOverview.CategoryOf(Usb(name, driver, classes)));

    [Fact]
    public void StatusLicenseAndOneRecommendedAction()
    {
        var nouveauRow = Pci("GM108M [GeForce 940MX]", "DISPLAY", "0x030200", "10de:134d", "nouveau");
        var gpu = new GpuDriverStatus { Vendor = "NVIDIA", Driver = "nouveau", DeviceId = "134d", PciId = "10de:134d" };
        var nouveau = DriverOverview.Describe(nouveauRow, gpu);
        Assert.Equal(DriverHealth.Attention, nouveau.Health); Assert.Equal(DriverAction.OpenNvidiaTab, nouveau.Action); Assert.Equal("DRIVER BÁSICO", nouveau.Badge);
        Assert.Equal("Livre (vem com o Linux)", nouveau.License); Assert.Contains("nouveau", nouveau.Plain);

        var nvRow = Pci("GA102", "DISPLAY", "0x030000", "10de:2204", "nvidia", "proprietário, fora da árvore do kernel");
        var nv = DriverOverview.Describe(nvRow, new GpuDriverStatus { Vendor = "NVIDIA", Driver = "nvidia", Installed = "580.95.05", DeviceId = "2204", PciId = "10de:2204" });
        Assert.Equal(DriverHealth.Ok, nv.Health); Assert.Equal("Proprietário (do fabricante)", nv.License); Assert.Contains("580.95.05", nv.Plain); Assert.Equal(DriverAction.OpenNvidiaTab, nv.Action);
        var update = DriverOverview.Describe(nvRow, new GpuDriverStatus { Vendor = "NVIDIA", Driver = "nvidia", Installed = "580.95.05", Latest = "580.105.08", DeviceId = "2204", PciId = "10de:2204" });
        Assert.Equal(DriverHealth.Attention, update.Health); Assert.Equal("ATUALIZAÇÃO", update.Badge);

        var intel = DriverOverview.Describe(Pci("HD Graphics 620", "DISPLAY", "0x030000", "8086:5916", "i915"));
        Assert.Equal(DriverHealth.Ok, intel.Health); Assert.Equal(DriverAction.None, intel.Action); Assert.Contains("não precisa instalar nada", intel.Plain);

        var wifiNoDriver = DriverOverview.Describe(Pci("BCM4360", "NET", "0x028000", "14e4:43a0", "sem driver", "sem driver carregado"));
        Assert.Equal(DriverHealth.Attention, wifiNoDriver.Health); Assert.Equal(DriverAction.OpenDriverManager, wifiNoDriver.Action); Assert.Equal("Wi-Fi", wifiNoDriver.Kind); Assert.Equal("—", wifiNoDriver.License);
        Assert.Contains("firmware (programa interno da peça)", wifiNoDriver.Plain);
        var broadcom = DriverOverview.Describe(Pci("BCM4360", "NET", "0x028000", "14e4:43a0", "wl", "módulo do kernel", wireless: true));
        Assert.Equal("Proprietário (do fabricante)", broadcom.License); Assert.Equal(DriverHealth.Ok, broadcom.Health);

        var idle = DriverOverview.Describe(Pci("Signal processing controller", "SYSTEM", "0x118000", "8086:9d31", "sem driver", "sem driver carregado"));
        Assert.Equal(DriverHealth.Info, idle.Health); Assert.Equal(DriverAction.None, idle.Action); Assert.Equal("Peça interna da placa-mãe", idle.Kind);
        var scanner = DriverOverview.Describe(Usb("CanoScan LiDE 300", "sem driver", "ff"));
        Assert.Equal(DriverHealth.Info, scanner.Health); Assert.Contains("CUPS", scanner.Plain);
    }

    [Fact]
    public void CardsAreAlwaysSixInOrderAndSummaryIsPlain()
    {
        var rows = new List<DriverRow>
        {
            Pci("HD Graphics 620", "DISPLAY", "0x030000", "8086:5916", "i915"),
            Pci("GM108M [GeForce 940MX]", "DISPLAY", "0x030200", "10de:134d", "nouveau"),
            Pci("Wireless 8265", "NET", "0x028000", "8086:24fd", "iwlwifi", wireless: true),
            Pci("Signal processing controller", "SYSTEM", "0x118000", "8086:9d31", "sem driver", "sem driver carregado"),
            Usb("Bluetooth", "btusb", "e0"),
        };
        var gpus = new List<GpuDriverStatus> { new() { Vendor = "NVIDIA", Driver = "nouveau", DeviceId = "134d", PciId = "10de:134d" } };
        var cards = DriverOverview.Build(rows, gpus);
        Assert.Equal([DriverCategory.Video, DriverCategory.Network, DriverCategory.Bluetooth, DriverCategory.Audio, DriverCategory.Printing, DriverCategory.Other], cards.Select(c => c.Category).ToArray());
        Assert.Equal(DriverHealth.Attention, cards[0].Health); Assert.Equal("GM108M [GeForce 940MX]", cards[0].Devices[0].Row.Device); // attention first
        Assert.Same(gpus[0], cards[0].Devices[0].Gpu); Assert.Null(cards[0].Devices[1].Gpu);
        Assert.Empty(cards[3].Devices); Assert.False(string.IsNullOrWhiteSpace(cards[3].Empty));
        var s = DriverOverview.Summarize(cards);
        Assert.Equal(1, s.Attention); Assert.Equal(1, s.Unused); Assert.Equal("1 ponto precisa de atenção.", s.Headline);
        Assert.Contains("Vídeo / placa de vídeo: GM108M [GeForce 940MX] — usando o driver básico (nouveau).", s.Lines);
        Assert.Contains("Rede e Wi-Fi: ok.", s.Lines); Assert.Contains("Bluetooth: ok.", s.Lines);
        var fine = DriverOverview.Summarize(DriverOverview.Build(rows.Where(r => r.Driver != "nouveau").ToList(), []));
        Assert.Equal(0, fine.Attention); Assert.StartsWith("Tudo certo", fine.Headline);
        Assert.Equal("Nenhum dispositivo encontrado no sysfs.", DriverOverview.Summarize(DriverOverview.Build([], [])).Headline);
    }

    [Fact]
    public void InventoryFillsBusIdClassAndWirelessFromSysfs()
    {
        string sys = Path.Combine(root, "sys"), proc = Path.Combine(root, "proc");
        Directory.CreateDirectory(Path.Combine(proc, "sys/kernel")); File.WriteAllText(Path.Combine(proc, "sys/kernel/osrelease"), "6.14.0\n");
        string dev = Path.Combine(sys, "bus/pci/devices/0000:02:00.0"); Directory.CreateDirectory(Path.Combine(dev, "net/wlp2s0/wireless"));
        File.WriteAllText(Path.Combine(dev, "class"), "0x028000\n"); File.WriteAllText(Path.Combine(dev, "vendor"), "0x8086\n"); File.WriteAllText(Path.Combine(dev, "device"), "0x24FD\n");
        string usb = Path.Combine(sys, "bus/usb/devices/1-7"); Directory.CreateDirectory(Path.Combine(usb, "1-7:1.0"));
        File.WriteAllText(Path.Combine(usb, "product"), "Bluetooth\n"); File.WriteAllText(Path.Combine(usb, "idVendor"), "8087\n"); File.WriteAllText(Path.Combine(usb, "idProduct"), "0a2b\n");
        File.WriteAllText(Path.Combine(usb, "1-7:1.0/bInterfaceClass"), "e0\n");
        var rows = new LinuxDriverInventory(sys, proc, Path.Combine(root, "etc")).Inventory(PciIds.Parse(""));
        var wifi = rows.Single(r => r.Bus == "PCI");
        Assert.Equal("8086:24fd", wifi.Id); Assert.Equal("0x028000", wifi.ClassCode); Assert.True(wifi.Wireless); Assert.Equal(DriverCategory.Network, DriverOverview.CategoryOf(wifi));
        var bt = rows.Single(r => r.Bus == "USB");
        Assert.Equal("8087:0a2b", bt.Id); Assert.Equal("e0", bt.ClassCode); Assert.Equal(DriverCategory.Bluetooth, DriverOverview.CategoryOf(bt));
    }
}
