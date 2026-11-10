// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

/// <summary>Bigger shader cache (environment.d file, undo), experimental Proton options (env building, conflicts) and the gaming apps catalogue.</summary>
public sealed class GameExtrasTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-extras-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(root, true); } catch (DirectoryNotFoundException) { } }

    private LinuxShaderCache Cache(string? session = null) =>
        new(Path.Combine(root, "home"), Path.Combine(root, "cfg"), Path.Combine(root, "cache"), k => k == ShaderCacheTweak.MesaVariable ? session : null);

    // ---- shader cache ----

    [Fact]
    public void ShaderCacheFileHasOnlyTheCacheLimitsAndNeverAVulkanDriverChoice()
    {
        string text = ShaderCacheTweak.FileContent();
        var vars = ShaderCacheTweak.Parse(text);
        Assert.Equal("10G", vars["MESA_SHADER_CACHE_MAX_SIZE"]);
        Assert.Equal("10737418240", vars["__GL_SHADER_DISK_CACHE_SIZE"]);
        Assert.Equal(2, vars.Count);
        Assert.DoesNotContain("AMD_VULKAN_ICD", text); Assert.DoesNotContain("SKIP_CLEANUP", text); Assert.DoesNotContain("RADV", text);
        Assert.True(ShaderCacheTweak.IsOurs(text)); Assert.False(ShaderCacheTweak.IsOurs("MESA_SHADER_CACHE_MAX_SIZE=10G\n")); Assert.False(ShaderCacheTweak.IsOurs(null));
    }

    [Fact]
    public void ApplyWritesTheUserFileAndUndoDeletesIt()
    {
        var cache = Cache();
        Assert.False(cache.IsOn);
        Assert.Equal(Path.Combine(root, "cfg", "environment.d", "90-ilmenyx-shader-cache.conf"), cache.ConfigPath);
        Assert.Null(cache.Apply());
        Assert.True(cache.IsOn); Assert.Equal(ShaderCacheTweak.FileContent(), File.ReadAllText(cache.ConfigPath));
        if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, File.GetUnixFileMode(cache.ConfigPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(cache.ConfigPath)!, "*.tmp"));
        Assert.Null(cache.Apply()); // idempotent
        Assert.Null(cache.Undo());
        Assert.False(File.Exists(cache.ConfigPath)); Assert.False(cache.IsOn);
        Assert.Null(cache.Undo()); // nothing to undo
    }

    [Fact]
    public void AFileNotWrittenByTheAppIsNeverOverwrittenOrDeleted()
    {
        var cache = Cache();
        Directory.CreateDirectory(Path.GetDirectoryName(cache.ConfigPath)!);
        File.WriteAllText(cache.ConfigPath, "MESA_SHADER_CACHE_MAX_SIZE=50G\n");
        Assert.True(cache.ForeignFile); Assert.False(cache.IsOn);
        Assert.NotNull(cache.Apply()); Assert.NotNull(cache.Undo());
        Assert.Equal("MESA_SHADER_CACHE_MAX_SIZE=50G\n", File.ReadAllText(cache.ConfigPath));
        Assert.False(ShaderCacheTweak.Describe(false, true, null, [], cache.ConfigPath).Adjustable);
    }

    [Fact]
    public void CacheSizesAreMeasuredFromTheKnownFoldersOnly()
    {
        var cache = Cache();
        void Bytes(string rel, int n) { string f = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllBytes(f, new byte[n]); }
        Bytes("cache/mesa_shader_cache/a/1", 1000); Bytes("cache/mesa_shader_cache_db/part0/x", 500); Bytes("cache/nvidia/GLCache/z", 300); Bytes("steam/steamapps/shadercache/730/f", 70);
        Bytes("cache/other/big", 99999);
        var sizes = cache.Caches(Path.Combine(root, "steam"));
        Assert.Equal(1500, sizes.Single(s => s.Label == "Mesa").Bytes); Assert.Equal(300, sizes.Single(s => s.Label == "NVIDIA").Bytes); Assert.Equal(70, sizes.Single(s => s.Label == "Steam").Bytes);
        Assert.DoesNotContain(cache.Caches(null), s => s.Label == "Steam");
        Assert.Null(LinuxShaderCache.DirSize(Path.Combine(root, "missing")));
        Assert.True(LinuxShaderCache.DirSize(Path.Combine(root, "cache"), maxFiles: 1) < 1000 + 500 + 300 + 99999); // bounded walk stops early
    }

    [Fact]
    public void DescriptionExplainsNextLoginDiskUseAndSessionState()
    {
        var off = ShaderCacheTweak.Describe(false, false, null, [("Mesa", 1536L * 1024 * 1024), ("NVIDIA", null)], "/h/.config/environment.d/x.conf");
        Assert.True(off.Adjustable); Assert.False(off.IsOn);
        Assert.Contains("próximo login", off.Effect); Assert.Contains("disco", off.Effect); Assert.Contains("Mesa 1,5 GB", off.Status); Assert.DoesNotContain("NVIDIA ", off.Status);
        Assert.Contains("padrão (1 GB)", off.Effect);
        var pending = ShaderCacheTweak.Describe(true, false, null, [], "/x"); Assert.Contains("próximo login", pending.Status);
        var active = ShaderCacheTweak.Describe(true, false, "10G", [], "/x"); Assert.Contains("em uso nesta sessão", active.Status);
        Assert.Equal("0 B", ShaderCacheTweak.SizeText(0)); Assert.Equal("10,0 GB", ShaderCacheTweak.SizeText(ShaderCacheTweak.NvidiaBytes));
    }

    [Fact]
    public void ManualLaunchCarriesTheShaderCacheVariablesOnlyWhenTheOptionIsOn()
    {
        static string? Find(string n) => "/usr/bin/" + n;
        var off = LinuxGameLauncher.BuildManual("/g/game.sh", new(), "", "AMD", Find);
        Assert.DoesNotContain(off.Environment, e => e.Key.Contains("SHADER"));
        var on = LinuxGameLauncher.BuildManual("/g/Title/game.exe", new(), "", "AMD", Find, prefixRoot: "/p", shaderCache: true);
        Assert.Contains(new KeyValuePair<string, string>("MESA_SHADER_CACHE_MAX_SIZE", "10G"), on.Environment);
        Assert.Contains(new KeyValuePair<string, string>("__GL_SHADER_DISK_CACHE_SIZE", "10737418240"), on.Environment);
        Assert.DoesNotContain(on.Environment, e => e.Key == "AMD_VULKAN_ICD");
    }

    // ---- Proton builds ----

    [Theory]
    [InlineData("GE-Proton10-25", 10, true, false)]
    [InlineData("GE-Proton9-27", 9, true, false)]
    [InlineData("UMU-Proton-10.0-4", 10, true, false)]
    [InlineData("Proton-6.21-GE-2", 6, true, false)]
    [InlineData("proton_10", 10, false, true)]
    [InlineData("proton_9", 9, false, true)]
    [InlineData("proton_experimental", null, false, true)]
    [InlineData("Proton-CachyOS", null, false, false)]
    public void ProtonNamesAreClassified(string name, int? major, bool ge, bool official)
    {
        var p = GameExtras.ParseProton(name)!;
        Assert.Equal(major, p.Major); Assert.Equal(ge, p.GeFamily); Assert.Equal(official, p.Official);
    }

    [Fact]
    public void EmptyProtonNameIsUnknown() { Assert.Null(GameExtras.ParseProton(null)); Assert.Null(GameExtras.ParseProton(" ")); }

    // ---- experimental options ----

    private static readonly GameProfile Both = new() { ProtonWayland = true, LowLatency = true };
    private static ExtrasContext Ctx(bool wayland = true, string? proton = "GE-Proton10-25", bool nvidia = true, bool windows = true) => new(wayland, GameExtras.ParseProton(proton), nvidia, windows);

    [Fact]
    public void BothOptionsAreOffByDefaultAndAddNothing()
    {
        var p = new GameProfile();
        Assert.False(p.ProtonWayland); Assert.False(p.LowLatency);
        Assert.Empty(GameExtras.Resolve(p, Ctx(), []));
        Assert.Equal("", GameLaunchOptions.BuildSteamOptions(p, "NVIDIA", extras: Ctx()));
    }

    [Fact]
    public void WaylandNeedsAWaylandSessionAndGeOrUmuProtonTenOrNewer()
    {
        var p = new GameProfile { ProtonWayland = true };
        Assert.Equal(["PROTON_ENABLE_WAYLAND=1"], GameExtras.Resolve(p, Ctx(), []).Single().Pieces);
        Assert.Equal(["PROTON_ENABLE_WAYLAND=1"], GameExtras.Resolve(p, Ctx(proton: "UMU-Proton-10.0-4"), []).Single().Pieces);
        Assert.Contains("X11", GameExtras.Resolve(p, Ctx(wayland: false), []).Single().Skipped);
        Assert.Contains("9-27", GameExtras.Resolve(p, Ctx(proton: "GE-Proton9-27"), []).Single().Skipped);
        Assert.Contains("oficial da Valve", GameExtras.Resolve(p, Ctx(proton: "proton_10"), []).Single().Skipped);
        Assert.Contains("não deu para saber", GameExtras.Resolve(p, Ctx(proton: null), []).Single().Skipped);
        Assert.Contains("Windows", GameExtras.Resolve(p, Ctx(windows: false), []).Single().Skipped);
    }

    [Fact]
    public void LowLatencyIsOnlyForTheNvidiaProprietaryDriver()
    {
        var p = new GameProfile { LowLatency = true };
        Assert.Equal(["PROTON_ENABLE_NVAPI=1"], GameExtras.Resolve(p, Ctx(proton: "proton_10"), []).Single().Pieces);
        var amd = GameExtras.Resolve(p, Ctx(nvidia: false), []).Single();
        Assert.False(amd.Applies); Assert.Contains("AMD e Intel", amd.Skipped);
        Assert.DoesNotContain(GameExtras.Resolve(p, Ctx(nvidia: false), []).SelectMany(c => c.Pieces), x => x.StartsWith("DXVK_NVAPI_ALLOW_OTHER_DRIVERS", StringComparison.Ordinal) || x.StartsWith("LOW_LATENCY_LAYER", StringComparison.Ordinal));
    }

    [Fact]
    public void OptionsThatConflictWithOtherPiecesAreLeftOutWithTheTriageReason()
    {
        var r = GameExtras.Resolve(Both, Ctx(), ["PROTON_USE_WINED3D=1", "gamescope"]);
        Assert.All(r, c => Assert.False(c.Applies));
        Assert.Contains("OpenGL", r.Single(c => c.Id == GameExtras.LowLatencyId).Skipped);
        Assert.Contains("Gamescope", r.Single(c => c.Id == GameExtras.WaylandId).Skipped);
        Assert.Contains("NVAPI", GameExtras.Resolve(Both, Ctx(), ["PROTON_DISABLE_NVAPI=1"]).Single(c => c.Id == GameExtras.LowLatencyId).Skipped);
        Assert.Contains("só guarda um valor", GameExtras.Resolve(Both, Ctx(), ["PROTON_ENABLE_WAYLAND=0"]).Single(c => c.Id == GameExtras.WaylandId).Skipped);
        // Compatible with the usual pieces of a profile.
        Assert.All(GameExtras.Resolve(Both, Ctx(), ["__NV_PRIME_RENDER_OFFLOAD=1", "mangohud", "gamemoderun", "-dx12", "DXVK_CONFIG=dxgi.syncInterval = 1"]), c => Assert.True(c.Applies, c.Skipped));
    }

    [Fact]
    public void RecommendedCombinationCheckUsesTheSameTable()
    {
        Assert.Empty(GameExtras.AgainstRecommended(Both, ["PROTON_ENABLE_NVAPI=1", "gamemoderun", "PROTON_ENABLE_WAYLAND=1"]));
        var hits = GameExtras.AgainstRecommended(Both, ["PROTON_HIDE_NVIDIA_GPU=1", "PROTON_USE_WAYLAND=1"]);
        Assert.Equal(2, hits.Count);
        Assert.Empty(GameExtras.AgainstRecommended(new GameProfile(), ["PROTON_USE_WINED3D=1"]));
    }

    [Fact]
    public void TriageTreatsTheTwoWaylandSwitchesAsOneAndKeepsThemAwayFromGamescope()
    {
        Assert.NotNull(LaunchOptionTriage.Conflict("PROTON_USE_WAYLAND=1", "PROTON_ENABLE_WAYLAND=1"));
        Assert.NotNull(LaunchOptionTriage.Conflict("gamescope", "PROTON_ENABLE_WAYLAND=1"));
        Assert.Null(LaunchOptionTriage.Conflict("PROTON_ENABLE_WAYLAND=1", "PROTON_ENABLE_HDR=1"));
    }

    [Fact]
    public void SteamOptionsCarryTheAppliedExtrasBeforeTheWrappers()
    {
        var p = new GameProfile { ProtonWayland = true, LowLatency = true, MangoHud = true, GameMode = true, WinMode = "full" };
        Assert.Equal("PROTON_ENABLE_WAYLAND=1 PROTON_ENABLE_NVAPI=1 mangohud gamemoderun %command% -fullscreen", GameLaunchOptions.BuildSteamOptions(p, "NVIDIA", extras: Ctx()));
        Assert.Equal("PROTON_ENABLE_NVAPI=1 mangohud gamemoderun %command% -fullscreen", GameLaunchOptions.BuildSteamOptions(p, "NVIDIA", extras: Ctx(wayland: false)));
        Assert.Equal("mangohud gamemoderun %command% -fullscreen", GameLaunchOptions.BuildSteamOptions(p, "NVIDIA")); // no context: unchanged builder
        Assert.Equal("PROTON_ENABLE_WAYLAND=1 %command%", GameLaunchOptions.BuildSteamOptions(new GameProfile { ProtonWayland = true }, "AMD", extras: Ctx(nvidia: false)));
    }

    [Fact]
    public void ManualWindowsLaunchAddsTheExtrasOnlyThroughUmu()
    {
        static string? Find(string n) => "/usr/bin/" + n;
        var cmd = LinuxGameLauncher.BuildManual("/g/Title/game.exe", Both, "", "NVIDIA", Find, prefixRoot: "/p", extras: Ctx(proton: "UMU-Proton-10.0-4"));
        Assert.Contains(new KeyValuePair<string, string>("PROTON_ENABLE_WAYLAND", "1"), cmd.Environment);
        Assert.Contains(new KeyValuePair<string, string>("PROTON_ENABLE_NVAPI", "1"), cmd.Environment);
        var native = LinuxGameLauncher.BuildManual("/g/game.sh", Both, "", "NVIDIA", Find, extras: Ctx());
        Assert.DoesNotContain(native.Environment, e => e.Key.StartsWith("PROTON_", StringComparison.Ordinal));
        var x11 = LinuxGameLauncher.BuildManual("/g/Title/game.exe", Both, "", "AMD", Find, prefixRoot: "/p", extras: Ctx(wayland: false, nvidia: false));
        Assert.DoesNotContain(x11.Environment, e => e.Key is "PROTON_ENABLE_WAYLAND" or "PROTON_ENABLE_NVAPI");
    }

    [Fact]
    public void WaylandSessionDetection()
    {
        Assert.True(GameExtras.IsWaylandSession(k => k == "XDG_SESSION_TYPE" ? "wayland" : null));
        Assert.True(GameExtras.IsWaylandSession(k => k == "WAYLAND_DISPLAY" ? "wayland-0" : null));
        Assert.False(GameExtras.IsWaylandSession(k => k == "XDG_SESSION_TYPE" ? "x11" : null));
    }

    [Fact]
    public void UmuProtonAndSteamMappingAreRead()
    {
        string tools = Path.Combine(root, "data", "Steam", "compatibilitytools.d");
        Assert.Null(LinuxGameExtras.UmuProtonName(root, Path.Combine(root, "data")));
        foreach (var d in new[] { "UMU-Proton-9.0-3", "UMU-Proton-10.0-4", "UMU-Proton-10.0-12", "GE-Proton8-1" }) Directory.CreateDirectory(Path.Combine(tools, d));
        Assert.Equal("UMU-Proton-10.0-12", LinuxGameExtras.UmuProtonName(root, Path.Combine(root, "data")));

        string home = Path.Combine(root, "h"), steam = Path.Combine(home, ".local/share/Steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps")); Directory.CreateDirectory(Path.Combine(steam, "config"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam + "\"\n\t}\n}\n");
        File.WriteAllText(Path.Combine(steam, "config", "config.vdf"), "\"InstallConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"CompatToolMapping\"\n\t\t\t\t{\n" +
            "\t\t\t\t\t\"0\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"name\"\t\t\"proton_10\"\n\t\t\t\t\t}\n\t\t\t\t\t\"730\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"name\"\t\t\"GE-Proton10-25\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n");
        var info = new SteamInfo(home, Path.Combine(root, "noproc"));
        Assert.Equal("GE-Proton10-25", info.CompatToolName("730"));
        Assert.Equal("proton_10", info.CompatToolName("440"));
    }

    // ---- gaming apps ----

    [Fact]
    public void GamingAppsAreInTheCatalogueWithLicensesCaveatsAndNoneIsPreSelected()
    {
        var apps = AppCatalog.Default();
        foreach (var id in new[] { "io.github.ilya_zlobintsev.LACT", "com.vysp3r.ProtonPlus", "net.davidotek.pupgui2", "org.freedesktop.Platform.VulkanLayer.gamescope", "dev.lizardbyte.app.Sunshine", "org.freedesktop.Piper", "io.github.pwr_solaar.solaar" })
        {
            var a = Assert.Single(apps, x => x.FlatpakId == id);
            Assert.False(a.PreSelect); Assert.NotEmpty(a.License); Assert.NotEqual("Essenciais", a.Category);
        }
        Assert.Contains("lactd", apps.Single(a => a.Name == "LACT").Caveat);
        Assert.Contains("ratbagd", apps.Single(a => a.Name == "Piper").Caveat);
        Assert.Contains("udev", apps.Single(a => a.Name == "Solaar").Caveat);
        Assert.Contains("additional-install.sh", apps.Single(a => a.Name == "Sunshine").Caveat);
        var gfn = apps.Single(a => a.Name == "GeForce NOW"); Assert.True(gfn.LinkOnly); Assert.StartsWith("https://www.nvidia.com/", gfn.LinkUrl);
        Assert.True(apps.Single(a => a.Name == "input-remapper").LinkOnly);
        Assert.Equal("com.valvesoftware.Steam", apps.Single(a => a.FlatpakId == "org.freedesktop.Platform.VulkanLayer.gamescope").RuntimeFor);
    }

    [Fact]
    public void RuntimeBranchParsing()
    {
        Assert.Equal("25.08", AppCatalog.RuntimeBranch("org.freedesktop.Platform/x86_64/25.08\n"));
        Assert.Null(AppCatalog.RuntimeBranch("error: com.valvesoftware.Steam not installed"));
        Assert.Null(AppCatalog.RuntimeBranch("org.freedesktop.Platform/x86_64/25.08; rm -rf /"));
        Assert.Equal(["org.freedesktop.Platform.VulkanLayer.gamescope//25.08"], AppCatalog.ParseRuntimeList("org.freedesktop.Platform.VulkanLayer.gamescope\t25.08\nnot an id\tx\nfoo\n").ToArray());
    }

    [Fact]
    public async Task GamescopeLayerInstallsTheBranchOfTheSteamFlatpakRuntime()
    {
        var calls = new List<string>(); bool steamFlatpak = true; string runtimes = "";
        var installer = new LinuxAppInstaller((p, a, t, ct) =>
        {
            calls.Add(string.Join(" ", a));
            if (a[0] == "info") return Task.FromResult(steamFlatpak && a[1] == "--system" ? (0, "org.freedesktop.Platform/x86_64/25.08\n") : (1, "error: not installed"));
            if (a[0] == "list" && a.Contains("--runtime")) return Task.FromResult((0, runtimes));
            return Task.FromResult((0, ""));
        }, _ => false);
        var apps = AppCatalog.Default(); await installer.RefreshInstalledAsync(apps);
        var layer = apps.Single(a => a.RuntimeFor.Length > 0);
        Assert.Equal("25.08", layer.Branch); Assert.Null(layer.Unavailable); Assert.False(layer.Installed);
        Assert.Contains("info --user --show-runtime com.valvesoftware.Steam", calls);
        Assert.Equal(["install", "--user", "-y", "--noninteractive", "flathub", "org.freedesktop.Platform.VulkanLayer.gamescope//25.08"], LinuxAppInstaller.InstallArguments(layer).ToArray());
        runtimes = "org.freedesktop.Platform.VulkanLayer.gamescope\t24.08\n"; await installer.RefreshInstalledAsync(apps); Assert.False(layer.Installed); // other branch does not count
        runtimes = "org.freedesktop.Platform.VulkanLayer.gamescope\t25.08\n"; await installer.RefreshInstalledAsync(apps); Assert.True(layer.Installed);
        steamFlatpak = false; await installer.RefreshInstalledAsync(apps);
        Assert.Null(layer.Branch); Assert.Contains("Steam", layer.Unavailable); Assert.False(layer.Installed);
        Assert.False((await installer.InstallAsync(layer, default)).Ok);
        Assert.Throws<ArgumentException>(() => LinuxAppInstaller.InstallArguments(layer));
        Assert.All(apps.Where(a => a.LinkOnly), a => Assert.NotNull(a.Unavailable));
        Assert.False((await installer.InstallAsync(apps.Single(a => a.Name == "GeForce NOW"), default)).Ok);
    }
}
