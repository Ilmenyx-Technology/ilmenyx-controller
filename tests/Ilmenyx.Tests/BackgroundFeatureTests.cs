// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class BackgroundFeatureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TurboSpikesNeverFireTheTemperatureAlert()
    {
        var alert = new TemperatureAlert(80);
        // Bursts like the ones seen on an i5-1135G7: a second or two at 75–85 °C, then back to the 40s.
        for (int s = 0; s < 600; s++)
        {
            double t = s % 15 is 0 or 1 ? 85 : 45;
            Assert.False(alert.Feed(T0.AddSeconds(s), t));
        }
    }

    [Fact]
    public void SustainedHeatFiresOnceThenWaitsForCoolingAndCooldown()
    {
        var alert = new TemperatureAlert(90);
        int fired = 0;
        for (int s = 0; s <= 60; s++) if (alert.Feed(T0.AddSeconds(s), 92 + s % 3)) fired++;
        Assert.Equal(1, fired);
        Assert.Equal(94, alert.Peak);
        // Still hot, after the cooldown: no repeat without cooling down first.
        Assert.False(alert.Feed(T0.AddMinutes(12), 93));
        Assert.False(alert.Feed(T0.AddMinutes(12).AddSeconds(30), 93));
        // Cooled below 85 °C, hot again for 20 s past the cooldown: fires again.
        Assert.False(alert.Feed(T0.AddMinutes(13), 70));
        Assert.False(alert.Feed(T0.AddMinutes(14), 91));
        Assert.True(alert.Feed(T0.AddMinutes(14).AddSeconds(20), 91));
    }

    [Fact]
    public void MissingReadingEndsTheHotStretch()
    {
        var alert = new TemperatureAlert(90);
        Assert.False(alert.Feed(T0, 95));
        Assert.False(alert.Feed(T0.AddSeconds(10), null));
        Assert.False(alert.Feed(T0.AddSeconds(25), 95));
        Assert.True(alert.Feed(T0.AddSeconds(45), 95));
    }

    private const string Releases = """
        [
          {"tag_name":"v0.32.0-beta","draft":true,"html_url":"https://github.com/Ilmenyx-Technology/ilmenyx/releases/tag/v0.32.0-beta"},
          {"tag_name":"v0.31.1","draft":false,"prerelease":true,"html_url":"https://github.com/Ilmenyx-Technology/ilmenyx/releases/tag/v0.31.1"},
          {"tag_name":"v0.31.0","draft":false,"prerelease":true,"html_url":"https://github.com/Ilmenyx-Technology/ilmenyx/releases/tag/v0.31.0"},
          {"tag_name":"nightly","draft":false}
        ]
        """;

    [Fact]
    public void NewestReleaseSkipsDraftsAndUnparsableTags()
    {
        var r = ReleaseCheck.Newest(Releases)!;
        Assert.Equal(new Version(0, 31, 1), r.Version);
        Assert.Equal("v0.31.1", r.Tag);
        Assert.EndsWith("/tag/v0.31.1", r.Url);
    }

    [Theory]
    [InlineData("0.31.0.0", true)]
    [InlineData("0.31.1.0", false)]
    [InlineData("0.32.0", false)]
    public void NewerThanComparesMajorMinorPatch(string current, bool expected) =>
        Assert.Equal(expected, ReleaseCheck.NewerThan(Releases, Version.Parse(current)) is not null);

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"message\":\"Not Found\"}")]
    [InlineData("[]")]
    public void UnusableAnswersMeanNoUpdate(string json) => Assert.Null(ReleaseCheck.NewerThan(json, new Version(0, 1, 0)));

    [Fact]
    public void ForeignReleaseLinksFallBackToTheProjectPage()
    {
        var r = ReleaseCheck.Newest("""[{"tag_name":"v9.0.0","html_url":"https://example.com/malware"}]""")!;
        Assert.Equal(ReleaseCheck.ReleasesPage, r.Url);
    }
}
