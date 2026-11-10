// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
namespace Ilmenyx.Platform.Linux;

/// <summary>Bounded read of one user-chosen regular file. Rejects relative paths, links, /dev /proc /sys, non-seekable and oversized files.</summary>
public static class SafeFileReader
{
    public static async Task<string> ReadTextAsync(string path, int maxCharacters, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException(L.T("Informe o caminho completo do arquivo."));
        string full = Path.GetFullPath(path);
        foreach (string prefix in new[] { "/dev/", "/proc/", "/sys/" })
            if (full.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException(L.T("Caminho de sistema não é aceito como arquivo de entrada."));
        ct.ThrowIfCancellationRequested();
        try
        {
            var reader = await Task.Run(() => Open(full, maxCharacters), ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
            using (reader)
            {
                var buffer = new char[4096]; var text = new System.Text.StringBuilder(); int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    if (text.Length + count > maxCharacters) throw new IOException(L.T("Arquivo excede o limite de ") + maxCharacters + " caracteres.");
                    text.Append(buffer, 0, count);
                }
                return text.ToString();
            }
        }
        catch (TimeoutException) { throw new TimeoutException(L.T("Timeout de 3 segundos abrindo o arquivo.")); }
    }

    private static StreamReader Open(string full, int maxCharacters)
    {
        var info = new FileInfo(full);
        if (info.LinkTarget is not null || !info.Exists) throw new ArgumentException(L.T("O arquivo deve existir e ser regular, sem link simbólico."));
        var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (!stream.CanSeek || stream.Length is 0 || stream.Length > (long)maxCharacters * 4) throw new IOException(L.T("Arquivo vazio, não regular ou acima do limite."));
            return new StreamReader(stream, System.Text.Encoding.UTF8, false);
        }
        catch { stream.Dispose(); throw; }
    }
}
