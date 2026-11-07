// New Ilmenyx additions: see LICENSING.md. Manipulador HTTP falso: nenhuma requisição real é feita.
using System.Net;
using System.Net.Http;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class FpsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-fps-" + Guid.NewGuid().ToString("N"));
    public FpsTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls; public HttpRequestMessage? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Interlocked.Increment(ref Calls); Last = request; return Task.FromResult(respond(request)); }
    }
    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ResponsesAreCachedForSevenDaysAndPersisted()
    {
        string path = Path.Combine(root, "fps-cache.json"); var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var h = new Handler(_ => Json("{\"avgFps\":144.5,\"min1Fps\":98}"));
        var c = new HowManyFpsClient(path, h, () => now) { ApiKey = "k" };
        var r = await c.GetAsync("Ryzen 7 7700X", "RTX 4070", "CS2", "1920x1080", "Native");
        Assert.Equal(144.5, r.AvgFps); Assert.Equal(98, r.Min1Fps); Assert.Equal(1, h.Calls);
        Assert.Equal("Bearer k", h.Last!.Headers.GetValues("Authorization").Single()); Assert.Equal("k", h.Last.Headers.GetValues("x-api-key").Single());
        await c.GetAsync("Ryzen 7 7700X", "RTX 4070", "CS2", "1920x1080", "Native"); Assert.Equal(1, h.Calls); // cache hit
        Assert.True(c.IsCached("Ryzen 7 7700X", "RTX 4070", "CS2", "1920x1080", "Native")); Assert.False(c.IsCached("Ryzen 7 7700X", "RTX 4070", "CS2", "2560x1440", "Native"));
        var again = new HowManyFpsClient(path, h, () => now.AddDays(6)) { ApiKey = "k" }; await again.GetAsync("Ryzen 7 7700X", "RTX 4070", "CS2", "1920x1080", "Native"); Assert.Equal(1, h.Calls); // persisted
        var expired = new HowManyFpsClient(path, h, () => now.AddDays(8)) { ApiKey = "k" }; await expired.GetAsync("Ryzen 7 7700X", "RTX 4070", "CS2", "1920x1080", "Native"); Assert.Equal(2, h.Calls);
    }

    [Theory]
    [InlineData(401, "chave recusada")]
    [InlineData(403, "chave recusada")]
    [InlineData(404, "não encontrado")]
    [InlineData(429, "cota")]
    [InlineData(500, "HTTP 500")]
    public async Task HttpErrorsBecomeReadableMessages(int code, string expected)
    {
        var c = new HowManyFpsClient(Path.Combine(root, "c.json"), new Handler(_ => Json("{}", (HttpStatusCode)code)), () => DateTime.UtcNow) { ApiKey = "k" };
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => c.GetAsync("a", "b", "g", "r", "u")); Assert.Contains(expected, e.Message);
    }

    [Fact]
    public async Task MissingKeyAndInvalidPayloadsAreRejectedWithoutCaching()
    {
        var h = new Handler(_ => Json("{\"min1Fps\":5}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HowManyFpsClient(Path.Combine(root, "n.json"), h) { ApiKey = " " }.GetAsync("a", "b", "g", "r", "u")); Assert.Equal(0, h.Calls); // no key: no request
        var c = new HowManyFpsClient(Path.Combine(root, "m.json"), h) { ApiKey = "k" };
        await Assert.ThrowsAsync<HttpRequestException>(() => c.GetAsync("a", "b", "g", "r", "u")); Assert.False(c.IsCached("a", "b", "g", "r", "u"));
        foreach (var body in new[] { "{\"avgFps\":-5}", "{\"avgFps\":1e9}", "null" })
            await Assert.ThrowsAsync<HttpRequestException>(() => new HowManyFpsClient(Path.Combine(root, "x" + body.GetHashCode() + ".json"), new Handler(_ => Json(body))) { ApiKey = "k" }.GetAsync("a", "b", "g", "r", "u"));
    }

    [Fact]
    public void CorruptCacheIsIgnored()
    {
        string path = Path.Combine(root, "bad.json"); File.WriteAllText(path, "{oops");
        Assert.False(new HowManyFpsClient(path) { ApiKey = "k" }.IsCached("a", "b", "g", "r", "u"));
    }

    [Fact]
    public void RowsRateAndEstimateLocallyLikeTheOriginal()
    {
        var rows = GameCatalog.Games.Select(g => new GameFpsRow(g)).ToList();
        var medium = FpsAccuracy.Assess(HardwareMatch.Desktop, 100, false, 100);
        GameFpsRow.RefreshLocal(rows, 100, 100, "1920x1080", "Alto", "Native", medium);
        // The local estimate is a range around the central value (340), never a single number.
        var cs2 = rows.Single(r => r.Name == "Counter-Strike 2"); Assert.Equal(340, cs2.Fps); Assert.Equal("289–391", cs2.FpsText); Assert.Equal("EXCELENTE", cs2.Rating); Assert.Equal("ESTIMATIVA", cs2.Source); Assert.Equal("gargalo: GPU", cs2.Detail); Assert.Equal("confiança média", cs2.LowText);
        Assert.Contains("não é medição", cs2.Summary);
        var elden = rows.Single(r => r.Name == "Elden Ring"); Assert.Equal("trava do jogo em 60 FPS", elden.Detail); Assert.Equal("JOGÁVEL", elden.Rating); Assert.Equal("51–60", elden.FpsText); // never above the engine cap
        cs2.Set(0, 0, "x", ""); Assert.Equal("—", cs2.FpsText); Assert.Equal("", cs2.Rating); Assert.Equal(0, cs2.Bar);
        cs2.Set(95.4, 60, "HOWMANYFPS", ""); Assert.Equal("ÓTIMO", cs2.Rating); Assert.Equal("1% low 60", cs2.LowText); Assert.InRange(cs2.Bar, 0.39, 0.4);
        foreach (var (fps, rating) in new[] { (29.0, "INJOGÁVEL"), (30.0, "FRACO"), (59.9, "FRACO"), (60.0, "JOGÁVEL"), (90.0, "ÓTIMO"), (144.0, "EXCELENTE") }) { cs2.Set(fps, 0, "", ""); Assert.Equal(rating, cs2.Rating); }
    }

    [Fact]
    public void SettingsKeepTheApiKeyInAUserOnlyFile()
    {
        string path = Path.Combine(root, "cfg/settings.json"); new IlmenyxSettings { HowManyFpsKey = "secret-token" }.Save(path);
        Assert.Equal("secret-token", IlmenyxSettings.Load(path).HowManyFpsKey);
        if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }
}
