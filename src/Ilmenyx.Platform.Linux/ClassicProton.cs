// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Security.Cryptography;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// A Proton of the 9.x generation for heavy 32-bit installers. Proton 10/11 run 32-bit programs through Wine's new WoW64 mode; a
/// 32-bit decompressor that reserves ~1.3 GB in one block was refused there ("allocate_virtual_memory out of memory", owner's test
/// 2026-11-08, also with GE-Proton 11 and large-address-aware on), while a classic Wine build installed the same files. GE-Proton9-27
/// still ships the classic 32-bit layout. Downloaded once, after the user's confirmation, from the project's GitHub release, checked
/// against the pinned SHA-512 and kept in ~/.local/share/ilmenyx/runners. umu receives its folder as PROTONPATH.
/// </summary>
public static class ClassicProton
{
    public const string Version = "GE-Proton9-27";
    public const string Url = "https://github.com/GloriousEggroll/proton-ge-custom/releases/download/GE-Proton9-27/GE-Proton9-27.tar.gz";
    public const string Sha512 = "86a2b2962a2509104201b3532bc829d058d666bc8220417f71bd4af660b6d05781e9f684b3982339d695a5fd4babe19e97ec42a82a78311faf99fc1257280623";
    public const long Size = 488_766_224;

    public static string Root(string? dataHome = null)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string data = dataHome ?? (Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } d && Path.IsPathRooted(d) ? d : Path.Combine(home, ".local", "share"));
        return Path.Combine(data, "ilmenyx", "runners");
    }

    public static string Folder(string? root = null) => Path.Combine(root ?? Root(), Version);

    public static bool IsInstalled(string? root = null) => File.Exists(Path.Combine(Folder(root), "proton"));

    /// <summary>True when an installer's Proton log shows the 32-bit address-space failure that the classic mode avoids.</summary>
    public static bool LogShowsAddressSpaceExhaustion(string? logPath)
    {
        if (logPath is null || !File.Exists(logPath)) return false;
        try
        {
            using var reader = new StreamReader(logPath);
            int hits = 0; string? line;
            while ((line = reader.ReadLine()) is not null)
                if (line.Contains("allocate_virtual_memory out of memory", StringComparison.Ordinal) && ++hits >= 5) return true;
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Downloads (streaming SHA-512), verifies and extracts the classic Proton. Progress is 0..1. Throws on any mismatch.</summary>
    public static async Task EnsureAsync(HttpClient http, IProgress<double>? progress, CancellationToken ct, string? root = null)
    {
        if (IsInstalled(root)) return;
        string dir = root ?? Root(); Directory.CreateDirectory(dir);
        string archive = Path.Combine(dir, Version + ".tar.gz.part");
        try
        {
            using (var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long len && len != Size) throw new IOException($"Tamanho inesperado do {Version}: {len} bytes.");
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(archive);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
                var buffer = new byte[1 << 20]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct); sha.AppendData(buffer, 0, read); total += read;
                    if (total > Size) throw new IOException($"O {Version} ficou maior que o esperado.");
                    progress?.Report(0.9 * total / Size);
                }
                if (total != Size || !Convert.ToHexString(sha.GetHashAndReset()).Equals(Sha512, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"O arquivo baixado do {Version} não confere com a soma oficial (SHA-512); foi descartado.");
            }
            var (code, output) = await BoundedProcess.RunAsync("tar", ["-xzf", archive, "-C", dir, "--no-same-owner"], TimeSpan.FromMinutes(10), 4096, ct);
            if (code != 0 || !IsInstalled(root)) throw new IOException("Não consegui extrair o " + Version + ": " + output.Trim());
            progress?.Report(1);
        }
        finally { try { File.Delete(archive); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }
}
