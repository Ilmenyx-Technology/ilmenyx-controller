// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Sonda falsa: nenhum teste envia um pacote ou abre uma conexão.
using System.Net;
using System.Net.Http;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class NetworkTests
{
    private static readonly AdapterInfo Adapter = new("eth0", "Ethernet (cabo)", "1000 Mbps", "192.168.0.10", "192.168.0.1", "1.1.1.1", IPAddress.Parse("192.168.0.1"));

    private sealed class FakeProbe : INetworkProbe
    {
        public AdapterInfo? Adapter = NetworkTests.Adapter;
        public Dictionary<string, List<double>> Rtts = new() { ["192.168.0.1"] = [2, 2, 3, 2, 2], ["1.1.1.1"] = [12, 13, 12, 14, 12], ["8.8.8.8"] = [15, 14, 15, 16, 15] };
        public int Sent = 5; public double? Down = 300, Up = 50, Dns = 20; public Exception? DownError; public List<double> Loaded = [14, 15, 14, 16, 15, 14];
        public int Calls; public CancellationTokenSource? CancelOnDownload;
        public AdapterInfo? ActiveAdapter() => Adapter;
        public Task<(List<double>, int)> PingLoopAsync(IPAddress a, int count, int intervalMs, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.FromResult(count <= 0 ? (Loaded, Loaded.Count) : (Rtts.GetValueOrDefault(a.ToString(), []), Sent)); }
        public Task<double?> DownloadAsync(int s, CancellationToken ct) { CancelOnDownload?.Cancel(); ct.ThrowIfCancellationRequested(); return DownError is null ? Task.FromResult(Down) : throw DownError; }
        public Task<double?> UploadAsync(int s, CancellationToken ct) => Task.FromResult(Up);
        public Task<double?> DnsAsync(string host, CancellationToken ct) => Task.FromResult(Dns);
    }

    private static Task<HealthCard> Run(FakeProbe p, List<string>? log = null, CancellationToken ct = default) => NetworkTestRunner.RunAsync(p, (t, f) => log?.Add($"{f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} {t}"), ct, 5, 1, 1, 1);

    [Fact]
    public async Task HealthyConnectionIsStableWithAllMetrics()
    {
        var log = new List<string>(); var card = await Run(new FakeProbe(), log);
        Assert.Equal("ESTÁVEL", card.VerdictText); Assert.Equal(HealthVerdict.Good, card.Verdict); Assert.Contains("Latência baixa", card.Reasons);
        var m = card.Metrics.ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("300 Mbps", m["Download"]); Assert.Equal("50 Mbps", m["Upload"]); Assert.Equal("20 ms", m["Resolução de DNS"]); Assert.Contains("durante o download", m["Latência sob carga"]);
        Assert.Contains("eth0 · Ethernet (cabo) · link 1000 Mbps", m["Adaptador"]); Assert.Equal("192.168.0.10 / 192.168.0.1 / 1.1.1.1", m["IP / Gateway / DNS"]);
        Assert.StartsWith("0.02 Procurando", log[0]); Assert.StartsWith("1.00 Concluído", log[^1]); Assert.True(log.Count >= 6);
        Assert.Contains("Roteador (192.168.0.1)", m.Keys);
    }

    [Fact]
    public async Task UnstableLocalNetworkAndPacketLossWarn()
    {
        var p = new FakeProbe { Sent = 10 }; p.Rtts["192.168.0.1"] = [40, 80, 45, 100, 50, 90, 40, 85]; // 8 of 10 answered, avg > 30 and big jitter
        p.Rtts["1.1.1.1"] = [12, 13, 12, 14, 12, 13, 12, 14, 12, 13]; p.Rtts["8.8.8.8"] = [15, 14, 15, 16, 15, 14, 15, 16, 15]; // internet: 0 % and 10 % loss → average 5 %
        var card = await Run(p);
        Assert.Equal("ATENÇÃO", card.VerdictText); Assert.Contains("rede local está instável", card.Reasons); Assert.Contains("perda de pacotes", card.Reasons); Assert.Contains("Provavelmente vem da rede local", card.Reasons);
    }

    [Fact]
    public async Task NoInternetAnswerIsBadAndSilentRouterAloneIsNotAProblem()
    {
        var dead = new FakeProbe(); dead.Rtts["1.1.1.1"] = []; dead.Rtts["8.8.8.8"] = [];
        var bad = await Run(dead); Assert.Equal("RUIM", bad.VerdictText); Assert.Contains("Nenhum dos servidores da internet respondeu", bad.Reasons);
        var silent = new FakeProbe(); silent.Rtts["192.168.0.1"] = [];
        var ok = await Run(silent); Assert.Equal("ESTÁVEL", ok.VerdictText); Assert.Contains("O roteador não responde a ping", ok.Reasons);
    }

    [Fact]
    public async Task HighLatencyBufferbloatSlowDnsAndSlowDownloadAreReported()
    {
        var p = new FakeProbe { Down = 5, Dns = 400, Loaded = [300, 310, 305, 320, 300, 315] }; p.Rtts["1.1.1.1"] = [200, 210, 190, 205, 200]; p.Rtts["8.8.8.8"] = [210, 200, 205, 195, 200];
        var card = await Run(p);
        Assert.Equal("RUIM", card.VerdictText); Assert.Contains("Latência alta até a internet", card.Reasons); Assert.Contains("O roteador responde bem", card.Reasons); Assert.Contains("bufferbloat", card.Reasons);
        Assert.Contains("DNS demorou 400 ms", card.Reasons); Assert.Contains("Download baixo (5 Mbps)", card.Reasons);
    }

    [Fact]
    public async Task JitterAndSpeedTestFailureAreReported()
    {
        var p = new FakeProbe { Down = null, DownError = new HttpRequestException("blocked") }; p.Rtts["1.1.1.1"] = [10, 60, 12, 70, 11]; p.Rtts["8.8.8.8"] = [10, 55, 15, 65, 12];
        var card = await Run(p); Assert.Contains("jitter está alto", card.Reasons); Assert.Contains("Não foi possível medir a velocidade", card.Reasons); Assert.Equal("—", card.Metrics.First(k => k.Key == "Download").Value);
    }

    [Fact]
    public async Task NoAdapterGivesTheNoConnectionCardWithoutProbing()
    {
        var p = new FakeProbe { Adapter = null }; var card = await Run(p);
        Assert.Equal("SEM REDE", card.VerdictText); Assert.Equal(HealthVerdict.Critical, card.Verdict); Assert.Equal(0, p.Calls);
    }

    [Fact]
    public async Task CancellationDuringDownloadStopsTheRunAndTheLoadedPing()
    {
        using var cts = new CancellationTokenSource(); var p = new FakeProbe { CancelOnDownload = cts };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(p, null, cts.Token));
    }

    [Fact]
    public void PingStatisticsAndAdapterNames()
    {
        var s = PingStat.From("x", "1.1.1.1", [10, 20, 10, 20], 5);
        Assert.Equal(1, s.Lost); Assert.Equal(20, s.LossPct); Assert.Equal(15, s.Avg); Assert.Equal(10, s.Jitter); Assert.Equal(10, s.Min); Assert.Equal(20, s.Max); Assert.Contains("perda 20%", s.Text);
        var none = PingStat.From("y", "8.8.8.8", [], 3); Assert.False(none.Answered); Assert.Equal("não responde", none.Text); Assert.Equal(100, none.LossPct);
        Assert.Equal(0, PingStat.From("z", "a", [5], 1).Jitter);
        Assert.Equal("Wi-Fi", SystemNetworkProbe.AdapterType(System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)); Assert.Equal("Ethernet (cabo)", SystemNetworkProbe.AdapterType(System.Net.NetworkInformation.NetworkInterfaceType.Ethernet));
        Assert.Contains("100 MB", NetworkEndpoints.Description); Assert.StartsWith("https://", NetworkEndpoints.Download);
    }
}
