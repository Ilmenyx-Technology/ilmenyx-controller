// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Temporary files only; no network.
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class GameCoversTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-covers-" + Guid.NewGuid().ToString("N"));
    public GameCoversTests() { Directory.CreateDirectory(root); }
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private string Make(string name, byte[] head, int size = 64)
    {
        string p = Path.Combine(root, name); var b = new byte[size]; head.CopyTo(b, 0); File.WriteAllBytes(p, b); return p;
    }
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0];

    [Theory]
    [InlineData("a.png", true)] [InlineData("a.jpg", false)]
    public void CopiesImagesIntoTheCoverFolder(string name, bool png)
    {
        string src = Make(name, png ? Png : Jpg), folder = Path.Combine(root, "covers");
        string dest = GameCovers.CopyLocal(src, folder, "Linux:x");
        Assert.NotEqual(src, dest); Assert.Equal(folder, Path.GetDirectoryName(dest)); Assert.EndsWith(Path.GetExtension(name), dest);
        Assert.Equal(16 + Path.GetExtension(name).Length, Path.GetFileName(dest).Length);
        Assert.True(File.Exists(src));
        // A new image for the same game replaces the old copy, even with another extension.
        string again = GameCovers.CopyLocal(Make("b.jpg", Jpg), folder, "Linux:x");
        Assert.Single(Directory.GetFiles(folder)); Assert.True(File.Exists(again));
    }

    [Fact]
    public void RejectsTextOversizedAndSymlinks()
    {
        string folder = Path.Combine(root, "covers");
        Assert.Throws<CoverException>(() => GameCovers.CopyLocal(Make("t.png", "hello world, not an image at all"u8.ToArray()), folder, "k"));
        Assert.Throws<CoverException>(() => GameCovers.CopyLocal(Make("big.png", Png, 200), folder, "k", maxBytes: 100));
        string real = Make("r.png", Png), link = Path.Combine(root, "l.png"); File.CreateSymbolicLink(link, real);
        Assert.Throws<CoverException>(() => GameCovers.CopyLocal(link, folder, "k"));
        Assert.Throws<CoverException>(() => GameCovers.CopyLocal(Path.Combine(root, "nope.png"), folder, "k"));
        Assert.False(Directory.Exists(folder) && Directory.GetFiles(folder).Length > 0);
    }

    [Theory]
    [InlineData("http://example.com/a.png")] [InlineData("file:///etc/passwd")] [InlineData("https://user:pw@example.com/a.png")]
    [InlineData("example.com/a.png")] [InlineData("")] [InlineData(null)]
    public void UrlRejected(string? url) => Assert.Throws<CoverException>(() => GameCovers.ValidateUrl(url));

    [Fact] public void UrlHttpsAccepted() => Assert.Equal("example.com", GameCovers.ValidateUrl(" https://example.com/capa.png ").Host);

    [Fact]
    public void HiddenGamesAreFilteredUnlessAskedAndRoundTrip()
    {
        LibGame[] games = [new("Linux", "a", "A", ""), new("Steam", "1", "B", ""), new("Manual", "/x", "C", "")];
        Assert.Equal(["B", "C"], GameCovers.Visible(games, ["Linux:a"], false).Select(g => g.Name));
        Assert.Equal(3, GameCovers.Visible(games, ["Linux:a"], true).Count());
        string path = Path.Combine(root, "cfg/settings.json");
        IlmenyxSettings.Update(path, s => { s.HiddenGames.Add("Linux:a"); s.GameProfiles["Linux:a"] = new() { CoverPath = "/c/x.png" }; });
        var back = IlmenyxSettings.Load(path);
        Assert.Equal(["Linux:a"], back.HiddenGames); Assert.Equal("/c/x.png", back.GameProfiles["Linux:a"].CoverPath);
        File.WriteAllText(path, "{\"HiddenGames\":null}");
        Assert.Empty(IlmenyxSettings.Load(path).HiddenGames);
    }
}
