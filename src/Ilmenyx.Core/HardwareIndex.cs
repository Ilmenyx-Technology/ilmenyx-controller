// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Method, anchors and fit errors: docs/meu-pc-roda.md. Specification figures (shader units, boost clock) are public manufacturer data.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

/// <summary>How a rating was reached; the order is the order of trust (the weakest side of a comparison decides what the page says).</summary>
public enum RatingBasis { Family = 0, Spec = 1, Model = 2 }

/// <summary>Relative gaming-performance index on the same scale as <see cref="GameCatalog"/> (GPU: RTX 4070 = 100; CPU: Ryzen 7 7700X = 100).</summary>
/// <param name="OldFamilyCap">The model itself is unknown: the index is the ceiling of an old family (never used to say "below").</param>
public sealed record Rating(string Label, double Index, RatingBasis Basis, bool OldFamilyCap = false);

/// <summary>
/// GPU index from public specifications: FP32 GFLOPS (shader units x 2 x boost clock) times a factor per architecture. The factors are the geometric
/// mean of index/GFLOPS over the GPUs that are also in <see cref="GameCatalog"/> (anchors); architectures without anchors are chained from
/// same-class pairs. Pure and offline. Errors per architecture: docs/meu-pc-roda.md.
/// </summary>
public static class GpuIndexer
{
    // Arch|key|shader units|clock MHz used in the formula (boost; Tesla/Fermi use the shader clock; Intel iGPU: EU x 8 lanes, EU x 4 on Gen6).
    private const string Data = """
Tesla|8400 gs|8|1400
Tesla|8500 gt|16|900
Tesla|8600 gt|32|1190
Tesla|8600 gts|32|1450
Tesla|8800 gs|96|1375
Tesla|8800 gt|112|1500
Tesla|8800 gts|96|1188
Tesla|8800 gts 512|128|1625
Tesla|8800 gtx|128|1350
Tesla|8800 ultra|128|1512
Tesla|9400 gt|16|1400
Tesla|9500 gt|32|1400
Tesla|9600 gt|64|1625
Tesla|9600 gso|96|1625
Tesla|9800 gt|112|1500
Tesla|9800 gtx|128|1688
Tesla|9800 gtx+|128|1836
Tesla|gts 250|128|1836
Tesla|gt 220|48|1360
Tesla|gt 240|96|1340
Tesla|gtx 260|216|1242
Tesla|gtx 275|240|1404
Tesla|gtx 280|240|1296
Tesla|gtx 285|240|1476
Fermi|gt 420|48|1400
Fermi|gt 430|96|1400
Fermi|gt 440|96|1620
Fermi|gts 450|192|1566
Fermi|gtx 460|336|1350
Fermi|gtx 465|352|1215
Fermi|gtx 470|448|1215
Fermi|gtx 480|480|1401
Fermi|gtx 550 ti|192|1800
Fermi|gtx 560|336|1620
Fermi|gtx 560 ti|384|1645
Fermi|gtx 560 ti 448|448|1464
Fermi|gtx 570|480|1464
Fermi|gtx 580|512|1544
Fermi|gt 520|48|1620
Fermi|gt 610|48|1620
Fermi|gt 620|96|1400
Fermi|gt 630|96|1620
Kepler|gt 640|384|900
Kepler|gt 710|192|954
Kepler|gt 740|384|993
Kepler|gtx 650|384|1058
Kepler|gtx 650 ti|768|928
Kepler|gtx 650 ti boost|768|1033
Kepler|gtx 660|960|1033
Kepler|gtx 660 ti|1344|1058
Kepler|gtx 670|1344|1058
Kepler|gtx 680|1536|1058
Kepler|gtx 760|1152|1033
Kepler|gtx 770|1536|1085
Kepler|gtx 780|2304|900
Kepler|gtx 780 ti|2880|928
Maxwell|gtx 750|512|1163
Maxwell|gtx 750 ti|640|1085
Maxwell|gtx 950|768|1329
Maxwell|gtx 960|1024|1178
Maxwell|gtx 970|1664|1178
Maxwell|gtx 980|2048|1216
Maxwell|gtx 980 ti|2816|1075
Maxwell|gtx 950m|640|1124
Maxwell|gtx 960m|640|1176
Maxwell|gtx 965m|1024|1150
Maxwell|gtx 970m|1280|1038
Maxwell|gtx 980m|1536|1127
Maxwell|940mx|384|1242
Pascal|gt 1030|384|1468
Pascal|gtx 1050|640|1455
Pascal|gtx 1050 ti|768|1392
Pascal|gtx 1060|1280|1708
Pascal|gtx 1070|1920|1683
Pascal|gtx 1070 ti|2432|1683
Pascal|gtx 1080|2560|1733
Pascal|gtx 1080 ti|3584|1582
Pascal|gtx 1050 ti mobile|768|1392
Pascal|gtx 1060 mobile|1280|1670
Pascal|gtx 1070 mobile|2048|1645
Pascal|gtx 1080 mobile|2560|1733
Pascal|mx150|384|1532
Turing|gtx 1650|896|1665
Turing|gtx 1650 super|1280|1725
Turing|gtx 1660|1408|1785
Turing|gtx 1660 super|1408|1785
Turing|gtx 1660 ti|1536|1770
Turing|rtx 2060|1920|1680
Turing|rtx 2060 super|2176|1650
Turing|rtx 2070|2304|1620
Turing|rtx 2070 super|2560|1770
Turing|rtx 2080|2944|1710
Turing|rtx 2080 super|3072|1815
Turing|rtx 2080 ti|4352|1545
Ampere|rtx 3050|2560|1777
Ampere|rtx 3060|3584|1777
Ampere|rtx 3060 ti|4864|1665
Ampere|rtx 3070|5888|1725
Ampere|rtx 3070 ti|6144|1770
Ampere|rtx 3080|8704|1710
Ampere|rtx 3080 ti|10240|1665
Ampere|rtx 3090|10496|1695
Ampere|rtx 3090 ti|10752|1860
Ada|rtx 4060|3072|2460
Ada|rtx 4060 ti|4352|2535
Ada|rtx 4070|5888|2475
Ada|rtx 4070 super|7168|2475
Ada|rtx 4070 ti|7680|2610
Ada|rtx 4070 ti super|8448|2610
Ada|rtx 4080|9728|2505
Ada|rtx 4080 super|10240|2550
Ada|rtx 4090|16384|2520
Blackwell|rtx 5060|3840|2497
Blackwell|rtx 5060 ti|4608|2572
Blackwell|rtx 5070|6144|2512
Blackwell|rtx 5070 ti|8960|2452
Blackwell|rtx 5080|10752|2617
Blackwell|rtx 5090|21760|2407
TeraScale|hd 2600 xt|120|800
TeraScale|hd 2900 xt|320|742
TeraScale|hd 3850|320|670
TeraScale|hd 3870|320|775
TeraScale|hd 4350|80|600
TeraScale|hd 4650|320|600
TeraScale|hd 4670|320|750
TeraScale|hd 4770|640|750
TeraScale|hd 4850|800|625
TeraScale|hd 4870|800|750
TeraScale|hd 4890|800|850
TeraScale|hd 5450|80|650
TeraScale|hd 5550|320|550
TeraScale|hd 5570|400|650
TeraScale|hd 5670|400|775
TeraScale|hd 5750|720|700
TeraScale|hd 5770|800|850
TeraScale|hd 5830|1120|800
TeraScale|hd 5850|1440|725
TeraScale|hd 5870|1600|850
TeraScale|hd 6450|160|625
TeraScale|hd 6570|480|650
TeraScale|hd 6670|480|800
TeraScale|hd 6750|720|700
TeraScale|hd 6770|800|850
TeraScale|hd 6850|960|775
TeraScale|hd 6870|1120|900
TeraScale|hd 6950|1408|800
TeraScale|hd 6970|1536|880
GCN1|hd 7750|512|800
GCN1|hd 7770|640|1000
GCN1|hd 7850|1024|860
GCN1|hd 7870|1280|1000
GCN1|hd 7950|1792|850
GCN1|hd 7970|2048|925
GCN1|r7 240|320|780
GCN1|r7 250|384|1000
GCN1|r7 250x|640|1000
GCN1|r7 260|768|1000
GCN1|r7 260x|896|1100
GCN1|r7 360|768|1050
GCN1|r7 370|1024|985
GCN1|r9 270|1280|925
GCN1|r9 270x|1280|1050
GCN1|r9 280|1792|933
GCN1|r9 280x|2048|1000
GCN1|r9 285|1792|918
GCN1|r9 290|2560|947
GCN1|r9 290x|2816|1000
GCN1|r9 380|1792|970
GCN1|r9 380x|2048|970
GCN1|r9 390|2560|1000
GCN1|r9 390x|2816|1050
GCN1|r9 fury|3584|1000
GCN1|r9 fury x|4096|1050
GCN|rx 460|896|1200
GCN|rx 470|2048|1206
GCN|rx 480|2304|1266
GCN|rx 550|512|1183
GCN|rx 560|1024|1275
GCN|rx 570|2048|1244
GCN|rx 580|2304|1340
GCN|rx 590|2304|1545
GCN|vega 56|3584|1471
GCN|vega 64|4096|1546
GCN|vii|3840|1750
GCN|vega 8|512|1200
GCN|vega 11|704|1300
RDNA|rx 5500 xt|1408|1845
RDNA|rx 5600 xt|2304|1560
RDNA|rx 5700|2304|1725
RDNA|rx 5700 xt|2560|1905
RDNA2|rx 6400|768|2321
RDNA2|rx 6500 xt|1024|2815
RDNA2|rx 6600|1792|2491
RDNA2|rx 6600 xt|2048|2589
RDNA2|rx 6650 xt|2048|2635
RDNA2|rx 6700 xt|2560|2581
RDNA2|rx 6750 xt|2560|2600
RDNA2|rx 6800|3840|2105
RDNA2|rx 6800 xt|4608|2250
RDNA2|rx 6900 xt|5120|2250
RDNA2|rx 6950 xt|5120|2310
RDNA2|680m|768|2200
RDNA2|660m|384|1900
RDNA2|610m|128|1900
RDNA3|rx 7600|2048|2655
RDNA3|rx 7600 xt|2048|2755
RDNA3|rx 7700 xt|3456|2544
RDNA3|rx 7800 xt|3840|2430
RDNA3|rx 7900 gre|5120|2245
RDNA3|rx 7900 xt|5376|2400
RDNA3|rx 7900 xtx|6144|2500
RDNA3|780m|768|2700
RDNA3|760m|512|2600
RDNA3|880m|768|2900
RDNA3|890m|1024|2900
RDNA4|rx 9070|3584|2520
RDNA4|rx 9070 xt|4096|2970
IntelGen6|hd graphics 2000|24|1350
IntelGen6|hd graphics 3000|48|1350
IntelGen7|hd graphics 2500|48|1150
IntelGen7|hd graphics 4000|128|1150
IntelGen7|hd graphics 4600|160|1250
IntelGen7|hd graphics 5500|192|950
IntelGen9|hd graphics 520|192|1050
IntelGen9|hd graphics 530|192|1150
IntelGen9|hd graphics 620|192|1100
IntelGen9|hd graphics 630|192|1150
IntelGen9|uhd graphics 620|192|1150
IntelGen9|uhd graphics 630|192|1150
IntelGen9|iris plus 650|384|1150
IntelGen11|iris plus g7|512|1100
IntelGen11|iris plus g4|384|1050
XeLP|iris xe 80eu|640|1300
XeLP|iris xe 96eu|768|1300
XeLP|iris xe 48eu|384|1250
XeLP|uhd graphics 770|256|1550
Arc|arc a380|1024|2450
Arc|arc a580|3072|2400
Arc|arc a750|3584|2400
Arc|arc a770|4096|2400
Battlemage|arc b570|2304|2500
Battlemage|arc b580|2560|2670
""";

    // index per GFLOPS. Fitted on anchors: Blackwell, Ada, Ampere, Turing, RDNA, RDNA2, RDNA3, RDNA4, Pascal, Maxwell, GCN, Arc, Battlemage, XeLP, IntelGen9, IntelGen11.
    // Chained (no anchor): GCN1 (HD 7000, R7/R9 200/300, Fury), Kepler, Fermi, Tesla, TeraScale, IntelGen6/7. See the doc.
    private static readonly Dictionary<string, double> Factor = new()
    {
        ["Blackwell"] = 3.32, ["Ada"] = 3.19, ["Ampere"] = 3.32, ["Turing"] = 6.03, ["RDNA4"] = 5.92, ["RDNA3"] = 4.65, ["RDNA2"] = 5.17, ["RDNA"] = 5.16,
        ["Battlemage"] = 4.17, ["Arc"] = 3.0, ["Pascal"] = 5.07, ["Maxwell"] = 4.84, ["GCN"] = 4.84, ["GCN1"] = 4.0, ["XeLP"] = 4.07, ["IntelGen9"] = 3.58, ["IntelGen11"] = 3.11,
        ["Kepler"] = 4.4, ["Fermi"] = 5.0, ["Tesla"] = 4.9, ["TeraScale"] = 2.7, ["IntelGen7"] = 2.5, ["IntelGen6"] = 2.5,
    };

    /// <summary>Public PCI vendor:device ids whose model is unambiguous. Intel iGPUs map to a typical configuration of the family (EU count varies per CPU: estimate).</summary>
    private static readonly Dictionary<string, string> Pci = new(StringComparer.OrdinalIgnoreCase)
    {
        ["8086:9A49"] = "iris xe 80eu", ["8086:9A40"] = "iris xe 80eu", ["8086:46A6"] = "iris xe 96eu", ["8086:46A8"] = "iris xe 96eu", ["8086:4680"] = "uhd graphics 770",
        ["8086:A780"] = "uhd graphics 770", ["8086:3E92"] = "uhd graphics 630", ["8086:3E9B"] = "uhd graphics 630", ["8086:5912"] = "hd graphics 630", ["8086:591B"] = "hd graphics 630",
        ["8086:5916"] = "hd graphics 620", ["8086:5917"] = "uhd graphics 620", ["8086:1912"] = "hd graphics 530", ["8086:191B"] = "hd graphics 530", ["8086:1916"] = "hd graphics 520",
        ["8086:0412"] = "hd graphics 4600", ["8086:0416"] = "hd graphics 4600", ["8086:0166"] = "hd graphics 4000", ["8086:0162"] = "hd graphics 4000", ["8086:0152"] = "hd graphics 2500",
        ["8086:0126"] = "hd graphics 3000", ["8086:8A52"] = "iris plus g7", ["8086:56A0"] = "arc a770", ["8086:56A1"] = "arc a750", ["8086:56A5"] = "arc a380",
        ["8086:E20B"] = "arc b580", ["8086:E20C"] = "arc b570",
        ["1002:731F"] = "rx 5700 xt", ["1002:73DF"] = "rx 6700 xt", ["1002:15D8"] = "vega 8",
        ["10DE:1C03"] = "gtx 1060", ["10DE:1B81"] = "gtx 1070", ["10DE:1B80"] = "gtx 1080", ["10DE:1B06"] = "gtx 1080 ti", ["10DE:1C82"] = "gtx 1050 ti", ["10DE:1C81"] = "gtx 1050",
        ["10DE:1D01"] = "gt 1030", ["10DE:1F82"] = "gtx 1650", ["10DE:2184"] = "gtx 1660", ["10DE:21C4"] = "gtx 1660 super", ["10DE:2182"] = "gtx 1660 ti", ["10DE:1F08"] = "rtx 2060",
        ["10DE:1F06"] = "rtx 2060 super", ["10DE:1F02"] = "rtx 2070", ["10DE:1E84"] = "rtx 2070 super", ["10DE:1E87"] = "rtx 2080", ["10DE:1E07"] = "rtx 2080 ti",
        ["10DE:2503"] = "rtx 3060", ["10DE:2504"] = "rtx 3060", ["10DE:2486"] = "rtx 3060 ti", ["10DE:2484"] = "rtx 3070", ["10DE:2206"] = "rtx 3080", ["10DE:2204"] = "rtx 3090",
        ["10DE:2684"] = "rtx 4090", ["10DE:2704"] = "rtx 4080", ["10DE:2782"] = "rtx 4070 ti", ["10DE:2786"] = "rtx 4070", ["10DE:2803"] = "rtx 4060 ti", ["10DE:2882"] = "rtx 4060",
        ["10DE:2B85"] = "rtx 5090", ["10DE:2C02"] = "rtx 5080", ["10DE:2C05"] = "rtx 5070 ti", ["10DE:2F04"] = "rtx 5070", ["10DE:2D04"] = "rtx 5060 ti", ["10DE:2D05"] = "rtx 5060",
        ["10DE:134D"] = "940mx", ["10DE:1401"] = "gtx 960", ["10DE:13C2"] = "gtx 970", ["10DE:13C0"] = "gtx 980", ["10DE:17C8"] = "gtx 980 ti", ["10DE:1380"] = "gtx 750 ti",
        ["10DE:1381"] = "gtx 750", ["10DE:1200"] = "gtx 560 ti", ["10DE:1081"] = "gtx 570", ["10DE:1080"] = "gtx 580", ["10DE:0E22"] = "gtx 460", ["10DE:06C0"] = "gtx 480",
    };

    private static readonly Dictionary<string, (string Arch, int Shaders, int Mhz)> Table = Load();

    /// <summary>Number of GPUs in the specification table (for docs and tests).</summary>
    public static int Count => Table.Count;
    public static int PciCount => Pci.Count;

    private static Dictionary<string, (string, int, int)> Load()
    {
        var d = new Dictionary<string, (string, int, int)>(StringComparer.Ordinal);
        foreach (var line in Data.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split('|');
            if (p.Length == 4 && Factor.ContainsKey(p[0])) d[p[1]] = (p[0], int.Parse(p[2], CultureInfo.InvariantCulture), int.Parse(p[3], CultureInfo.InvariantCulture));
        }
        return d;
    }

    private static Rating Spec(string key, RatingBasis basis)
    {
        var (arch, sh, mhz) = Table[key];
        return new(key, Math.Round(sh * 2.0 * mhz / 1e6 * Factor[arch], 2), basis);
    }

    /// <summary>Index computed from the specification table only, ignoring the catalog anchors (used to measure the fit error).</summary>
    public static double? SpecIndex(string name) => Norm(name) is { Length: > 0 } n && Table.ContainsKey(n) ? Spec(n, RatingBasis.Spec).Index : null;

    public static string Strip(string s) => Regex.Replace(s.Replace("®", "").Replace("™", "").Replace("©", ""), @"\((?:R|TM|C)\)", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "nvidia", "geforce", "amd", "ati", "radeon", "intel", "mesa", "corporation", "series", "card", "gpu", "or", "ou", "better", "equivalent", "equal", "higher", "above", "up",
        "superior", "melhor", "equivalente", "similar", "compatible", "compativel", "and", "with", "de", "dedicated", "family", "edition", "video", "vram", "dx9", "dx10", "dx11", "dx12",
    };

    /// <summary>Lower-case, vendor words and memory sizes removed, "GTX1060" → "gtx 1060", "8800GTS" → "8800 gts"; null/empty when nothing is left.</summary>
    public static string Norm(string name)
    {
        string s = Strip(CanRunCompare.GpuLabel(name)).ToLowerInvariant();
        s = Regex.Replace(s, @"\([^)]*\)", " ");
        s = Regex.Replace(s, @"\b\d{1,3}\s*(?:gb|mb)\b", " ");
        s = Regex.Replace(s, @"[^a-z0-9+ ]", " ");
        s = Regex.Replace(s, @"\b(gtx|gts|gt|rtx|rx|hd|mx)(?=\d)", "$1 ");
        s = Regex.Replace(s, @"(?<=\d)(gts|gtx|ti|xt|xtx|gre|super)\b", " $1");
        s = s.Replace("rx vega", "vega");
        var t = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => !Noise.Contains(x)).ToList();
        if (t.Count > 0 && t[^1] == "graphics") t.RemoveAt(t.Count - 1);
        return string.Join(' ', t);
    }

    // Families too old for the table. Only used on the requirement side, as a ceiling.
    private static readonly (Regex Rx, string Label, double Cap)[] OldFamilies =
    [
        (new(@"^(?:g[t]s? |gtx )?[89]\d{3}\b|^g(?:t|ts|tx) 2\d\d\b", RegexOptions.CultureInvariant), "GeForce 8/9/200", 4),
        (new(@"^g(?:t|ts|tx) [45]\d\d\b", RegexOptions.CultureInvariant), "GeForce 400/500", 8),
        (new(@"^hd [2-6]\d{3}\b", RegexOptions.CultureInvariant), "Radeon HD 2000–6000", 8),
    ];

    /// <summary>Rating of a GPU (this PC when <paramref name="pciId"/> is given as "VVVV:DDDD", or a requirement text).</summary>
    public static Rating? Rate(string name, string? pciId = null, bool requirement = false)
    {
        pciId = pciId?.Replace("0x", "", StringComparison.OrdinalIgnoreCase).Trim().ToUpperInvariant();
        string clean = Strip(CanRunCompare.GpuLabel(name));
        if (clean.Length > 0 && GameCatalog.GpuMatchWhole(clean) is { } m) return new(m.Key, m.Index, RatingBasis.Model);
        string n = Norm(name);
        if (n.Length > 0 && Table.ContainsKey(n)) return Spec(n, RatingBasis.Spec);
        if (pciId is not null)
        {
            if (Pci.TryGetValue(pciId, out var k) && Table.ContainsKey(k))
                return Spec(k, Table[k].Arch.StartsWith("Intel", StringComparison.Ordinal) || k.StartsWith("iris xe", StringComparison.Ordinal) ? RatingBasis.Family : RatingBasis.Spec);
            var parts = pciId.Split(':');
            if (parts.Length == 2 && GpuSpecDb.Find(parts[0], parts[1], name) is { Shaders: > 0, BoostMHz: > 0 } s && ArchOf(s.Arch) is { } a)
                return new($"{s.Chip}", Math.Round(s.Shaders * 2.0 * s.BoostMHz / 1e6 * Factor[a], 2), RatingBasis.Spec);
        }
        if (requirement)
            foreach (var (rx, label, cap) in OldFamilies)
                if (rx.IsMatch(n)) return new(label, cap, RatingBasis.Family, true);
        return null;
    }

    private static string? ArchOf(string specDbArch) => specDbArch switch
    {
        "Ada Lovelace" => "Ada", "RDNA 4" => "RDNA4", "RDNA 3" => "RDNA3", "RDNA 2" => "RDNA2", "RDNA 1" => "RDNA", var x when Factor.ContainsKey(x) => x, _ => null,
    };
}

/// <summary>
/// CPU index: IPC of the microarchitecture (Skylake = 1) x boost GHz x cores^0.2 (P cores + half the E cores), times a constant, on the scale of
/// <see cref="GameCatalog"/> (Ryzen 7 7700X = 100). The exponent and the constant are fitted on the desktop anchors of the catalog; mobile parts apply a class
/// factor on the desktop part of the same generation and tier. A requirement given as "family + GHz" uses the same formula with the clock the game states.
/// </summary>
public static class CpuIndexer
{
    private const double K = 8.6, Alpha = 0.2;

    // IPC relative to Skylake. Zen..LionCove and Haswell are checked against the anchors; the others are published relative-IPC figures (approximate, no anchors).
    private static readonly Dictionary<string, double> Ipc = new()
    {
        ["NetBurst"] = 0.33, ["K8"] = 0.52, ["Core2"] = 0.60, ["K10"] = 0.60, ["Bulldozer"] = 0.55, ["Piledriver"] = 0.60, ["Steamroller"] = 0.64, ["Nehalem"] = 0.69,
        ["Sandy"] = 0.79, ["Ivy"] = 0.83, ["Haswell"] = 0.90, ["Broadwell"] = 0.95, ["Skylake"] = 1.0, ["Rocket"] = 1.18, ["Golden"] = 1.40, ["Raptor"] = 1.45,
        ["Redwood"] = 1.40, ["LionCove"] = 1.52, ["Zen"] = 1.0, ["Zen+"] = 1.03, ["Zen2"] = 1.15, ["Zen3"] = 1.30, ["Zen4"] = 1.43, ["Zen5"] = 1.58,
    };

    public static double Score(string arch, double ghz, double cores) => K * Ipc[arch] * ghz * Math.Pow(Math.Max(cores, 1), Alpha);

    // Typical desktop part per Core generation and tier: arch, P cores, E cores, boost GHz.
    private static readonly Dictionary<(int Gen, int Tier), (string Arch, int P, int E, double Ghz)> Intel = new()
    {
        [(1, 3)] = ("Nehalem", 2, 0, 3.2), [(1, 5)] = ("Nehalem", 4, 0, 3.2), [(1, 7)] = ("Nehalem", 4, 0, 3.4),
        [(2, 3)] = ("Sandy", 2, 0, 3.3), [(2, 5)] = ("Sandy", 4, 0, 3.6), [(2, 7)] = ("Sandy", 4, 0, 3.8),
        [(3, 3)] = ("Ivy", 2, 0, 3.4), [(3, 5)] = ("Ivy", 4, 0, 3.7), [(3, 7)] = ("Ivy", 4, 0, 3.9),
        [(4, 3)] = ("Haswell", 2, 0, 3.7), [(4, 5)] = ("Haswell", 4, 0, 3.7), [(4, 7)] = ("Haswell", 4, 0, 4.0),
        [(5, 3)] = ("Broadwell", 2, 0, 3.4), [(5, 5)] = ("Broadwell", 4, 0, 3.4), [(5, 7)] = ("Broadwell", 4, 0, 3.6),
        [(6, 3)] = ("Skylake", 2, 0, 3.9), [(6, 5)] = ("Skylake", 4, 0, 3.6), [(6, 7)] = ("Skylake", 4, 0, 4.0),
        [(7, 3)] = ("Skylake", 2, 0, 3.9), [(7, 5)] = ("Skylake", 4, 0, 3.8), [(7, 7)] = ("Skylake", 4, 0, 4.2),
        [(8, 3)] = ("Skylake", 4, 0, 3.6), [(8, 5)] = ("Skylake", 6, 0, 4.0), [(8, 7)] = ("Skylake", 6, 0, 4.6),
        [(9, 3)] = ("Skylake", 4, 0, 4.2), [(9, 5)] = ("Skylake", 6, 0, 4.1), [(9, 7)] = ("Skylake", 8, 0, 4.7), [(9, 9)] = ("Skylake", 8, 0, 5.0),
        [(10, 3)] = ("Skylake", 4, 0, 4.3), [(10, 5)] = ("Skylake", 6, 0, 4.3), [(10, 7)] = ("Skylake", 8, 0, 4.8), [(10, 9)] = ("Skylake", 10, 0, 5.2),
        [(11, 3)] = ("Skylake", 4, 0, 4.0), [(11, 5)] = ("Rocket", 6, 0, 4.4), [(11, 7)] = ("Rocket", 8, 0, 4.9), [(11, 9)] = ("Rocket", 8, 0, 5.2),
        [(12, 3)] = ("Golden", 4, 0, 4.3), [(12, 5)] = ("Golden", 6, 0, 4.4), [(12, 7)] = ("Golden", 8, 4, 4.9), [(12, 9)] = ("Golden", 8, 8, 5.2),
        [(13, 3)] = ("Raptor", 4, 0, 4.5), [(13, 5)] = ("Raptor", 6, 4, 4.6), [(13, 7)] = ("Raptor", 8, 8, 5.2), [(13, 9)] = ("Raptor", 8, 16, 5.6),
        [(14, 3)] = ("Raptor", 4, 0, 4.7), [(14, 5)] = ("Raptor", 6, 4, 4.7), [(14, 7)] = ("Raptor", 8, 12, 5.4), [(14, 9)] = ("Raptor", 8, 16, 5.8),
    };

    // Core Ultra: series 1 = Meteor Lake (mobile), series 2 = Arrow Lake.
    private static readonly Dictionary<(int Series, int Tier), (string Arch, int P, int E, double Ghz)> Ultra = new()
    {
        [(1, 5)] = ("Redwood", 4, 8, 4.5), [(1, 7)] = ("Redwood", 6, 8, 4.8), [(1, 9)] = ("Redwood", 6, 8, 5.1),
        [(2, 5)] = ("LionCove", 6, 8, 5.0), [(2, 7)] = ("LionCove", 8, 12, 5.3), [(2, 9)] = ("LionCove", 8, 16, 5.6),
    };

    private static readonly Regex Ghz = new(@"(\d{1,2}(?:[.,]\d{1,3})?)\s*ghz", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Mhz = new(@"(?<![\d.,])(\d{3,4})\s*mhz", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CoreWord = new(@"\b(single|dual|duo|triple|tri|quad|hexa|six|octa|eight)[- ]?core", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CoreNum = new(@"(?<![\d.])(\d{1,2})[- ]?cores?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UltraRx = new(@"core\s*ultra\s*([3579])\s*(?:processor\s*)?(\d{3})([a-z]{0,2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CoreI = new(@"\bi([3579])[- ]?(\d{3,5})([a-z]{0,3}\d?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CoreIGeneric = new(@"\bcore\s*i([3579])\b(?!-\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Ryzen = new(@"ryzen\s*([3579])\s*(?:pro\s*)?(\d)(\d{3})([a-z0-9]{0,4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RyzenGeneric = new(@"ryzen\s*([3579])\b(?!\s*(?:pro\s*)?\d{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Fx = new(@"\bfx[- ]?(\d)(\d)(\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static double? ClockGhz(string t)
    {
        if (Ghz.Match(t) is { Success: true } g && double.TryParse(g.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v is > 0.3 and < 8) return v;
        if (Mhz.Match(t) is { Success: true } m && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double mh) && mh is >= 300 and <= 7000) return mh / 1000;
        return null;
    }

    private static int? CoresIn(string t)
    {
        if (CoreWord.Match(t) is { Success: true } w)
            return w.Groups[1].Value.ToLowerInvariant() switch { "single" => 1, "dual" or "duo" => 2, "triple" or "tri" => 3, "quad" => 4, "hexa" or "six" => 6, _ => 8 };
        if (CoreNum.Match(t) is { Success: true } n && int.TryParse(n.Groups[1].Value, out int c) && c is >= 1 and <= 64) return c;
        return null;
    }

    private static string G(double v) => v.ToString("0.0#", CultureInfo.InvariantCulture) + " GHz";

    private static Rating Make(string label, string arch, double ghz, double cores, RatingBasis basis) => new(label, Math.Round(Score(arch, ghz, cores), 1), basis);

    private static double MobileFactor(string suffix, int gen)
    {
        string s = suffix.ToUpperInvariant();
        if (s.StartsWith('G') && s.Length == 2 && char.IsDigit(s[1])) return 0.82;
        if (s.Length >= 2 && s[^1] == 'M' || s == "M") return 0.78;
        return s switch
        {
            "U" or "UE" => 0.80, "Y" => 0.45, "P" => 0.70, "HX" => 0.95, "H" => gen >= 12 ? 0.86 : 0.91, "HQ" or "HK" => 0.91, "T" or "TE" => 0.85, _ => 1.0,
        };
    }

    /// <summary>Rating of a processor name (this PC) or a requirement text. Exact anchors first; then a model-number family; then family + stated GHz; null when unknown.</summary>
    public static Rating? Rate(string text)
    {
        string t = GpuIndexer.Strip(text);
        t = Regex.Replace(t, @"core\s*2", "core 2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        t = Regex.Replace(t, @"^[\s:;,\-–]+", "");
        if (t.Length == 0) return null;
        if (GameCatalog.CpuMatchWhole(t) is { } m) return new(m.Key, m.Index, RatingBasis.Model);
        double? clock = ClockGhz(t); int? cores = CoresIn(t);
        string low = t.ToLowerInvariant();

        if (UltraRx.Match(t) is { Success: true } u)
        {
            int tier = int.Parse(u.Groups[1].Value, CultureInfo.InvariantCulture), series = int.Parse(u.Groups[2].Value[..1], CultureInfo.InvariantCulture);
            if (!Ultra.TryGetValue((series, tier), out var c)) return null;
            double f = MobileFactor(u.Groups[3].Value, 12);
            return Make($"Core Ultra {tier} {u.Groups[2].Value}{u.Groups[3].Value.ToUpperInvariant()}", c.Arch, c.Ghz, c.P + 0.5 * c.E, RatingBasis.Family) with { Index = Math.Round(Score(c.Arch, c.Ghz, c.P + 0.5 * c.E) * f, 1) };
        }
        if (CoreI.Match(t) is { Success: true } i)
        {
            int tier = int.Parse(i.Groups[1].Value, CultureInfo.InvariantCulture); string num = i.Groups[2].Value;
            int gen = num.Length switch { 3 => 1, 4 => num[0] == '1' ? int.Parse(num[..2], CultureInfo.InvariantCulture) : num[0] - '0', _ => int.Parse(num[..2], CultureInfo.InvariantCulture) };
            if (!Intel.TryGetValue((gen, tier), out var c)) return null;
            double f = MobileFactor(i.Groups[3].Value, gen);
            return new($"Core i{tier} gen {gen}", Math.Round(Score(c.Arch, c.Ghz, c.P + 0.5 * c.E) * f, 1), RatingBasis.Family);
        }
        if (Ryzen.Match(t) is { Success: true } r)
        {
            int tier = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture), series = int.Parse(r.Groups[2].Value, CultureInfo.InvariantCulture); string suf = r.Groups[4].Value.ToUpperInvariant();
            if (suf.Contains("X3D") || series is 0 || (tier == 9 && series < 3)) return null;
            bool mobile = suf is "U" or "H" or "HS" or "HX" or "UE";
            string arch = mobile && series == 3 ? "Zen+" : mobile && series == 2 ? "Zen" : series switch { 1 => "Zen", 2 => "Zen+", 3 or 4 => "Zen2", 5 or 6 => "Zen3", 7 or 8 => "Zen4", _ => "Zen5" };
            double ghz = series switch { 1 => 3.7, 2 => 4.2, 3 => 4.4, 4 => 4.2, 5 or 6 => 4.6, 7 => 5.3, 8 => 5.0, _ => 5.5 };
            int n = mobile && series <= 3 ? 4 : tier switch { 3 => 4, 5 => 6, 7 => 8, _ => 12 };
            double f = suf switch { "U" or "UE" => 0.62, "H" or "HS" => 0.83, "HX" => 0.93, "G" or "GE" => 0.92, _ => 1.0 };
            return new($"Ryzen {tier} {series}000", Math.Round(Score(arch, ghz, n) * f, 1), RatingBasis.Family);
        }
        if (Fx.Match(t) is { Success: true } x)
        {
            int n = x.Groups[1].Value switch { "4" => 4, "6" => 6, _ => 8 };
            bool bd = x.Groups[2].Value == "1";
            double ghz = clock ?? (x.Groups[1].Value == "9" ? 4.7 : bd ? 3.8 : x.Groups[1].Value switch { "4" => 4.0, "6" => 4.1, _ => 4.2 });
            return Make($"FX-{x.Groups[1].Value}{x.Groups[2].Value}{x.Groups[3].Value}", bd ? "Bulldozer" : "Piledriver", ghz, n, RatingBasis.Family);
        }
        string? arch2 = null; double? defGhz = null; int? defCores = null; string label = "";
        if (Regex.IsMatch(low, @"core\s*2\s*quad")) { arch2 = "Core2"; defCores = 4; defGhz = 2.4; label = "Core 2 Quad"; }
        else if (Regex.IsMatch(low, @"core\s*2")) { arch2 = "Core2"; defCores = 2; defGhz = 2.4; label = "Core 2 Duo"; }
        else if (Regex.IsMatch(low, @"core\s*(duo|solo)")) { arch2 = "Core2"; defCores = low.Contains("solo") ? 1 : 2; defGhz = 2.0; label = "Core Duo"; }
        else if (Regex.IsMatch(low, @"pentium\s*(d|4|iv)\b")) { arch2 = "NetBurst"; defCores = low.Contains("pentium d") ? 2 : 1; defGhz = 2.8; label = "Pentium 4/D"; }
        else if (Regex.IsMatch(low, @"pentium\s*dual[- ]core")) { arch2 = "Core2"; defCores = 2; defGhz = 2.4; label = "Pentium Dual-Core"; }
        else if (Regex.Match(low, @"phenom\s*ii\s*x([2346])") is { Success: true } p2) { arch2 = "K10"; defCores = p2.Groups[1].Value[0] - '0'; defGhz = 3.0; label = "Phenom II X" + p2.Groups[1].Value; }
        else if (Regex.Match(low, @"phenom\s*x([234])") is { Success: true } p1) { arch2 = "K10"; defCores = p1.Groups[1].Value[0] - '0'; defGhz = 2.4; label = "Phenom X" + p1.Groups[1].Value; }
        else if (Regex.Match(low, @"athlon\s*ii\s*x([234])") is { Success: true } a2) { arch2 = "K10"; defCores = a2.Groups[1].Value[0] - '0'; defGhz = 3.0; label = "Athlon II X" + a2.Groups[1].Value; }
        else if (Regex.Match(low, @"athlon\s*x([24])\s*\d{3}") is { Success: true } a4) { arch2 = "Piledriver"; defCores = a4.Groups[1].Value[0] - '0'; defGhz = 3.5; label = "Athlon X" + a4.Groups[1].Value; }
        else if (Regex.IsMatch(low, @"athlon\s*(64\s*)?x2")) { arch2 = "K8"; defCores = 2; defGhz = 2.4; label = "Athlon X2"; }
        else if (Regex.IsMatch(low, @"athlon\s*\d{3,4}\s*(g|ge|u)\b")) { arch2 = "Zen"; defCores = 2; defGhz = 3.2; label = "Athlon (Zen)"; }
        else if (Regex.IsMatch(low, @"athlon\s*64|athlon\s*xp|sempron")) { arch2 = "K8"; defCores = 1; defGhz = 2.0; label = "Athlon 64"; }
        else if (CoreIGeneric.Match(t) is { Success: true } gi) { int tier = int.Parse(gi.Groups[1].Value, CultureInfo.InvariantCulture); arch2 = "Nehalem"; defCores = tier == 3 ? 2 : 4; defGhz = 2.8; label = "Core i" + tier; }
        else if (RyzenGeneric.Match(t) is { Success: true } rg) { int tier = int.Parse(rg.Groups[1].Value, CultureInfo.InvariantCulture); arch2 = "Zen"; defCores = tier switch { 3 => 4, 5 => 6, 7 => 8, _ => 12 }; defGhz = 3.6; label = "Ryzen " + tier; }
        if (arch2 is not null)
        {
            // The clock the game states is the requirement itself ("spec"); without it the family's lowest typical clock is assumed ("family").
            double ghz = clock ?? defGhz!.Value;
            return Make($"{label} {G(ghz)}", arch2, ghz, cores ?? defCores!.Value, clock is not null ? RatingBasis.Spec : RatingBasis.Family);
        }
        // Only a clock (and maybe a core count): oldest typical IPC, so the requirement is never overstated.
        if (clock is { } gg && (cores is not null || Regex.IsMatch(low, @"processor|cpu|processador|ghz|mhz")))
            return Make($"{cores ?? 1} {(cores is 1 or null ? "core" : "cores")} {G(gg)}", "Core2", gg, cores ?? 1, RatingBasis.Family);
        return null;
    }
}
