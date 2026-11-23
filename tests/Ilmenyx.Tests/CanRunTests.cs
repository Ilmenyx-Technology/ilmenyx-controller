// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Synthetic requirement texts and fake HTTP handlers only: no network, no real hardware.
using System.Net;
using System.Text;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class CanRunTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-canrun-" + Guid.NewGuid().ToString("N"));
    public CanRunTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private const string EnMin = "<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li><strong>Requires a 64-bit processor and operating system</strong><br></li>"
        + "<li><strong>OS *:</strong> Windows 10 64-bit<br></li><li><strong>Processor:</strong> Intel Core i5-8400 or AMD Ryzen 5 1600<br></li>"
        + "<li><strong>Memory:</strong> 8 GB RAM<br></li><li><strong>Graphics:</strong> NVIDIA GeForce GTX 1060 6GB or AMD Radeon RX 580 8GB<br></li>"
        + "<li><strong>DirectX:</strong> Version 12<br></li><li><strong>Storage:</strong> 50 GB available space<br></li><li><strong>Additional Notes:</strong> SSD recommended<br></li></ul>";
    private const string EnRec = "<strong>Recommended:</strong><br><ul class=\"bb_ul\"><li><strong>OS *:</strong> Windows 11<br></li>"
        + "<li><strong>Processor:</strong> Intel Core i7-8700K / AMD Ryzen 5 3600<br></li><li><strong>Memory:</strong> 16 GB RAM<br></li>"
        + "<li><strong>Graphics:</strong> NVIDIA GeForce RTX 3070 / AMD Radeon RX 6800 XT (8 GB VRAM)<br></li><li><strong>Storage:</strong> 50 GB available space<br></li></ul>";

    [Fact]
    public void EnglishBlockIsReadItemByItem()
    {
        var r = GameRequirementsParser.Parse(EnMin);
        Assert.Equal("Windows 10 64-bit", r.Os); Assert.Equal(["Intel Core i5-8400", "AMD Ryzen 5 1600"], r.CpuAlternatives);
        Assert.Equal(8, r.RamGb); Assert.Equal(50, r.StorageGb); Assert.Equal(12, r.DirectX); Assert.Equal(6, r.VramGb);
        Assert.Equal(2, r.GpuAlternatives.Count); Assert.Contains("GTX 1060", r.GpuAlternatives[0]); Assert.DoesNotContain("6GB", r.GpuAlternatives[0]);
        Assert.Contains("Requires a 64-bit processor and operating system", r.Unrecognised); Assert.Contains(r.Unrecognised, u => u.StartsWith("Additional Notes"));
        var rec = GameRequirementsParser.Parse(EnRec);
        Assert.Equal(["Intel Core i7-8700K", "AMD Ryzen 5 3600"], rec.CpuAlternatives); Assert.Equal(16, rec.RamGb); Assert.Equal(8, rec.VramGb);
        Assert.DoesNotContain("Minimum", r.Text); Assert.DoesNotContain("<", r.Text);
    }

    [Fact]
    public void PortugueseBlockWithCommasMegabytesAndTerabytes()
    {
        var r = GameRequirementsParser.Parse("<strong>Mínimos:</strong><br><ul><li><strong>SO:</strong> Windows 10<br></li><li><strong>Processador:</strong> AMD Ryzen 5 2600 ou Intel Core i5-8400<br></li>"
            + "<li><strong>Memória:</strong> 4096 MB de RAM<br></li><li><strong>Placa de vídeo:</strong> GeForce GTX 970 (4 GB de VRAM)<br></li><li><strong>DirectX:</strong> Versão 11<br></li>"
            + "<li><strong>Armazenamento:</strong> 1,5 GB de espaço disponível<br></li></ul>");
        Assert.Equal(["AMD Ryzen 5 2600", "Intel Core i5-8400"], r.CpuAlternatives); Assert.Equal(4, r.RamGb); Assert.Equal(4, r.VramGb); Assert.Equal(11, r.DirectX); Assert.Equal(1.5, r.StorageGb);
        Assert.Equal(["GeForce GTX 970"], r.GpuAlternatives);
        Assert.Equal(1024, GameRequirementsParser.ToGb("1 TB"));
    }

    [Fact]
    public void LooseOneLineTextStillYieldsMemoryAndApisAndKeepsTheRest()
    {
        var r = GameRequirementsParser.Parse("Windows 7, Core 2 Duo 2.4GHz, 2GB RAM, GeForce 8800GT. Requires Vulkan 1.1 or OpenGL 4.5");
        Assert.Equal(2, r.RamGb); Assert.Equal("1.1", r.Vulkan); Assert.Equal("4.5", r.OpenGl); Assert.Empty(r.CpuAlternatives); Assert.Empty(r.GpuAlternatives);
        Assert.NotEmpty(r.Unrecognised);
        Assert.True(GameRequirementsParser.Parse("").IsEmpty); Assert.True(GameRequirementsParser.Parse("  <br> ").IsEmpty);
    }

    [Fact]
    public void HtmlIsOnlyEverTextAndEntitiesAreDecoded()
    {
        var lines = GameRequirementsParser.Lines("<script>alert(1)</script><li><b>OS:</b> Windows&nbsp;10 &amp; newer</li><img src=x onerror=y>");
        Assert.DoesNotContain(lines, l => l.Contains('<')); Assert.Contains("OS: Windows 10 & newer", lines);
    }

    [Theory]
    [InlineData("1245620", "1245620")]
    [InlineData("https://store.steampowered.com/app/1245620/ELDEN_RING/", "1245620")]
    [InlineData("store.steampowered.com/app/570", "570")]
    [InlineData("https://evil.example/app/1245620", null)]
    [InlineData("Elden Ring", null)]
    [InlineData("0", null)]
    [InlineData("12345678901", null)]
    public void AppIdFromNumberOrStoreLink(string input, string? expected) => Assert.Equal(expected, GameRequirementsParser.AppIdFrom(input));

    [Fact]
    public void AppDetailsJsonIsReadAndMalformedShapesAreRejected()
    {
        string json = "{\"42\":{\"success\":true,\"data\":{\"name\":\"Jogo\",\"pc_requirements\":{\"minimum\":\"<li>OS: x</li>\"},\"linux_requirements\":[]}}}";
        var d = GameRequirementsParser.ParseAppDetails(json, "42", DateTimeOffset.UnixEpoch)!;
        Assert.Equal("Jogo", d.Name); Assert.Equal("<li>OS: x</li>", d.PcMinimum); Assert.Equal("", d.PcRecommended); Assert.False(d.HasLinux); Assert.True(d.HasAny);
        Assert.Null(GameRequirementsParser.ParseAppDetails("{\"42\":{\"success\":false}}", "42", DateTimeOffset.UnixEpoch));
        Assert.Null(GameRequirementsParser.ParseAppDetails("{\"42\":{\"success\":true,\"data\":[]}}", "42", DateTimeOffset.UnixEpoch));
        Assert.Null(GameRequirementsParser.ParseAppDetails("not json", "42", DateTimeOffset.UnixEpoch));
        Assert.False(GameRequirementsParser.ParseAppDetails("{\"1\":{\"success\":true,\"data\":{\"name\":\"x\",\"pc_requirements\":[]}}}", "1", DateTimeOffset.UnixEpoch)!.HasAny);
    }

    [Theory]
    [InlineData("Intel Core i5-11600K", null)]            // "1600" is not inside "11600"
    [InlineData("AMD Ryzen 5 1600", "1600")]
    [InlineData("Intel(R) Core(TM) i5-12400F CPU @ 2.50GHz", "12400")]
    [InlineData("Intel Core i3-9100", null)]               // not in the table: never guessed
    public void CpuMatchesOnlyWholeModelNumbers(string name, string? key) => Assert.Equal(key, GameCatalog.CpuMatchWhole(name)?.Key);

    [Theory]
    [InlineData("NVIDIA GeForce GTX 1650 SUPER", null)]    // SUPER is a different chip than the table's GTX 1650
    [InlineData("NVIDIA GeForce RTX 3060 Ti", "RTX 3060 Ti")]
    [InlineData("NVIDIA GeForce RTX 3070 Laptop GPU", null)]
    [InlineData("NVIDIA GeForce RTX 3060 Laptop GPU", "RTX 3060 Laptop")]
    [InlineData("AMD Radeon Graphics", null)]              // generic iGPU name
    [InlineData("GeForce GTX1060", "GTX 1060")]
    [InlineData("AMD Radeon RX 580", "RX 580")]
    public void GpuMatchesOnlyTheExactChip(string name, string? key) => Assert.Equal(key, GameCatalog.GpuMatchWhole(name)?.Key);

    private static LocalPc Pc(string cpu = "AMD Ryzen 5 5600 6-Core Processor", string gpu = "NVIDIA GeForce RTX 3060", double ramGib = 15.5, double vramGib = 12, bool igpu = false, double? freeGib = 200)
        => new(cpu, gpu, (ulong)(vramGib * 1073741824), igpu, (long)(ramGib * 1073741824), freeGib is { } f ? (long)(f * 1073741824) : null, "home", true);

    private static CanRunItem Item(CanRunReport r, string key) => r.Items.Single(i => i.Key == key);

    [Fact]
    public void ItemsAreJudgedAgainstMinimumAndRecommended()
    {
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(EnMin), GameRequirementsParser.Parse(EnRec), Pc());
        Assert.Equal(ReqStatus.MeetsRec, Item(r, "cpu").Status);   // 73 >= 62
        Assert.Equal(ReqStatus.MeetsMin, Item(r, "gpu").Status);   // 51 >= 24 but < min(70, 90)
        Assert.Equal(ReqStatus.MeetsRec, Item(r, "ram").Status);   // 15.5 GiB reported counts as 16 GB (90 %)
        Assert.Equal(ReqStatus.MeetsRec, Item(r, "vram").Status);  // 12 >= 8
        Assert.Equal(ReqStatus.MeetsRec, Item(r, "storage").Status);
        Assert.Equal(ReqStatus.Unknown, Item(r, "api").Status); Assert.False(Item(r, "api").Counts); Assert.Contains("VKD3D", Item(r, "api").Note);
        Assert.Equal(CanRunVerdict.Minimum, r.Verdict); Assert.Equal("Roda no mínimo", r.Headline);
    }

    [Fact]
    public void BelowMinimumNamesTheItems()
    {
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(EnMin), GameRequirementsParser.Parse(EnRec), Pc(ramGib: 3.8, gpu: "NVIDIA GeForce GT 1030", vramGib: 2, freeGib: 20));
        Assert.Equal(ReqStatus.Below, Item(r, "ram").Status); Assert.Equal(ReqStatus.Below, Item(r, "gpu").Status);
        Assert.Equal(ReqStatus.Below, Item(r, "vram").Status); Assert.Equal(ReqStatus.Below, Item(r, "storage").Status);
        Assert.Equal(CanRunVerdict.BelowMinimum, r.Verdict); Assert.StartsWith("Abaixo do mínimo em: ", r.Headline); Assert.Contains("Memória RAM", r.BelowTitles);
    }

    [Fact]
    public void EverythingAtRecommendedSaysSo()
    {
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(EnMin), GameRequirementsParser.Parse(EnRec), Pc(gpu: "NVIDIA GeForce RTX 4080", vramGib: 16, ramGib: 31));
        Assert.Equal(CanRunVerdict.Recommended, r.Verdict); Assert.Equal("Roda no recomendado", r.Headline);
    }

    [Fact]
    public void ModelsMissingFromTheTableAreNeverGuessed()
    {
        // i3-9100 / FX-8350 are not in the table: with a weak PC the CPU stays "?", not "below". A PC unknown to the table is "?" too.
        var req = GameRequirementsParser.Parse("<li><strong>Processor:</strong> Intel Core i3-9100 or AMD FX-8350</li><li><strong>Memory:</strong> 8 GB RAM</li><li><strong>Graphics:</strong> GeForce GTX 1060</li>");
        var weak = CanRunCompare.Compare(req, GameRequirement.Empty, Pc(cpu: "Intel Core i5-8400"));
        Assert.Equal(ReqStatus.Unknown, Item(weak, "cpu").Status);
        var strange = CanRunCompare.Compare(req, GameRequirement.Empty, Pc(cpu: "Loongson 3A5000", gpu: "Matrox G200eR2"));
        Assert.Equal(ReqStatus.Unknown, Item(strange, "cpu").Status); Assert.Equal(ReqStatus.Unknown, Item(strange, "gpu").Status);
        Assert.Equal(CanRunVerdict.NoData, strange.Verdict); Assert.Equal("Sem dados suficientes", strange.Headline);
    }

    [Fact]
    public void IntegratedGpuNeverGetsAGuessedVram()
    {
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(EnMin), GameRequirement.Empty, Pc(gpu: "Intel Iris Xe", vramGib: 0.25, igpu: true));
        Assert.Equal(ReqStatus.Unknown, Item(r, "vram").Status); Assert.Equal("—", Item(r, "vram").Local);
        var none = CanRunCompare.Compare(GameRequirementsParser.Parse(EnMin), GameRequirement.Empty, Pc(vramGib: 0, freeGib: null));
        Assert.Equal(ReqStatus.Unknown, Item(none, "vram").Status); Assert.Equal(ReqStatus.Unknown, Item(none, "storage").Status);
    }

    [Fact]
    public void OnlyMinimumPublishedNeverClaimsTheRecommended()
    {
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(EnMin), GameRequirement.Empty, Pc(gpu: "NVIDIA GeForce RTX 4080", vramGib: 16, ramGib: 31));
        Assert.Equal(CanRunVerdict.Minimum, r.Verdict);
    }

    [Fact]
    public void WindowsRequirementIsOnlyInformative()
    {
        var r = CanRunCompare.Compare(GameRequirementsParser.Parse(EnMin), GameRequirement.Empty, Pc());
        var os = Item(r, "os"); Assert.False(os.Counts); Assert.Contains("Proton", os.Note);
    }

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public int Calls; public Uri? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; Last = request.RequestUri; return Task.FromResult(f(request)); }
    }

    private static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ClientAsksOnlyTheFixedHostWithTheAppIdAndParsesTheAnswer()
    {
        var h = new Fake(_ => Json("{\"42\":{\"success\":true,\"data\":{\"name\":\"Jogo\",\"pc_requirements\":{\"minimum\":\"<li>OS: x</li>\"}}}}"));
        var (res, data) = await new SteamRequirementsClient(h).FetchAsync("42");
        Assert.Equal(RequirementsFetch.Ok, res); Assert.Equal("Jogo", data!.Name);
        Assert.Equal("store.steampowered.com", h.Last!.Host); Assert.Equal("?appids=42&l=brazilian", h.Last.Query);
    }

    [Fact]
    public async Task ClientRefusesBadIdsRedirectsHugeAnswersAndErrors()
    {
        var h = new Fake(_ => Json("{}"));
        var c = new SteamRequirementsClient(h);
        Assert.Equal(RequirementsFetch.Invalid, (await c.FetchAsync("4 2")).Result); Assert.Equal(RequirementsFetch.Invalid, (await c.FetchAsync("")).Result); Assert.Equal(0, h.Calls);
        Assert.Equal(RequirementsFetch.NotFound, (await c.FetchAsync("7")).Result);
        var redirect = new Fake(_ => { var r = new HttpResponseMessage(HttpStatusCode.Found); r.Headers.Location = new Uri("https://evil.example/x"); return r; });
        Assert.Equal(RequirementsFetch.Offline, (await new SteamRequirementsClient(redirect).FetchAsync("7")).Result); Assert.Equal(1, redirect.Calls);
        var huge = new Fake(_ => Json(new string('x', SteamRequirementsClient.MaxBytes + 10)));
        Assert.Equal(RequirementsFetch.TooLarge, (await new SteamRequirementsClient(huge).FetchAsync("7")).Result);
        var down = new Fake(_ => throw new HttpRequestException("offline"));
        Assert.Equal(RequirementsFetch.Offline, (await new SteamRequirementsClient(down).FetchAsync("7")).Result);
        var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SteamRequirementsClient(new Fake(_ => throw new TaskCanceledException())).FetchAsync("7", cts.Token));
    }

    [Fact]
    public void CacheRoundTripsFindsByNameAndIgnoresJunk()
    {
        var cache = new RequirementsCache(Path.Combine(root, "req"));
        Assert.Empty(cache.Recent()); Assert.Null(cache.Load("42")); Assert.Null(cache.Load("../x"));
        cache.Save(new("42", "Hollow Knight: Silksong", DateTimeOffset.UtcNow, "<li>OS: x</li>", "", "", ""));
        Assert.Equal("<li>OS: x</li>", cache.Load("42")!.PcMinimum); Assert.Equal("42", cache.FindByName("hollow knight - silksong")!.AppId);
        Assert.Null(cache.FindByName("Outro jogo"));
        File.WriteAllText(Path.Combine(root, "req", "99.json"), "{broken"); File.WriteAllText(Path.Combine(root, "req", "7.json"), "{\"AppId\":\"8\"}");
        Assert.Null(cache.Load("99")); Assert.Null(cache.Load("7")); Assert.Single(cache.Recent());
    }
}
