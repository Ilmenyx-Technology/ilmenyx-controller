// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class Ipv6PrivacyTests
{
    [Theory]
    [InlineData("2001:db8::1")]
    [InlineData("2001:0db8:0000:0000:0000:0000:1234:5678")]
    [InlineData("2001:DB8::ABCD")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("::ffff:192.0.2.128")]
    [InlineData("fe80::abcd%eth0")]
    [InlineData("fe80::abcd%enp3s0.100")]
    [InlineData("fe80::abcd%42")]
    [InlineData("64:ff9b::192.0.2.1")]
    [InlineData("fe80::abcd%wg+0")]
    public void ValidIpv6AndScopeAreRedactedAsAWhole(string address)
    {
        Assert.Equal("sensor [IP redigido] label", LinuxParsers.Clean("sensor " + address + " label"));
        Assert.DoesNotContain(address, LinuxParsers.Clean(address));
    }

    [Theory]
    [InlineData("[2001:db8::1]:443", "[[IP redigido]]:443")]
    [InlineData("(2001:db8::1).", "([IP redigido]).")]
    [InlineData("2001:db8::1.", "[IP redigido].")]
    [InlineData("2001:db8::/64", "[IP redigido]/64")]
    [InlineData("2001:db8::1; fe80::2%eth0", "[IP redigido]; [IP redigido]")]
    [InlineData("connect 2001:db8::1: refused", "connect [IP redigido]: refused")]
    [InlineData("fe80::1%eth0: down", "[IP redigido]: down")]
    [InlineData("[fe80::1%eth0]:22", "[[IP redigido]]:22")]
    [InlineData("/home/x/2001:db8::1", "[caminho redigido]")]
    [InlineData("IPv6:2001:db8::1", "IPv6=[redigido]")]
    [InlineData("2001:db8::1,fe80::2", "[IP redigido],[IP redigido]")]
    public void CommonDelimitersArePreserved(string input, string expected) =>
        Assert.Equal(expected, LinuxParsers.Clean(input));

    [Theory]
    [InlineData("PCI 0000:00:02.0")]
    [InlineData("12:34:56")]
    [InlineData("Intel Core i5 @ 2.40GHz")]
    [InlineData("Package id 0")]
    [InlineData("dead:beef")]
    [InlineData("0000:00:1f.3")]
    [InlineData("12:34:56.789")]
    [InlineData("2001:db8::xyz")]
    [InlineData("2001:db8::1suffix")]
    [InlineData("namespace::Class")]
    [InlineData("7.2.11-1-cachyos")]
    public void NonAddressesAndEmbeddedInvalidTokensArePreserved(string input) =>
        Assert.Equal(input, LinuxParsers.Clean(input));

    [Fact]
    public void LegacyRedactionsRemainAndCleaningIsIdempotentAndBounded()
    {
        string clean = LinuxParsers.Clean("192.0.2.19 ::ffff:192.0.2.128 02:ab:cd:ef:12:34 hostname=example");
        Assert.DoesNotContain("192.0.2", clean); Assert.DoesNotContain("ffff", clean); Assert.DoesNotContain("02:ab", clean);
        Assert.DoesNotContain("example", clean); Assert.Equal(clean, LinuxParsers.Clean(clean));
        Assert.True(LinuxParsers.Clean(new string('x', 4096)).Length <= 512);
    }

    [Fact]
    public void ExpansionRemainsBoundedAndTruncatedAddressTokensAreDiscarded()
    {
        var repeated = LinuxParsers.Clean(string.Concat(Enumerable.Repeat("2001:db8::1 ", 100)));
        Assert.True(repeated.Length <= 512); Assert.DoesNotContain("db8", repeated);
        var cut = LinuxParsers.Clean(new string('x', 490) + " 2001:db8:1234:5678:9abc:def0:1234:5678");
        Assert.True(cut.Length <= 512); Assert.DoesNotContain("db8", cut);
        Assert.Contains("[texto truncado]", cut);
    }

    private sealed class Files : ILinuxFiles
    {
        public IReadOnlyList<string> BlockDevices() => [];
        public Task<string> ReadAsync(string path, CancellationToken ct) => Task.FromResult(path switch
        {
            "/proc/cpuinfo" => "processor: 0\nmodel name: Fixture 2001:db8::1234 CPU",
            "/proc/meminfo" => "MemTotal: 1024 kB\nMemAvailable: 512 kB",
            "/etc/os-release" => "PRETTY_NAME=Fixture fe80::abcd%test0 Linux",
            "/proc/sys/kernel/osrelease" => "fixture-kernel",
            _ => throw new FileNotFoundException()
        });
    }
    [Fact]
    public async Task CollectedFixtureIsRedactedInTextJsonAndHtml()
    {
        var snapshot = await new LinuxHardwareInfoProvider(new Files()).CollectAsync();
        foreach (var output in new[] { SnapshotReport.Text(snapshot), SnapshotReport.Json(snapshot), HardwareHtmlReport.Render(snapshot) })
        {
            Assert.DoesNotContain("2001:db8", output); Assert.DoesNotContain("fe80::abcd", output);
            Assert.DoesNotContain("%test0", output); Assert.Contains("redigido", output);
        }
    }
}
