// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Core;
namespace Ilmenyx.Platform.Linux;

public interface ILinuxFiles
{
    Task<string> ReadAsync(string path, CancellationToken ct);
    IReadOnlyList<string> BlockDevices();
    /// <summary>Final target of a symbolic link, or null when unavailable. Default for test doubles.</summary>
    string? ResolveLink(string path) => null;
}
public sealed class LinuxFiles : ILinuxFiles
{
    public const int MaxCharacters = 4 * 1024 * 1024;
    public async Task<string> ReadAsync(string path, CancellationToken ct)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true));
        var buffer = new char[8192];
        var result = new System.Text.StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (result.Length + count > MaxCharacters) throw new IOException(L.T("Fonte excede limite de 4 Mi caracteres."));
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
    /// <summary>
    /// Lean synchronous read of a small kernel file (/proc, /sys): no async handle and one pooled buffer, instead of the ~32 KB that
    /// <see cref="ReadAsync"/> allocates per file. It blocks the caller, so use it only off the UI thread (the usage sampler reads
    /// ~300 files per second this way). Same 4 Mi limit.
    /// </summary>
    public static string ReadSmall(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 0);
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(4096);
        int total = 0;
        try
        {
            int count;
            while ((count = stream.Read(buffer, total, buffer.Length - total)) > 0)
            {
                total += count;
                if (total > MaxCharacters) throw new IOException(L.T("Fonte excede limite de 4 Mi caracteres."));
                if (total < buffer.Length) continue;
                byte[] bigger = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                Buffer.BlockCopy(buffer, 0, bigger, 0, total);
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                buffer = bigger;
            }
            return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
    }
    public string? ResolveLink(string path)
    {
        try { return new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }
    public IReadOnlyList<string> BlockDevices() => Directory.EnumerateDirectories("/sys/block")
        .Take(257).Select(p => Path.GetFileName(p)).ToArray();
}
