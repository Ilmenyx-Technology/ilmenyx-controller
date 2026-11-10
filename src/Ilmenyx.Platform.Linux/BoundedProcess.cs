// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Diagnostics;
using System.Text;

namespace Ilmenyx.Platform.Linux;

/// <summary>Runs one local program without a shell: fixed directories only, bounded output, hard timeout, never elevated.</summary>
public static class BoundedProcess
{
    // /usr/games: Debian/Ubuntu/Mint install gamemoderun there; without it the built-in fallback replaced the real GameMode (seen on Mint 22.3, 2026-11-06).
    private static readonly string[] Directories = ["/usr/bin", "/usr/sbin", "/bin", "/sbin", "/usr/local/bin", "/usr/games"];
    /// <summary>Host program path. In Flatpak it is looked up in the host's directories (/run/host) and run there by <see cref="HostSpawn"/>.</summary>
    public static string? FindSystem(string name) => name.Contains('/') ? null : Directories.Select(d => Path.Combine(d, name)).FirstOrDefault(p => File.Exists(HostSpawn.HostPath(p)));

    /// <summary>System program first; for the integrated tools (mangohud, gamemoderun, openrgb) the copy that ships in the package is the fallback.</summary>
    public static string? Find(string name)
    {
        if (name.Contains('/')) return null;
        if (BundledComponents.PreferBundled && BundledComponents.Resolve(name) is { } preferred) return preferred;
        return FindSystem(name) ?? BundledComponents.Resolve(name);
    }

    public static async Task<(int ExitCode, string Output)> RunAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, int maxCharacters = 65536, CancellationToken ct = default)
    {
        string path = Find(program) ?? throw new FileNotFoundException($"{program} não encontrado nos diretórios padrão.");
        var start = HostSpawn.Start(path, arguments, new Dictionary<string, string> { ["LC_ALL"] = "C" });
        using var process = new Process { StartInfo = start };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct); stop.CancelAfter(timeout);
        if (!process.Start()) throw new IOException($"Não foi possível iniciar {program}.");
        try
        {
            async Task<string> Bounded(StreamReader reader)
            {
                var sb = new StringBuilder(); var buffer = new char[2048]; int n;
                while ((n = await reader.ReadAsync(buffer.AsMemory(), stop.Token)) != 0) { if (sb.Length + n > maxCharacters) throw new IOException("Saída excede o limite."); sb.Append(buffer, 0, n); }
                return sb.ToString();
            }
            var output = Bounded(process.StandardOutput); var errors = Bounded(process.StandardError);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(stop.Token));
            return (process.ExitCode, await output);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException($"{program} excedeu {timeout.TotalSeconds:0} s."); }
        finally
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } await process.WaitForExitAsync(CancellationToken.None); }
        }
    }
}
