// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class RunnerCatalogTests
{
    private const string Ge = "GloriousEggroll/proton-ge-custom";
    private static string Asset(string repo, string tag, string file, long size) => $"{{\"name\":\"{file}\",\"size\":{size},\"browser_download_url\":\"https://github.com/{repo}/releases/download/{tag}/{file}\"}}";

    [Fact]
    public void OnlyStableX64ArchivesWithTheirChecksumFromTheRepositoryItselfAreListed()
    {
        // Shapes seen on GitHub, 2026-11-12: GE names the archive "-x86_64" (and ships aarch64), UMU-Proton and old GE do not.
        string json = "[" + string.Join(",",
            $"{{\"tag_name\":\"GE-Proton11-12\",\"prerelease\":false,\"assets\":[{Asset(Ge, "GE-Proton11-12", "GE-Proton11-12-aarch64.tar.gz", 650286473)},{Asset(Ge, "GE-Proton11-12", "GE-Proton11-12-x86_64.sha512sum", 159)},{Asset(Ge, "GE-Proton11-12", "GE-Proton11-12-x86_64.tar.gz", 566859228)}]}}",
            $"{{\"tag_name\":\"GE-Proton9-27\",\"prerelease\":false,\"assets\":[{Asset(Ge, "GE-Proton9-27", "GE-Proton9-27.sha512sum", 150)},{Asset(Ge, "GE-Proton9-27", "GE-Proton9-27.tar.gz", 488766224)}]}}",
            $"{{\"tag_name\":\"GE-Proton12-beta\",\"prerelease\":true,\"assets\":[{Asset(Ge, "GE-Proton12-beta", "GE-Proton12-beta.sha512sum", 150)},{Asset(Ge, "GE-Proton12-beta", "GE-Proton12-beta.tar.gz", 5)}]}}",
            $"{{\"tag_name\":\"no-checksum\",\"prerelease\":false,\"assets\":[{Asset(Ge, "no-checksum", "no-checksum.tar.gz", 5)}]}}",
            $"{{\"tag_name\":\"../../etc\",\"prerelease\":false,\"assets\":[]}}",
            "{\"tag_name\":\"elsewhere\",\"prerelease\":false,\"assets\":[{\"name\":\"elsewhere.tar.gz\",\"size\":5,\"browser_download_url\":\"https://evil.example/elsewhere.tar.gz\"},{\"name\":\"elsewhere.sha512sum\",\"size\":5,\"browser_download_url\":\"https://evil.example/elsewhere.sha512sum\"}]}") + "]";
        var list = RunnerCatalog.Parse(json, Ge);
        Assert.Equal(["GE-Proton11-12", "GE-Proton9-27"], list.Select(r => r.Name));
        Assert.Equal(566859228, list[0].Size);
        Assert.EndsWith("GE-Proton11-12-x86_64.tar.gz", list[0].Url); Assert.EndsWith("GE-Proton11-12-x86_64.sha512sum", list[0].Sha512Url);
        Assert.Empty(RunnerCatalog.Parse("{\"message\":\"API rate limit exceeded\"}", Ge));
    }

    [Fact]
    public void ChecksumFileGivesTheHashOrNothing()
    {
        string hex = new('a', 128);
        Assert.Equal(hex, RunnerCatalog.ParseSha512(hex + "  UMU-Proton-9.0-3.tar.gz\n"));
        Assert.Null(RunnerCatalog.ParseSha512("<html>not found</html>")); Assert.Null(RunnerCatalog.ParseSha512(new string('a', 64) + "  x"));
    }

    /// <summary>Opt-in (ILMENYX_NET_TESTS=1): lists both sources on GitHub and installs the smallest UMU-Proton for real (~420 MB).</summary>
    [Fact]
    public async Task RealListAndInstall()
    {
        if (Environment.GetEnvironmentVariable("ILMENYX_NET_TESTS") != "1" || !OperatingSystem.IsLinux()) return;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        Assert.NotEmpty(await RunnerCatalog.ListAsync(http, Ge, CancellationToken.None));
        var umu = await RunnerCatalog.ListAsync(http, "Open-Wine-Components/umu-proton", CancellationToken.None);
        var smallest = umu.OrderBy(r => r.Size).First();
        string root = Directory.CreateTempSubdirectory("ilx-runner").FullName;
        try
        {
            string folder = await RunnerCatalog.InstallAsync(http, smallest, null, CancellationToken.None, root);
            Assert.True(GameRunners.IsProton(folder)); Assert.Equal(smallest.Name, Path.GetFileName(folder));
            Assert.Equal([smallest.Name], Directory.GetFileSystemEntries(root).Select(Path.GetFileName)); // no .part or staging left
        }
        finally { Directory.Delete(root, true); }
    }
}
