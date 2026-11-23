// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Synthetic requirement texts and hardware names only: no network, no real hardware.
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class HardwareIndexTests
{
    private static string Html(string label, string value) => $"<ul><li><strong>{label}:</strong>{value}<br></li></ul>";

    // --- parser fixtures: real-world requirement lines, pt-BR and en -------------------------------------------------------------------

    [Theory]
    [InlineData(": Intel® Core™2 Duo 2.4 Ghz / better / AMD Athlon™ X2 2.8 Ghz / better", "Intel Core2 Duo 2.4 Ghz|AMD Athlon X2 2.8 Ghz")]
    [InlineData("Intel® Core™2 Quad 2.7 Ghz / better / AMD Phenom™ II X4 3 Ghz / better", "Intel Core2 Quad 2.7 Ghz|AMD Phenom II X4 3 Ghz")]
    [InlineData("Dual core 2.0 GHz", "Dual core 2.0 GHz")]
    [InlineData("Quad-core Intel or AMD processor, 2.5 GHz", "Quad-core Intel & AMD processor, 2.5 GHz")]
    [InlineData("Intel Core i5-4460 or AMD FX-6300", "Intel Core i5-4460|AMD FX-6300")]
    [InlineData("4 cores / 8 threads", "4 cores 8 threads")]
    [InlineData("Intel Core i5-2400 ou AMD Phenom II X4 955 ou superior", "Intel Core i5-2400|AMD Phenom II X4 955")]
    [InlineData("Intel Core i7-3770 / AMD FX-8350 or equivalent", "Intel Core i7-3770|AMD FX-8350")]
    [InlineData("Core 2 Duo E8400 3.0GHz", "Core 2 Duo E8400 3.0GHz")]
    [InlineData("AMD Ryzen 5 1600 or Intel Core i5-7500 or better", "AMD Ryzen 5 1600|Intel Core i5-7500")]
    [InlineData(": AMD Athlon 64 X2 4800+ or Intel Pentium D 3.0 GHz", "AMD Athlon 64 X2 4800|Intel Pentium D 3.0 GHz")]
    [InlineData("Intel Core i3-9100 ou AMD Ryzen 3 3200G", "Intel Core i3-9100|AMD Ryzen 3 3200G")]
    [InlineData("Processador de 2 GHz ou superior", "Processador de 2 GHz")]
    public void CpuLinesAreCleanedAndSplit(string value, string expected) =>
        Assert.Equal(expected, string.Join('|', GameRequirementsParser.Parse(Html("Processor", value)).CpuAlternatives));

    [Theory]
    [InlineData("NVIDIA® GeForce® 8800GTS / better", "NVIDIA GeForce 8800GTS")]
    [InlineData("NVIDIA® GeForce® GTX 560 / better", "NVIDIA GeForce GTX 560")]
    [InlineData("NVIDIA GeForce GTX 650 Ti ou equivalente", "NVIDIA GeForce GTX 650 Ti")]
    [InlineData("AMD Radeon HD 7850 ou melhor", "AMD Radeon HD 7850")]
    [InlineData("GeForce GTX 970 4GB / Radeon R9 290X 4GB", "GeForce GTX 970|Radeon R9 290X")]
    [InlineData("NVIDIA GeForce GTX 1060 6GB or AMD Radeon RX 580 8GB or better", "NVIDIA GeForce GTX 1060|AMD Radeon RX 580")]
    [InlineData("Intel HD Graphics 4000 or better", "Intel HD Graphics 4000")]
    [InlineData("RTX 2060 / RX 5700", "RTX 2060|RX 5700")]
    public void GpuLinesAreCleanedAndSplit(string value, string expected) =>
        Assert.Equal(expected, string.Join('|', GameRequirementsParser.Parse(Html("Graphics", value)).GpuAlternatives));

    [Fact]
    public void ColonLeftoverNeverStartsTheValue()
    {
        var r = GameRequirementsParser.Parse("<ul><li><strong>Processor:</strong>: Intel® Core™2 Duo 2.4 Ghz / better</li></ul>");
        Assert.Equal("Intel Core2 Duo 2.4 Ghz", r.CpuAlternatives.Single());
    }

    // --- CPU ----------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Intel Core2 Duo 2.4 Ghz", RatingBasis.Spec, 12, 17)]
    [InlineData("AMD Athlon X2 2.8 Ghz", RatingBasis.Spec, 12, 17)]
    [InlineData("Intel Core2 Quad 2.7 Ghz", RatingBasis.Spec, 16, 22)]
    [InlineData("AMD Phenom II X4 3 Ghz", RatingBasis.Spec, 17, 24)]
    [InlineData("Dual core 2.0 GHz", RatingBasis.Spec, 9, 14)]
    [InlineData("Quad-core Intel & AMD processor, 2.5 GHz", RatingBasis.Family, 14, 20)]
    [InlineData("Intel Core i5-4460", RatingBasis.Family, 33, 43)]
    [InlineData("Intel Core i3-9100", RatingBasis.Family, 42, 54)]
    [InlineData("AMD FX-6300", RatingBasis.Family, 26, 35)]
    [InlineData("Intel Core i5 3.0 GHz", RatingBasis.Spec, 20, 30)]
    [InlineData("11th Gen Intel(R) Core(TM) i5-1135G7 @ 2.40GHz", RatingBasis.Model, 55, 55)]
    public void CpuRatingsByFamilyClockAndModel(string text, RatingBasis basis, double lo, double hi)
    {
        var r = CpuIndexer.Rate(text)!;
        Assert.Equal(basis, r.Basis); Assert.InRange(r.Index, lo, hi);
    }

    [Fact]
    public void CpuIndexOrdersGenerationsAndNeverGuessesWithoutData()
    {
        double I(string s) => CpuIndexer.Rate(s)!.Index;
        Assert.True(I("Intel Core2 Duo 2.4 Ghz") < I("Intel Core2 Quad 2.7 Ghz")); Assert.True(I("Intel Core2 Quad 2.7 Ghz") < I("AMD Phenom II X4 3 Ghz"));
        Assert.True(I("AMD Phenom II X4 3 Ghz") < I("Intel Core i5-4460")); Assert.True(I("Intel Core i5-4460") < I("Intel Core i5-8400"));
        Assert.True(I("AMD FX-6300") < I("AMD Ryzen 5 1600")); Assert.True(I("Intel Core i7-3770") < I("Intel Core i7-8700K"));
        Assert.True(I("Intel Core i5-11400H") < I("Intel Core i5-11600K")); Assert.True(I("Intel Core i5-1235U") < I("Intel Core i5-12400"));
        Assert.Null(CpuIndexer.Rate("4 cores 8 threads")); Assert.Null(CpuIndexer.Rate("Loongson 3A5000")); Assert.Null(CpuIndexer.Rate(""));
        Assert.Null(CpuIndexer.Rate("AMD Ryzen 7 7600X3D"));   // cache variants are not guessed from the plain part
    }

    [Fact]
    public void ShortModelNumbersDoNotMatchInsideLongerOnes()
    {
        Assert.Equal("1600", CpuIndexer.Rate("AMD Ryzen 5 1600")!.Label);
        var r = CpuIndexer.Rate("Intel Core i5-11600K")!;
        Assert.NotEqual("1600", r.Label); Assert.Equal(RatingBasis.Family, r.Basis); Assert.True(r.Index > 60);
    }

    [Fact]
    public void CpuFormulaStaysNearTheCatalogAnchors()
    {
        // (arch, GHz, effective cores) of anchor parts vs their catalog index; the doc lists the full fit (mean 7.6 %).
        foreach (var (arch, ghz, cores, anchor) in new[] { ("Zen4", 5.4, 8.0, 100.0), ("Golden", 4.4, 6.0, 78.0), ("Zen3", 4.4, 6.0, 73.0), ("Zen2", 4.2, 6.0, 62.0), ("Skylake", 4.0, 6.0, 58.0), ("Haswell", 4.4, 4.0, 45.0), ("Zen", 3.6, 6.0, 45.0) })
            Assert.InRange(CpuIndexer.Score(arch, ghz, cores) / anchor, 0.8, 1.25);
    }

    // --- GPU ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TablesAreAsBigAsDocumented()
    {
        Assert.True(GpuIndexer.Count >= 230, $"{GpuIndexer.Count}"); Assert.True(GpuIndexer.PciCount >= 70, $"{GpuIndexer.PciCount}");
    }

    [Theory]
    [InlineData("NVIDIA GeForce 8800GTS", "8800 gts", RatingBasis.Spec)]
    [InlineData("NVIDIA GeForce GTX 560", "gtx 560", RatingBasis.Spec)]
    [InlineData("GeForce GTX 1650 SUPER", "gtx 1650 super", RatingBasis.Spec)]
    [InlineData("GeForce GTX1660Ti", "gtx 1660 ti", RatingBasis.Spec)]
    [InlineData("AMD Radeon HD 7850 2GB", "hd 7850", RatingBasis.Spec)]
    [InlineData("Radeon HD7970", "hd 7970", RatingBasis.Spec)]
    [InlineData("Radeon R9 280X", "r9 280x", RatingBasis.Spec)]
    [InlineData("Radeon RX Vega 56", "vega 56", RatingBasis.Spec)]
    [InlineData("Radeon RX 5700XT", "rx 5700 xt", RatingBasis.Spec)]
    [InlineData("GeForce 9800 GTX+", "9800 gtx+", RatingBasis.Spec)]
    [InlineData("Intel(R) HD Graphics 4000", "hd graphics 4000", RatingBasis.Spec)]
    [InlineData("Intel Arc A750 Graphics", "Arc A750", RatingBasis.Model)]
    [InlineData("Intel Corporation TigerLake-LP GT2 [Iris Xe Graphics]", "Iris Xe", RatingBasis.Model)]
    [InlineData("NVIDIA GeForce RTX 3070 Ti", "RTX 3070 Ti", RatingBasis.Model)]
    public void GpuNamesFindTheirEntryByFamily(string name, string label, RatingBasis basis)
    {
        var r = GpuIndexer.Rate(name, null, requirement: true)!;
        Assert.Equal(label, r.Label); Assert.Equal(basis, r.Basis);
    }

    [Fact]
    public void VariantsAreNeverMergedAndOrderFollowsTheSpecs()
    {
        double I(string s) => GpuIndexer.Rate(s, null, true)!.Index;
        Assert.Null(GpuIndexer.Rate("GeForce GTX 1060 Ti"));        // no such chip
        Assert.Null(GpuIndexer.Rate("GeForce GTX 560 SE"));          // not in the table: never the plain 560
        Assert.True(I("GeForce GTX 1650") < I("GeForce GTX 1650 SUPER")); Assert.True(I("GeForce GTX 1660") < I("GeForce GTX 1660 Ti"));
        Assert.True(I("RX 6600") < I("RX 6600 XT")); Assert.True(I("RTX 4070") < I("RTX 4070 SUPER")); Assert.True(I("RTX 4070 SUPER") < I("RTX 4070 Ti"));
        Assert.True(I("GeForce 8800 GTX") < I("GeForce GTX 280")); Assert.True(I("GeForce GTX 280") < I("GeForce GTX 480")); Assert.True(I("GeForce GTX 480") < I("GeForce GTX 780 Ti"));
        Assert.True(I("Radeon HD 4850") < I("Radeon HD 5870")); Assert.True(I("Radeon HD 5870") < I("Radeon HD 7970"));
        Assert.True(I("Intel HD Graphics 4000") < I("Intel UHD Graphics 630"));
    }

    [Fact]
    public void SpecFormulaStaysNearTheCatalogAnchors()
    {
        // GPUs in both sources: index from shaders x2 x clock x architecture factor vs the catalog index. Mean error in docs/meu-pc-roda.md (5 to 19 % per architecture).
        string[] names = ["RTX 5090", "RTX 5070", "RTX 4090", "RTX 4070", "RTX 4060", "RTX 3090", "RTX 3070", "RTX 3060", "RTX 2080 Ti", "RTX 2060", "GTX 1660", "GTX 1080 Ti", "GTX 1060", "GTX 980", "GTX 960",
            "RX 9070 XT", "RX 7900 XTX", "RX 7800 XT", "RX 6800 XT", "RX 6600", "RX 5700 XT", "RX 580", "Arc A770"];
        var errs = names.Select(n => Math.Abs(Math.Log(GpuIndexer.SpecIndex(n)! .Value / GameCatalog.GpuMatchWhole(n)!.Value.Index))).ToList();
        Assert.True(errs.Average() < 0.15, $"mean {errs.Average():0.00}"); Assert.True(errs.Max() < 0.45, $"max {errs.Max():0.00}");
    }

    [Theory]
    [InlineData("", "10DE:2504", "rtx 3060", RatingBasis.Spec)]
    [InlineData("NVIDIA Corporation GA106", "0x10de:0x2486", "rtx 3060 ti", RatingBasis.Spec)]
    [InlineData("Intel Graphics", "8086:9A49", "iris xe 80eu", RatingBasis.Family)]
    [InlineData("Intel Graphics", "8086:56A1", "arc a750", RatingBasis.Spec)]
    [InlineData("", "8086:5917", "uhd graphics 620", RatingBasis.Family)]
    public void PciIdIdentifiesTheModelWhenTheNameIsGeneric(string name, string pci, string label, RatingBasis basis)
    {
        var r = GpuIndexer.Rate(name, pci)!;
        Assert.Equal(label, r.Label); Assert.Equal(basis, r.Basis);
    }

    [Fact]
    public void GenericIntegratedNameWithoutAnIdStaysUnknown()
    {
        Assert.Null(GpuIndexer.Rate("AMD Radeon Graphics")); Assert.Null(GpuIndexer.Rate("Intel(R) Graphics")); Assert.Null(GpuIndexer.Rate("AMD Radeon Graphics", "1002:FFFF"));
        Assert.Null(GpuIndexer.Rate("Matrox G200eR2", "102B:0534"));
    }

    // --- comparison ---------------------------------------------------------------------------------------------------------------------

    private const string UserMin = "<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li><strong>OS:</strong> Windows XP<br></li><li><strong>Processor:</strong>: Intel® Core™2 Duo 2.4 Ghz / better / AMD Athlon™ X2 2.8 Ghz / better<br></li>"
        + "<li><strong>Memory:</strong> 2 GB RAM<br></li><li><strong>Graphics:</strong> NVIDIA® GeForce® 8800GTS / better<br></li><li><strong>Storage:</strong> 8 GB available space<br></li></ul>";
    private const string UserRec = "<strong>Recommended:</strong><br><ul class=\"bb_ul\"><li><strong>OS:</strong> Windows 7<br></li><li><strong>Processor:</strong>: Intel® Core™2 Quad 2.7 Ghz / better / AMD Phenom™ II X4 3 Ghz / better<br></li>"
        + "<li><strong>Memory:</strong> 4 GB RAM<br></li><li><strong>Graphics:</strong> NVIDIA® GeForce® GTX 560 / better<br></li><li><strong>Storage:</strong> 8 GB available space<br></li></ul>";

    private static LocalPc Pc(string cpu, string gpu, string? pci = null, double ramGib = 15.3, bool igpu = false)
        => new(cpu, gpu, igpu ? 0 : 4UL << 30, igpu, (long)(ramGib * 1073741824), 100L << 30, "home", true, pci);

    private static CanRunItem Item(CanRunReport r, string key) => r.Items.Single(i => i.Key == key);

    [Fact]
    public void ReportedCaseIrisXeWithTigerLakeMeetsTheRecommended()
    {
        // i5-1135G7 = 55 (catalog), Iris Xe = 8.1 (catalog); Core 2 Quad 2.7 GHz = 18.4, Phenom II X4 3 GHz = 20.4 (lowest 18.4); GTX 560 = 5.4 (spec); 8800 GTS = 1.1.
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(UserMin), GameRequirementsParser.Parse(UserRec),
            Pc("11th Gen Intel(R) Core(TM) i5-1135G7 @ 2.40GHz", "Iris Xe Graphics", "8086:9A49"));
        var cpu = Item(r, "cpu"); var gpu = Item(r, "gpu");
        Assert.Equal(ReqStatus.MeetsRec, cpu.Status); Assert.Equal(ReqStatus.MeetsRec, gpu.Status);
        Assert.Equal(RatingBasis.Spec, cpu.Basis); Assert.Equal(RatingBasis.Spec, gpu.Basis);
        Assert.DoesNotContain(":", cpu.Minimum.Substring(0, 2)); Assert.StartsWith("Intel Core2 Duo", cpu.Minimum);
        Assert.Equal(CanRunVerdict.Recommended, r.Verdict); Assert.Equal(0, r.NotCompared);
    }

    [Fact]
    public void ReportedCaseAlsoWorksWhenOnlyThePciIdIsKnown()
    {
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(UserMin), GameRequirementsParser.Parse(UserRec), Pc("11th Gen Intel(R) Core(TM) i5-1135G7", "", "8086:9A49"));
        Assert.Equal(ReqStatus.MeetsRec, Item(r, "gpu").Status); Assert.Equal(RatingBasis.Family, Item(r, "gpu").Basis);   // typical 80 EU of the family: an estimate
    }

    [Fact]
    public void OldPcFallsBelowOnlyWithEnoughDataAndOtherwiseStaysUnsure()
    {
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(UserMin), GameRequirementsParser.Parse(UserRec), Pc("Intel Core2 Duo 2.4 Ghz", "GeForce 8600 GT", ramGib: 3.9));
        Assert.Equal(ReqStatus.MeetsMin, Item(r, "cpu").Status);   // 14.2 vs the minimum 14.2; against the recommended 18.4 it is 0.77, inside the 0.85 margin of spec data: not "below"
        Assert.Equal(ReqStatus.Below, Item(r, "gpu").Status);      // 0.4 vs 1.1 (8800 GTS): far below even with spec data
        Assert.Equal(CanRunVerdict.BelowMinimum, r.Verdict);
    }

    [Fact]
    public void UnknownModelOfAnOldFamilyIsMetOnlyByAMuchNewerPcAndNeverBelow()
    {
        var req = GameRequirementsParser.Parse(Html("Graphics", "Radeon HD 6310 or better"));
        var strong = CanRunCompare.Compare(req, GameRequirement.Empty, Pc("AMD Ryzen 5 5600 6-Core Processor", "NVIDIA GeForce RTX 3060"));
        Assert.Equal(ReqStatus.MeetsMin, Item(strong, "gpu").Status); Assert.Contains("família bem antiga", Item(strong, "gpu").Note); Assert.Equal(RatingBasis.Family, Item(strong, "gpu").Basis);
        var close = CanRunCompare.Compare(req, GameRequirement.Empty, Pc("AMD Ryzen 5 5600 6-Core Processor", "Iris Xe Graphics"));
        Assert.Equal(ReqStatus.Unknown, Item(close, "gpu").Status);
        var weak = CanRunCompare.Compare(req, GameRequirement.Empty, Pc("AMD Ryzen 5 5600 6-Core Processor", "Intel HD Graphics 4000"));
        Assert.Equal(ReqStatus.Unknown, Item(weak, "gpu").Status);
    }

    [Fact]
    public void CloseCallsWithSpecDataAreNotDeclaredBelow()
    {
        var req = GameRequirementsParser.Parse(Html("Graphics", "GeForce GTX 750 Ti"));
        var near = CanRunCompare.Compare(req, GameRequirement.Empty, Pc("AMD Ryzen 5 5600 6-Core Processor", "GeForce GTX 750"));   // 5.8 vs 6.7: 0.86
        Assert.Equal(ReqStatus.Unknown, Item(near, "gpu").Status);
        var far = CanRunCompare.Compare(req, GameRequirement.Empty, Pc("AMD Ryzen 5 5600 6-Core Processor", "Radeon HD 5770"));    // 3.7 vs 6.7
        Assert.Equal(ReqStatus.Below, Item(far, "gpu").Status);
    }

    [Fact]
    public void CpuRequirementsByFamilyAndGenericClockAreComparedWithTheSameFormula()
    {
        string[] ok = ["Quad-core Intel or AMD processor, 2.5 GHz", "Dual core 2.0 GHz", "Intel Core i5-4460 or AMD FX-6300", "Intel Core i3-9100 or AMD Ryzen 3 3200G"];
        foreach (var line in ok)
        {
            var r = CanRunCompare.Compare(GameRequirementsParser.Parse(Html("Processor", line)), GameRequirement.Empty, Pc("AMD Ryzen 5 5600 6-Core Processor", "NVIDIA GeForce RTX 3060"));
            Assert.Equal(ReqStatus.MeetsMin, Item(r, "cpu").Status);
        }
        var slow = CanRunCompare.Compare(GameRequirementsParser.Parse(Html("Processor", "Intel Core i7-8700K or AMD Ryzen 5 3600")), GameRequirement.Empty, Pc("Intel Core2 Duo 2.4 Ghz", "x"));
        Assert.Equal(ReqStatus.Below, Item(slow, "cpu").Status);
        var cores = CanRunCompare.Compare(GameRequirementsParser.Parse(Html("Processor", "4 cores / 8 threads")), GameRequirement.Empty, Pc("AMD Ryzen 5 5600 6-Core Processor", "x"));
        Assert.Equal(ReqStatus.Unknown, Item(cores, "cpu").Status);   // no clock, no model: not guessed
    }
}
