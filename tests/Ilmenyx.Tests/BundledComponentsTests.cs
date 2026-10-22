// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public class BundledComponentsTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ilmenyx-comp-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(dir, "components");
    private string Data => Path.Combine(dir, "data");
    public BundledComponentsTests() { Directory.CreateDirectory(Root); Directory.CreateDirectory(Data); }
    public void Dispose() => Directory.Delete(dir, true);

    private void FakeMangoHud() { string lib = BundledComponents.MangoHudLibraryFolder(Root); Directory.CreateDirectory(lib); File.WriteAllText(Path.Combine(lib, "libMangoHud.so"), "x"); File.WriteAllText(Path.Combine(lib, "libMangoHud_shim.so"), "x"); }

    [Fact]
    public void MissingBundleResolvesNothingExceptTheBuiltInGameMode()
    {
        Assert.Null(BundledComponents.ResolveIn("mangohud", Root, Data));
        Assert.Null(BundledComponents.ResolveIn("openrgb", Root, Data));
        Assert.Null(BundledComponents.ResolveIn("unknown", Root, Data));
        string gm = BundledComponents.ResolveIn("gamemoderun", Root, Data)!;
        Assert.True(File.Exists(gm));
        Assert.Contains("ionice", File.ReadAllText(gm));
        Assert.True(File.GetUnixFileMode(gm).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void MangoHudWrapperUsesBundledShimAndManifestWithAbsolutePaths()
    {
        FakeMangoHud();
        string script = BundledComponents.ResolveIn("mangohud", Root, Data)!;
        string text = File.ReadAllText(script);
        Assert.Contains(Path.Combine(BundledComponents.MangoHudLibraryFolder(Root), "libMangoHud_shim.so"), text);
        Assert.Contains("MANGOHUD=1", text);
        Assert.Contains("VK_ADD_IMPLICIT_LAYER_PATH", text);
        string manifest = File.ReadAllText(Path.Combine(Data, "vulkan", "implicit_layer.d", "MangoHud.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(manifest);
        Assert.Equal(Path.Combine(BundledComponents.MangoHudLibraryFolder(Root), "libMangoHud.so"), doc.RootElement.GetProperty("layer").GetProperty("library_path").GetString());
        Assert.Equal(script, BundledComponents.ResolveIn("mangohud", Root, Data));   // idempotent
    }

    [Fact]
    public void MangoHudWrapperQuotesAPathWithSpacesAndQuotes()
    {
        string odd = Path.Combine(dir, "it's a dir"); string lib = BundledComponents.MangoHudLibraryFolder(odd); Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "libMangoHud.so"), "x"); File.WriteAllText(Path.Combine(lib, "libMangoHud_shim.so"), "x");
        string script = BundledComponents.ResolveIn("mangohud", odd, Data)!;
        Assert.Contains("'\\''", File.ReadAllText(script));
    }

    [Fact]
    public void OpenRgbWrapperExtractsAndRunsTheAppImageWithoutFuse()
    {
        Directory.CreateDirectory(Path.Combine(Root, "openrgb")); File.WriteAllText(BundledComponents.OpenRgbImage(Root), "x");
        string text = File.ReadAllText(BundledComponents.ResolveIn("openrgb", Root, Data)!);
        Assert.Contains("APPIMAGE_EXTRACT_AND_RUN=1", text);
        Assert.Contains("OpenRGB.AppImage", text);
    }

    [Fact]
    public void WrapperWriteFailureIsReportedAsUnavailableNotAsACrash()
    {
        string blocked = Path.Combine(dir, "file"); File.WriteAllText(blocked, "x");
        Assert.Null(BundledComponents.ResolveIn("gamemoderun", Root, Path.Combine(blocked, "sub")));
    }

    [Fact]
    public void GameModeFallbackRunsTheProgramAndPassesArguments()
    {
        string gm = BundledComponents.ResolveIn("gamemoderun", Root, Data)!;
        var info = new System.Diagnostics.ProcessStartInfo(gm) { RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add("/bin/echo"); info.ArgumentList.Add("ola mundo");
        using var p = System.Diagnostics.Process.Start(info)!; string output = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000);
        Assert.Equal("ola mundo", output.Trim());
        Assert.Equal(0, p.ExitCode);
    }
}

public class FlatpakHintTests
{
    [Theory]
    [InlineData("cachyos", "arch", "sudo pacman -S flatpak")]
    [InlineData("ubuntu", "debian", "sudo apt install flatpak")]
    [InlineData("fedora", "", "sudo dnf install flatpak")]
    [InlineData("opensuse-tumbleweed", "suse opensuse", "sudo zypper install flatpak")]
    public void CommandMatchesTheDistribution(string id, string like, string expected) => Assert.Equal(expected, Ilmenyx.Platform.Linux.LinuxAppInstaller.FlatpakInstallHint(id, like));

    [Fact]
    public void UnknownDistributionListsTheCommonOnes() => Assert.Contains("apt install", Ilmenyx.Platform.Linux.LinuxAppInstaller.FlatpakInstallHint("", ""));
}
