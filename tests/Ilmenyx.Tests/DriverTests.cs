// New Ilmenyx additions: see LICENSING.md. Fixtures sintéticos.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class DriverTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-drv-" + Guid.NewGuid().ToString("N"));
    public DriverTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static readonly PciIds Ids = PciIds.Parse("8086  Intel Corporation\n\t9a49  Iris Xe Graphics\n10de  NVIDIA Corporation\n\t2204  GA102 [GeForce RTX 3090]\n1002  Advanced Micro Devices, Inc. [AMD/ATI]\n\t744c  Navi 31 [Radeon RX 7900 XTX]\n");

    private string Pci(string sys, string addr, string cls, string vendor, string device, string? driver)
    {
        string d = Path.Combine(sys, "devices/pci0000:00", addr); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "class"), cls + "\n"); File.WriteAllText(Path.Combine(d, "vendor"), vendor + "\n"); File.WriteAllText(Path.Combine(d, "device"), device + "\n");
        Directory.CreateDirectory(Path.Combine(sys, "bus/pci/devices"));
        Directory.CreateSymbolicLink(Path.Combine(sys, "bus/pci/devices", addr), d);
        if (driver is not null)
        {
            string drv = Path.Combine(sys, "bus/pci/drivers", driver); Directory.CreateDirectory(drv);
            Directory.CreateSymbolicLink(Path.Combine(d, "driver"), drv);
        }
        return d;
    }

    [Fact]
    public void InventoryListsRelevantClassesWithDriverVersionAndFlagsAndSkipsBridges()
    {
        string sys = Path.Combine(root, "sys"), proc = Path.Combine(root, "proc"); Directory.CreateDirectory(Path.Combine(proc, "sys/kernel")); File.WriteAllText(Path.Combine(proc, "sys/kernel/osrelease"), "9.9.9-fx\n");
        Pci(sys, "0000:00:02.0", "0x030000", "0x8086", "0x9a49", "i915");
        Pci(sys, "0000:01:00.0", "0x030200", "0x10de", "0x2204", "nvidia");
        Pci(sys, "0000:00:1c.0", "0x060400", "0x8086", "0x1234", null); // bridge: ignored
        Pci(sys, "0000:03:00.0", "0x020000", "0x8086", "0xffff", null);  // network without driver
        Directory.CreateDirectory(Path.Combine(sys, "module/nvidia")); File.WriteAllText(Path.Combine(sys, "module/nvidia/version"), "550.54.14\n"); File.WriteAllText(Path.Combine(sys, "module/nvidia/taint"), "PO\n");
        Directory.CreateDirectory(Path.Combine(sys, "module/i915")); File.WriteAllText(Path.Combine(sys, "module/i915/taint"), "\n");
        string usb = Path.Combine(sys, "bus/usb/devices/1-2"); Directory.CreateDirectory(usb);
        File.WriteAllText(Path.Combine(usb, "product"), "Fixture Keyboard\n"); File.WriteAllText(Path.Combine(usb, "manufacturer"), "FixtureCo\n");
        string usbDrv = Path.Combine(sys, "bus/usb/drivers/usbhid"); Directory.CreateDirectory(usbDrv);
        string iface = Path.Combine(usb, "1-2:1.0"); Directory.CreateDirectory(iface); Directory.CreateSymbolicLink(Path.Combine(iface, "driver"), usbDrv);
        Directory.CreateDirectory(Path.Combine(sys, "bus/usb/devices/usb1")); File.WriteAllText(Path.Combine(sys, "bus/usb/devices/usb1/product"), "Root Hub\n");

        var rows = new LinuxDriverInventory(sys, proc, Path.Combine(root, "etc")).Inventory(Ids);
        Assert.DoesNotContain(rows, r => r.Device.Contains("1234"));
        var nv = rows.Single(r => r.Device.Contains("RTX 3090"));
        Assert.Equal("DISPLAY", nv.Class); Assert.Equal("nvidia", nv.Driver); Assert.Equal("550.54.14", nv.Version); Assert.Equal("proprietário, fora da árvore do kernel", nv.Flags);
        var intel = rows.Single(r => r.Device == "Iris Xe Graphics");
        Assert.Equal("kernel 9.9.9-fx", intel.Version); Assert.Equal("módulo do kernel", intel.Flags);
        var net = rows.Single(r => r.Class == "NET"); Assert.Equal("sem driver", net.Driver); Assert.Contains("Dispositivo PCI", net.Device);
        var kb = rows.Single(r => r.Class == "USB"); Assert.Equal("Fixture Keyboard", kb.Device); Assert.Equal("usbhid", kb.Driver);
        Assert.DoesNotContain(rows, r => r.Device == "Root Hub");
    }

    [Fact]
    public void GpuStatusReadsNvidiaProprietaryVersionAndFlagsNouveauAndOtherVendors()
    {
        string sys = Path.Combine(root, "sys2"), proc = Path.Combine(root, "proc2"); Directory.CreateDirectory(Path.Combine(proc, "driver/nvidia")); Directory.CreateDirectory(Path.Combine(proc, "sys/kernel"));
        File.WriteAllText(Path.Combine(proc, "driver/nvidia/version"), "NVRM version: NVIDIA UNIX x86_64 Kernel Module  550.54.14  Thu\n"); File.WriteAllText(Path.Combine(proc, "sys/kernel/osrelease"), "9.9\n");
        Pci(sys, "0000:01:00.0", "0x030000", "0x10de", "0x2204", "nvidia"); Pci(sys, "0000:02:00.0", "0x030000", "0x1002", "0x744c", "amdgpu"); Pci(sys, "0000:03:00.0", "0x038000", "0x10de", "0x2204", "nouveau");
        var gpus = new LinuxDriverInventory(sys, proc).Gpus(Ids);
        Assert.Equal(3, gpus.Count);
        var nv = gpus[0]; Assert.Equal("NVIDIA", nv.Vendor); Assert.Equal("550.54.14", nv.Installed); Assert.Equal("nvidia", nv.Driver);
        Assert.Equal("AMD", gpus[1].Vendor); Assert.Contains("amdgpu", gpus[1].Installed); Assert.Contains("distribuição", gpus[1].Message);
        Assert.Equal("", gpus[2].Installed); Assert.Contains("nouveau", gpus[2].Message); // free driver must not inherit the proprietary version
        nv.Latest = "570.133.07"; Assert.True(nv.UpdateAvailable);
        nv.Latest = "550.54.14"; Assert.False(nv.UpdateAvailable);
        nv.Latest = "550.9"; Assert.False(nv.UpdateAvailable);
    }

    [Fact]
    public void DriverManagerUsesOnlyTheDistributionsToolWithFixedArguments()
    {
        string? Find(string name) => "/usr/bin/" + name;
        var mint = DriverTools.FindDriverManager("linuxmint", "ubuntu debian", Find)!;
        Assert.Equal("/usr/bin/mintdrivers", mint.Program); Assert.Empty(mint.Arguments);
        var ubuntu = DriverTools.FindDriverManager("ubuntu", "debian", Find)!;
        Assert.Equal("/usr/bin/software-properties-gtk", ubuntu.Program); Assert.Equal(["--open-tab=4"], ubuntu.Arguments);
        Assert.Null(DriverTools.FindDriverManager("fedora", "", Find));
        Assert.Null(DriverTools.FindDriverManager("linuxmint", "ubuntu", _ => null));
        Assert.Throws<ArgumentException>(() => DriverTools.LaunchDriverManager(new("sh", ["-c", "anything"])));
    }

    [Theory]
    [InlineData("570.133.07", "550.54.14", 1)]
    [InlineData("550.54.14", "550.54.14", 0)]
    [InlineData("550.9", "550.54", -1)]
    [InlineData("550", "550.0.0", 0)]
    [InlineData("abc", "1", -1)]
    public void VersionComparisonIsNumericPerSegment(string a, string b, int expected) => Assert.Equal(expected, Math.Sign(DriverVersions.Compare(a, b)));

    [Fact]
    public void BoardSupportPagesFollowTheOriginalMapping()
    {
        var msi = DriverBoard.Describe("Micro-Star International Co., Ltd.", "MAG B650 TOMAHAWK WIFI", "Micro-Star", "MS-7D75", "1.20", "03/15/2024");
        Assert.Equal("MSI", msi.Brand); Assert.Equal("https://www.msi.com/search/MAG%20B650%20TOMAHAWK%20WIFI", msi.SupportUrl); Assert.Equal("1.20 (03/15/2024)", msi.Bios);
        Assert.Equal("ASUS", DriverBoard.Describe("ASUSTeK COMPUTER INC.", "PRIME", "ASUS", "", "", "").Brand);
        Assert.Equal("HP", DriverBoard.Describe("HP", "8A47", "HP", "HP Laptop", "F.20", "").Brand);
        Assert.Equal("HP", DriverBoard.Describe("", "", "Hewlett-Packard", "", "", "").Brand);
        Assert.Equal("", DriverBoard.Describe("Unknown Co", "X", "Unknown Co", "", "", "").Brand);
        Assert.Equal("Product Y", DriverBoard.Describe("", "", "SysCo", "Product Y", "", "").Model);
    }

    [Fact]
    public void InstallHintsAreDistroSpecificAndNeverExecuted()
    {
        Assert.Contains("pacman -S nvidia-open-dkms", DriverInstallHints.For("cachyos", "arch", "NVIDIA"));
        Assert.Contains("ubuntu-drivers install", DriverInstallHints.For("ubuntu", "debian", "NVIDIA"));
        Assert.Contains("akmod-nvidia", DriverInstallHints.For("fedora", "", "NVIDIA"));
        Assert.Contains("vulkan-intel", DriverInstallHints.For("arch", "", "Intel"));
        Assert.Contains("não instala drivers", DriverInstallHints.For("gentoo", "", "NVIDIA"));
        Assert.Contains("não é executada", DriverInstallHints.For("arch", "", "AMD"));
    }

    [Fact]
    public void ParsersRejectMalformedExternalText()
    {
        Assert.Equal("570.133.07", DriverTools.ParseNvidiaLatest("570.133.07 570.133.07/NVIDIA-Linux-x86_64-570.133.07.run\n"));
        Assert.Null(DriverTools.ParseNvidiaLatest("<html>blocked</html>")); Assert.Null(DriverTools.ParseNvidiaLatest("")); Assert.Null(DriverTools.ParseNvidiaLatest("570"));
        var items = DriverTools.ParseFwupd("{\"Devices\":[{\"Name\":\"UEFI dbx\",\"Version\":\"371\",\"Serial\":\"FIXTURE-SERIAL-NOT-REAL\",\"Releases\":[{\"Version\":\"372\"}]},{\"Name\":\"NVMe\",\"Version\":\"L183\"},{\"Version\":\"x\"}]}");
        Assert.Equal(2, items.Count); Assert.Equal("372", items[0].Update); Assert.Equal("", items[1].Update);
        Assert.DoesNotContain("FIXTURE-SERIAL", System.Text.Json.JsonSerializer.Serialize(items));
        Assert.Empty(DriverTools.ParseFwupd("{}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => DriverTools.ParseFwupd("not json"));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("-o /tmp/x")]
    public async Task OnlyHttpsUrlsCanBeOpened(string url) => await Assert.ThrowsAsync<ArgumentException>(() => DriverTools.OpenUrlAsync(url));
}
