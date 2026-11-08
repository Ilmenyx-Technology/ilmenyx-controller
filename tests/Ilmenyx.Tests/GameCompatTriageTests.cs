// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Launch-option triage and Steam artwork cache; no network (fake handler).
using System.Net;
using System.Net.Http;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class GameCompatTriageTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ilmenyx-triage-" + Guid.NewGuid().ToString("N"));
    public GameCompatTriageTests() => Directory.CreateDirectory(dir);
    public void Dispose() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

    private static readonly CompatHardware Amd = new("AMD Radeon RX 7900 XT (radeonsi, navi31)", "Arch Linux", false);
    private static readonly CompatHardware Nvidia = new("NVIDIA GeForce RTX 4070", "Fedora 41", true);
    private static readonly CompatHardware Nouveau = new("NVIDIA GeForce GTX 1060", "Fedora 41", false);
    private static bool All(string _) => true;
    private static List<CompatSuggestion> S(params (string Option, int Count)[] o) => o.Select(x => new CompatSuggestion(x.Option, x.Count, 10)).ToList();
    private static LaunchTriage T(CompatHardware hw, Func<string, bool> tools, params (string, int)[] o) => LaunchOptionTriage.Triage(S(o), [], hw, tools);
    private static string[] Kept(LaunchTriage t) => t.Recommended.Select(x => x.Option).ToArray();

    [Fact]
    public void Wined3dNeverJoinsDxvkOrVkd3dOptions()
    {
        var t = T(Amd, All, ("DXVK_ASYNC=1", 5), ("PROTON_USE_WINED3D=1", 3), ("VKD3D_CONFIG=dxr", 2));
        Assert.Equal(["DXVK_ASYNC=1", "VKD3D_CONFIG=dxr"], Kept(t));
        Assert.Contains("PROTON_USE_WINED3D=1", t.Alternatives.Single().Option);
        Assert.Contains("DXVK_ASYNC=1", t.Alternatives.Single().Reason);
        // The more used one wins either way.
        var u = T(Amd, All, ("PROTON_USE_WINED3D=1", 6), ("DXVK_HUD=fps", 2), ("-dx12", 1));
        Assert.Equal(["PROTON_USE_WINED3D=1"], Kept(u));
        Assert.Equal(2, u.Alternatives.Count);
    }

    [Fact]
    public void OneGraphicsApiOneValuePerVariableAndNoDuplicateOverlay()
    {
        var t = T(Amd, All, ("-dx11", 4), ("-dx12", 3), ("-vulkan", 2), ("WINEDLLOVERRIDES=dinput8=n,b", 4), ("WINEDLLOVERRIDES=version=n,b", 2),
            ("mangohud", 3), ("MANGOHUD=1", 2), ("gamemoderun", 5));
        Assert.Equal(["gamemoderun", "-dx11", "WINEDLLOVERRIDES=dinput8=n,b", "mangohud"], Kept(t));
        Assert.Contains(t.Alternatives, a => a.Option == "-vulkan" && a.Reason.Contains("API gráfica"));
        Assert.Contains(t.Alternatives, a => a.Option == "WINEDLLOVERRIDES=version=n,b" && a.Reason.Contains("só guarda um valor"));
        Assert.Contains(t.Alternatives, a => a.Option == "MANGOHUD=1" && a.Reason.Contains("basta um"));
        Assert.Equal("WINEDLLOVERRIDES=dinput8=n,b gamemoderun mangohud %command% -dx11", t.CommandLine); // %command% between wrappers and game arguments
        Assert.True(LaunchOptionSafety.Check(t.CommandLine).Applicable);
    }

    [Fact]
    public void EsyncAndFsyncSwitchesCombineButContradictoryValuesDoNot()
    {
        Assert.Equal(["PROTON_NO_ESYNC=1", "PROTON_NO_FSYNC=1"], Kept(T(Amd, All, ("PROTON_NO_ESYNC=1", 3), ("PROTON_NO_FSYNC=1", 3))));
        var t = T(Amd, All, ("PROTON_NO_FSYNC=1", 3), ("PROTON_NO_FSYNC=0", 1));
        Assert.Equal(["PROTON_NO_FSYNC=1"], Kept(t));
        Assert.Contains(t.Alternatives, a => a.Option == "PROTON_NO_FSYNC=0");
        Assert.Contains("DirectX 11", LaunchOptionTriage.Conflict("-dx11", "PROTON_NO_D3D11=1"));
    }

    [Fact]
    public void VendorOnlyOptionsFollowTheUsersGpuAndDriver()
    {
        (string, int)[] mix = [("PROTON_ENABLE_NVAPI=1", 4), ("DXVK_ENABLE_NVAPI=1", 3), ("RADV_PERFTEST=gpl", 3), ("MESA_SHADER_CACHE_MAX_SIZE=10G", 2), ("__GL_SHADER_DISK_CACHE=1", 2)];
        Assert.Equal(["PROTON_ENABLE_NVAPI=1", "DXVK_ENABLE_NVAPI=1", "__GL_SHADER_DISK_CACHE=1"], Kept(T(Nvidia, All, mix)));
        var amd = T(Amd, All, mix);
        Assert.Equal(["RADV_PERFTEST=gpl", "MESA_SHADER_CACHE_MAX_SIZE=10G"], Kept(amd));
        Assert.Contains(amd.Alternatives, a => a.Option == "PROTON_ENABLE_NVAPI=1" && a.Reason.Contains("NVIDIA com o driver proprietário"));
        Assert.Equal(["MESA_SHADER_CACHE_MAX_SIZE=10G"], Kept(T(Nouveau, All, mix))); // NVIDIA on nouveau is a Mesa driver
        Assert.Equal(5, Kept(T(new CompatHardware("", null, false), All, [("RADV_PERFTEST=gpl", 3), ("MESA_SHADER_CACHE_MAX_SIZE=10G", 2), ("__GL_SHADER_DISK_CACHE=1", 2), ("ANV_DEBUG=x", 1), ("DRI_PRIME=1", 1)])).Length); // unknown GPU: not filtered
        var nv = T(Nvidia, All, ("PROTON_ENABLE_NVAPI=1", 4), ("PROTON_HIDE_NVIDIA_GPU=1", 2));
        Assert.Equal(["PROTON_ENABLE_NVAPI=1"], Kept(nv));
        Assert.Contains("NVAPI", nv.Alternatives.Single().Reason);
    }

    [Fact]
    public void MissingToolsAndManualWrappersAreAlternativesOnly()
    {
        var t = T(Amd, tool => tool == "gamemoderun", ("gamemoderun", 5), ("mangohud", 4), ("gamescope", 3), ("strangle", 2), ("PROTON_ENABLE_WAYLAND=1", 2));
        Assert.Equal(["gamemoderun", "PROTON_ENABLE_WAYLAND=1"], Kept(t));
        Assert.Contains(t.Alternatives, a => a.Option == "mangohud" && a.Reason.Contains("pacote mangohud"));
        Assert.Contains(t.Alternatives, a => a.Option == "gamescope" && a.Reason.Contains("resolução"));
        Assert.Equal("PROTON_ENABLE_WAYLAND=1 gamemoderun %command%", t.CommandLine);
    }

    [Fact]
    public void OneProtonVersionIsRecommendedAndTheCombinationIsBounded()
    {
        var t = LaunchOptionTriage.Triage([], S(("Proton 9.0-4", 2), ("GE-Proton10-29", 5), ("Proton Experimental", 1)), Amd, All);
        Assert.Equal("GE-Proton10-29", t.Proton!.Option);
        Assert.Equal(["Proton 9.0-4", "Proton Experimental"], t.OtherProtons.Select(o => o.Option));
        Assert.Null(t.CommandLine); Assert.Empty(t.Recommended);
        var many = LaunchOptionTriage.Triage(Enumerable.Range(0, 12).Select(i => new CompatSuggestion($"VAR{i}=1", 20 - i, 20)).ToList(), [], Amd, All);
        Assert.Equal(8, many.Recommended.Count); Assert.Equal(4, many.Alternatives.Count);
        Assert.True(LaunchOptionSafety.Check(many.CommandLine).Applicable);
    }

    [Fact]
    public void EveryTableRuleIsReachableAndPatternsMatchAsDocumented()
    {
        Assert.True(LaunchOptionTriage.Matches("DXVK_*", "DXVK_HUD=fps")); Assert.False(LaunchOptionTriage.Matches("DXVK_*", "VKD3D_CONFIG=dxr"));
        Assert.True(LaunchOptionTriage.Matches("PROTON_ENABLE_NVAPI", "PROTON_ENABLE_NVAPI=0")); Assert.False(LaunchOptionTriage.Matches("PROTON_ENABLE_NVAPI=1", "PROTON_ENABLE_NVAPI=0"));
        Assert.True(LaunchOptionTriage.Matches("-dx12", "-DX12")); Assert.False(LaunchOptionTriage.Matches("mangohud", "MANGOHUD=0"));
        foreach (var c in LaunchOptionTriage.Conflicts)
        {
            string a = Sample(c.A[0]), b = c.B.Length > 0 ? Sample(c.B[0]) : Sample(c.A[1]);
            Assert.NotNull(LaunchOptionTriage.Conflict(a, b)); Assert.NotNull(LaunchOptionTriage.Conflict(b, a));
        }
        Assert.Null(LaunchOptionTriage.Conflict("gamemoderun", "mangohud"));
        static string Sample(string p) => p.EndsWith('*') ? p[..^1] + "X=1" : p;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Urls.Add(request.RequestUri!); return Task.FromResult(respond(request)); }
    }

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[60]];

    [Fact]
    public async Task ArtworkIsDownloadedFromTheSteamCdnValidatedAndCached()
    {
        var handler = new FakeHandler(req => req.RequestUri!.AbsolutePath.EndsWith("/570/library_hero.jpg", StringComparison.Ordinal)
            ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(Jpeg) { Headers = { ContentType = new("image/jpeg") } } }
            : req.RequestUri.AbsolutePath.Contains("/1/") ? new(HttpStatusCode.OK) { Content = new StringContent("<html>not an image</html>") } : new(HttpStatusCode.NotFound));
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var online = new CompatOnline(dir, handler, () => now);
        Assert.Null(online.CachedArt("570")); Assert.Empty(handler.Urls); // offline look-up never uses the network
        string file = (await online.ArtAsync("570"))!;
        Assert.Equal(Jpeg, File.ReadAllBytes(file)); Assert.Equal(file, online.CachedArt("570"));
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/570/library_hero.jpg", handler.Urls.Single().ToString());
        await online.ArtAsync("570"); Assert.Single(handler.Urls); // cached
        Assert.Null(await online.ArtAsync("1")); // HTML instead of an image: rejected
        Assert.Null(await online.ArtAsync("2")); int calls = handler.Urls.Count;
        Assert.Null(await online.ArtAsync("2")); Assert.Equal(calls, handler.Urls.Count); // miss remembered
        Assert.All(handler.Urls, u => Assert.Contains(u.Host, CompatOnline.Hosts));
        Assert.False(CompatOnline.IsImage("<html>............"u8)); Assert.True(CompatOnline.IsImage([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[20]]));
        await Assert.ThrowsAsync<ArgumentException>(() => online.ArtAsync("../x"));
        var big = new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, .. new byte[CompatOnline.MaxArtBytes]]) });
        Assert.Null(await new CompatOnline(Path.Combine(dir, "big"), big).ArtAsync("3")); // announced size over the limit: not downloaded
    }
}
