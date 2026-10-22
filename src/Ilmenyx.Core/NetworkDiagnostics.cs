// New Ilmenyx additions, ported from the flow, thresholds and texts of the original NetworkTest (MIT, see NOTICE): see LICENSING.md.
// Generates network traffic: only ever started by an explicit click behind a confirmation that lists the endpoints. Never by tests or startup.
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Ilmenyx.Core;

public static class NetworkEndpoints
{
    public const string Cloudflare = "1.1.1.1", Google = "8.8.8.8", DnsName = "www.cloudflare.com";
    public const string Download = "https://speed.cloudflare.com/__down?bytes=25000000", Upload = "https://speed.cloudflare.com/__up";
    public const string Description = "ping ao roteador, a 1.1.1.1 e a 8.8.8.8; resolução de www.cloudflare.com; download e upload de dados de teste em speed.cloudflare.com (cerca de 100 MB no total, mais em conexões muito rápidas)";
}

public sealed record PingStat(string Name, string Address, int Sent, int Lost, double Min, double Avg, double Max, double Jitter)
{
    public double LossPct => Sent == 0 ? 0 : 100.0 * Lost / Sent;
    public bool Answered => Sent - Lost > 0;
    public string Text => !Answered ? "não responde" : $"média {Avg:0.#} ms · mín {Min:0.#} · máx {Max:0.#} · jitter {Jitter:0.#} ms · perda {LossPct:0.#}%";

    public static PingStat From(string name, string address, IReadOnlyList<double> rtts, int sent)
    {
        if (rtts.Count == 0) return new(name, address, sent, sent, 0, 0, 0, 0);
        double jit = 0;
        for (int i = 1; i < rtts.Count; i++) jit += Math.Abs(rtts[i] - rtts[i - 1]);
        jit = rtts.Count > 1 ? jit / (rtts.Count - 1) : 0;
        return new(name, address, sent, sent - rtts.Count, rtts.Min(), rtts.Average(), rtts.Max(), jit);
    }
}

public sealed record AdapterInfo(string Name, string Type, string Link, string Ip, string Gateway, string Dns, IPAddress GatewayAddress);

public sealed record NetworkMeasurements(AdapterInfo Adapter, IReadOnlyList<PingStat> Pings, double? DnsMs, double? DownMbps, double? UpMbps, double? LoadedMs, string? Error);

public interface INetworkProbe
{
    AdapterInfo? ActiveAdapter();
    Task<(List<double> Rtts, int Sent)> PingLoopAsync(IPAddress address, int count, int intervalMs, CancellationToken ct);
    Task<double?> DownloadAsync(int seconds, CancellationToken ct);
    Task<double?> UploadAsync(int seconds, CancellationToken ct);
    Task<double?> DnsAsync(string host, CancellationToken ct);
}

public static class NetworkVerdict
{
    public static HealthCard NoConnection() => new()
    {
        Device = "rede", Title = "Sem conexão", Meta = "Nenhum adaptador conectado com gateway", Verdict = HealthVerdict.Critical, Label = "SEM REDE",
        Reasons = "O sistema não encontrou uma conexão ativa com acesso ao roteador. Verifique o cabo, o Wi-Fi ou o adaptador."
    };

    public static HealthCard Evaluate(NetworkMeasurements m, DateTime now)
    {
        var pings = m.Pings; var gwS = pings[0];
        var net = pings.Skip(1).Where(p => p.Answered).ToList();
        double netAvg = net.Count > 0 ? net.Average(p => p.Avg) : 0, netLoss = pings.Skip(1).Average(p => p.LossPct), netJit = net.Count > 0 ? net.Max(p => p.Jitter) : 0;
        var warn = new List<string>(); bool bad = false;
        bool lanBad = gwS.Answered && (gwS.LossPct > 2 || gwS.Avg > 30 || gwS.Jitter > 15);
        if (lanBad) warn.Add($"A rede local está instável (roteador: {gwS.Avg:0} ms, perda {gwS.LossPct:0.#}%, jitter {gwS.Jitter:0} ms). Se for Wi-Fi, aproxime-se do roteador ou use cabo.");
        if (!gwS.Answered) warn.Add("O roteador não responde a ping (alguns modelos bloqueiam). Isso por si só não é problema.");
        if (net.Count == 0) { bad = true; warn.Add("Nenhum dos servidores da internet respondeu: sem acesso à internet ou ping bloqueado pela rede."); }
        else
        {
            if (netLoss > 5 || netAvg > 150) bad = true;
            if (netLoss > 1) warn.Add($"Há perda de pacotes até a internet ({netLoss:0.#}%). {(lanBad ? "Provavelmente vem da rede local." : "Como o roteador está bem, o problema deve estar na linha ou no provedor.")}");
            if (netAvg > 80) warn.Add($"Latência alta até a internet ({netAvg:0} ms).{(lanBad ? "" : " O roteador responde bem, então o atraso vem do provedor.")}");
            if (netJit > 20) warn.Add($"O jitter está alto ({netJit:0} ms): a latência oscila, o que causa engasgos em jogos online.");
        }
        if (m.LoadedMs is { } loaded && net.Count > 0 && loaded - net.Min(p => p.Avg) > 100)
            warn.Add($"A latência sobe para {loaded:0} ms quando a conexão está cheia (bufferbloat). Ative SQM/QoS no roteador ou limite a velocidade a ~90%.");
        if (m.DnsMs > 150) warn.Add($"O DNS demorou {m.DnsMs:0} ms. Trocar para 1.1.1.1 ou 8.8.8.8 pode acelerar o carregamento de sites.");
        if (m.DownMbps is < 10) warn.Add($"Download baixo ({m.DownMbps:0.#} Mbps) para jogos, streaming e atualizações.");
        if (m.Error != null && m.DownMbps == null) warn.Add("Não foi possível medir a velocidade (speed.cloudflare.com inacessível ou bloqueado).");

        var verdict = bad ? HealthVerdict.Critical : warn.Any(w => !w.StartsWith("O roteador não responde", StringComparison.Ordinal)) ? HealthVerdict.Warning : HealthVerdict.Good;
        string label = verdict switch { HealthVerdict.Critical => "RUIM", HealthVerdict.Warning => "ATENÇÃO", _ => "ESTÁVEL" };
        string reasons = warn.Count > 0 ? string.Join(" ", warn) : "Latência baixa, sem perda de pacotes e sem sinais de bufferbloat. Se o jogo ainda engasga, o problema provavelmente não é a conexão: veja o Gargalo e as temperaturas.";
        var metrics = new List<KeyValuePair<string, string>>();
        foreach (var p in pings) metrics.Add(new($"{p.Name} ({p.Address})", p.Text));
        var c = System.Globalization.CultureInfo.InvariantCulture;
        if (m.DnsMs is { } d) metrics.Add(new("Resolução de DNS", d.ToString("0", c) + " ms"));
        metrics.Add(new("Download", m.DownMbps is { } dn ? dn.ToString("0.#", c) + " Mbps" : "—"));
        metrics.Add(new("Upload", m.UpMbps is { } u ? u.ToString("0.#", c) + " Mbps" : "—"));
        if (m.LoadedMs is { } l) metrics.Add(new("Latência sob carga", l.ToString("0", c) + " ms (durante o download)"));
        metrics.Add(new("Adaptador", $"{m.Adapter.Name} · {m.Adapter.Type} · link {m.Adapter.Link}"));
        metrics.Add(new("IP / Gateway / DNS", $"{m.Adapter.Ip} / {m.Adapter.Gateway} / {m.Adapter.Dns}"));
        return new() { Device = "rede", Title = "Diagnóstico de rede", Meta = $"{m.Adapter.Type} · testado às {now:HH:mm:ss}", Verdict = verdict, Label = label, Reasons = reasons, Metrics = metrics };
    }
}

public static class NetworkTestRunner
{
    /// <summary>Same sequence as the original: latency/loss to router and two public DNS in parallel, DNS lookup, download with loaded ping, upload.</summary>
    public static async Task<HealthCard> RunAsync(INetworkProbe probe, Action<string, double> progress, CancellationToken ct, int pingCount = 30, int intervalMs = 200, int downloadSeconds = 8, int uploadSeconds = 6)
    {
        progress("Procurando a conexão ativa…", 0.02);
        var adapter = probe.ActiveAdapter();
        if (adapter is null) return NetworkVerdict.NoConnection();

        progress($"Medindo latência e perda de pacotes (≈ {pingCount * intervalMs / 1000} s)…", 0.08);
        var targets = new (string Name, IPAddress Addr)[] { ("Roteador", adapter.GatewayAddress), ("Internet · Cloudflare", IPAddress.Parse(NetworkEndpoints.Cloudflare)), ("Internet · Google", IPAddress.Parse(NetworkEndpoints.Google)) };
        var pings = await Task.WhenAll(targets.Select(async t => { var (rtts, sent) = await probe.PingLoopAsync(t.Addr, pingCount, intervalMs, ct); return PingStat.From(t.Name, t.Addr.ToString(), rtts, sent); }));
        ct.ThrowIfCancellationRequested();

        progress("Testando o DNS…", 0.30);
        double? dnsMs = null;
        try { dnsMs = await probe.DnsAsync(NetworkEndpoints.DnsName, ct); } catch (OperationCanceledException) { throw; } catch (Exception e) when (e is SocketException or IOException or HttpRequestException) { }

        progress($"Medindo a velocidade de download (≈ {downloadSeconds} s)…", 0.36);
        using var loadStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loadedPing = probe.PingLoopAsync(IPAddress.Parse(NetworkEndpoints.Cloudflare), 0, intervalMs, loadStop.Token);
        double? down = null; string? error = null;
        try { down = await probe.DownloadAsync(downloadSeconds, ct); }
        catch (OperationCanceledException) { loadStop.Cancel(); throw; }
        catch (Exception e) when (e is HttpRequestException or IOException or SocketException) { error = e.Message; }
        loadStop.Cancel();
        var (lrtts, _) = await loadedPing;
        double? loadedMs = lrtts.Count >= 5 ? lrtts.Average() : null;

        progress($"Medindo a velocidade de upload (≈ {uploadSeconds} s)…", 0.66);
        double? up = null;
        try { up = await probe.UploadAsync(uploadSeconds, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is HttpRequestException or IOException or SocketException) { error ??= e.Message; }

        progress("Analisando…", 0.95);
        var card = NetworkVerdict.Evaluate(new(adapter, pings, dnsMs, down, up, loadedMs, error), DateTime.Now);
        progress("Concluído", 1);
        return card;
    }
}

/// <summary>Real probe over System.Net (ICMP via the OS, HTTPS to speed.cloudflare.com). Writes nothing to disk.</summary>
public sealed class SystemNetworkProbe : INetworkProbe
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string AdapterType(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet (cabo)",
        _ => t.ToString()
    };

    public AdapterInfo? ActiveAdapter()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                            && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .OrderByDescending(n => n.Speed).FirstOrDefault();
            if (nic is null) return null;
            var props = nic.GetIPProperties();
            var gw = props.GatewayAddresses.First(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)).Address;
            string ip = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "—";
            string dns = props.DnsAddresses.FirstOrDefault(d => d.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "—";
            return new(nic.Name, AdapterType(nic.NetworkInterfaceType), nic.Speed > 0 ? $"{nic.Speed / 1_000_000.0:0} Mbps" : "—", ip, gw.ToString(), dns, gw);
        }
        catch (Exception e) when (e is NetworkInformationException or InvalidOperationException or PlatformNotSupportedException) { return null; }
    }

    public async Task<(List<double> Rtts, int Sent)> PingLoopAsync(IPAddress ip, int count, int intervalMs, CancellationToken ct)
    {
        var rtts = new List<double>(); int sent = 0; using var p = new Ping();
        for (int i = 0; (count <= 0 || i < count) && !ct.IsCancellationRequested; i++)
        {
            var sw = Stopwatch.StartNew(); sent++;
            try { var r = await p.SendPingAsync(ip, 1500); if (r.Status == IPStatus.Success) rtts.Add(Math.Max(1, r.RoundtripTime)); }
            catch (PingException) { } catch (InvalidOperationException) { }
            int wait = intervalMs - (int)sw.ElapsedMilliseconds;
            if (wait > 0) { try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { break; } }
        }
        return (rtts, sent);
    }

    public async Task<double?> DownloadAsync(int seconds, CancellationToken ct)
    {
        long total = 0; var sw = Stopwatch.StartNew(); using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(seconds * 1000);
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try
            {
                var buf = new byte[81920];
                while (!cts.IsCancellationRequested)
                {
                    using var resp = await Http.GetAsync(NetworkEndpoints.Download, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    resp.EnsureSuccessStatusCode();
                    using var st = await resp.Content.ReadAsStreamAsync(cts.Token);
                    int n; while ((n = await st.ReadAsync(buf, cts.Token)) > 0) Interlocked.Add(ref total, n);
                }
            }
            catch (OperationCanceledException) { } catch (HttpRequestException) { }
        })).ToArray();
        await Task.WhenAll(tasks); ct.ThrowIfCancellationRequested();
        double secs = sw.Elapsed.TotalSeconds; return total > 0 && secs > 0 ? total * 8 / 1e6 / secs : null;
    }

    public async Task<double?> UploadAsync(int seconds, CancellationToken ct)
    {
        long total = 0; var sw = Stopwatch.StartNew(); using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(seconds * 1000);
        var payload = new byte[1_000_000]; new Random(1).NextBytes(payload);
        var tasks = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    using var resp = await Http.PostAsync(NetworkEndpoints.Upload, new ByteArrayContent(payload), cts.Token);
                    resp.EnsureSuccessStatusCode(); Interlocked.Add(ref total, payload.Length);
                }
            }
            catch (OperationCanceledException) { } catch (HttpRequestException) { }
        })).ToArray();
        await Task.WhenAll(tasks); ct.ThrowIfCancellationRequested();
        double secs = sw.Elapsed.TotalSeconds; return total > 0 && secs > 0 ? total * 8 / 1e6 / secs : null;
    }

    public async Task<double?> DnsAsync(string host, CancellationToken ct) { var sw = Stopwatch.StartNew(); await Dns.GetHostAddressesAsync(host, ct); return sw.Elapsed.TotalMilliseconds; }
}
