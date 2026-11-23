// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Closes a game on request: SIGTERM (or SIGKILL when forced) only to the PIDs <see cref="GameProcesses"/> finds for that game right now,
/// never to Ilmenyx itself, then waits for them to go. The PID list is searched again before every signal, so a PID reused by another
/// program after the game ended is never hit. Flatpak: the signal goes through the host (<c>kill</c> run by <see cref="BoundedProcess"/>).
/// </summary>
public static class GameCloser
{
    public delegate Task SignalFunc(int pid, bool force, CancellationToken ct);

    /// <summary>True when the game's processes are gone within <paramref name="wait"/> (nothing found counts as gone).</summary>
    public static async Task<bool> CloseAsync(Func<CancellationToken, Task<IReadOnlyList<int>>> find, SignalFunc signal, bool force, TimeSpan wait, CancellationToken ct, TimeSpan? poll = null)
    {
        int self = Environment.ProcessId;
        foreach (int pid in (await find(ct)).Where(p => p > 1 && p != self).Distinct()) await signal(pid, force, ct);
        var step = poll ?? TimeSpan.FromMilliseconds(500); var end = DateTime.UtcNow + wait;
        while (true)
        {
            if ((await find(ct)).Count == 0) return true;
            if (DateTime.UtcNow >= end) return false;
            await Task.Delay(step, ct);
        }
    }

    public static async Task SignalAsync(int pid, bool force, CancellationToken ct)
    {
        if (pid <= 1 || pid == Environment.ProcessId) return;
        if (HostSpawn.InFlatpak) { await BoundedProcess.RunAsync("kill", [force ? "-KILL" : "-TERM", pid.ToString(CultureInfo.InvariantCulture)], TimeSpan.FromSeconds(3), 4096, ct); return; }
        _ = Kill(pid, force ? 9 : 15); // a process that already left (ESRCH) is not an error
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
