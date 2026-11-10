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
    public string? ResolveLink(string path)
    {
        try { return new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }
    public IReadOnlyList<string> BlockDevices() => Directory.EnumerateDirectories("/sys/block")
        .Take(257).Select(p => Path.GetFileName(p)).ToArray();
}
