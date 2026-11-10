// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Fixtures sintéticos; sem serial ou modelo real.
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class DiskHealthTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-dh-" + Guid.NewGuid().ToString("N"));
    public DiskHealthTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static DiskHealthInput Input(string bus = "NVMe", string media = "SSD", bool? passed = true, double? temp = 40, double? wear = 5, long? uncorrected = 0, long? hours = 100) =>
        new("nvme0n1", "Fixture NVMe", "FW1", bus, media, 256_060_514_304, "/", passed, temp, wear, hours, 50, uncorrected, 0, 1e12, 2e12);

    [Fact]
    public void HealthyDiskIsGoodWithMetricsAndMeta()
    {
        var c = DiskHealthEvaluator.Evaluate(Input());
        Assert.Equal(HealthVerdict.Good, c.Verdict); Assert.Equal("SAUDÁVEL", c.VerdictText); Assert.False(c.IsAlert);
        Assert.Equal("SSD · NVMe · 256.1 GB · Montado em: /", c.Meta);
        Assert.Contains("Nenhum problema", c.Reasons);
        var m = c.Metrics.ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("40 °C", m["Temperatura"]); Assert.Equal("5%", m["Desgaste"]); Assert.Equal("95%", m["Vida útil restante"]);
        Assert.Equal("Não", m["Falha prevista (S.M.A.R.T.)"]); Assert.Equal("1 TB", m["Total gravado"]); Assert.Equal("FW1", m["Firmware"]);
    }

    [Fact]
    public void ReportedFailureIsCritical()
    {
        var c = DiskHealthEvaluator.Evaluate(Input(passed: false));
        Assert.Equal(HealthVerdict.Critical, c.Verdict); Assert.Equal("CRÍTICO", c.VerdictText); Assert.True(c.IsAlert);
        Assert.Contains("backup agora", c.Reasons); Assert.Equal("SIM", c.Metrics.First(k => k.Key.StartsWith("Falha")).Value);
    }

    [Theory]
    [InlineData(79, HealthVerdict.Good)]
    [InlineData(80, HealthVerdict.Warning)]
    [InlineData(94, HealthVerdict.Warning)]
    [InlineData(95, HealthVerdict.Critical)]
    [InlineData(100, HealthVerdict.Critical)]
    public void WearThresholdsMatchTheOriginal(double wear, HealthVerdict expected) => Assert.Equal(expected, DiskHealthEvaluator.Evaluate(Input(wear: wear)).Verdict);

    [Theory]
    [InlineData("NVMe", "SSD", 69, HealthVerdict.Good)]
    [InlineData("NVMe", "SSD", 70, HealthVerdict.Warning)]
    [InlineData("NVMe", "SSD", 85, HealthVerdict.Critical)]
    [InlineData("SATA/ATA", "HDD", 54, HealthVerdict.Good)]
    [InlineData("SATA/ATA", "HDD", 55, HealthVerdict.Warning)]
    [InlineData("SATA/ATA", "HDD", 65, HealthVerdict.Critical)]
    public void TemperatureThresholdsDependOnDiskKind(string bus, string media, double temp, HealthVerdict expected) =>
        Assert.Equal(expected, DiskHealthEvaluator.Evaluate(Input(bus: bus, media: media, temp: temp, wear: null)).Verdict);

    [Fact]
    public void UncorrectedErrorsWarn() => Assert.Equal(HealthVerdict.Warning, DiskHealthEvaluator.Evaluate(Input(uncorrected: 3)).Verdict);

    [Fact]
    public void NoDataIsUnknownWithGuidanceAndUsbStaysUnknown()
    {
        var none = DiskHealthEvaluator.Evaluate(new("sda", "Fixture HDD", "", "SATA/ATA", "HDD", 1_000_000_000_000, "", null, null, null, null, null, null, null, null, null));
        Assert.Equal(HealthVerdict.Unknown, none.Verdict); Assert.Equal("SEM DADOS", none.VerdictText); Assert.Contains("smartctl", none.Reasons);
        var usb = DiskHealthEvaluator.Evaluate(new("sdb", "Fixture USB", "", "USB", "", 16_000_000_000, "", null, null, null, null, null, null, null, null, null));
        Assert.Equal(HealthVerdict.Unknown, usb.Verdict);
        Assert.Equal("", DiskHealthEvaluator.FmtSize(0));
    }

    [Fact]
    public void ReaderBuildsInputsFromSysfsMountsHwmonAndSmart()
    {
        string sys = Path.Combine(root, "sys");
        string nvme = Path.Combine(sys, "block/nvme0n1"); Directory.CreateDirectory(Path.Combine(nvme, "device/hwmon/hwmon3")); Directory.CreateDirectory(Path.Combine(nvme, "queue"));
        File.WriteAllText(Path.Combine(nvme, "size"), "500000\n"); File.WriteAllText(Path.Combine(nvme, "device/model"), "Fixture NVMe\n");
        File.WriteAllText(Path.Combine(nvme, "device/firmware_rev"), "FW9\n"); File.WriteAllText(Path.Combine(nvme, "queue/rotational"), "0\n");
        File.WriteAllText(Path.Combine(nvme, "device/hwmon/hwmon3/temp1_input"), "44000\n");
        string sda = Path.Combine(sys, "block/sda"); Directory.CreateDirectory(Path.Combine(sda, "device")); Directory.CreateDirectory(Path.Combine(sda, "queue"));
        File.WriteAllText(Path.Combine(sda, "size"), "1000\n"); File.WriteAllText(Path.Combine(sda, "queue/rotational"), "1\n"); File.WriteAllText(Path.Combine(sda, "device/rev"), "R1\n");
        Directory.CreateDirectory(Path.Combine(sys, "block/loop0"));
        string mounts = "/dev/nvme0n1p2 / btrfs rw 0 0\n/dev/nvme0n1p1 /boot/efi vfat rw 0 0\n/dev/sda1 /mnt/HDD ext4 rw 0 0\ntmpfs /tmp tmpfs rw 0 0\n";
        var smart = new Dictionary<string, SmartDiagnostic>
        {
            ["nvme0n1"] = new("1.1", DateTimeOffset.UtcNow, "nvme0n1", "available", null, 0, Reading<bool?>.Available(true, "boolean", "x"), [],
                [new("Percentual usado (NVMe)", 9, "%", "s"), new("Horas ligado", 1000, "h", "s"), new("Erros de mídia", 2, "erros", "s"), new("Total gravado", 5e12, "B", "s")])
        };
        var inputs = new LinuxDiskHealthReader(sys).Read(mounts, smart);
        Assert.Equal(["nvme0n1", "sda"], inputs.Select(i => i.Device).ToArray());
        var n = inputs[0];
        Assert.Equal("Fixture NVMe", n.Model); Assert.Equal("FW9", n.Firmware); Assert.Equal(256_000_000L, n.SizeBytes); Assert.Equal(44.0, n.Temp);
        Assert.Equal("/, /boot/efi", n.Mounts); Assert.True(n.ReportedPassed); Assert.Equal(9, n.Wear); Assert.Equal(1000, n.PowerOnHours); Assert.Equal(2, n.UncorrectedErrors);
        var s = inputs[1];
        Assert.Equal("HDD", s.Media); Assert.Equal("R1", s.Firmware); Assert.Equal("/mnt/HDD", s.Mounts); Assert.Null(s.ReportedPassed); Assert.Equal("sda", s.Model);
        Assert.Equal(HealthVerdict.Warning, DiskHealthEvaluator.Evaluate(n).Verdict); // 2 media errors
    }

    [Fact]
    public void ImplausibleHwmonTemperatureIsIgnoredAndUnavailableSmartIsNotUsed()
    {
        string sys = Path.Combine(root, "sys2"); string d = Path.Combine(sys, "block/sda"); Directory.CreateDirectory(Path.Combine(d, "device/hwmon/hwmon1")); Directory.CreateDirectory(Path.Combine(d, "queue"));
        File.WriteAllText(Path.Combine(d, "device/hwmon/hwmon1/temp1_input"), "999999\n"); File.WriteAllText(Path.Combine(d, "size"), "10\n");
        var smart = new Dictionary<string, SmartDiagnostic> { ["sda"] = new("1.1", DateTimeOffset.UtcNow, "sda", "unavailable", "x", 4, Reading<bool?>.Missing("boolean", "x", "x"), []) };
        var i = new LinuxDiskHealthReader(sys).Read("", smart).Single();
        Assert.Null(i.Temp); Assert.Null(i.ReportedPassed);
        Assert.Throws<IOException>(() => new LinuxDiskHealthReader(Path.Combine(root, "missing")).Read(""));
    }
}
