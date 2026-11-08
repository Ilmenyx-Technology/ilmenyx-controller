// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Tabela de inicialização: entradas reais de Mint/Ubuntu/Fedora/KDE/XFCE em fixtures; nada do usuário real é tocado.
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class StartupCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ilmenyx-stc-" + Guid.NewGuid().ToString("N"));
    public StartupCatalogTests() { Directory.CreateDirectory(root); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static readonly Dictionary<string, string> NoKeys = new();
    private static StartupAdvice Sys(string id, string exec = "", Dictionary<string, string>? keys = null, string? pkg = null, string? desktop = "X-Cinnamon") =>
        StartupCatalog.Classify(new(id, "Desktop", exec, "", true, keys ?? NoKeys, pkg, desktop));

    [Theory]
    // Cinnamon (Linux Mint 22.3, observed in /etc/xdg/autostart)
    [InlineData("cinnamon-settings-daemon-power.desktop", "csd-power", StartupRole.Required, "Cinnamon")]
    [InlineData("cinnamon-settings-daemon-media-keys.desktop", "csd-media-keys", StartupRole.Required, "Cinnamon")]
    [InlineData("cinnamon-settings-daemon-wacom.desktop", "csd-wacom", StartupRole.Optional, "Cinnamon")]
    [InlineData("cinnamon-settings-daemon-settings-remap.desktop", "csd-settings-remap", StartupRole.System, "Cinnamon")]
    [InlineData("polkit-gnome-authentication-agent-1.desktop", "/usr/lib/policykit-1-gnome/polkit-gnome-authentication-agent-1", StartupRole.Required, "Permissões")]
    [InlineData("gnome-keyring-secrets.desktop", "/usr/bin/gnome-keyring-daemon --start --components=secrets", StartupRole.Required, "Chaveiro")]
    [InlineData("nm-applet.desktop", "nm-applet", StartupRole.Required, "Rede")]
    [InlineData("blueman.desktop", "blueman-applet", StartupRole.Optional, "Bluetooth")]
    [InlineData("print-applet.desktop", "system-config-printer-applet", StartupRole.Optional, "Impressão")]
    [InlineData("im-launch.desktop", "sh -c 'if [ \"x$XDG_SESSION_TYPE\" = \"xwayland\" ] ; then exec env IM_CONFIG_CHECK_ENV=1 im-launch true; fi'", StartupRole.Optional, "Método de entrada")]
    [InlineData("mintupdate.desktop", "mintupdate-launcher", StartupRole.Recommended, "Linux Mint")]
    [InlineData("warpinator-autostart.desktop", "warpinator --autostart", StartupRole.Optional, "Linux Mint")]
    [InlineData("at-spi-dbus-bus.desktop", "/usr/libexec/at-spi-bus-launcher --launch-immediately", StartupRole.Required, "Acessibilidade")]
    [InlineData("xapp-sn-watcher.desktop", "/usr/libexec/xapps/xapp-sn-watcher", StartupRole.Required, "Bandeja")]
    [InlineData("nvidia-prime.desktop", "sh -c 'sleep 5 && nvidia-prime-applet'", StartupRole.Optional, "NVIDIA")]
    // GNOME / Ubuntu / Fedora
    [InlineData("org.gnome.SettingsDaemon.Power.desktop", "/usr/libexec/gsd-power", StartupRole.Required, "GNOME")]
    [InlineData("org.gnome.SettingsDaemon.Wacom.desktop", "/usr/libexec/gsd-wacom", StartupRole.Optional, "GNOME")]
    [InlineData("org.gnome.SettingsDaemon.DiskUtilityNotify.desktop", "/usr/libexec/gsd-disk-utility-notify", StartupRole.Recommended, "GNOME")]
    [InlineData("org.gnome.Software.desktop", "/usr/bin/gnome-software --gapplication-service", StartupRole.Optional, "GNOME")]
    [InlineData("org.gnome.Evolution-alarm-notify.desktop", "/usr/libexec/evolution-data-server/evolution-alarm-notify", StartupRole.Optional, "Agenda")]
    [InlineData("tracker-miner-fs-3.desktop", "/usr/libexec/tracker-miner-fs-3", StartupRole.Optional, "GNOME")]
    [InlineData("org.freedesktop.IBus.session.GNOME.service", "", StartupRole.Optional, "Método de entrada")]
    // KDE Plasma, XFCE, MATE, LXQt
    [InlineData("baloo_file.desktop", "/usr/bin/baloo_file", StartupRole.Optional, "KDE Plasma")]
    [InlineData("org.kde.plasmashell.desktop", "plasmashell", StartupRole.Required, "KDE Plasma")]
    [InlineData("polkit-kde-authentication-agent-1.desktop", "/usr/lib/x86_64-linux-gnu/libexec/polkit-kde-authentication-agent-1", StartupRole.Required, "KDE Plasma")]
    [InlineData("org.kde.kdeconnect.daemon.desktop", "/usr/lib/x86_64-linux-gnu/libexec/kdeconnectd", StartupRole.Optional, "KDE Plasma")]
    [InlineData("xfce4-power-manager.desktop", "xfce4-power-manager", StartupRole.Required, "XFCE")]
    [InlineData("xfce4-clipman-plugin-autostart.desktop", "xfce4-clipman", StartupRole.Optional, "XFCE")]
    [InlineData("mate-screensaver.desktop", "mate-screensaver", StartupRole.Required, "MATE")]
    [InlineData("lxqt-panel.desktop", "lxqt-panel", StartupRole.Required, "LXQt")]
    [InlineData("budgie-desktop-screensaver.desktop", "budgie-desktop-screensaver", StartupRole.Required, "Budgie")]
    // systemd user units shipped by distributions
    [InlineData("pipewire.service", "", StartupRole.Required, "Som")]
    [InlineData("wireplumber.service", "", StartupRole.Required, "Som")]
    [InlineData("gnome-keyring-daemon.service", "", StartupRole.Required, "Chaveiro")]
    [InlineData("gcr-ssh-agent.service", "", StartupRole.Optional, "Chaveiro")]
    [InlineData("obex.service", "", StartupRole.Optional, "Bluetooth")]
    public void KnownSystemEntriesAreGroupedAsSystemWithPlainAdvice(string id, string exec, StartupRole role, string component)
    {
        var a = Sys(id, exec);
        Assert.Equal(role, a.Role); Assert.Equal(component, a.Component); Assert.Equal(StartupGroup.System, a.Group);
        Assert.True(a.ConfirmBeforeDisable);
        Assert.False(string.IsNullOrWhiteSpace(a.Explanation)); Assert.EndsWith(".", a.Explanation);
        Assert.Equal(role switch
        {
            StartupRole.Required => "Necessário — não desative.",
            StartupRole.Recommended => "Recomendado manter ativado.",
            StartupRole.System => "Do sistema — mantenha ativado se não souber o que é.",
            _ => a.Recommendation,
        }, a.Recommendation);
        if (role == StartupRole.Optional) Assert.StartsWith("Pode desativar se não usa ", a.Recommendation);
    }

    [Theory]
    [InlineData("steam.desktop", "/usr/bin/steam %U -silent", "Steam")]
    [InlineData("discord.desktop", "/usr/bin/discord --start-minimized", "Discord")]
    [InlineData("com.discordapp.Discord.desktop", "flatpak run --command=discord com.discordapp.Discord --start-minimized", "Discord")]
    [InlineData("autostart-app.desktop", "/usr/bin/flatpak run --branch=stable --arch=x86_64 --command=telegram-desktop org.telegram.desktop -startintray", "Telegram")]
    [InlineData("dropbox.desktop", "dropbox start -i", "Dropbox")]
    [InlineData("io.github.bismuthtechnology.ilmenyx.desktop", "ilmenyx --minimized", "Ilmenyx")]
    public void KnownAppsAreTheUsersOwn(string id, string exec, string component)
    {
        var a = StartupCatalog.Classify(new(id, "Desktop", exec, "", false, NoKeys));
        Assert.Equal(StartupRole.App, a.Role); Assert.Equal(component, a.Component); Assert.Equal(StartupGroup.User, a.Group);
        Assert.False(a.ConfirmBeforeDisable); Assert.Equal("Do usuário — pode desativar; o programa continua instalado.", a.Recommendation);
    }

    [Fact]
    public void FallbacksUseDesktopKeysPackageThenLocation()
    {
        // Session phase or KDE phase: piece of the desktop even when the name is new.
        var phase = Sys("new-shell-thing.desktop", "x", new() { ["X-GNOME-Autostart-Phase"] = "Panel", ["OnlyShowIn"] = "MATE;" }, desktop: null);
        Assert.Equal(StartupRole.Required, phase.Role); Assert.Equal("MATE", phase.Component);
        Assert.Equal(StartupRole.Required, Sys("whatever.desktop", "x", new() { ["X-KDE-autostart-phase"] = "1" }).Role);
        // Auto-restart / OnlyShowIn of a single desktop, from the system folder: part of the desktop.
        var restart = Sys("daemon-like.desktop", "zzz", new() { ["X-GNOME-AutoRestart"] = "true" });
        Assert.Equal(StartupRole.System, restart.Role); Assert.Equal(StartupGroup.System, restart.Group);
        Assert.Equal("Cinnamon", Sys("x.desktop", "zzz", new() { ["OnlyShowIn"] = "X-Cinnamon;" }).Component);
        // Package ownership (dpkg -S / rpm -qf / pacman -Qo) identifies the desktop.
        var byPkg = Sys("unlisted.desktop", "zzz", pkg: "xfce4-session");
        Assert.Equal(StartupRole.System, byPkg.Role); Assert.Equal("XFCE", byPkg.Component);
        // Unknown system entry: says so, mentions the package and asks before disabling.
        var unknown = StartupCatalog.Classify(new("vendor-tray.desktop", "Desktop", "/opt/vendor/tray", "Ícone do fabricante", true, NoKeys, "vendor-tools"));
        Assert.Equal(StartupRole.Unknown, unknown.Role); Assert.Equal(StartupGroup.User, unknown.Group); Assert.True(unknown.ConfirmBeforeDisable);
        Assert.Equal("Desconhecido: pesquise antes de desativar.", unknown.Recommendation); Assert.Contains("Ícone do fabricante.", unknown.Explanation); Assert.Contains("vendor-tools", unknown.Explanation);
        // Unknown entry of the user's own folder: the user's.
        var mine = StartupCatalog.Classify(new("myscript.desktop", "Desktop", "/home/u/bin/x.sh", "", false, NoKeys));
        Assert.Equal(StartupRole.User, mine.Role); Assert.False(mine.ConfirmBeforeDisable); Assert.Equal("DO USUÁRIO", mine.Badge);
        // A user copy that overrides a system file is classified as the system entry.
        var over = StartupCatalog.Classify(new("cinnamon-settings-daemon-power.desktop", "Desktop", "csd-power", "", false, NoKeys, OverridesSystem: true));
        Assert.Equal(StartupGroup.System, over.Group);
    }

    [Fact]
    public void OnlyShowInAndNotShowInTellWhetherTheEntryRunsInThisDesktop()
    {
        var mate = Sys("polkit-mate-authentication-agent-1.desktop", "/usr/libexec/polkit-mate-authentication-agent-1", new() { ["OnlyShowIn"] = "MATE;" });
        Assert.False(mate.RunsHere); Assert.Equal("só em MATE", mate.WhereItRuns);
        Assert.Equal("Não abre nesta área de trabalho (só em MATE): ligado ou desligado, não muda nada aqui.", mate.Recommendation);
        Assert.False(mate.ConfirmBeforeDisable); // never starts here: disabling cannot break anything
        Assert.Equal(StartupGroup.System, mate.Group);
        var nm = Sys("nm-applet.desktop", "nm-applet", new() { ["NotShowIn"] = "KDE;GNOME;" }, desktop: "ubuntu:GNOME");
        Assert.False(nm.RunsHere); Assert.Equal("não abre em KDE Plasma, GNOME", nm.WhereItRuns);
        Assert.True(Sys("nm-applet.desktop", "nm-applet", new() { ["NotShowIn"] = "KDE;GNOME;" }).RunsHere);
        Assert.True(Sys("x.desktop", "", new() { ["OnlyShowIn"] = "MATE;" }, desktop: null).RunsHere);
        Assert.True(Sys("orca-autostart.desktop", "orca", new() { ["OnlyShowIn"] = "GNOME;MATE;Unity;Cinnamon;" }, desktop: "X-Cinnamon").RunsHere); // Mint writes both spellings // unknown desktop: assume it runs
    }

    [Fact]
    public void EveryRuleIsWellFormed()
    {
        foreach (var r in StartupCatalog.Rules)
        {
            Assert.NotEmpty(r.Match); Assert.All(r.Match, m => Assert.Equal(m.ToLowerInvariant(), m));
            Assert.EndsWith(".", r.Explanation); Assert.True(r.Explanation.Length <= 140, r.Explanation);
            Assert.Equal(r.Role == StartupRole.Optional, r.Feature is not null);
        }
    }

    [Fact]
    public void PackageOwnerParsersAcceptToolOutputAndRejectNoise()
    {
        var dpkg = StartupCatalog.ParseDpkgOwners("cinnamon-settings-daemon: /etc/xdg/autostart/a.desktop\nlibfoo:amd64: /etc/xdg/autostart/b.desktop\ndiversion by x from: /etc/xdg/autostart/c.desktop\ndpkg-query: no path found matching pattern /etc/xdg/autostart/d.desktop\n");
        Assert.Equal("cinnamon-settings-daemon", dpkg["/etc/xdg/autostart/a.desktop"]); Assert.Equal("libfoo", dpkg["/etc/xdg/autostart/b.desktop"]); Assert.Equal(2, dpkg.Count);
        var rpm = StartupCatalog.ParseRpmOwners(["/a", "/b"], "gnome-settings-daemon\nfile /b is not owned by any package\n");
        Assert.Equal("gnome-settings-daemon", rpm["/a"]); Assert.False(rpm.ContainsKey("/b"));
        Assert.Empty(StartupCatalog.ParseRpmOwners(["/a", "/b"], "only-one\n")); // cannot pair: nothing
        var pac = StartupCatalog.ParsePacmanOwners("/etc/xdg/autostart/a.desktop is owned by plasma-workspace 6.2.0-1\nerror: No package owns /etc/xdg/autostart/b.desktop\n");
        Assert.Equal("plasma-workspace", pac["/etc/xdg/autostart/a.desktop"]); Assert.Single(pac);
    }

    [Fact]
    public async Task ManagerAdvisesEntriesAsksOwnersOnceAndSortsSystemFirst()
    {
        string cfg = Path.Combine(root, "cfg"), sys = Path.Combine(root, "sys");
        Directory.CreateDirectory(Path.Combine(cfg, "autostart")); Directory.CreateDirectory(Path.Combine(cfg, "systemd/user")); Directory.CreateDirectory(sys);
        File.WriteAllText(Path.Combine(sys, "cinnamon-settings-daemon-power.desktop"), "[Desktop Entry]\nName=Power\nExec=csd-power\nOnlyShowIn=X-Cinnamon;\nX-GNOME-Autostart-Phase=Initialization\n");
        File.WriteAllText(Path.Combine(sys, "vendor.desktop"), "[Desktop Entry]\nName=Vendor\nExec=/opt/v\n");
        File.WriteAllText(Path.Combine(cfg, "autostart/steam.desktop"), "[Desktop Entry]\nName=Steam\nExec=/usr/bin/steam -silent\n");
        File.WriteAllText(Path.Combine(cfg, "systemd/user/mine.service"), "[Service]\nExecStart=/bin/true\n");
        var asked = new List<IReadOnlyList<string>>();
        var m = new LinuxStartupManager(Path.Combine(root, "cfg"), Path.Combine(root, "data"), [sys],
            (args, _) => Task.FromResult((0, args.Contains("list-unit-files") ? "pipewire.service enabled enabled\nmine.service enabled enabled\nodd.service enabled enabled\n" : "")),
            (files, _) => { asked.Add(files); return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { [Path.Combine(sys, "vendor.desktop")] = "vendor-pkg" }); },
            "X-Cinnamon");
        var list = await m.ListAsync();
        Assert.Single(asked); Assert.Equal(2, asked[0].Count); // only system files, one batch
        Assert.Equal(StartupGroup.System, list[0].Advice.Group);
        Assert.True(list.TakeWhile(e => e.Advice.Group == StartupGroup.System).Count() == list.Count(e => e.Advice.Group == StartupGroup.System));
        Assert.Equal(StartupRole.Required, list.Single(e => e.Name == "Power").Advice.Role);
        Assert.Contains("vendor-pkg", list.Single(e => e.Name == "Vendor").Advice.Explanation);
        Assert.Equal(StartupRole.App, list.Single(e => e.Name == "Steam").Advice.Role);
        Assert.Equal(StartupRole.Required, list.Single(e => e.Id == "pipewire.service").Advice.Role);
        Assert.Equal(StartupRole.User, list.Single(e => e.Id == "mine.service").Advice.Role);    // ~/.config/systemd/user
        Assert.Equal(StartupRole.Unknown, list.Single(e => e.Id == "odd.service").Advice.Role);  // shipped by a package, not in the table
        // A failing lookup only loses the package names.
        var failing = new LinuxStartupManager(cfg, Path.Combine(root, "data"), [sys], null, (_, _) => throw new TimeoutException(), "X-Cinnamon");
        Assert.Equal(StartupRole.Unknown, (await failing.ListAsync()).Single(e => e.Name == "Vendor").Advice.Role);
    }
}
