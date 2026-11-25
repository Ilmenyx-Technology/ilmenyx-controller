// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Pure math of the console-style restyle (ambient tint, clock, requirement meters, trophy numbers). No UI, no network, no hardware.
using Ilmenyx.Core;
using Ilmenyx.Desktop;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class ConsoleStyleTests
{
    private static readonly Rgb Fg = new(0xF4, 0xF4, 0xF6), Muted = new(0xB8, 0xB8, 0xC0), Faint = new(0xA0, 0xA2, 0xAD);
    private static readonly Rgb[] Text = [Fg, Muted, Faint];

    public static TheoryData<byte, byte, byte> Arts => new()
    {
        { 255, 255, 255 }, { 0, 0, 0 }, { 255, 0, 0 }, { 255, 255, 0 }, { 0, 255, 255 }, { 70, 130, 180 }, { 255, 140, 0 }, { 60, 179, 113 }, { 128, 128, 128 }, { 255, 20, 147 },
    };

    [Theory, MemberData(nameof(Arts))]
    public void TintKeepsContrastWithEveryThemeTextColour(byte r, byte g, byte b)
    {
        var t = AmbientTint.Tint(new(r, g, b), Text);
        foreach (var f in Text) Assert.True(AmbientTint.Contrast(f, t) >= AmbientTint.MinContrast, $"{f} on {t}: {AmbientTint.Contrast(f, t):0.0}");
        Assert.True(AmbientTint.Luminance(t) < 0.05); // dark whatever the art is
    }

    [Fact]
    public void TintStaysDesaturatedAndPureColoursKeepTheirHue()
    {
        var red = AmbientTint.Tint(new(255, 0, 0), Text);
        Assert.True(red.R > red.G && red.R > red.B);
        Assert.True(red.R - Math.Min(red.G, red.B) < 80); // never a vivid red window
        var grey = AmbientTint.Tint(new(128, 128, 128), Text);
        Assert.Equal(grey.R, grey.G); Assert.Equal(grey.G, grey.B);
    }

    [Fact]
    public void TintLowersLightnessWhenAForegroundIsTooCloseToIt()
    {
        var weak = new Rgb[] { new(0x90, 0x90, 0x90) }; // fails on the top of the usual band
        var t = AmbientTint.Tint(new(255, 255, 255), weak);
        Assert.True(AmbientTint.Contrast(weak[0], t) >= AmbientTint.MinContrast);
    }

    [Fact]
    public void AverageWeighsVividPixelsAndSkipsTransparentOnes()
    {
        byte[] px = [0, 0, 255, 255, /* red */ 128, 128, 128, 255 /* grey */, 0, 255, 0, 0 /* transparent green */];
        var avg = AmbientTint.Average(px)!.Value;
        Assert.True(avg.R > 170 && avg.G < 140); // red dominates the grey one; the transparent green is ignored
        Assert.Null(AmbientTint.Average([1, 2, 3, 0]));
        Assert.Null(AmbientTint.Average([]));
    }

    [Fact]
    public void ContrastMatchesWcagReferences()
    {
        Assert.Equal(21, AmbientTint.Contrast(new(0, 0, 0), new(255, 255, 255)), 3);
        Assert.Equal(1, AmbientTint.Contrast(new(9, 9, 9), new(9, 9, 9)), 6);
    }

    [Fact]
    public void ClockIsHoursAndMinutesAndWaitsForTheNextMinute()
    {
        var now = new DateTime(2026, 11, 24, 9, 5, 42, 250);
        Assert.Equal("09:05", MinuteClock.Text(now, System.Globalization.CultureInfo.GetCultureInfo("pt-BR")));
        Assert.Equal(17_750, MinuteClock.MillisecondsToNextMinute(now));
        Assert.Equal(50, MinuteClock.MillisecondsToNextMinute(new DateTime(2026, 1, 1, 0, 0, 59, 999)));
    }

    [Fact]
    public void MeterPlacesTicksAndPcOnOneScale()
    {
        var m = RequirementMeter.Layout(local: 80, minimum: 40, recommended: 100)!;
        Assert.Equal(80 / 115.0, m.Local, 6); Assert.Equal(40 / 115.0, m.Minimum!.Value, 6); Assert.Equal(100 / 115.0, m.Recommended!.Value, 6);
        Assert.True(m.Minimum < m.Local && m.Local < m.Recommended);
    }

    [Fact]
    public void MeterNeverLeavesTheBarAndKeepsOrderWhenThePcIsFarAbove()
    {
        var m = RequirementMeter.Layout(1000, 10, 20)!;
        Assert.InRange(m.Local, 0, 1); Assert.InRange(m.Minimum!.Value, 0, 1); Assert.True(m.Minimum <= m.Recommended && m.Recommended < m.Local);
        var zero = RequirementMeter.Layout(0, 10, 20)!;
        Assert.Equal(0, zero.Local);
    }

    [Fact]
    public void MeterNeedsThePcValueAndAtLeastOneRequirement()
    {
        Assert.Null(RequirementMeter.Layout(null, 10, 20));
        Assert.Null(RequirementMeter.Layout(10, null, null));
        Assert.Null(RequirementMeter.Layout(double.NaN, 10, 20));
        Assert.Null(RequirementMeter.Layout(-1, 10, 20));
        Assert.Null(RequirementMeter.Layout(10, 0, double.PositiveInfinity)); // zero/invalid requirements count as missing
        var onlyRec = RequirementMeter.Layout(8, null, 16)!; Assert.Null(onlyRec.Minimum); Assert.NotNull(onlyRec.Recommended);
        var onlyMin = RequirementMeter.Layout(8, 4, null)!; Assert.Null(onlyMin.Recommended); Assert.NotNull(onlyMin.Minimum);
    }

    private const string Min = "<strong>Minimum:</strong><br><ul><li><strong>Processor:</strong> Intel Core i5-8400<br></li><li><strong>Memory:</strong> 8 GB RAM<br></li><li><strong>Graphics:</strong> NVIDIA GeForce GTX 1060 6GB<br></li><li><strong>Storage:</strong> 50 GB available space<br></li></ul>";
    private const string Rec = "<strong>Recommended:</strong><br><ul><li><strong>Processor:</strong> Intel Core i7-8700K<br></li><li><strong>Memory:</strong> 16 GB RAM<br></li><li><strong>Graphics:</strong> NVIDIA GeForce RTX 3070<br></li><li><strong>Storage:</strong> 50 GB available space<br></li></ul>";

    [Fact]
    public void ReportCarriesTheNumbersTheMetersDrawWithoutChangingTheVerdict()
    {
        var pc = new LocalPc("AMD Ryzen 5 5600 6-Core Processor", "NVIDIA GeForce RTX 3060", 12UL << 30, false, (long)(15.5 * 1073741824), 200L << 30, "home", true);
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(Min), GameRequirementsParser.Parse(Rec), pc);
        var gpu = r.Items.Single(i => i.Key == "gpu"); var ram = r.Items.Single(i => i.Key == "ram");
        Assert.NotNull(gpu.LocalValue); Assert.NotNull(gpu.MinValue); Assert.NotNull(gpu.RecValue); Assert.True(gpu.MinValue < gpu.RecValue);
        Assert.Equal(8, ram.MinValue); Assert.Equal(16, ram.RecValue); Assert.Equal(15.5, ram.LocalValue!.Value, 1);
        Assert.NotNull(RequirementMeter.Layout(gpu.LocalValue, gpu.MinValue, gpu.RecValue));
        Assert.Equal(CanRunVerdict.Minimum, r.Verdict);
    }

    [Fact]
    public void UnknownPcHardwareLeavesTheRowWithoutMeter()
    {
        var pc = new LocalPc("", "", 0, false, 0, null, "home", true);
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(Min), GameRequirementsParser.Parse(Rec), pc);
        foreach (var i in r.Items.Where(i => i.Counts)) Assert.Null(CanRunPage.MeterOf(i));
    }

    [Fact]
    public void VerdictLooksFollowTheComparisonResult()
    {
        CanRunReport R(CanRunVerdict v, params string[] near) => new([], v, [], 0, "x", near);
        Assert.Equal("Roda no recomendado", CanRunPage.VerdictLook(R(CanRunVerdict.Recommended)).Label);
        Assert.Equal("Roda no mínimo", CanRunPage.VerdictLook(R(CanRunVerdict.Minimum)).Label);
        Assert.Equal("No limite", CanRunPage.VerdictLook(R(CanRunVerdict.Minimum, "Placa de vídeo")).Label);
        Assert.Equal("Abaixo do mínimo", CanRunPage.VerdictLook(R(CanRunVerdict.BelowMinimum)).Label);
        Assert.Equal("Sem dados suficientes", CanRunPage.VerdictLook(R(CanRunVerdict.NoData)).Label);
    }

    [Fact]
    public void TrophyPercentageNeverRoundsUpToComplete()
    {
        Assert.Equal(0, AchievementsPage.Percent(0, 0)); Assert.Equal(0, AchievementsPage.Percent(0, 10));
        Assert.Equal(99, AchievementsPage.Percent(999, 1000)); Assert.Equal(100, AchievementsPage.Percent(10, 10)); Assert.Equal(50, AchievementsPage.Percent(5, 10));
    }

    [Fact]
    public void LastUnlockIsShownOnlyWhenTheSourceRecordedTimes()
    {
        var now = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        Assert.Null(AchievementsPage.LastUnlockText([null, null], now)); Assert.Null(AchievementsPage.LastUnlockText([], now));
        Assert.Equal("última há 20 min", AchievementsPage.LastUnlockText([now.AddMinutes(-20)], now));
        Assert.Equal("última há 5 h", AchievementsPage.LastUnlockText([null, now.AddHours(-5), now.AddDays(-9)], now)); // the newest time wins
        Assert.Equal("última há 2 dias", AchievementsPage.LastUnlockText([now.AddHours(-49)], now));
        Assert.Equal("última há 9 dias", AchievementsPage.LastUnlockText([now.AddDays(-9)], now));
        Assert.Equal("última há 0 min", AchievementsPage.LastUnlockText([now.AddMinutes(5)], now)); // clock skew never goes negative
    }
}
