// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Plain-language explanation of each startup entry: does it belong to the desktop/system or to an app the user installed?
// Data-driven: known file names / commands first, then the standard keys of the .desktop file, then the owning package (optional).

namespace Ilmenyx.Core;

/// <summary>Which list the entry goes to on the Inicialização page.</summary>
public enum StartupGroup { System, User }

/// <summary>What the page recommends.</summary>
public enum StartupRole
{
    /// <summary>Piece of the desktop the session needs ("Necessário — não desative").</summary>
    Required,
    /// <summary>System piece that is better kept (updates, backups, disk warnings).</summary>
    Recommended,
    /// <summary>System piece that only matters if the user uses a feature ("Pode desativar se não usa X").</summary>
    Optional,
    /// <summary>Recognised as part of the desktop/distribution, but without a specific explanation.</summary>
    System,
    /// <summary>Known third-party app.</summary>
    App,
    /// <summary>Entry of the user's own autostart folder that is not in the table.</summary>
    User,
    /// <summary>Installed for all users and not recognised.</summary>
    Unknown,
}

/// <summary>Facts read from the entry. Keys are the [Desktop Entry] keys (empty for systemd services).</summary>
public sealed record StartupFacts(string Id, string Kind, string Exec, string Comment, bool FromSystemDir, IReadOnlyDictionary<string, string> Keys, string? Package = null, string? CurrentDesktop = null, bool OverridesSystem = false);

public sealed record StartupAdvice(StartupGroup Group, StartupRole Role, string Component, string Explanation, string Recommendation, bool RunsHere, string WhereItRuns)
{
    /// <summary>The page asks before disabling anything that is not plainly the user's own program.</summary>
    public bool ConfirmBeforeDisable => RunsHere && (Group == StartupGroup.System || Role == StartupRole.Unknown);

    public string Badge => Role switch
    {
        StartupRole.Required => "NECESSÁRIO",
        StartupRole.Recommended => "RECOMENDADO",
        StartupRole.Optional => "OPCIONAL",
        StartupRole.System => "DO SISTEMA",
        StartupRole.Unknown => "DESCONHECIDO",
        _ => "DO USUÁRIO",
    };
}

/// <summary>One line of the table. <see cref="Match"/> entries are lower-case file-name stems or command names; a trailing '*' is a prefix.</summary>
public sealed record StartupRule(string[] Match, StartupRole Role, string Component, string Explanation, string? Feature = null);

public static class StartupCatalog
{
    public const string GroupSystemTitle = "Do sistema (área de trabalho)";
    public const string GroupUserTitle = "Programas instalados por você";

    private const string Cin = "Cinnamon", Gn = "GNOME", Kde = "KDE Plasma", Xf = "XFCE", Ma = "MATE", Lq = "LXQt", Bg = "Budgie", Mint = "Linux Mint";
    private const string Polkit = "Mostra a janela que pede a sua senha quando um programa precisa de permissão de administrador.";
    private const string Keys_ = "Faz funcionar as teclas de volume, brilho e mídia e os atalhos do sistema.";
    private const string Power = "Controla bateria, suspensão, brilho da tela e o que acontece ao fechar a tampa.";
    private const string Xsettings = "Passa tema, fontes e ícones para os programas abrirem com a aparência certa.";
    private const string Keyboard = "Aplica o layout do teclado (ABNT2, acentos) e o Num Lock.";
    private const string A11y = "Aplica as opções de acessibilidade (letras grandes, contraste, teclas de aderência).";
    private const string Housekeeping = "Avisa quando o disco está ficando cheio e limpa miniaturas antigas.";
    private const string ScreenProxy = "Impede que a tela apague ou bloqueie enquanto você assiste a vídeos.";
    private const string Lock = "Bloqueia a tela quando você se afasta ou suspende o PC.";
    private const string InputMethod = "Inicia o método de entrada (IBus/Fcitx), usado para digitar em outros alfabetos e inserir emojis.";
    private const string InputFeature = "teclados de outros alfabetos (chinês, japonês, coreano…) nem o seletor de emojis";
    private const string Search = "Indexa os seus arquivos para a busca encontrá-los rápido (usa disco e processador no início).";
    private const string Updates = "Avisa quando há atualizações do sistema, inclusive de segurança.";
    private const string Vm = "Integração com máquina virtual (copiar e colar, ajustar a tela).";

    /// <summary>Most specific first: the first matching rule wins.</summary>
    public static readonly IReadOnlyList<StartupRule> Rules =
    [
        // Cinnamon (cinnamon-settings-daemon, one .desktop per plugin)
        new(["cinnamon-settings-daemon-a11y-settings", "csd-a11y-settings"], StartupRole.Required, Cin, A11y),
        new(["cinnamon-settings-daemon-automount", "csd-automount"], StartupRole.Required, Cin, "Abre e monta pendrives, HDs externos e CDs quando você os conecta."),
        new(["cinnamon-settings-daemon-background", "csd-background"], StartupRole.Required, Cin, "Desenha o papel de parede da área de trabalho."),
        new(["cinnamon-settings-daemon-clipboard", "csd-clipboard"], StartupRole.Required, Cin, "Mantém o texto copiado disponível mesmo depois de fechar o programa de onde ele veio."),
        new(["cinnamon-settings-daemon-color", "csd-color"], StartupRole.Optional, Cin, "Aplica perfis de cor calibrados ao monitor e à impressora.", "perfis de cor (calibração de monitor)"),
        new(["cinnamon-settings-daemon-housekeeping", "csd-housekeeping"], StartupRole.Required, Cin, Housekeeping),
        new(["cinnamon-settings-daemon-keyboard", "csd-keyboard"], StartupRole.Required, Cin, Keyboard),
        new(["cinnamon-settings-daemon-media-keys", "csd-media-keys"], StartupRole.Required, Cin, Keys_),
        new(["cinnamon-settings-daemon-power", "csd-power"], StartupRole.Required, Cin, Power),
        new(["cinnamon-settings-daemon-print-notifications", "csd-print-notifications"], StartupRole.Optional, Cin, "Mostra avisos da impressora (sem papel, impressão concluída).", "impressora"),
        new(["cinnamon-settings-daemon-screensaver-proxy", "csd-screensaver-proxy"], StartupRole.Required, Cin, ScreenProxy),
        new(["cinnamon-settings-daemon-smartcard", "csd-smartcard"], StartupRole.Optional, Cin, "Reage a cartões inteligentes (por exemplo, certificado digital em cartão).", "leitor de cartão inteligente"),
        new(["cinnamon-settings-daemon-wacom", "csd-wacom"], StartupRole.Optional, Cin, "Configura mesas digitalizadoras (Wacom).", "mesa digitalizadora"),
        new(["cinnamon-settings-daemon-xsettings", "csd-xsettings"], StartupRole.Required, Cin, Xsettings),
        new(["cinnamon-settings-daemon-*", "csd-*"], StartupRole.System, Cin, "Parte do serviço de configurações do Cinnamon."),
        new(["cinnamon*", "nemo-autostart", "nemo-desktop"], StartupRole.Required, Cin, "Parte da área de trabalho Cinnamon (painel, ícones da área de trabalho ou sessão)."),

        // GNOME (gnome-settings-daemon plugins, also used by Ubuntu, Budgie and others)
        new(["org.gnome.settingsdaemon.a11ysettings"], StartupRole.Required, Gn, A11y),
        new(["org.gnome.settingsdaemon.color"], StartupRole.Optional, Gn, "Aplica perfis de cor e a luz noturna.", "perfis de cor nem a luz noturna"),
        new(["org.gnome.settingsdaemon.datetime"], StartupRole.Required, Gn, "Acerta o fuso horário automaticamente."),
        new(["org.gnome.settingsdaemon.housekeeping"], StartupRole.Required, Gn, Housekeeping),
        new(["org.gnome.settingsdaemon.keyboard"], StartupRole.Required, Gn, Keyboard),
        new(["org.gnome.settingsdaemon.mediakeys"], StartupRole.Required, Gn, Keys_),
        new(["org.gnome.settingsdaemon.power"], StartupRole.Required, Gn, Power),
        new(["org.gnome.settingsdaemon.printnotifications"], StartupRole.Optional, Gn, "Mostra avisos da impressora (sem papel, impressão concluída).", "impressora"),
        new(["org.gnome.settingsdaemon.rfkill"], StartupRole.Required, Gn, "Controla o modo avião e liga/desliga Wi-Fi e Bluetooth."),
        new(["org.gnome.settingsdaemon.screensaverproxy"], StartupRole.Required, Gn, ScreenProxy),
        new(["org.gnome.settingsdaemon.sharing"], StartupRole.Optional, Gn, "Liga o compartilhamento de tela e de arquivos pela rede quando ativado.", "compartilhamento pela rede"),
        new(["org.gnome.settingsdaemon.smartcard"], StartupRole.Optional, Gn, "Reage a cartões inteligentes (por exemplo, certificado digital em cartão).", "leitor de cartão inteligente"),
        new(["org.gnome.settingsdaemon.sound"], StartupRole.Required, Gn, "Aplica as configurações de som da sessão."),
        new(["org.gnome.settingsdaemon.usbprotection"], StartupRole.Required, Gn, "Bloqueia aparelhos USB novos enquanto a tela está travada."),
        new(["org.gnome.settingsdaemon.wacom"], StartupRole.Optional, Gn, "Configura mesas digitalizadoras (Wacom).", "mesa digitalizadora"),
        new(["org.gnome.settingsdaemon.wwan"], StartupRole.Optional, Gn, "Controla modems de celular (4G/5G).", "modem de celular"),
        new(["org.gnome.settingsdaemon.xsettings"], StartupRole.Required, Gn, Xsettings),
        new(["org.gnome.settingsdaemon.diskutilitynotify", "gsd-disk-utility-notify"], StartupRole.Recommended, Gn, "Avisa se um disco começar a dar sinais de defeito (SMART)."),
        new(["org.gnome.settingsdaemon.*", "gsd-*", "gnome-settings-daemon*"], StartupRole.System, Gn, "Parte do serviço de configurações do GNOME."),
        new(["org.gnome.software", "gnome-software-service", "gnome-software"], StartupRole.Optional, Gn, "A Loja de apps fica em segundo plano para procurar atualizações.", "os avisos de atualização da Loja (atualize por conta própria)"),
        new(["org.gnome.evolution-alarm-notify", "evolution-alarm-notify", "evolution-*"], StartupRole.Optional, "Agenda", "Mostra lembretes da agenda (Evolution / Agenda do GNOME).", "lembretes de agenda"),
        new(["tracker-*", "localsearch-*"], StartupRole.Optional, Gn, Search, "a busca de arquivos do GNOME"),
        new(["org.gnome.dejadup.monitor", "deja-dup-monitor"], StartupRole.Recommended, "Backup", "Faz os backups agendados do Déjà Dup."),
        new(["gnome-keyring-secrets", "gnome-keyring-daemon"], StartupRole.Required, "Chaveiro", "Guarda as suas senhas salvas (Wi-Fi, navegador, e-mail)."),
        new(["gnome-keyring-pkcs11", "p11-kit-server"], StartupRole.Recommended, "Chaveiro", "Disponibiliza certificados digitais guardados no chaveiro."),
        new(["gnome-keyring-ssh", "gcr-ssh-agent", "ssh-agent", "gpg-agent*"], StartupRole.Optional, "Chaveiro", "Guarda chaves SSH/GPG para você não digitar a senha delas toda vez.", "SSH (acesso remoto pelo terminal) nem GPG"),
        new(["org.gnome.shell*", "gnome-shell*", "gnome-session*", "gnome-initial-setup*", "org.gnome.*"], StartupRole.System, Gn, "Parte da área de trabalho GNOME."),

        // KDE Plasma
        new(["org.kde.plasmashell", "plasmashell"], StartupRole.Required, Kde, "Painel, menu e área de trabalho do Plasma."),
        new(["polkit-kde-authentication-agent-1", "org.kde.polkit-kde-authentication-agent-1"], StartupRole.Required, Kde, Polkit),
        new(["org.kde.powerdevil", "powerdevil"], StartupRole.Required, Kde, Power),
        new(["xembedsniproxy"], StartupRole.Required, Kde, "Faz ícones de programas antigos aparecerem na bandeja."),
        new(["gmenudbusmenuproxy"], StartupRole.Required, Kde, "Mostra os menus de alguns programas (GTK) no painel."),
        new(["kaccess", "org.kde.kaccess"], StartupRole.Required, Kde, A11y),
        new(["pam_kwallet_init", "pam_kwallet5_init", "org.kde.kwalletd*"], StartupRole.Required, Kde, "Abre a carteira de senhas do KDE (KWallet) junto com o login."),
        new(["baloo_file", "org.kde.baloo*"], StartupRole.Optional, Kde, Search, "a busca de arquivos do KDE"),
        new(["org.kde.discover.notifier", "discover-notifier"], StartupRole.Recommended, Kde, Updates),
        new(["org.kde.kdeconnect.daemon", "org.kde.kdeconnect*", "kdeconnectd", "kdeconnect-indicator"], StartupRole.Optional, Kde, "Integra o PC com o celular (notificações, arquivos, controle remoto).", "KDE Connect"),
        new(["org.kde.klipper", "klipper"], StartupRole.Optional, Kde, "Guarda o histórico da área de transferência.", "o histórico de itens copiados"),
        new(["org.kde.kalendarac", "kalendarac", "org.kde.merkuro*"], StartupRole.Optional, Kde, "Mostra lembretes da agenda.", "lembretes de agenda"),
        new(["org.kde.plasma-welcome", "plasma-welcome"], StartupRole.Optional, Kde, "Tela de boas-vindas do Plasma.", "a tela de boas-vindas"),
        new(["org.kde.*", "plasma-*", "plasma_*", "kde*", "kwin*", "ksmserver*", "kded*"], StartupRole.System, Kde, "Parte da área de trabalho KDE Plasma."),

        // XFCE
        new(["xfce4-power-manager"], StartupRole.Required, Xf, Power),
        new(["xfce4-notifyd"], StartupRole.Required, Xf, "Mostra as notificações na tela."),
        new(["xfce4-screensaver", "light-locker"], StartupRole.Required, Xf, Lock),
        new(["xfce4-volumed-pulse", "xfce4-volumed"], StartupRole.Required, Xf, "Faz funcionar as teclas de volume."),
        new(["xfce4-clipman-plugin-autostart", "xfce4-clipman"], StartupRole.Optional, Xf, "Guarda o histórico da área de transferência.", "o histórico de itens copiados"),
        new(["xiccd"], StartupRole.Optional, Xf, "Aplica perfis de cor ao monitor.", "perfis de cor (calibração de monitor)"),
        new(["xfce-polkit", "xfce4-polkit"], StartupRole.Required, Xf, Polkit),
        new(["xfce4-*", "xfce-*", "xfsettingsd", "xfdesktop", "xfwm4", "thunar*"], StartupRole.System, Xf, "Parte da área de trabalho XFCE."),

        // MATE
        new(["mate-settings-daemon"], StartupRole.Required, Ma, "Aplica tema, teclado, mouse e teclas especiais no MATE."),
        new(["mate-power-manager"], StartupRole.Required, Ma, Power),
        new(["mate-screensaver"], StartupRole.Required, Ma, Lock),
        new(["mate-volume-control-status-icon", "mate-volume-control-applet"], StartupRole.Required, Ma, "Ícone de volume no painel."),
        new(["polkit-mate-authentication-agent-1", "mate-polkit*"], StartupRole.Required, Ma, Polkit),
        new(["mate-*", "caja*"], StartupRole.System, Ma, "Parte da área de trabalho MATE."),

        // LXQt / LXDE
        new(["lxqt-panel"], StartupRole.Required, Lq, "Painel e menu do LXQt."),
        new(["lxqt-policykit-agent"], StartupRole.Required, Lq, Polkit),
        new(["lxqt-powermanagement"], StartupRole.Required, Lq, Power),
        new(["lxqt-notifications"], StartupRole.Required, Lq, "Mostra as notificações na tela."),
        new(["lxqt-globalkeyshortcuts"], StartupRole.Required, Lq, "Faz funcionar os atalhos de teclado do sistema."),
        new(["lxqt-runner"], StartupRole.Optional, Lq, "Lançador rápido de programas (Alt+F2).", "o lançador rápido"),
        new(["lxqt-*", "pcmanfm-qt*"], StartupRole.System, Lq, "Parte da área de trabalho LXQt."),
        new(["lxpolkit"], StartupRole.Required, "LXDE", Polkit),

        // Budgie
        new(["budgie-desktop-screensaver"], StartupRole.Required, Bg, Lock),
        new(["budgie-desktop-nm-applet"], StartupRole.Required, Bg, "Ícone de rede no painel: é por ele que você escolhe o Wi-Fi."),
        new(["budgie-*", "org.buddiesofbudgie.*"], StartupRole.System, Bg, "Parte da área de trabalho Budgie."),

        // Shared pieces (any desktop)
        new(["polkit-gnome-authentication-agent-1", "polkit-gnome*", "polkit-*"], StartupRole.Required, "Permissões", Polkit),
        new(["at-spi-dbus-bus", "at-spi-dbus-bus.service"], StartupRole.Required, "Acessibilidade", "Ponte de acessibilidade: leitores de tela e muitos programas dependem dela."),
        new(["xapp-sn-watcher"], StartupRole.Required, "Bandeja", "Faz os ícones dos programas aparecerem na bandeja do painel."),
        new(["xdg-user-dirs", "user-dirs-update-gtk", "xdg-user-dirs-gtk"], StartupRole.Required, "Pastas pessoais", "Mantém as pastas pessoais (Documentos, Downloads…) com os nomes do seu idioma."),
        new(["nm-applet", "network-manager-applet"], StartupRole.Required, "Rede", "Ícone de rede no painel: é por ele que você escolhe o Wi-Fi."),
        new(["blueman", "blueman-applet"], StartupRole.Optional, "Bluetooth", "Ícone do Bluetooth no painel, para parear fones, mouses e caixas de som.", "Bluetooth"),
        new(["obex"], StartupRole.Optional, "Bluetooth", "Envia e recebe arquivos por Bluetooth.", "envio de arquivos por Bluetooth"),
        new(["print-applet", "system-config-printer-applet"], StartupRole.Optional, "Impressão", "Ícone da fila de impressão.", "impressora"),
        new(["im-launch", "ibus*", "org.freedesktop.ibus*", "fcitx*", "org.fcitx.*"], StartupRole.Optional, "Método de entrada", InputMethod, InputFeature),
        new(["geoclue-demo-agent", "geoclue*"], StartupRole.Optional, "Localização", "Libera a localização para apps que pedem (fuso horário automático, luz noturna, clima).", "localização automática"),
        new(["onboard-autostart", "onboard", "caribou*"], StartupRole.Optional, "Acessibilidade", "Teclado na tela (só abre se estiver ligado nas opções de acessibilidade).", "teclado na tela"),
        new(["orca-autostart", "orca"], StartupRole.Optional, "Acessibilidade", "Leitor de tela para quem não enxerga (só abre se estiver ligado na acessibilidade).", "leitor de tela"),
        new(["pulseaudio", "pulseaudio.service"], StartupRole.Required, "Som", "Servidor de som: sem ele os programas ficam sem áudio."),
        new(["pipewire", "pipewire-pulse"], StartupRole.Required, "Som", "Servidor de som (PipeWire): sem ele os programas ficam sem áudio."),
        new(["wireplumber"], StartupRole.Required, "Som", "Escolhe as saídas e entradas de som (fone, caixa, microfone) para o PipeWire."),
        new(["filter-chain"], StartupRole.Recommended, "Som", "Filtros de áudio do PipeWire (por exemplo, cancelamento de eco)."),
        new(["xdg-desktop-portal-rewrite-launchers"], StartupRole.Recommended, "Portais", "Ajusta os atalhos de apps Flatpak/Snap depois de atualizações."),
        new(["xdg-desktop-portal*"], StartupRole.Required, "Portais", "Ponte que deixa apps Flatpak/Snap abrirem arquivos e compartilharem a tela."),
        new(["dbus*"], StartupRole.Required, "Sistema", "Canal de comunicação entre os programas da sessão."),
        new(["gvfs-*"], StartupRole.Required, "Arquivos", "Acesso a pendrives, celulares e pastas de rede no gerenciador de arquivos."),
        new(["session-migration"], StartupRole.Recommended, "Sistema", "Ajusta as suas configurações depois de atualizações do sistema (roda rápido e termina)."),
        new(["snap-userd-autostart", "snapd.session-agent", "io.snapcraft.*"], StartupRole.Recommended, "Snap", "Necessário para apps Snap abrirem links e funcionarem direito."),
        new(["xscreensaver", "lxqt-xscreensaver-autostart"], StartupRole.Recommended, "Tela", Lock),
        new(["update-notifier", "org.freedesktop.packagekit*", "gnome-packagekit*"], StartupRole.Recommended, "Atualizações", Updates),
        new(["zeitgeist-datahub", "zeitgeist*"], StartupRole.Optional, "Sistema", "Registra a sua atividade recente para sugestões e arquivos recentes.", "a lista de atividades recentes"),
        new(["spice-vdagent", "vmware-user*", "vboxclient*", "open-vm-tools*"], StartupRole.Optional, "Máquina virtual", Vm, "máquina virtual"),

        // Distribution tools (Linux Mint)
        new(["mintupdate", "mintupdate-launcher"], StartupRole.Recommended, Mint, Updates),
        new(["mintreport", "mintreport-tray"], StartupRole.Optional, Mint, "Relatórios do sistema: avisa sobre falhas de programas e pacotes de idioma faltando.", "os avisos de relatório do sistema"),
        new(["mintwelcome"], StartupRole.Optional, Mint, "Tela de boas-vindas do Linux Mint.", "a tela de boas-vindas"),
        new(["warpinator-autostart", "warpinator", "org.x.warpinator"], StartupRole.Optional, Mint, "Envia e recebe arquivos de outros PCs da mesma rede (Warpinator).", "a troca de arquivos pela rede"),
        new(["sticky", "org.x.sticky"], StartupRole.Optional, Mint, "Notas adesivas na área de trabalho.", "notas adesivas"),
        new(["touchegg"], StartupRole.Optional, "Touchpad", "Gestos com vários dedos no touchpad.", "gestos no touchpad"),

        // GPU vendor tools that come with the driver
        new(["nvidia-prime", "nvidia-prime-applet"], StartupRole.Optional, "NVIDIA", "Ícone para trocar entre a placa integrada e a NVIDIA (PRIME).", "o ícone de troca de placa de vídeo"),
        new(["nvidia-settings-autostart", "nvidia-settings"], StartupRole.Recommended, "NVIDIA", "Carrega as configurações salvas no painel da NVIDIA ao entrar."),

        // Apps the user installs (common on Linux desktops)
        new(["steam", "com.valvesoftware.steam"], StartupRole.App, "Steam", "Abre a Steam (loja e biblioteca de jogos) ao entrar."),
        new(["discord", "com.discordapp.discord", "vesktop", "dev.vencord.vesktop"], StartupRole.App, "Discord", "Abre o Discord (chat de voz e texto) ao entrar."),
        new(["telegramdesktop", "org.telegram.desktop", "telegram-desktop"], StartupRole.App, "Telegram", "Abre o Telegram ao entrar."),
        new(["signal-desktop", "org.signal.signal"], StartupRole.App, "Signal", "Abre o Signal ao entrar."),
        new(["slack", "com.slack.slack"], StartupRole.App, "Slack", "Abre o Slack ao entrar."),
        new(["skypeforlinux", "com.skype.client"], StartupRole.App, "Skype", "Abre o Skype ao entrar."),
        new(["spotify", "com.spotify.client"], StartupRole.App, "Spotify", "Abre o Spotify ao entrar."),
        new(["zoom", "us.zoom.zoom"], StartupRole.App, "Zoom", "Abre o Zoom ao entrar."),
        new(["dropbox"], StartupRole.App, "Dropbox", "Sincroniza a pasta do Dropbox com a nuvem."),
        new(["megasync", "nz.mega.megasync"], StartupRole.App, "MEGA", "Sincroniza pastas com a nuvem do MEGA."),
        new(["nextcloud", "com.nextcloud.desktopclient.nextcloud", "owncloud", "com.owncloud.*"], StartupRole.App, "Nextcloud", "Sincroniza pastas com o seu servidor Nextcloud/ownCloud."),
        new(["insync", "onedrive", "pcloud", "com.github.abraunegg.onedrive"], StartupRole.App, "Nuvem", "Sincroniza pastas com um serviço de nuvem."),
        new(["syncthing*", "com.github.zocker_160.syncthinggtk"], StartupRole.App, "Syncthing", "Sincroniza pastas entre os seus aparelhos."),
        new(["flameshot", "org.flameshot.flameshot"], StartupRole.App, "Flameshot", "Ferramenta de captura de tela (Print Screen)."),
        new(["copyq", "com.github.hluk.copyq"], StartupRole.App, "CopyQ", "Guarda o histórico da área de transferência."),
        new(["ulauncher", "albert"], StartupRole.App, "Lançador", "Lançador rápido de programas por teclado."),
        new(["conky*"], StartupRole.App, "Conky", "Mostra informações do sistema na área de trabalho."),
        new(["plank", "docky", "cairo-dock"], StartupRole.App, "Dock", "Barra de atalhos (dock) na tela."),
        new(["redshift*", "gammastep*"], StartupRole.App, "Luz noturna", "Deixa a tela mais amarelada à noite."),
        new(["solaar"], StartupRole.App, "Solaar", "Gerencia mouses e teclados sem fio da Logitech."),
        new(["openrgb", "org.openrgb.openrgb"], StartupRole.App, "OpenRGB", "Controla as luzes RGB das peças."),
        new(["corectrl", "org.corectrl.corectrl", "lact", "io.github.ilya_zlobintsev.lact"], StartupRole.App, "GPU", "Ajusta perfis da placa de vídeo (ventoinha, clock)."),
        new(["protonvpn*", "nordvpn*", "com.protonvpn.*"], StartupRole.App, "VPN", "Conecta a VPN ao entrar."),
        new(["keepassxc", "org.keepassxc.keepassxc", "bitwarden", "com.bitwarden.desktop", "1password"], StartupRole.App, "Senhas", "Gerenciador de senhas."),
        new(["guake", "tilda"], StartupRole.App, "Terminal", "Terminal que desce do topo da tela com um atalho."),
        new(["barrier", "input-leap", "com.github.debauchee.barrier"], StartupRole.App, "Barrier", "Compartilha teclado e mouse entre computadores."),
        new(["teamviewer*", "anydesk", "rustdesk", "com.rustdesk.rustdesk"], StartupRole.App, "Acesso remoto", "Permite acesso remoto a este PC."),
        new(["jetbrains-toolbox"], StartupRole.App, "JetBrains", "Gerencia as IDEs da JetBrains."),
        new(["variety"], StartupRole.App, "Variety", "Troca o papel de parede automaticamente."),
        new(["ilmenyx*", "io.github.bismuthtechnology.ilmenyx*"], StartupRole.App, "Ilmenyx", "O próprio Ilmenyx (monitor ou avisos de dispositivos)."),
    ];

    private static readonly (string Token, string Name)[] Desktops =
    [
        ("X-Cinnamon", Cin), ("Cinnamon", Cin), ("GNOME", Gn), ("KDE", Kde), ("XFCE", Xf), ("MATE", Ma), ("LXQt", Lq), ("LXDE", "LXDE"),
        ("Budgie", Bg), ("Unity", "Unity"), ("Pantheon", "Pantheon"), ("Deepin", "Deepin"), ("COSMIC", "COSMIC"),
    ];

    /// <summary>Package name prefixes that identify the desktop (used when the owning package was found).</summary>
    private static readonly (string Prefix, string Name)[] DesktopPackages =
    [
        ("cinnamon", Cin), ("nemo", Cin), ("gnome-shell", Gn), ("gnome-session", Gn), ("gnome-settings-daemon", Gn), ("plasma-", Kde), ("kde-", Kde), ("kwin", Kde), ("powerdevil", Kde),
        ("xfce4-", Xf), ("xfdesktop", Xf), ("mate-", Ma), ("caja", Ma), ("lxqt-", Lq), ("budgie-", Bg), ("xapp", Mint), ("mint", Mint),
    ];

    private static readonly string[] SessionPhases = ["PreDisplayServer", "DisplayServer", "Initialization", "WindowManager", "Panel", "Desktop"];

    public static string Stem(string id)
    {
        string s = id.ToLowerInvariant();
        foreach (var ext in new[] { ".desktop", ".service" }) if (s.EndsWith(ext, StringComparison.Ordinal)) return s[..^ext.Length];
        return s;
    }

    private static bool Matches(string pattern, string value) => pattern.EndsWith('*') ? value.StartsWith(pattern[..^1], StringComparison.Ordinal) : value == pattern;

    public static StartupRule? Find(string id, string exec)
    {
        string stem = Stem(id);
        string exe = Path.GetFileName(ExeOf(exec)).ToLowerInvariant();
        foreach (var r in Rules) if (r.Match.Any(m => Matches(m, stem))) return r;
        if (exe.Length > 0 && exe is not ("env" or "sh" or "bash" or "flatpak" or "python3" or "python"))
            foreach (var r in Rules) if (r.Match.Any(m => Matches(m, exe))) return r;
        // Flatpak autostart: "flatpak run --command=x com.app.Id" — the last argument without '-' is the app id.
        if (exe == "flatpak")
        {
            string? appId = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(a => !a.StartsWith('-') && a.Contains('.'))?.ToLowerInvariant();
            if (appId is not null) foreach (var r in Rules) if (r.Match.Any(m => Matches(m, appId))) return r;
        }
        return null;
    }

    private static string ExeOf(string exec)
    {
        string c = exec.Trim();
        if (c.Length == 0) return "";
        if (c[0] == '"' && c.IndexOf('"', 1) is > 1 and var end) return c[1..end];
        return c.Split(' ', 2)[0];
    }

    private static string? DesktopOf(string tokens)
    {
        foreach (var t in tokens.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var (token, name) in Desktops) if (t.Equals(token, StringComparison.OrdinalIgnoreCase)) return name;
        return null;
    }

    /// <summary>Does the entry run in <paramref name="currentDesktop"/> (XDG_CURRENT_DESKTOP, colon separated)? Unknown desktop: assume yes.</summary>
    public static (bool Runs, string Where) RunsIn(IReadOnlyDictionary<string, string> keys, string? currentDesktop)
    {
        var current = (currentDesktop ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        var only = keys.GetValueOrDefault("OnlyShowIn", "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var not = keys.GetValueOrDefault("NotShowIn", "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        string Names(IEnumerable<string> t) => string.Join(", ", t.Select(x => DesktopOf(x) ?? x).Distinct());
        string where = only.Count > 0 ? "só em " + Names(only) : not.Count > 0 ? "não abre em " + Names(not) : "";
        if (current.Count == 0) return (true, where);
        // "Cinnamon" and "X-Cinnamon" are both used in the wild for the same desktop: compare without the X- prefix.
        static string Norm(string t) => t.StartsWith("X-", StringComparison.OrdinalIgnoreCase) ? t[2..] : t;
        bool Has(string t) => current.Any(c => Norm(c).Equals(Norm(t), StringComparison.OrdinalIgnoreCase));
        bool runs = (only.Count == 0 || only.Any(Has)) && !not.Any(Has);
        return (runs, where);
    }

    public static string RecommendationFor(StartupRole role, string? feature) => role switch
    {
        StartupRole.Required => "Necessário — não desative.",
        StartupRole.Recommended => "Recomendado manter ativado.",
        StartupRole.Optional => $"Pode desativar se não usa {feature ?? "este recurso"}.",
        StartupRole.System => "Do sistema — mantenha ativado se não souber o que é.",
        StartupRole.Unknown => "Desconhecido: pesquise antes de desativar.",
        _ => "Do usuário — pode desativar; o programa continua instalado.",
    };

    public static StartupAdvice Classify(StartupFacts f)
    {
        var (runs, where) = f.Kind == "Desktop" ? RunsIn(f.Keys, f.CurrentDesktop) : (true, "");
        StartupAdvice Make(StartupRole role, string component, string explanation, string? feature = null)
        {
            var group = role is StartupRole.App or StartupRole.User or StartupRole.Unknown ? StartupGroup.User : StartupGroup.System;
            // Entries limited to another desktop never start here: the honest advice is that the switch changes nothing.
            string advice = runs ? RecommendationFor(role, feature) : $"Não abre nesta área de trabalho ({where}): ligado ou desligado, não muda nada aqui.";
            return new(group, role, component, explanation, advice, runs, where);
        }
        string described = f.Comment.Length > 0 ? f.Comment.TrimEnd('.') + "." : "";

        if (Find(f.Id, f.Exec) is { } rule) return Make(rule.Role, rule.Component, rule.Explanation, rule.Feature);

        // Standard keys: session phases and auto-restart are only used by pieces of the desktop session.
        string? desktop = DesktopOf(f.Keys.GetValueOrDefault("OnlyShowIn", ""));
        string phase = f.Keys.GetValueOrDefault("X-GNOME-Autostart-Phase", "");
        if (SessionPhases.Contains(phase, StringComparer.OrdinalIgnoreCase) || f.Keys.ContainsKey("X-KDE-autostart-phase"))
            return Make(StartupRole.Required, desktop ?? (f.Keys.ContainsKey("X-KDE-autostart-phase") ? Kde : "Área de trabalho"), described.Length > 0 ? described : "Parte da sessão da área de trabalho (abre numa fase inicial do login).");
        bool autoRestart = f.Keys.Any(k => k.Key is "X-GNOME-AutoRestart" or "X-MATE-AutoRestart" or "X-Cinnamon-AutoRestart" && k.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
        if (f.Keys.Keys.Any(k => k.StartsWith("X-KDE-", StringComparison.Ordinal) || k.StartsWith("X-Cinnamon", StringComparison.Ordinal)) && (f.FromSystemDir || f.OverridesSystem))
            desktop ??= f.Keys.Keys.Any(k => k.StartsWith("X-KDE-", StringComparison.Ordinal)) ? Kde : Cin;
        if (f.Package is { Length: > 0 } pkg && DesktopPackages.FirstOrDefault(p => pkg.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase)) is { Name: { } byPackage })
            return Make(StartupRole.System, byPackage, described.Length > 0 ? described : $"Parte da área de trabalho {byPackage} (pacote {pkg}).");
        if ((f.FromSystemDir || f.OverridesSystem) && (autoRestart || desktop is not null))
            return Make(StartupRole.System, desktop ?? "Área de trabalho", described.Length > 0 ? described : "Parte da área de trabalho.");

        if (!f.FromSystemDir && !f.OverridesSystem)
            return Make(StartupRole.User, "", described.Length > 0 ? described : "Adicionado por você ou por um programa que você instalou.");
        string package = f.Package is { Length: > 0 } p2 ? $" Instalado pelo pacote {p2}." : "";
        return Make(StartupRole.Unknown, "", (described.Length > 0 ? described : "Sem descrição no arquivo.") + package);
    }

    /// <summary>Owning package from <c>dpkg -S</c> output ("pkg[:arch]: /path"). Diversions and errors are skipped.</summary>
    public static Dictionary<string, string> ParseDpkgOwners(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n'))
        {
            int i = line.IndexOf(": /", StringComparison.Ordinal);
            if (i <= 0 || line.StartsWith("diversion", StringComparison.Ordinal)) continue;
            string pkg = line[..i].Split(',')[0].Trim(); int colon = pkg.IndexOf(':'); if (colon > 0) pkg = pkg[..colon];
            if (IsPackageName(pkg)) map.TryAdd(line[(i + 2)..].Trim(), pkg);
        }
        return map;
    }

    /// <summary><c>rpm -qf --queryformat "%{NAME}\n" files…</c>: one line per file, in order; "file … is not owned" lines stay unmatched.</summary>
    public static Dictionary<string, string> ParseRpmOwners(IReadOnlyList<string> files, string output)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
        if (lines.Count != files.Count) return map; // cannot pair safely
        for (int i = 0; i < files.Count; i++) if (!lines[i].Contains(' ') && IsPackageName(lines[i])) map[files[i]] = lines[i];
        return map;
    }

    /// <summary><c>pacman -Qo files…</c>: "/path is owned by pkg version".</summary>
    public static Dictionary<string, string> ParsePacmanOwners(string output)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n'))
        {
            int i = line.IndexOf(" is owned by ", StringComparison.Ordinal);
            if (i <= 0 || !line.StartsWith('/')) continue;
            var rest = line[(i + 13)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (rest.Length >= 1 && IsPackageName(rest[0])) map.TryAdd(line[..i], rest[0]);
        }
        return map;
    }

    private static bool IsPackageName(string s) => s.Length is > 0 and <= 128 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '+' or '.' or '_' or '@');
}
