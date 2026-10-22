// New Ilmenyx additions: see LICENSING.md. Fixtures sintéticos; nenhuma porta real é aberta.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class SerialPortTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-ser-" + Guid.NewGuid().ToString("N"));
    public SerialPortTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static SerialEnvironment Env(bool brltty = false, bool mm = false, bool inGroup = true, (bool, bool)? module = null) => new(brltty, mm, inGroup, "dialout", _ => module ?? (true, true));
    private static SerialPortInfo Port(string vid = "1A86", string driver = "ch341", bool? accessible = true, string? group = "dialout") => new("/dev/ttyUSB0", vid, "7523", "USB Serial", "wch.cn", driver, accessible, group);

    [Fact]
    public void ChipsAreRecognisedByVendorId()
    {
        Assert.Equal("CH340/CH341", SerialDiagnosis.ChipByVid("1a86")!.Name); Assert.Equal("cp210x", SerialDiagnosis.ChipByVid("10C4")!.Modules[0]); Assert.Equal("ftdi_sio", SerialDiagnosis.ChipByVid("0403")!.Modules[0]);
        Assert.Equal("cdc_acm", SerialDiagnosis.ChipByVid("2341")!.Modules[0]); Assert.Equal("cdc_acm", SerialDiagnosis.ChipByVid("2A03")!.Modules[0]); Assert.Null(SerialDiagnosis.ChipByVid("ffff")); Assert.Null(SerialDiagnosis.ChipByVid(null));
    }

    [Fact]
    public void WorkingPortAndModemManagerNote()
    {
        var ok = SerialDiagnosis.Diagnose(Port(), Env()); Assert.True(ok.Ok); Assert.Equal("Funcionando", ok.Verdict); Assert.DoesNotContain("ModemManager", ok.Advice);
        Assert.Contains("ModemManager", SerialDiagnosis.Diagnose(Port(), Env(mm: true)).Advice);
    }

    [Fact]
    public void UnboundDeviceExplainsBrlttyMissingOrUnloadedModule()
    {
        var brltty = SerialDiagnosis.Diagnose(Port(driver: ""), Env(brltty: true)); Assert.False(brltty.Ok); Assert.Contains("brltty", brltty.Verdict);
        Assert.DoesNotContain("brltty", SerialDiagnosis.Diagnose(Port(vid: "10C4", driver: ""), Env(brltty: true, module: (true, false))).Verdict); // brltty only matters for CH34x
        var unloaded = SerialDiagnosis.Diagnose(Port(driver: ""), Env(module: (true, false))); Assert.Contains("não carregado", unloaded.Verdict); Assert.Contains("modprobe ch341", unloaded.Advice);
        var missing = SerialDiagnosis.Diagnose(Port(driver: ""), Env(module: (false, false))); Assert.Contains("sem o módulo", missing.Verdict.ToLowerInvariant()); Assert.Contains("linux-modules-extra", missing.Advice);
        Assert.Contains("dmesg", SerialDiagnosis.Diagnose(Port(driver: ""), Env(module: (true, true))).Advice);
    }

    [Fact]
    public void InaccessibleNodeSuggestsTheNodesOwningGroupWithoutChangingAnything()
    {
        var r = SerialDiagnosis.Diagnose(Port(accessible: false, group: "uucp"), Env(inGroup: false)); Assert.False(r.Ok); Assert.Contains("usermod -aG uucp", r.Advice); Assert.Contains("não altera grupos", r.Advice);
        Assert.False(SerialDiagnosis.Diagnose(Port(accessible: null, group: null), Env(inGroup: false)).Ok); // unknown access + not in group
        Assert.True(SerialDiagnosis.Diagnose(Port(accessible: null, group: null), Env(inGroup: true)).Ok);
    }

    [Theory]
    [InlineData(1000, new[] { 20 }, 0, 20, 0x1B0, true)]    // 660 root:dialout, user in dialout
    [InlineData(1000, new[] { 1000 }, 0, 20, 0x1B0, false)] // not in group
    [InlineData(1000, new[] { 1000 }, 1000, 20, 0x180, true)] // owner rw (600)
    [InlineData(1000, new[] { 1000 }, 0, 20, 0x1B6, true)]   // 666 other rw
    [InlineData(0, new int[0], 5, 5, 0, true)]               // root
    [InlineData(1000, new[] { 20 }, 0, 20, 0x120, false)]    // group read-only (440)
    public void ReadWriteAccessFollowsModeBits(int uid, int[] gids, int nodeUid, int nodeGid, int mode, bool expected) => Assert.Equal(expected, LinuxSerialPorts.CanReadWrite(uid, gids, nodeUid, nodeGid, mode));

    [Fact]
    public void IdentityIsReadFromProcStatus()
    {
        Directory.CreateDirectory(Path.Combine(root, "proc/self")); File.WriteAllText(Path.Combine(root, "proc/self/status"), "Name:\tx\nUid:\t1000\t1000\t1000\t1000\nGid:\t1000\t1000\t1000\t1000\nGroups:\t20 985 1000\n");
        var (uid, gids) = LinuxSerialPorts.Identity(Path.Combine(root, "proc")); Assert.Equal(1000, uid); Assert.Equal([20, 985, 1000], gids.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task ReaderWalksSysfsToTheUsbDeviceAndNeverNeedsToOpenThePort()
    {
        string sys = Path.Combine(root, "sys"), proc = Path.Combine(root, "proc"); Directory.CreateDirectory(Path.Combine(proc, "self")); Directory.CreateDirectory(Path.Combine(proc, "sys/kernel"));
        File.WriteAllText(Path.Combine(proc, "self/status"), "Uid:\t1000\t1\t1\t1\nGid:\t1000\t1\t1\t1\nGroups:\t1000\n"); File.WriteAllText(Path.Combine(proc, "sys/kernel/osrelease"), "9.9\n");
        Directory.CreateDirectory(Path.Combine(proc, "42")); File.WriteAllText(Path.Combine(proc, "42/comm"), "ModemManager\n");
        string usb = Path.Combine(sys, "devices/pci/usb1/1-1"); string iface = Path.Combine(usb, "1-1:1.0"); string ttyDev = Path.Combine(iface, "ttyUSB0");
        Directory.CreateDirectory(ttyDev);
        File.WriteAllText(Path.Combine(usb, "idVendor"), "1a86\n"); File.WriteAllText(Path.Combine(usb, "idProduct"), "7523\n"); File.WriteAllText(Path.Combine(usb, "product"), "USB Serial\n");
        string drv = Path.Combine(sys, "bus/usb-serial/drivers/ch341-uart"); Directory.CreateDirectory(drv); Directory.CreateSymbolicLink(Path.Combine(ttyDev, "driver"), drv);
        Directory.CreateDirectory(Path.Combine(sys, "class/tty/ttyUSB0")); Directory.CreateSymbolicLink(Path.Combine(sys, "class/tty/ttyUSB0/device"), ttyDev); // real layout: device → the usb-serial port device
        Directory.CreateDirectory(Path.Combine(sys, "class/tty/ttyS0")); // not a USB serial: ignored
        Directory.CreateDirectory(Path.Combine(sys, "module/ch341"));
        string[] statCalls = [];
        var reader = new LinuxSerialPorts(sys, proc, Path.Combine(root, "dev"), null, (node, _) => { statCalls = [.. statCalls, node]; return Task.FromResult<(int, int, int)?>((0, 20, 0x1B0)); });
        var rows = await reader.ReadAsync();
        var row = Assert.Single(rows);
        Assert.Equal("1A86", row.Port.Vid); Assert.Equal("7523", row.Port.Pid); Assert.Equal("USB Serial", row.Port.Product); Assert.Equal(Path.Combine(root, "dev/ttyUSB0"), row.Port.Node);
        Assert.Equal("CH340/CH341", row.Chip!.Name); Assert.False(row.Port.Accessible); // gid 20 not among the user's groups
        Assert.False(row.Diagnosis.Ok); Assert.Contains("permissão", row.Diagnosis.Verdict); Assert.Equal([Path.Combine(root, "dev/ttyUSB0")], statCalls);
        Assert.Empty(await new LinuxSerialPorts(Path.Combine(root, "none"), proc).ReadAsync());
        Assert.True(reader.Environment().ModemManagerRunning); Assert.True(reader.ModuleState("ch341").Loaded); Assert.False(reader.ModuleState("nonexistent_mod").Available);
    }
}
