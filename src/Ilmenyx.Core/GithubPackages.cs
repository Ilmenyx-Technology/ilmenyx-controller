// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

/// <summary>Which native package of a GitHub release fits this distribution.</summary>
public enum PackageFormat { Deb, Rpm, Pacman, AppImage }

/// <summary>Pure rules for installing an app from a GitHub release: package format per distribution, asset names, checksum lookup.</summary>
public static partial class GithubPackages
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9._-]{1,100}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepoPattern();
    [GeneratedRegex("^[a-z0-9][a-z0-9._+-]{0,80}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackagePattern();
    [GeneratedRegex("^v?[0-9][0-9A-Za-z.+-]{0,40}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    public static bool ValidRepo(string repo) => RepoPattern().IsMatch(repo) && !repo.Contains("..", StringComparison.Ordinal);
    public static bool ValidPackage(string name) => PackagePattern().IsMatch(name);
    public static bool ValidVersion(string tag) => VersionPattern().IsMatch(tag);

    /// <summary>The distribution's own package tool decides: apt (Debian, Ubuntu, Mint) → .deb; dnf/zypper (Fedora, RHEL, openSUSE) → .rpm;
    /// pacman (Arch, Manjaro, CachyOS) → .pkg.tar.zst; anything else → AppImage in ~/Applications (no root).</summary>
    public static PackageFormat Choose(Func<string, bool> hasTool)
    {
        if (hasTool("apt-get") && hasTool("dpkg")) return PackageFormat.Deb;
        if (hasTool("dnf") || hasTool("zypper")) return PackageFormat.Rpm;
        if (hasTool("pacman")) return PackageFormat.Pacman;
        return PackageFormat.AppImage;
    }

    public static string Label(PackageFormat f) => f switch
    {
        PackageFormat.Deb => "pacote .deb (apt)", PackageFormat.Rpm => "pacote .rpm (dnf/zypper)", PackageFormat.Pacman => "pacote do Arch (pacman)", _ => "AppImage (sem senha)",
    };

    /// <summary>"photocraft-0.21.0-linux-x86_64.deb" for tag "v0.21.0".</summary>
    public static string AssetName(string package, string tag, string arch, PackageFormat format) =>
        $"{package}-{tag.TrimStart('v')}-linux-{arch}" + format switch
        {
            PackageFormat.Deb => ".deb", PackageFormat.Rpm => ".rpm", PackageFormat.Pacman => ".pkg.tar.zst", _ => ".AppImage",
        };

    /// <summary>Linux architecture name used by the releases ("x86_64", "aarch64"); null when not supported.</summary>
    public static string? Arch(System.Runtime.InteropServices.Architecture a) => a switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x86_64", System.Runtime.InteropServices.Architecture.Arm64 => "aarch64", _ => null,
    };

    /// <summary>SHA-256 (lowercase hex) of <paramref name="file"/> in a SHA256SUMS text ("hash  name" or "hash *name"), or null.</summary>
    public static string? Sha256For(string sums, string file)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var parts = raw.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*', ' ') == file && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit)) return parts[0].ToLowerInvariant();
        }
        return null;
    }
}
