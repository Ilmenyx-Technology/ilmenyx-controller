// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Fixtures sintéticos.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class BatteryPowerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-bat-" + Guid.NewGuid().ToString("N"));
    public BatteryPowerTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static BatteryInput Bat(long designWh = 50, long fullWh = 50, long? cycles = 100, long? pct = 80, string status = "Discharging", long? nowWh = 40, long? powerW = 10) =>
        new("BAT0", designWh * 1_000_000, fullWh * 1_000_000, nowWh * 1_000_000, cycles, pct, status, 12_000_000, powerW * 1_000_000, "FixtureCo", "FX-1", "Li-ion");
    private static HealthCard Battery(BatteryInput b, bool? ac = false) => BatteryEvaluator.Evaluate([b], ac, "Equilibrado", [])[0];

    [Fact]
    public void LabelsAndReasonsForCriticalWarningAndGood()
    {
        var critical = Battery(Bat(fullWh: 20)); Assert.Equal("DESGASTADA", critical.VerdictText); Assert.Contains("Vale trocar", critical.Reasons);
        var warn = Battery(Bat(fullWh: 34)); Assert.Equal("ATENÇÃO", warn.VerdictText);
        var good = Battery(Bat(fullWh: 49)); Assert.Equal("SAUDÁVEL", good.VerdictText); Assert.Contains("ótimo estado", good.Reasons);
        Assert.Equal("EM USO", BatteryEvaluator.Evaluate([Bat()], false, null, []).Last().VerdictText);
    }

    [Fact]
    public void CyclesNoteAndWarningAndZeroMeansUnknown()
    {
        var many = Battery(Bat(cycles: 1000)); Assert.Equal(HealthVerdict.Warning, many.Verdict); Assert.Contains("1000 ciclos", many.Reasons);
        var near = Battery(Bat(cycles: 850)); Assert.Equal(HealthVerdict.Good, near.Verdict); Assert.Contains("850 ciclos", near.Reasons);
        Assert.DoesNotContain(Battery(Bat(cycles: 0)).Metrics, m => m.Key == "Ciclos de carga");
    }

    [Fact]
    public void MissingCapacityIsUnknownAndLowChargeOffMainsWarnsInReasons()
    {
        var unknown = Battery(new("B", null, null, null, null, 50, "Discharging", null, null, null, null, null));
        Assert.Equal(HealthVerdict.Unknown, unknown.Verdict); Assert.Equal("SEM DADOS", unknown.VerdictText);
        Assert.Contains("Carga baixa", Battery(Bat(pct: 10)).Reasons);
        Assert.DoesNotContain("Carga baixa", Battery(Bat(pct: 10), ac: true).Reasons);
    }

    [Fact]
    public void RuntimeAndRatesAreDerivedFromEnergyAndPower()
    {
        var m = Battery(Bat(nowWh: 40, powerW: 10)).Metrics.ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("4h 00min", m["Autonomia estimada"]); Assert.Equal("10.0 W", m["Consumo atual"]); Assert.Equal("50.0 Wh", m["Capacidade de projeto"]);
        Assert.Equal("Descarregando", m["Estado"]); Assert.Equal("Íon-lítio", m["Química"]); Assert.Equal("12.00 V", m["Tensão"]);
        var charging = Battery(Bat(status: "Charging", powerW: 20), ac: true).Metrics.ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("20.0 W", charging["Potência de carga"]); Assert.DoesNotContain("Autonomia estimada", charging.Keys);
    }

    [Fact]
    public void DesktopHasOnlyThePowerCardAndPlanAdviceDependsOnProfile()
    {
        var desktop = BatteryEvaluator.Evaluate([], null, "Equilibrado", [new("hwmon · PPT", "55.0 W")]);
        var card = Assert.Single(desktop);
        Assert.Equal("Tomada (PC de mesa)", card.Metrics[0].Value); Assert.Contains("Alto desempenho", card.Reasons); Assert.Contains("PSU", card.Reasons);
        Assert.Contains(card.Metrics, k => k.Key == "hwmon · PPT");
        Assert.DoesNotContain("evita que o PC diminua", BatteryEvaluator.Evaluate([], null, "Alto desempenho", []).Single().Reasons);
        Assert.Contains("não identificado", BatteryEvaluator.Evaluate([], null, null, []).Single().Reasons);
    }

    [Fact]
    public void ReaderConvertsEnergyChargeAndPowerAndSkipsAbsentBatteries()
    {
        string ps = Path.Combine(root, "class/power_supply");
        void W(string dev, string file, string text) { Directory.CreateDirectory(Path.Combine(ps, dev)); File.WriteAllText(Path.Combine(ps, dev, file), text + "\n"); }
        W("BAT0", "type", "Battery"); W("BAT0", "present", "1"); W("BAT0", "status", "Discharging"); W("BAT0", "capacity", "80"); W("BAT0", "energy_full_design", "50000000");
        W("BAT0", "energy_full", "40000000"); W("BAT0", "energy_now", "32000000"); W("BAT0", "power_now", "8000000"); W("BAT0", "voltage_now", "12000000");
        W("BAT0", "cycle_count", "321"); W("BAT0", "technology", "Li-ion"); W("BAT0", "manufacturer", "FixtureCo"); W("BAT0", "serial_number", "FIXTURE-SERIAL-NOT-REAL");
        W("BAT1", "type", "Battery"); W("BAT1", "present", "1"); W("BAT1", "status", "Charging"); W("BAT1", "charge_full_design", "4000000"); W("BAT1", "charge_full", "3000000");
        W("BAT1", "voltage_min_design", "11000000"); W("BAT1", "current_now", "-1000000"); W("BAT1", "voltage_now", "11500000"); W("BAT1", "capacity", "150");
        W("BAT2", "type", "Battery"); W("BAT2", "present", "0");
        W("AC", "type", "Mains"); W("AC", "online", "1"); W("usb", "type", "USB"); W("usb", "online", "1");
        var r = new LinuxPowerReader(root).Read();
        Assert.True(r.MainsOnline); Assert.Equal(2, r.Batteries.Count);
        var a = r.Batteries[0];
        Assert.Equal(50_000_000, a.DesignEnergyUwh); Assert.Equal(40_000_000, a.FullEnergyUwh); Assert.Equal(32_000_000, a.NowEnergyUwh); Assert.Equal(8_000_000, a.PowerUw);
        Assert.Equal(321, a.Cycles); Assert.Equal(80, a.Percent); Assert.Equal("FixtureCo", a.Manufacturer);
        var b = r.Batteries[1];
        Assert.Equal(44_000_000, b.DesignEnergyUwh);  // 4 Ah * 11 V
        Assert.Equal(33_000_000, b.FullEnergyUwh);
        Assert.Equal(11_500_000, b.PowerUw);          // |-1 A| * 11.5 V
        Assert.Null(b.Percent);                       // 150 % is implausible
        Assert.DoesNotContain("FIXTURE-SERIAL", System.Text.Json.JsonSerializer.Serialize(r));
        Assert.Empty(new LinuxPowerReader(Path.Combine(root, "none")).Read().Batteries);
    }

    [Fact]
    public async Task BoundedProcessRunsWithoutShellAndEnforcesLimits()
    {
        var (code, output) = await BoundedProcess.RunAsync("echo", ["hi", "$HOME;x"], TimeSpan.FromSeconds(3));
        Assert.Equal(0, code); Assert.Equal("hi $HOME;x\n", output); // no shell expansion
        await Assert.ThrowsAsync<TimeoutException>(() => BoundedProcess.RunAsync("sleep", ["5"], TimeSpan.FromMilliseconds(200)));
        await Assert.ThrowsAsync<IOException>(() => BoundedProcess.RunAsync("seq", ["1", "100000"], TimeSpan.FromSeconds(3), maxCharacters: 100));
        await Assert.ThrowsAsync<FileNotFoundException>(() => BoundedProcess.RunAsync("definitely-not-a-program", [], TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<FileNotFoundException>(() => BoundedProcess.RunAsync("/bin/echo", [], TimeSpan.FromSeconds(1)));
        Assert.Equal("Equilibrado", LinuxPowerReader.ProfileName("balanced"));
    }

    [Fact]
    public void SysfsTextReadsRealSysfsAttributesThatReportSizeFourThousandNinetySix()
    {
        // Regression: /sys attributes have st_size 4096, so a FileInfo.Length guard silently dropped every value on real hardware.
        if (!OperatingSystem.IsLinux() || !File.Exists("/sys/devices/system/cpu/online")) return;
        Assert.Matches("^[0-9,-]+$", SysfsText.Read("/sys/devices/system/cpu/online"));
        Assert.Equal("", SysfsText.Read("/sys/devices/system/cpu/online", 1)); // longer than the limit: rejected, not truncated
        Assert.Equal("", SysfsText.Read("/nonexistent/attr"));
    }
}
