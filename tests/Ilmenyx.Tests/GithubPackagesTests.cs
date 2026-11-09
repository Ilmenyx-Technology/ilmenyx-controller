// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class GithubPackagesTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "ilx-gh-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(home, true); } catch (IOException) { } }

    [Theory]
    [InlineData("apt-get dpkg", PackageFormat.Deb)]      // Debian, Ubuntu, Mint
    [InlineData("dnf", PackageFormat.Rpm)]               // Fedora, RHEL
    [InlineData("zypper", PackageFormat.Rpm)]            // openSUSE
    [InlineData("pacman", PackageFormat.Pacman)]         // Arch, Manjaro, CachyOS
    [InlineData("", PackageFormat.AppImage)]             // anything else: no root needed
    public void ThePackageFollowsTheDistribution(string tools, PackageFormat expected) =>
        Assert.Equal(expected, GithubPackages.Choose(t => tools.Split(' ').Contains(t)));

    [Fact]
    public void AssetNamesAndChecksumsMatchTheReleaseLayout()
    {
        Assert.Equal("photocraft-0.21.0-linux-x86_64.deb", GithubPackages.AssetName("photocraft", "v0.21.0", "x86_64", PackageFormat.Deb));
        Assert.Equal("photocraft-0.21.0-linux-aarch64.pkg.tar.zst", GithubPackages.AssetName("photocraft", "v0.21.0", "aarch64", PackageFormat.Pacman));
        string sums = new string('a', 64) + "  x.deb\n" + new string('B', 64) + " *y.AppImage\n";
        Assert.Equal(new string('b', 64), GithubPackages.Sha256For(sums, "y.AppImage"));
        Assert.Null(GithubPackages.Sha256For(sums, "z.rpm"));
        Assert.False(GithubPackages.ValidRepo("../etc"));
        Assert.True(GithubPackages.ValidRepo("storytold/photocraft"));
    }

    [Fact]
    public void ArtCraftAppsAreInTheCatalogNotPreSelected()
    {
        var craft = AppCatalog.Default().Where(a => a.FromGithub).ToList();
        Assert.Contains(craft, a => a.PackageName == "artcraft-launcher" && a.GithubRepo == "storytold/craft-launcher");
        Assert.Contains(craft, a => a.PackageName == "wordcraft");
        Assert.All(craft, a => { Assert.False(a.PreSelect); Assert.Equal("Criação e escritório", a.Category); Assert.StartsWith("gh.", a.Key); });
    }

    private sealed class FakeGithub(byte[] package, string sumsHash, string ext = ".AppImage") : HttpMessageHandler
    {
        public readonly List<string> Urls = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            string url = r.RequestUri!.ToString(); Urls.Add(url);
            HttpContent body = url.Contains("/releases/latest")
                ? new StringContent($$"""
                    {"tag_name":"v1.2.0","assets":[
                    {"name":"demo-1.2.0-linux-x86_64{{ext}}","size":{{package.Length}},"browser_download_url":"https://github.com/o/demo/releases/download/v1.2.0/demo-1.2.0-linux-x86_64{{ext}}"},
                    {"name":"SHA256SUMS.txt","size":100,"browser_download_url":"https://github.com/o/demo/releases/download/v1.2.0/SHA256SUMS.txt"}]}
                    """)
                : url.EndsWith("SHA256SUMS.txt") ? new StringContent(sumsHash + "  demo-1.2.0-linux-x86_64" + ext + "\n")
                : new ByteArrayContent(package);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }

    [Fact]
    public async Task AVerifiedAppImageIsPlacedForTheUserAndAMismatchInstallsNothing()
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64) return;
        byte[] pkg = [1, 2, 3, 4, 5];
        var app = new InstallableApp { Name = "Demo", GithubRepo = "o/demo", PackageName = "demo" };
        bool ran = false;
        Task<(int, string)> Runner(string p, IReadOnlyList<string> a, TimeSpan t, CancellationToken c) { ran = true; return Task.FromResult((0, "")); }

        var bad = new GithubAppInstaller(Runner, new FakeGithub(pkg, new string('0', 64)), _ => false, home);
        var (ok, msg) = await bad.InstallAsync(app, null, CancellationToken.None);
        Assert.False(ok); Assert.Contains("SHA-256", msg);
        Assert.False(File.Exists(bad.AppImagePath(app)));

        var progress = new List<double>();
        var good = new GithubAppInstaller(Runner, new FakeGithub(pkg, Convert.ToHexString(SHA256.HashData(pkg)).ToLowerInvariant()), _ => false, home);
        (ok, msg) = await good.InstallAsync(app, new SyncProgress(progress.Add), CancellationToken.None);
        Assert.True(ok, msg);
        Assert.Equal(pkg, File.ReadAllBytes(good.AppImagePath(app)));
        Assert.True(File.Exists(Path.Combine(home, ".local/share/applications/ilmenyx-demo.desktop")));
        Assert.False(ran); // AppImage: no root, no package tool
        Assert.NotEmpty(progress);
        Assert.False(Directory.EnumerateFileSystemEntries(Path.Combine(home, ".cache/ilmenyx/downloads")).Any()); // download folder cleaned
    }

    [Fact]
    public async Task ADebIsInstalledByAptThroughPkexecCalledByName()
    {
        if (!OperatingSystem.IsLinux() || BoundedProcess.FindSystem("pkexec") is null || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64) return;
        byte[] pkg = [9, 8, 7];
        var app = new InstallableApp { Name = "Demo", GithubRepo = "o/demo", PackageName = "demo" };
        string? program = null; IReadOnlyList<string>? args = null;
        Task<(int, string)> Runner(string p, IReadOnlyList<string> a, TimeSpan t, CancellationToken c) { program = p; args = a; return Task.FromResult((0, "")); }
        var handler = new FakeGithub(pkg, Convert.ToHexString(SHA256.HashData(pkg)).ToLowerInvariant(), ".deb");
        var (ok, msg) = await new GithubAppInstaller(Runner, handler, t => t is "apt-get" or "dpkg", home).InstallAsync(app, null, CancellationToken.None);
        Assert.True(ok, msg);
        Assert.Equal("pkexec", program); // the runner refuses paths: a full /usr/bin/pkexec failed on the owner's PC (2026-11-08)
        Assert.EndsWith("apt-get", args![0]); Assert.Equal("install", args[1]); Assert.EndsWith("demo-1.2.0-linux-x86_64.deb", args[^1]);
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
}
