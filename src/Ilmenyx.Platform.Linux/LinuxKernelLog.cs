// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>Read-only reader of kernel hardware-error lines through journalctl (no dmesg, no privileges, bounded output).</summary>
public sealed class LinuxKernelLog(Func<IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Output)>>? run = null)
{
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Output)>> run =
        run ?? ((args, ct) => BoundedProcess.RunAsync("journalctl", args, TimeSpan.FromSeconds(8), 4 << 20, ct));
    private bool? readable;

    public const string NoAccess = "sem permissão para ler o log do kernel (entre no grupo systemd-journal ou adm para habilitar)";

    public async Task<KernelHardwareErrors> ErrorsSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        try
        {
            if (readable is null)
            {
                var (probeCode, probe) = await run(["-k", "-n", "1", "--no-pager", "-q", "-o", "cat"], ct);
                readable = probeCode == 0 && probe.Trim().Length > 0;
            }
            if (readable != true) return KernelHardwareErrors.Unavailable(NoAccess);
            string from = since.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
            var (code, output) = await run(["-k", "--since", from, "--no-pager", "-q", "-o", "short-iso"], ct);
            return code == 0 ? KernelLogParser.Parse(output) : KernelHardwareErrors.Unavailable($"journalctl terminou com código {code}");
        }
        catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or InvalidOperationException)
        {
            return KernelHardwareErrors.Unavailable(e.Message);
        }
    }

    /// <summary>Board vendor and name from /sys/class/dmi/id (world-readable); empty strings when absent (VMs, ARM).</summary>
    public static (string Vendor, string Name) Board(string root = "/sys/class/dmi/id")
    {
        string Read(string file) { try { return SysfsText.Read(Path.Combine(root, file), 128)?.Trim() ?? ""; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; } }
        return (Read("board_vendor"), Read("board_name"));
    }

    public static async Task<IReadOnlyList<NvidiaTuningState>> NvidiaStateAsync(CancellationToken ct = default)
    {
        try
        {
            var (code, output) = await BoundedProcess.RunAsync("nvidia-smi", [$"--query-gpu={NvidiaTuning.QueryFields}", "--format=csv,noheader,nounits"], TimeSpan.FromSeconds(3), 65536, ct);
            return code == 0 ? NvidiaTuning.Parse(output) : [];
        }
        catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException) { return []; }
    }
}
