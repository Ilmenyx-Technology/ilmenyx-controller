// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
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

    private static readonly (string, string)[] Hollow =
    [("1", "Scarlet Hollow"), ("2", "Hollow Knight: Silksong"), ("3", "Hollow Knight: Silksong Official Soundtrack"), ("4", "Hollow Knight"), ("5", "Hollow Knight - Artbook")];

    [Fact]
    public void TypoRanksTheClosestFirstAndDropsSoundtracks()
    {
        var r = SteamAppLookup.RankSuggestions("Hollow Knigth", Hollow);
        Assert.Equal("Hollow Knight", r[0].Name);
        Assert.DoesNotContain(r, x => x.Name.Contains("Soundtrack") || x.Name.Contains("Artbook"));
        Assert.True(r.Count <= 3);
    }

    [Fact]
    public void MissingWordLetterFindsTheRemaster() =>
        Assert.Equal("God of War III Remastered", SteamAppLookup.RankSuggestions("God of Wr III", [("9", "God of War III Remastered"), ("8", "Gods Will Be Watching")])[0].Name);

    [Fact]
    public void NothingSimilarGivesNothingAndExactIsNotSuggested()
    {
        Assert.Empty(SteamAppLookup.RankSuggestions("Zzzzqx", Hollow));
        Assert.Empty(SteamAppLookup.RankSuggestions("Hollow Knight", [("4", "Hollow Knight")]));
        Assert.Equal(1.0, SteamAppLookup.Similarity("Doom", "DOOM"));
    }

    private sealed class Counting : HttpMessageHandler
    {
        public List<string> Terms = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Terms.Add(Uri.UnescapeDataString(request.RequestUri!.Query));
            bool word = !Terms[^1].EndsWith("Hollow Knigth", StringComparison.Ordinal);
            string body = word && Terms[^1].EndsWith("Hollow") ? """{"items":[{"type":"app","name":"Hollow Knight","id":367520},{"type":"app","name":"Hollow Knight OST","id":5}]}""" : """{"items":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task SuggestSearchesWordByWordWithALimitAndHonoursRefusals()
    {
        var h = new Counting();
        var r = await new SteamAppLookup(h).SuggestAsync("Hollow Knigth");
        Assert.Equal([("367520", "Hollow Knight")], r);
        Assert.True(h.Terms.Count is >= 2 and <= 4);
        Assert.Empty(await new SteamAppLookup(new Counting()).SuggestAsync("Hollow Knigth", exclude: new HashSet<string> { "367520" }));
        var h2 = new Counting();
        Assert.Empty(await new SteamAppLookup(h2).SuggestAsync("Aaaa Bbbb Cccc Dddd Eeee Ffff"));
        Assert.Equal(4, h2.Terms.Count);
        Assert.Empty(await new SteamAppLookup(new Fake(HttpStatusCode.InternalServerError, "")).SuggestAsync("Hollow Knigth"));
    }
}
