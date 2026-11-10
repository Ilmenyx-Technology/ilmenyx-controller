// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Arquivo .tar.xz sintético servido por um HttpMessageHandler falso: nada vai para a rede.
using System.Net;
using System.Security.Cryptography;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class RunnerDownloadTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("ilmenyx-runner-").FullName;
    public void Dispose() => Directory.Delete(root, true);

    private sealed class Serve(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    /// <summary>soda-x-x86_64/bin/{wine,wineserver} packed like the Bottles release.</summary>
    private byte[] Archive()
    {
        string src = Path.Combine(root, "src", "soda-x-x86_64", "bin"); Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "wine"), "#!/bin/sh\n"); File.WriteAllText(Path.Combine(src, "wineserver"), "#!/bin/sh\n");
        string tar = Path.Combine(root, "soda.tar.xz");
        var p = System.Diagnostics.Process.Start("tar", ["-cJf", tar, "-C", Path.Combine(root, "src"), "soda-x-x86_64"])!; p.WaitForExit();
        return File.ReadAllBytes(tar);
    }

    [Fact]
    public async Task VerifiedArchiveIsExtractedUnderOurOwnName()
    {
        var body = Archive(); string target = Path.Combine(root, "runners", "wine-soda-x");
        using var http = new HttpClient(new Serve(body));
        await RunnerDownload.FetchAsync(http, "https://exemplo/soda.tar.xz", body.Length, HashAlgorithmName.SHA256, Convert.ToHexString(SHA256.HashData(body)), target, "Wine x", null, CancellationToken.None, stripTopFolder: true);
        Assert.True(PlainWine.IsUsable(target));
        Assert.Equal(["wine-soda-x"], Directory.GetFileSystemEntries(Path.Combine(root, "runners")).Select(Path.GetFileName)); // no .part or .extracting left
    }

    [Fact]
    public async Task WrongHashOrSizeIsDiscarded()
    {
        var body = Archive(); string target = Path.Combine(root, "runners", "wine-soda-x");
        using var http = new HttpClient(new Serve(body));
        await Assert.ThrowsAsync<IOException>(() => RunnerDownload.FetchAsync(http, "https://exemplo/s", body.Length, HashAlgorithmName.SHA256, new string('0', 64), target, "Wine x", null, CancellationToken.None, true));
        await Assert.ThrowsAsync<IOException>(() => RunnerDownload.FetchAsync(http, "https://exemplo/s", body.Length + 1, HashAlgorithmName.SHA256, Convert.ToHexString(SHA256.HashData(body)), target, "Wine x", null, CancellationToken.None, true));
        Assert.False(Directory.Exists(target));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "runners")));
    }

    [Fact]
    public void OwnSodaIsPreferredOverBottles()
    {
        string home = Path.Combine(root, "home");
        foreach (var dir in new[] { Path.Combine(home, ".local/share/ilmenyx/runners/wine-soda-11.0-10/bin"), Path.Combine(home, ".var/app/com.usebottles.bottles/data/bottles/runners/soda-11.0-10/bin") })
        { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "wine"), ""); File.WriteAllText(Path.Combine(dir, "wineserver"), ""); }
        Assert.Equal(Path.Combine(home, ".local/share/ilmenyx/runners/wine-soda-11.0-10/bin/wine"), PlainWine.Find(home));
    }
}
