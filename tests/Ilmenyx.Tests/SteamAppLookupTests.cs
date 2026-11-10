// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Net;
using System.Net.Http;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class SteamAppLookupTests
{
    private const string Search = """
        {"total":3,"items":[
          {"type":"app","name":"Hollow Knight: Silksong","id":1030300},
          {"type":"app","name":"Hollow Knight","id":367520},
          {"type":"app","name":"Hollow Knight: Silksong Soundtrack","id":999}]}
        """;

    [Theory]
    [InlineData("Hollow Knight - Silksong", "1030300")]
    [InlineData("hollow knight silksong", "1030300")]
    [InlineData("Hollow Knight", "367520")]
    [InlineData("Silksong", null)] // partial names are not guessed
    public void OnlyAnExactNameMatchIsAccepted(string name, string? id) =>
        Assert.Equal(id, SteamAppLookup.Parse(Search, SteamAppLookup.Normalize(name))?.Id);

    [Fact]
    public void TwoDifferentGamesWithTheSameNameAreNotGuessed() =>
        Assert.Null(SteamAppLookup.Parse("""{"items":[{"type":"app","name":"Doom","id":1},{"type":"app","name":"DOOM","id":2}]}""", "doom"));

    [Fact]
    public void TrademarkSignsAndAccentsAreIgnored() =>
        Assert.Equal(SteamAppLookup.Normalize("Pokémon™ Legends"), SteamAppLookup.Normalize("Pokemon Legends"));

    private sealed class Fake(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public Uri? Asked;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Asked = request.RequestUri; return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) }); }
    }

    [Fact]
    public async Task SendsOnlyTheNameAndSurvivesErrors()
    {
        var ok = new Fake(HttpStatusCode.OK, Search);
        Assert.Equal(("1030300", "Hollow Knight: Silksong"), await new SteamAppLookup(ok).FindAsync("Hollow Knight - Silksong"));
        Assert.Equal("store.steampowered.com", ok.Asked!.Host);
        Assert.Contains("term=Hollow%20Knight%20Silksong", ok.Asked.Query);
        Assert.Null(await new SteamAppLookup(new Fake(HttpStatusCode.InternalServerError, "")).FindAsync("Hollow Knight"));
        Assert.Null(await new SteamAppLookup(new Fake(HttpStatusCode.OK, "not json")).FindAsync("Hollow Knight"));
    }
}
