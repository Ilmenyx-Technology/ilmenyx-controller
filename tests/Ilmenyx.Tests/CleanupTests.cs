// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class CleanupTests
{
    [Fact]
    public void RealToolOutputsAreParsed()
    {
        Assert.Equal((long)(4.36 * (1L << 30)), Cleanup.ParsePaccache("==> finished dry run: 567 candidates (disk space saved: 4.36 GiB)")); // CachyOS, 2026-11-11
        Assert.Equal(0, Cleanup.ParsePaccache("==> no candidate packages found for pruning"));
        Assert.Equal((long)(47.7 * (1L << 20)), Cleanup.ParseJournal("Archived and active journals take up 47.7M in the file system."));
        Assert.Null(Cleanup.ParseJournal("No journal files were found."));
    }

    [Theory]
    [InlineData("cachyos", "arch", true, "paccache -rk2")]
    [InlineData("arch", "", false, "pacman -Sc --noconfirm")]
    [InlineData("linuxmint", "ubuntu debian", false, "apt-get clean")]
    [InlineData("fedora", "", false, "dnf clean packages")]
    [InlineData("opensuse-tumbleweed", "suse opensuse", false, "zypper clean --all")]
    public void PackageCachePlanPerDistro(string id, string like, bool paccache, string command) => Assert.Equal(command, Cleanup.PackagePlan(id, like, paccache).Command);

    [Fact]
    public void StaleFilesSkipsHiddenFoldersSmallAndRecentFiles()
    {
        string home = Directory.CreateTempSubdirectory("ilx-home").FullName;
        try
        {
            var now = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
            string Make(string rel, long size, int daysAgo)
            {
                string p = Path.Combine(home, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                using (var f = File.Create(p)) f.SetLength(size);
                File.SetLastWriteTimeUtc(p, now.AddDays(-daysAgo)); File.SetLastAccessTimeUtc(p, now.AddDays(-daysAgo));
                return p;
            }
            string old = Make("Downloads/ubuntu.iso", 60L << 20, 94);
            Make("Downloads/new.iso", 60L << 20, 3);
            Make("Downloads/small.txt", 1 << 20, 200);
            Make(".local/share/Steam/game.pak", 90L << 20, 300);
            var found = Assert.Single(Cleanup.StaleFiles(home, now, CancellationToken.None));
            Assert.Equal(old, found.Path); Assert.Equal(94, found.DaysUnused);
            Assert.Equal(0, Cleanup.DeleteFiles([new StaleFile("/etc/passwd", 1, 99)], home, CancellationToken.None)); // outside home: refused
            Assert.Equal(60L << 20, Cleanup.DeleteFiles([found], home, CancellationToken.None));
            Assert.False(File.Exists(old));
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public void EmptyDirectoryKeepsTheFolderAndTheSkippedEntry()
    {
        string root = Directory.CreateTempSubdirectory("ilx-cache").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "thumbnails/large")); File.WriteAllBytes(Path.Combine(root, "thumbnails/large/a.png"), new byte[1000]);
            Directory.CreateDirectory(Path.Combine(root, "ilmenyx")); File.WriteAllBytes(Path.Combine(root, "ilmenyx/keep"), new byte[10]);
            File.WriteAllBytes(Path.Combine(root, "loose.bin"), new byte[500]);
            Assert.Equal(1510, Cleanup.DirectorySize(root, CancellationToken.None));
            Assert.Equal(1500, Cleanup.EmptyDirectory(root, CancellationToken.None, skip: "ilmenyx"));
            Assert.True(File.Exists(Path.Combine(root, "ilmenyx/keep"))); Assert.False(Directory.Exists(Path.Combine(root, "thumbnails")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OnlyFixedRootCommandsRun()
    {
        var (ok, _, freed) = await Cleanup.RunAsync(new CleanupItem(CleanupKind.PackageCache, "x", "x", 10, true, true, "rm -rf /"), CancellationToken.None);
        Assert.False(ok); Assert.Equal(0, freed);
    }
}
