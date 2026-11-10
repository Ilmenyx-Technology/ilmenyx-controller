// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Security.Cryptography;
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

/// <summary>
/// Ilmenyx's own plain Wine for the "Wine comum (Soda)" installer mode, so nobody needs Bottles: Bottles' Soda 11.0-10 (LGPL-2.1-or-later),
/// the exact build that installed the large repack on both test machines (2026-11-08 and 2026-11-10). Downloaded once, after the user's
/// confirmation, from the Bottles project's GitHub release, checked against the pinned SHA-256 and kept in ~/.local/share/ilmenyx/runners
/// as wine-soda-11.0-10, where <see cref="PlainWine.Find"/> prefers it. Downloaded from upstream, not redistributed by Ilmenyx.
/// </summary>
public static class PortableWine
{
    public const string Version = "soda-11.0-10";
    public const string Url = "https://github.com/bottlesdevs/wine/releases/download/soda-11.0-10/soda-11.0-10-x86_64.tar.xz";
    public const string Sha256 = "14633fabd180d308d2c1ebcb84a16f36d0484dded5809c64f8a7c93e654d7296";
    public const long Size = 174_028_776;

    public static string Folder(string? root = null) => Path.Combine(root ?? ClassicProton.Root(), "wine-" + Version);

    public static bool IsInstalled(string? root = null) => PlainWine.IsUsable(Folder(root));

    /// <summary>Downloads, verifies and extracts the Soda build. Progress is 0..1. Throws on any mismatch.</summary>
    public static async Task EnsureAsync(HttpClient http, IProgress<double>? progress, CancellationToken ct, string? root = null)
    {
        if (IsInstalled(root)) return;
        await RunnerDownload.FetchAsync(http, Url, Size, HashAlgorithmName.SHA256, Sha256, Folder(root), "Wine " + Version, progress, ct, stripTopFolder: true);
        if (!IsInstalled(root)) throw new IOException("O Wine " + Version + L.T(" foi extraído, mas não tem bin/wine e bin/wineserver."));
    }
}
