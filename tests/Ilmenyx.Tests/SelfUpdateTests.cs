// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class SelfUpdateTests
{
    [Fact]
    public void ReleaseFileNamesAndHashLines()
    {
        Assert.Equal("Ilmenyx-Controller-0.33.0-x86_64.AppImage", SelfUpdate.FileName("v0.33.0", InstallKind.AppImage));
        Assert.Equal("Ilmenyx-Controller-0.34.1-x86_64.flatpak", SelfUpdate.FileName("v0.34.1", InstallKind.Flatpak));
        Assert.Equal("19242222957ef3c91830355ac84ca8b95f1c303ff30a1ba9679a3bb4ca738070",
            SelfUpdate.ParseSha256("19242222957EF3C91830355AC84CA8B95F1C303FF30A1BA9679A3BB4CA738070  Ilmenyx-Controller-x86_64.AppImage\n"));
        Assert.Null(SelfUpdate.ParseSha256("<html>Not Found</html>"));
    }

    [Fact]
    public async Task RealReleaseSignatureIsAcceptedAndATamperedFileIsRefused()
    {
        if (BoundedProcess.FindSystem("gpgv") is null || BoundedProcess.FindSystem("gpg") is null) return; // nothing to check without GnuPG
        // Fixtures: the .sha256 of the v0.33.0 AppImage and its detached signature, as published on GitHub (signed with the release subkey).
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        Directory.CreateDirectory(SelfUpdate.WorkDir);
        string file = Path.Combine(SelfUpdate.WorkDir, "test-sha256"), signature = file + ".asc";
        File.Copy(Path.Combine(fixtures, "Ilmenyx-Controller-0.33.0-x86_64.AppImage.sha256"), file, true);
        File.Copy(Path.Combine(fixtures, "Ilmenyx-Controller-0.33.0-x86_64.AppImage.sha256.asc"), signature, true);
        try
        {
            Assert.Equal("", await SelfUpdate.VerifySignatureAsync(file, signature, CancellationToken.None));
            File.AppendAllText(file, "x");
            await Assert.ThrowsAsync<IOException>(() => SelfUpdate.VerifySignatureAsync(file, signature, CancellationToken.None));
            Assert.False(File.Exists(file)); // a file with a bad signature is deleted, never installed
        }
        finally { File.Delete(file); File.Delete(signature); }
    }

    /// <summary>Opt-in (ILMENYX_NET_TESTS=1): the whole AppImage path against the real v0.33.0 release on GitHub (~100 MB).</summary>
    [Fact]
    public async Task RealAppImageUpdateEndToEnd()
    {
        if (Environment.GetEnvironmentVariable("ILMENYX_NET_TESTS") != "1" || !OperatingSystem.IsLinux()) return;
        string dir = Directory.CreateTempSubdirectory("ilx-appimage").FullName, fake = Path.Combine(dir, "Ilmenyx.AppImage");
        File.WriteAllText(fake, "old"); File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string? before = Environment.GetEnvironmentVariable("APPIMAGE");
        Environment.SetEnvironmentVariable("APPIMAGE", fake);
        try
        {
            var (file, note) = await SelfUpdate.FetchAsync("v0.33.0", null, CancellationToken.None);
            Assert.Equal("", note);
            await SelfUpdate.InstallAsync(file, CancellationToken.None);
            Assert.True(new FileInfo(fake).Length > 50_000_000);
            Assert.True((File.GetUnixFileMode(fake) & UnixFileMode.UserExecute) != 0);
        }
        finally { Environment.SetEnvironmentVariable("APPIMAGE", before); Directory.Delete(dir, true); }
    }
}
