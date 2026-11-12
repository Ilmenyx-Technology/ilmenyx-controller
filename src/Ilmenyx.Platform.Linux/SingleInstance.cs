// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Net.Sockets;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// One Ilmenyx window per user: a second launch asks the running copy to show itself and exits. Without this, a window hidden in the
/// notification area plus a click on the app menu left several copies running (seen 2026-11-12: three at once).
/// A Unix socket in the user's runtime folder; nothing travels on it, the connection itself is the "show yourself" request.
/// </summary>
public static class SingleInstance
{
    /// <summary>Flatpak shares only $XDG_RUNTIME_DIR/app/ID between copies of the app; null when there is no runtime folder (then no guard).</summary>
    public static string? DefaultPath(string appId) =>
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run && Directory.Exists(run)
            ? HostSpawn.InFlatpak ? Path.Combine(run, "app", appId, "ilmenyx.sock") : Path.Combine(run, "ilmenyx-controller.sock") : null;

    /// <summary>True when another copy answered (it now shows its window): the caller should exit.</summary>
    public static bool TryHandOff(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(path));
            return true;
        }
        catch (SocketException) { return false; } // stale socket of a copy that died: Listen replaces it
    }

    /// <summary>Starts answering second launches; <paramref name="shown"/> runs (on a pool thread) for each one. Failing to listen only disables the guard.</summary>
    public static void Listen(string path, Action shown, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Delete(path);
            var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            server.Bind(new UnixDomainSocketEndPoint(path)); server.Listen(4);
            ct.Register(() => { server.Dispose(); try { File.Delete(path); } catch (IOException) { } });
            _ = Task.Run(async () =>
            {
                try { while (!ct.IsCancellationRequested) { using var peer = await server.AcceptAsync(ct); shown(); } }
                catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
            }, CancellationToken.None);
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException) { }
    }
}
