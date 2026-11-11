// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

// Readings taken from a real HP 250 G8 (i5-1135G7, Iris Xe, NVMe, CachyOS) on 2026-11-11.
public sealed class SensorFriendlyTests
{
    private static SensorMeasurement M(string chip, string device, string channel, string label, double? value, string unit, string status = "available") =>
        new(device, chip, channel, label, DateTimeOffset.UnixEpoch, "raw", new Reading<double?>(value, unit, "sysfs", status, null));

    private static SensorSnapshot Hp() => new("1.1", DateTimeOffset.UnixEpoch, "ok", null,
    [
        M("acpitz", "hwmon1", "temp1", "temp1", 27.8, "°C"), M("acpitz", "hwmon1", "temp2", "temp2", 10, "°C"),
        M("BAT1", "hwmon2", "in0", "in0", 12.769, "V"), M("BAT1", "hwmon2", "power1", "power1", null, "W", "unavailable"),
        M("nvme", "hwmon3", "temp1", "Composite", 46.85, "°C"), M("nvme", "hwmon3", "temp2", "Sensor 1", 46.85, "°C"),
        M("coretemp", "hwmon4", "temp1", "Package id 0", 82, "°C"), M("coretemp", "hwmon4", "temp2", "Core 0", 72, "°C"), M("coretemp", "hwmon4", "temp3", "Core 1", 79, "°C"),
        M("hp", "hwmon5", "fan1", "fan1", 0, "RPM"),
        M("SEN3", "Thermal/thermal_zone4", "temp", "thermal_zone4 · temp", 0.05, "°C"),
        M("Battery", "PowerSupply/BAT1", "capacity", "BAT1 · capacity", 100, "%"), M("Battery", "PowerSupply/BAT1", "voltage_now", "BAT1 · voltage_now", 12.769, "V"),
        M("Battery", "PowerSupply/BAT1", "energy_full", "BAT1 · energy_full", 29.99, "Wh"), M("Battery", "PowerSupply/BAT1", "energy_full_design", "BAT1 · energy_full_design", 41.05, "Wh"),
        M("TigerLake-LP GT2 [Iris Xe Graphics]", "gpu:0000:00:02.0", "freq1", "Core clock", 400, "MHz"), M("TigerLake-LP GT2 [Iris Xe Graphics]", "gpu:0000:00:02.0", "temp1", "Core temperature", null, "°C", "unavailable"),
    ], [], [new("ACAD", new("Mains", "", "sysfs", "available", null), new("Online", "", "sysfs", "available", null), new(1, "", "sysfs", "available", null))]);

    [Fact]
    public void CardsUsePlainNamesAndKeepTheTechnicalOne()
    {
        var cards = SensorFriendly.Build(Hp());
        Assert.Equal([SensorGroup.Cpu, SensorGroup.Gpu, SensorGroup.Storage, SensorGroup.Battery, SensorGroup.Fans, SensorGroup.Board], cards.Select(c => c.Group));
        var cpu = cards[0].Readings;
        var package = cpu.Single(r => r.Name == "Temperatura geral");
        Assert.Equal("coretemp · Package id 0 (temp1)", package.Technical);
        Assert.Equal(SensorLevel.Hot, package.Level); // 82 °C: between 80 and 95
        Assert.Contains(cpu, r => r.Name == "Núcleo 1" && r.Order == 10);
        Assert.Equal(SensorLevel.Normal, cards[2].Readings.Single(r => r.Name == "Temperatura do SSD/disco").Level);
        Assert.Equal(SensorLevel.Hot, SensorFriendly.Overall(cards));
    }

    [Fact]
    public void UnavailableReadingsAreLeftOutAndBatteryHealthIsComputed()
    {
        var cards = SensorFriendly.Build(Hp());
        Assert.DoesNotContain(cards.SelectMany(c => c.Readings), r => r.Value == "—");
        var battery = cards.Single(c => c.Group == SensorGroup.Battery).Readings;
        var health = battery.Single(r => r.Name == "Saúde da bateria");
        Assert.Equal(73, Math.Round(health.Number!.Value)); // 29.99 / 41.05
        Assert.Single(battery, r => r.Name == "Tensão da bateria"); // hwmon and power_supply report the same voltage
        Assert.Contains(battery, r => r.Name == "Carregador" && r.Value == "presente");
    }

    [Fact]
    public void StoppedFansAndDeadSensorsAreExplained()
    {
        var cards = SensorFriendly.Build(Hp());
        var fan = cards.Single(c => c.Group == SensorGroup.Fans).Readings.Single();
        Assert.Equal("parada", fan.Value); Assert.NotEmpty(fan.Help);
        var dead = cards.Single(c => c.Group == SensorGroup.Board).Readings.Single(r => r.Number < 1);
        Assert.Equal(SensorLevel.None, dead.Level); Assert.Contains("não está ligado", dead.Help);
    }

    [Theory]
    [InlineData(79.9, SensorLevel.Normal)]
    [InlineData(80, SensorLevel.Hot)]
    [InlineData(95, SensorLevel.Critical)]
    public void CpuThresholds(double c, SensorLevel level) =>
        Assert.Equal(level, SensorFriendly.Classify(M("k10temp", "hwmon0", "temp1", "Tctl", c, "°C"))!.Level);
}
