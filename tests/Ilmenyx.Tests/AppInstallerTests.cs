// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Executor falso: nenhum flatpak real é executado e nada é instalado.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class AppInstallerTests
{
    [Fact]
    public void CatalogHasValidFlatpakIdsAndSensibleDefaults()
    {
        var apps = AppCatalog.Default(); Assert.True(apps.Count >= 8);
        Assert.All(apps, a => { Assert.True(a.LinkOnly ? a.FlatpakId.Length == 0 && a.LinkUrl.StartsWith("https://", StringComparison.Ordinal) : a.FromGithub ? a.FlatpakId.Length == 0 && GithubPackages.ValidRepo(a.GithubRepo) && GithubPackages.ValidPackage(a.PackageName) : AppCatalog.ValidId(a.FlatpakId), a.Name); Assert.NotEmpty(a.Description); Assert.Contains(a.Category, AppCatalog.Categories); });
        Assert.Equal(apps.Count, apps.Select(a => a.Key).Distinct().Count()); Assert.Contains(apps, a => a.Name == "Steam" && a.PreSelect); Assert.Contains(apps, a => a.Name == "Lutris" && !a.PreSelect);
        Assert.Contains(apps, a => a.FlatpakId == "io.github.peazip.PeaZip" && a.Description.Contains("RAR")); Assert.DoesNotContain(apps, a => a.Name.Contains("WinRAR"));
    }

    [Theory]
    [InlineData("com.valvesoftware.Steam", true)]
    [InlineData("org.videolan.VLC", true)]
    [InlineData("steam", false)]
    [InlineData("a.b", false)]
    [InlineData("com.x.y; rm -rf /", false)]
    [InlineData("-com.x.y", false)]
    [InlineData("", false)]
    public void IdValidationBlocksOptionsAndShellText(string id, bool valid) => Assert.Equal(valid, AppCatalog.ValidId(id));

    [Fact]
    public async Task InstalledStateComesFromFlatpakListsAndNativeBinaries()
    {
        var calls = new List<string>();
        var installer = new LinuxAppInstaller((p, a, t, ct) => { calls.Add(p + " " + string.Join(" ", a)); return Task.FromResult(a.Contains("--user") ? (0, "com.valvesoftware.Steam\norg.videolan.VLC\nnot an id\n") : (0, "com.discordapp.Discord\n")); }, name => name == "flameshot");
        var apps = AppCatalog.Default(); foreach (var a in apps) a.Selected = true;
        await installer.RefreshInstalledAsync(apps);
        Assert.Equal(["Discord", "Flameshot", "Steam", "VLC"], apps.Where(a => a.Installed).Select(a => a.Name).OrderBy(x => x).ToArray());
        Assert.All(apps.Where(a => a.Installed), a => Assert.False(a.Selected)); Assert.Contains(apps, a => a.Name == "Google Chrome" && a is { Installed: false, Selected: true });
        Assert.Contains("flatpak list --user --app --columns=application", calls); Assert.Contains("flatpak list --system --app --columns=application", calls);
    }

    [Fact]
    public async Task FailingOrMissingFlatpakLeavesNothingMarkedAsInstalledByFlatpak()
    {
        var installer = new LinuxAppInstaller((_, _, _, _) => throw new FileNotFoundException("flatpak"), _ => false);
        var apps = AppCatalog.Default(); await installer.RefreshInstalledAsync(apps); Assert.DoesNotContain(apps, a => a.Installed);
        Assert.False(await installer.HasFlathubAsync());
    }

    [Fact]
    public void RemoteDetectionAndOutputHelpers()
    {
        Assert.True(AppCatalog.HasFlathub("flathub\nfedora\n")); Assert.True(AppCatalog.HasFlathub("flathub\tsystem\n")); Assert.False(AppCatalog.HasFlathub("flathub-beta\nother\n")); Assert.False(AppCatalog.HasFlathub(""));
        Assert.Equal("Error: something broke", AppCatalog.LastLine("Installing… [####    ] 40%\n----\nError: something broke\n  3 MB/s\n"));
    }

    [Fact]
    public async Task InstallUsesUserScopeNoninteractiveArgumentsAndMapsResults()
    {
        var recorded = new List<IReadOnlyList<string>>(); (int, string) result = (0, "");
        var installer = new LinuxAppInstaller((p, a, t, ct) => { recorded.Add(a); return Task.FromResult(result); });
        var app = AppCatalog.Default()[0];
        Assert.Equal((true, "Instalado."), await installer.InstallAsync(app, default));
        Assert.Equal(["install", "--user", "-y", "--noninteractive", "flathub", "com.valvesoftware.Steam"], recorded[0].ToArray()); Assert.DoesNotContain("sudo", recorded[0]);
        result = (1, "Error: already installed"); Assert.Equal((true, "Já estava instalado."), await installer.InstallAsync(app, default));
        result = (1, "error: No remote refs found for 'x'"); Assert.Equal((false, "Pacote não encontrado no Flathub."), await installer.InstallAsync(app, default));
        result = (1, "Could not resolve host"); Assert.Contains("internet", (await installer.InstallAsync(app, default)).Item2);
        result = (7, "Installing…\nweird failure\n"); var odd = await installer.InstallAsync(app, default); Assert.False(odd.Item1); Assert.Contains("código 7", odd.Item2); Assert.Contains("weird failure", odd.Item2);
        Assert.Equal(["update", "--user", "-y", "--noninteractive", "com.valvesoftware.Steam"], LinuxAppInstaller.InstallArguments(app, upgrade: true).ToArray());
        Assert.Throws<ArgumentException>(() => LinuxAppInstaller.InstallArguments(new InstallableApp { FlatpakId = "x; reboot" }));
    }

    [Fact]
    public async Task AddingFlathubTargetsTheUserInstallationOnly()
    {
        IReadOnlyList<string>? args = null; var installer = new LinuxAppInstaller((p, a, t, ct) => { args = a; return Task.FromResult((0, "")); });
        Assert.True((await installer.AddFlathubAsync()).Ok); Assert.Equal(["remote-add", "--user", "--if-not-exists", "flathub", "https://flathub.org/repo/flathub.flatpakrepo"], args!.ToArray());
        Assert.False((await new LinuxAppInstaller((_, _, _, _) => Task.FromResult((1, "denied"))).AddFlathubAsync()).Ok);
    }
}
