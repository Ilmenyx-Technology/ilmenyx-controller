// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Security.Cryptography;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Download of a pinned runner archive (Proton, Wine) into Ilmenyx's runners folder: exact size and hash checked while streaming, the
/// archive extracted with the system tar into a temporary folder and renamed into place only when complete. Throws on any mismatch.
/// </summary>
public static class RunnerDownload
{
    /// <param name="hash">Expected digest in hex of <paramref name="algorithm"/> (SHA-256 or SHA-512).</param>
    /// <param name="stripTopFolder">The archive has one top folder whose name differs from <paramref name="target"/>.</param>
    public static async Task FetchAsync(HttpClient http, string url, long size, HashAlgorithmName algorithm, string hash, string target, string name,
        IProgress<double>? progress, CancellationToken ct, bool stripTopFolder = false)
    {
        string dir = Path.GetDirectoryName(target)!; Directory.CreateDirectory(dir);
        string archive = target + ".part", staging = target + ".extracting";
        try
        {
            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long len && len != size) throw new IOException(L.T($"Tamanho inesperado do {name}: {len} bytes."));
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(archive);
                using var digest = IncrementalHash.CreateHash(algorithm);
                var buffer = new byte[1 << 20]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct); digest.AppendData(buffer, 0, read); total += read;
                    if (total > size) throw new IOException(L.T($"O {name} ficou maior que o esperado."));
                    progress?.Report(0.9 * total / size);
                }
                if (total != size || !Convert.ToHexString(digest.GetHashAndReset()).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(L.T($"O arquivo baixado do {name} não confere com a soma oficial ({algorithm.Name}); foi descartado."));
            }
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            string[] args = ["-xf", archive, "-C", staging, "--no-same-owner", .. stripTopFolder ? new[] { "--strip-components=1" } : []];
            var (code, output) = await BoundedProcess.RunAsync("tar", args, TimeSpan.FromMinutes(10), 4096, ct);
            if (code != 0) throw new IOException(L.T("Não consegui extrair o ") + name + ": " + output.Trim());
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(stripTopFolder ? staging : Path.Combine(staging, Path.GetFileName(target)), target);
            progress?.Report(1);
        }
        finally
        {
            try { File.Delete(archive); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
