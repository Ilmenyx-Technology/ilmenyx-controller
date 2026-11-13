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

    [Fact]
    public void PackageDetailsGroupOldInstallersByProgram()
    {
        // paccache -dvk2 on CachyOS, 2026-11-11 (trimmed).
        const string output = "==> Candidate packages:\n7zip-26.04-1.1-x86_64_v4.pkg.tar.zst\n7zip-26.04-1.1-x86_64_v4.pkg.tar.zst.sig\nalsa-card-profiles-1:1.6.9-1-x86_64.pkg.tar.zst\n\n==> finished dry run: 3 candidates (disk space saved: 2.64 GiB)\n";
        Assert.Equal(["7zip-26.04-1.1-x86_64_v4.pkg.tar.zst", "7zip-26.04-1.1-x86_64_v4.pkg.tar.zst.sig", "alsa-card-profiles-1:1.6.9-1-x86_64.pkg.tar.zst"], Cleanup.PaccacheCandidates(output));
        Assert.Equal("alsa-card-profiles", Cleanup.PackageName("alsa-card-profiles-1:1.6.9-1-x86_64.pkg.tar.zst"));
        Assert.Equal("7zip", Cleanup.PackageName("7zip-26.04-1.1-x86_64_v4.pkg.tar.zst.sig"));
        Assert.Equal("libc6", Cleanup.PackageName("libc6_2.39-0ubuntu8_amd64.deb"));
        Assert.Equal("kernel-core", Cleanup.PackageName("kernel-core-6.17.1-300.fc43.x86_64.rpm"));
        // The same listing the host's find gives inside the Flatpak (names, sizes, times).
        var files = Cleanup.ParseFindListing("7zip-26.04-1.1-x86_64_v4.pkg.tar.zst\t700\t1762000000.5\n7zip-26.04-1.1-x86_64_v4.pkg.tar.zst.sig\t10\t1762000000\n"
            + "7zip-26.05-1.1-x86_64_v4.pkg.tar.zst\t800\t1762800000\nalsa-card-profiles-1:1.6.9-1-x86_64.pkg.tar.zst\t100\t1762000000\nlinha quebrada\n");
        Assert.Equal(4, files.Count);
        Assert.Equal(new DateTime(2025, 11, 1, 12, 26, 40, 500, DateTimeKind.Utc), files[0].Modified);
        var parts = Cleanup.PackageParts(Cleanup.PaccacheCandidates(output), files);
        Assert.Equal(["7zip", "alsa-card-profiles"], parts.Select(p => p.Name)); // the newest 7zip is kept by paccache: not listed
        Assert.Equal(710, parts[0].Bytes);
        Assert.Equal(1510, Cleanup.PackageParts(null, files).Single(p => p.Name == "7zip").Bytes); // apt/dnf/zypper: the whole folder goes
    }

    [Fact]
    public async Task SystemFoldersAreListedDirectlyOrThroughFind()
    {
        string dir = Directory.CreateTempSubdirectory("ilx-pkg").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "fedora/packages"));
            File.WriteAllBytes(Path.Combine(dir, "fedora/packages/kernel-core-6.17.1-300.fc43.x86_64.rpm"), new byte[300]);
            var files = await Cleanup.HostFilesAsync(dir, 3, CancellationToken.None);
            Assert.Equal(300, Assert.Single(files).Bytes);
            var viaFind = await Cleanup.HostFilesAsync(dir, 3, CancellationToken.None, useFind: true); // the Flatpak path, with the real find
            Assert.Equal(files, viaFind.Select(f => (f.Name, f.Bytes, files[0].Modified)).ToArray());
            Assert.True(Math.Abs((viaFind[0].Modified - files[0].Modified).TotalSeconds) < 1);
            Assert.Empty(await Cleanup.HostFilesAsync(Path.Combine(dir, "não-existe"), 3, CancellationToken.None));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FedoraLooksInDnf5AndDnf4Folders() =>
        Assert.Equal(["/var/cache/libdnf5", "/var/cache/dnf"], Cleanup.PackagePlan("rocky", "rhel centos fedora", false).Folders);

    [Fact]
    public void CacheFolderFollowsXdgOnlyWhenItIsSafe()
    {
        string home = Directory.CreateTempSubdirectory("ilx-home").FullName;
        try
        {
            string custom = Directory.CreateDirectory(Path.Combine(home, "cache-x")).FullName;
            Assert.Equal(custom, Cleanup.CacheFolder(home, custom, inFlatpak: false));
            Assert.Equal(Path.Combine(home, ".cache"), Cleanup.CacheFolder(home, custom, inFlatpak: true)); // the sandbox's own variable
            Assert.Equal(Path.Combine(home, ".cache"), Cleanup.CacheFolder(home, null, inFlatpak: false));
            Assert.Equal(Path.Combine(home, ".cache"), Cleanup.CacheFolder(home, "relativo", inFlatpak: false));
            Assert.Equal(Path.Combine(home, ".cache"), Cleanup.CacheFolder(home, home + "/", inFlatpak: false)); // the home folder itself: never
            Assert.Equal(Path.Combine(home, ".cache"), Cleanup.CacheFolder(home, "/", inFlatpak: false));
            Assert.Equal(Path.Combine(home, ".cache"), Cleanup.CacheFolder(home, Path.GetDirectoryName(home), inFlatpak: false));
            Assert.Equal(Path.Combine(home, ".cache"), Cleanup.CacheFolder(home, Path.Combine(home, "não-existe"), inFlatpak: false));
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public void JournalCountsOnlyArchivedFilesOlderThanFourWeeks()
    {
        var now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        var recent = new (string, long, DateTime)[] { ("system@a-1.journal", 6_553_600, now.AddDays(-5)), ("system.journal", 8_000_000, now) };
        var (bytes, parts) = Cleanup.JournalPlan(recent, now);
        Assert.Equal(0, bytes); // this PC on 2026-11-11: only 5 days of logs, the old item showed 47 MB to free
        Assert.Equal(14_553_600, parts.Single(p => p.Key == "kept").Bytes);
        var old = recent.Append(("system@b-2.journal", 9_000_000, now.AddDays(-40))).Append(("user-1000.journal", 1_000, now.AddDays(-60))).ToArray();
        Assert.Equal(9_000_000, Cleanup.JournalPlan(old, now).Bytes); // the active file never counts, however old
    }

    [Fact]
    public void FlatpakDetailsSayWhichAppsUseEachRuntime()
    {
        // flatpak 1.18.4 with LC_ALL=C, 2026-11-11 (trimmed).
        const string runtimes = "org.freedesktop.Platform\t25.08\tuser\t670.7 MB\norg.freedesktop.Sdk\t25.08\tuser\t1.7 GB\norg.gtk.Gtk3theme.Breeze\t3.22\tsystem\t650.2 kB\n";
        const string apps = "SuperTuxKart\torg.freedesktop.Platform/x86_64/25.08\nIlmenyx Controller\torg.freedesktop.Platform/x86_64/25.08\n";
        var parts = Cleanup.FlatpakParts(runtimes, apps);
        Assert.Equal(3, parts.Count);
        Assert.Equal("org.freedesktop.Platform 25.08", parts[^1].Name); // the ones in use go last
        Assert.Contains("SuperTuxKart", parts[^1].Note);
        Assert.Equal(1_700_000_000, parts[0].Bytes);
        Assert.Equal(650_200, Cleanup.ParseFlatpakSize("650,2 kB"));
    }

    [Fact]
    public void CacheAndTrashDetailsNameEachEntryAndDeleteOnlyTheChosenOnes()
    {
        string root = Directory.CreateTempSubdirectory("ilx-cache").FullName;
        try
        {
            string cache = Path.Combine(root, "cache");
            foreach (var (dir, size) in new[] { ("mozilla", 3000), ("ms-playwright-mcp", 2000), ("ilmenyx", 50), ("ilmenyx-update", 70), ("Shelly", 4000) })
            { Directory.CreateDirectory(Path.Combine(cache, dir)); File.WriteAllBytes(Path.Combine(cache, dir, "x"), new byte[size]); }
            foreach (var (dir, size) in new[] { ("pip", 600), ("antigo-sumido", 500), ("ksycoca6_pt_abc=", 10) })
            { Directory.CreateDirectory(Path.Combine(cache, dir)); File.WriteAllBytes(Path.Combine(cache, dir, "x"), new byte[size]); }
            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(Path.Combine(cache, "antigo-sumido", "x"), now.AddDays(-200)); Directory.SetLastWriteTimeUtc(Path.Combine(cache, "antigo-sumido"), now.AddDays(-200));
            // The installed app whose .desktop id ends in the folder name (CachyOS, 2026-11-12: ~/.cache/Shelly belongs to com.shellyorg.shelly).
            string data = Path.Combine(root, "share"); Directory.CreateDirectory(Path.Combine(data, "applications"));
            File.WriteAllText(Path.Combine(data, "applications", "com.shellyorg.shelly.desktop"), "[Desktop Entry]\nName=Shelly Package Manager\n");
            var parts = Cleanup.CacheParts(cache, CancellationToken.None, new DesktopApps([data]), now);
            Assert.Equal(["Shelly", "mozilla", "ms-playwright-mcp", "pip", "antigo-sumido", "ksycoca6_pt_abc="], parts.Select(p => p.Key)); // biggest first, Ilmenyx's own left out
            CleanupPart Part(string key) => parts.Single(p => p.Key == key);
            Assert.Equal("Firefox", Part("mozilla").Name);
            Assert.True(Part("ms-playwright-mcp").Redownload); Assert.False(Part("mozilla").Redownload);
            Assert.Equal("Shelly Package Manager", Part("Shelly").Name); Assert.Contains("instalado", Part("Shelly").Note);
            // Recommended = rebuilt without a download (browser, desktop caches) or untouched for 90+ days; never what must be downloaded again or an app in use.
            Assert.Equal(["mozilla", "antigo-sumido", "ksycoca6_pt_abc="], parts.Where(p => p.Recommended).Select(p => p.Key));
            Assert.Equal(200, Part("antigo-sumido").IdleDays); Assert.Equal(0, Part("pip").IdleDays);
            Assert.Null(Cleanup.KnownCache("Shelly")); Assert.True(Cleanup.KnownCache("gnome-software")!.Value.Safe); Assert.False(Cleanup.KnownCache("mesa_shader_cache_db")!.Value.Safe);
            Assert.True(Cleanup.KnownCache("yay")!.Value.Redownload); Assert.True(Cleanup.KnownCache("cosmic-settings")!.Value.Safe); Assert.Null(Cleanup.KnownCache("pipeline-tool")); // "pip" must not match by prefix alone
            Assert.Equal(3000, Cleanup.EmptyDirectory(cache, CancellationToken.None, skip: "ilmenyx", only: new HashSet<string> { "mozilla" }));
            Assert.True(Directory.Exists(Path.Combine(cache, "Shelly"))); Assert.False(Directory.Exists(Path.Combine(cache, "mozilla")));

            string trash = Path.Combine(root, "Trash");
            Directory.CreateDirectory(Path.Combine(trash, "files")); Directory.CreateDirectory(Path.Combine(trash, "info"));
            File.WriteAllBytes(Path.Combine(trash, "files", "foto.png"), new byte[123]);
            File.WriteAllText(Path.Combine(trash, "info", "foto.png.trashinfo"), "[Trash Info]\nPath=" + Cleanup.Home + "/Imagens/foto.png\nDeletionDate=2026-10-29T00:31:26\n");
            var item = Assert.Single(Cleanup.TrashParts(CancellationToken.None, trash));
            Assert.Equal("foto.png", item.Name); Assert.Equal(123, item.Bytes);
            Assert.Contains("~/Imagens", item.Note);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OnlyDirectEntryNamesCanBeChosen()
    {
        var item = new CleanupItem(CleanupKind.UserCache, "x", "x", 10, true, false, "");
        var (ok, _, freed) = await Cleanup.RunAsync(item, CancellationToken.None, new HashSet<string> { "../Documentos" });
        Assert.False(ok); Assert.Equal(0, freed);
    }
}
