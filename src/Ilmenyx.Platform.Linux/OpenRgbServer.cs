// New Ilmenyx additions: see LICENSING.md.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Starts the OpenRGB SDK server (system copy or the one bundled in the package) as a child of Ilmenyx, bound to 127.0.0.1 only,
/// with an Ilmenyx-owned configuration folder. Started only by an explicit click; stopped (process tree killed) on dispose.
/// </summary>
public sealed class OpenRgbServer : IAsyncDisposable
{
    private Process? process;
    public int Port { get; }
    public string Program { get; }

    private OpenRgbServer(Process process, int port, string program) { this.process = process; Port = port; Program = program; }

    public static string ConfigFolder() => Path.Combine(Path.GetDirectoryName(BundledComponents.DataFolder())!, "openrgb");

    public static async Task<OpenRgbServer> StartAsync(int port, TimeSpan readyTimeout, CancellationToken ct)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "Porta fora de 1024–65535.");
        string program = BoundedProcess.Find("openrgb") ?? throw new FileNotFoundException("OpenRGB não encontrado (nem no pacote, nem no sistema).");
        Directory.CreateDirectory(ConfigFolder());
        var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[] { "--server", "--server-host", "127.0.0.1", "--server-port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--noautoconnect", "--config", ConfigFolder() }) start.ArgumentList.Add(a);
        var p = new Process { StartInfo = start };
        if (!p.Start()) throw new IOException("Não foi possível iniciar o OpenRGB.");
        _ = DrainAsync(p.StandardOutput); _ = DrainAsync(p.StandardError);   // keep the pipes from filling; content is not kept
        var server = new OpenRgbServer(p, port, program);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(readyTimeout);
            while (true)
            {
                if (p.HasExited) throw new IOException($"O OpenRGB encerrou ao iniciar (código {p.ExitCode}).");
                using var tcp = new TcpClient();
                try { await tcp.ConnectAsync(IPAddress.Loopback, port, limit.Token); return server; }
                catch (SocketException) { await Task.Delay(300, limit.Token); }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { await server.DisposeAsync(); throw new TimeoutException("O servidor do OpenRGB não respondeu a tempo."); }
        catch { await server.DisposeAsync(); throw; }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        try { while (await reader.ReadAsync(buffer.AsMemory()) != 0) { } } catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    public bool IsRunning => process is { HasExited: false };

    public async ValueTask DisposeAsync()
    {
        var p = Interlocked.Exchange(ref process, null);
        if (p is null) return;
        try { if (!p.HasExited) { p.Kill(entireProcessTree: true); await p.WaitForExitAsync(CancellationToken.None); } } catch (InvalidOperationException) { }
        p.Dispose();
    }
}
