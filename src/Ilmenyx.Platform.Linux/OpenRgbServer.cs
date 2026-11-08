// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
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
        var start = HostSpawn.Start(program, ["--server", "--server-host", "127.0.0.1", "--server-port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--noautoconnect", "--config", ConfigFolder()]);
        var p = new Process { StartInfo = start };
        if (!p.Start()) throw new IOException("Não foi possível iniciar o OpenRGB.");
        var errors = new System.Text.StringBuilder();
        _ = DrainAsync(p.StandardOutput, null); _ = DrainAsync(p.StandardError, errors);   // keep the pipes from filling; only a short error tail is kept
        var server = new OpenRgbServer(p, port, program);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(readyTimeout);
            while (true)
            {
                if (p.HasExited) { await Task.Delay(100, CancellationToken.None); throw new IOException($"O OpenRGB encerrou ao iniciar (código {p.ExitCode}). {Tail(errors)}".TrimEnd()); }
                using var tcp = new TcpClient();
                try { await tcp.ConnectAsync(IPAddress.Loopback, port, limit.Token); return server; }
                catch (SocketException) { await Task.Delay(300, limit.Token); }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { await server.DisposeAsync(); throw new TimeoutException("O servidor do OpenRGB não respondeu a tempo."); }
        catch { await server.DisposeAsync(); throw; }
    }

    private static string Tail(System.Text.StringBuilder errors)
    {
        string text; lock (errors) text = errors.ToString();
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault(l => l.Contains("shared librar", StringComparison.Ordinal)) ?? text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return line is null ? "" : line.Length > 200 ? line[..200] : line;
    }

    private static async Task DrainAsync(StreamReader reader, System.Text.StringBuilder? keep)
    {
        var buffer = new char[1024];
        try { while (true) { int n = await reader.ReadAsync(buffer.AsMemory()); if (n == 0) break; if (keep is not null) lock (keep) { if (keep.Length < 8192) keep.Append(buffer, 0, n); } } } catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
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
