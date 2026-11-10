// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Comandos no formato real do SuperTuxKart via Flatpak (validado com o overlay aparecendo, 2026-11-10).
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class FlatpakLaunchTests
{
    private static readonly string[] Stk = ["run", "--branch=stable", "--arch=x86_64", "--command=supertuxkart", "net.supertuxkart.SuperTuxKart"];

    [Fact]
    public void MangoHudWrapsTheAppCommandInsideTheSandbox()
    {
        var r = FlatpakLaunch.WithMangoHud(Stk, "ignored", "/home/u/.config/ilmenyx/mangohud.conf", "/home/u/.local/share/ilmenyx/mangohud");
        Assert.Equal(["run", "--branch=stable", "--arch=x86_64", "--env=MANGOHUD=1", "--env=MANGOHUD_CONFIGFILE=/home/u/.config/ilmenyx/mangohud.conf",
            "--filesystem=/home/u/.config/ilmenyx:ro", "--filesystem=/home/u/.local/share/ilmenyx/mangohud", "--command=" + FlatpakLaunch.MangoHudScript,
            "net.supertuxkart.SuperTuxKart", "supertuxkart"], r);
    }

    [Fact]
    public void WithoutCommandOptionTheMetadataCommandIsUsedAndGameArgsStay()
    {
        var r = FlatpakLaunch.WithMangoHud(["run", "org.exemplo.Jogo", "--fullscreen"], "jogo-bin", null, "/logs");
        Assert.Equal(["run", "--env=MANGOHUD=1", "--filesystem=/logs", "--command=" + FlatpakLaunch.MangoHudScript, "org.exemplo.Jogo", "jogo-bin", "--fullscreen"], r);
        Assert.Throws<ArgumentException>(() => FlatpakLaunch.WithMangoHud(["install", "x"], "c", null, "/l"));
    }

    [Theory]
    [InlineData("net.supertuxkart.SuperTuxKart", true)]
    [InlineData("com.hypixel.HytaleLauncher", true)]
    [InlineData("supertuxkart", false)]
    [InlineData("org.x.$(reboot)", false)]
    [InlineData("org.1x.App", false)]
    public void AppIdsAreReverseDns(string id, bool ok) => Assert.Equal(ok, FlatpakLaunch.IsAppId(id));

    [Fact]
    public void MetadataGivesCommandAndRuntimeBranch()
    {
        const string meta = "[Application]\nname=net.supertuxkart.SuperTuxKart\nruntime=org.freedesktop.Platform/x86_64/26.08\nsdk=org.freedesktop.Sdk/x86_64/26.08\ncommand=supertuxkart\n\n[Context]\nshared=network;ipc;\n";
        Assert.Equal("supertuxkart", FlatpakApps.Parse(meta, "command"));
        Assert.Equal("org.freedesktop.Platform/x86_64/26.08", FlatpakApps.Parse(meta, "runtime"));
        Assert.Null(FlatpakApps.Parse(meta, "shared")); // other groups are ignored
        Assert.Equal(["install", "--user", "-y", "--noninteractive", "flathub", "org.freedesktop.Platform.VulkanLayer.MangoHud//26.08"], FlatpakApps.InstallMangoHud("26.08"));
    }

    [Fact]
    public void FlatpakGameProcessIsFoundByAppIdNotByFlatpakItself()
    {
        var g = new LibGame("Linux", "net.supertuxkart.SuperTuxKart", "SuperTuxKart", "", "/usr/bin/flatpak", null, string.Join(' ', Stk.ToArray()), "Linux · Flatpak");
        // Inside the sandbox the command line is the game's own; the app ID is only in the environment.
        Assert.True(GameProcesses.CommandLineHit(g, g.ExePath, "/app/bin/supertuxkart", ["PATH=/app/bin:/usr/bin", "FLATPAK_ID=net.supertuxkart.SuperTuxKart", "MANGOHUD=1"]));
        Assert.False(GameProcesses.CommandLineHit(g, g.ExePath, "/app/bin/stremio", ["FLATPAK_ID=com.stremio.Stremio"])); // another Flatpak app
        Assert.False(GameProcesses.CommandLineHit(g, g.ExePath, "/usr/bin/flatpak run net.supertuxkart.SuperTuxKart")); // no environment read
        var ps = "4242 /app/bin/supertuxkart HOME=/home/u FLATPAK_ID=net.supertuxkart.SuperTuxKart\n4300 /app/bin/stremio FLATPAK_ID=com.stremio.Stremio";
        Assert.Equal([4242], GameProcesses.FindInPs(ps, g, g.ExePath));
        var native = new LibGame("Linux", "jogo", "Jogo", "", "/usr/games/jogo");
        Assert.True(GameProcesses.CommandLineHit(native, native.ExePath, "/usr/games/jogo --full"));
    }
}
