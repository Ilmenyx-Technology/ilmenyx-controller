// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
namespace Ilmenyx.Core;

internal static class ReportFile
{
    // A temporary file beside the destination guarantees rename stays on the same filesystem.
    internal static async Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(L.T("Escolha um arquivo de destino."), nameof(path));
        ct.ThrowIfCancellationRequested();
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".ilmenyx-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool created = false;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                created = true;
                await write(output, ct);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (created && File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
