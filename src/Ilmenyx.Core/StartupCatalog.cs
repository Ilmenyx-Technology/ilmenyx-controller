// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
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
        StartupRole.Required => L.T("NECESSÁRIO"),
        StartupRole.Recommended => L.T("RECOMENDADO"),
        StartupRole.Optional => L.T("OPCIONAL"),
        StartupRole.System => L.T("DO SISTEMA"),
        StartupRole.Unknown => L.T("DESCONHECIDO"),
        _ => L.T("DO USUÁRIO"),
    };
}

/// <summary>One line of the table. <see cref="Match"/> entries are lower-case file-name stems or command names; a trailing '*' is a prefix.</summary>
public sealed record StartupRule(string[] Match, StartupRole Role, string Component, string Explanation, string? Feature = null);

public static class StartupCatalog
{
    public static readonly string GroupSystemTitle = L.T("Do sistema (área de trabalho)");
    public static readonly string GroupUserTitle = L.T("Programas instalados por você");

    private const string Cin = "Cinnamon", Gn = "GNOME", Kde = "KDE Plasma", Xf = "XFCE", Ma = "MATE", Lq = "LXQt", Bg = "Budgie", Mint = "Linux Mint";
    private static readonly string Polkit = L.T("Mostra a janela que pede a sua senha quando um programa precisa de permissão de administrador.");
    private static readonly string Keys_ = L.T("Faz funcionar as teclas de volume, brilho e mídia e os atalhos do sistema.");
    private static readonly string Power = L.T("Controla bateria, suspensão, brilho da tela e o que acontece ao fechar a tampa.");
    private static readonly string Xsettings = L.T("Passa tema, fontes e ícones para os programas abrirem com a aparência certa.");
    private static readonly string Keyboard = L.T("Aplica o layout do teclado (ABNT2, acentos) e o Num Lock.");
    private static readonly string A11y = L.T("Aplica as opções de acessibilidade (letras grandes, contraste, teclas de aderência).");
    private static readonly string Housekeeping = L.T("Avisa quando o disco está ficando cheio e limpa miniaturas antigas.");
    private static readonly string ScreenProxy = L.T("Impede que a tela apague ou bloqueie enquanto você assiste a vídeos.");
    private static readonly string Lock = L.T("Bloqueia a tela quando você se afasta ou suspende o PC.");
    private static readonly string InputMethod = L.T("Inicia o método de entrada (IBus/Fcitx), usado para digitar em outros alfabetos e inserir emojis.");
    private static readonly string InputFeature = L.T("teclados de outros alfabetos (chinês, japonês, coreano…) nem o seletor de emojis");
    private static readonly string Search = L.T("Indexa os seus arquivos para a busca encontrá-los rápido (usa disco e processador no início).");
    private static readonly string Updates = L.T("Avisa quando há atualizações do sistema, inclusive de segurança.");
    private static readonly string Vm = L.T("Integração com máquina virtual (copiar e colar, ajustar a tela).");

    /// <summary>Most specific first: the first matching rule wins.</summary>
    public static readonly IReadOnlyList<StartupRule> Rules =
    [
        // Cinnamon (cinnamon-settings-daemon, one .desktop per plugin)
        new(["cinnamon-settings-daemon-a11y-settings", "csd-a11y-settings"], StartupRole.Required, Cin, A11y),
        new(["cinnamon-settings-daemon-automount", "csd-automount"], StartupRole.Required, Cin, L.T("Abre e monta pendrives, HDs externos e CDs quando você os conecta.")),
        new(["cinnamon-settings-daemon-background", "csd-background"], StartupRole.Required, Cin, L.T("Desenha o papel de parede da área de trabalho.")),
        new(["cinnamon-settings-daemon-clipboard", "csd-clipboard"], StartupRole.Required, Cin, L.T("Mantém o texto copiado disponível mesmo depois de fechar o programa de onde ele veio.")),
        new(["cinnamon-settings-daemon-color", "csd-color"], StartupRole.Optional, Cin, L.T("Aplica perfis de cor calibrados ao monitor e à impressora."), L.T("perfis de cor (calibração de monitor)")),
        new(["cinnamon-settings-daemon-housekeeping", "csd-housekeeping"], StartupRole.Required, Cin, Housekeeping),
        new(["cinnamon-settings-daemon-keyboard", "csd-keyboard"], StartupRole.Required, Cin, Keyboard),
        new(["cinnamon-settings-daemon-media-keys", "csd-media-keys"], StartupRole.Required, Cin, Keys_),
        new(["cinnamon-settings-daemon-power", "csd-power"], StartupRole.Required, Cin, Power),
        new(["cinnamon-settings-daemon-print-notifications", "csd-print-notifications"], StartupRole.Optional, Cin, L.T("Mostra avisos da impressora (sem papel, impressão concluída)."), "impressora"),
        new(["cinnamon-settings-daemon-screensaver-proxy", "csd-screensaver-proxy"], StartupRole.Required, Cin, ScreenProxy),
        new(["cinnamon-settings-daemon-smartcard", "csd-smartcard"], StartupRole.Optional, Cin, L.T("Reage a cartões inteligentes (por exemplo, certificado digital em cartão)."), L.T("leitor de cartão inteligente")),
        new(["cinnamon-settings-daemon-wacom", "csd-wacom"], StartupRole.Optional, Cin, L.T("Configura mesas digitalizadoras (Wacom)."), L.T("mesa digitalizadora")),
        new(["cinnamon-settings-daemon-xsettings", "csd-xsettings"], StartupRole.Required, Cin, Xsettings),
        new(["cinnamon-settings-daemon-*", "csd-*"], StartupRole.System, Cin, L.T("Parte do serviço de configurações do Cinnamon.")),
        new(["cinnamon*", "nemo-autostart", "nemo-desktop"], StartupRole.Required, Cin, L.T("Parte da área de trabalho Cinnamon (painel, ícones da área de trabalho ou sessão).")),

        // GNOME (gnome-settings-daemon plugins, also used by Ubuntu, Budgie and others)
        new(["org.gnome.settingsdaemon.a11ysettings"], StartupRole.Required, Gn, A11y),
        new(["org.gnome.settingsdaemon.color"], StartupRole.Optional, Gn, L.T("Aplica perfis de cor e a luz noturna."), L.T("perfis de cor nem a luz noturna")),
        new(["org.gnome.settingsdaemon.datetime"], StartupRole.Required, Gn, L.T("Acerta o fuso horário automaticamente.")),
        new(["org.gnome.settingsdaemon.housekeeping"], StartupRole.Required, Gn, Housekeeping),
        new(["org.gnome.settingsdaemon.keyboard"], StartupRole.Required, Gn, Keyboard),
        new(["org.gnome.settingsdaemon.mediakeys"], StartupRole.Required, Gn, Keys_),
        new(["org.gnome.settingsdaemon.power"], StartupRole.Required, Gn, Power),
        new(["org.gnome.settingsdaemon.printnotifications"], StartupRole.Optional, Gn, L.T("Mostra avisos da impressora (sem papel, impressão concluída)."), "impressora"),
        new(["org.gnome.settingsdaemon.rfkill"], StartupRole.Required, Gn, L.T("Controla o modo avião e liga/desliga Wi-Fi e Bluetooth.")),
        new(["org.gnome.settingsdaemon.screensaverproxy"], StartupRole.Required, Gn, ScreenProxy),
        new(["org.gnome.settingsdaemon.sharing"], StartupRole.Optional, Gn, L.T("Liga o compartilhamento de tela e de arquivos pela rede quando ativado."), L.T("compartilhamento pela rede")),
        new(["org.gnome.settingsdaemon.smartcard"], StartupRole.Optional, Gn, L.T("Reage a cartões inteligentes (por exemplo, certificado digital em cartão)."), L.T("leitor de cartão inteligente")),
        new(["org.gnome.settingsdaemon.sound"], StartupRole.Required, Gn, L.T("Aplica as configurações de som da sessão.")),
        new(["org.gnome.settingsdaemon.usbprotection"], StartupRole.Required, Gn, L.T("Bloqueia aparelhos USB novos enquanto a tela está travada.")),
        new(["org.gnome.settingsdaemon.wacom"], StartupRole.Optional, Gn, L.T("Configura mesas digitalizadoras (Wacom)."), L.T("mesa digitalizadora")),
        new(["org.gnome.settingsdaemon.wwan"], StartupRole.Optional, Gn, L.T("Controla modems de celular (4G/5G)."), L.T("modem de celular")),
        new(["org.gnome.settingsdaemon.xsettings"], StartupRole.Required, Gn, Xsettings),
        new(["org.gnome.settingsdaemon.diskutilitynotify", "gsd-disk-utility-notify"], StartupRole.Recommended, Gn, L.T("Avisa se um disco começar a dar sinais de defeito (SMART).")),
        new(["org.gnome.settingsdaemon.*", "gsd-*", "gnome-settings-daemon*"], StartupRole.System, Gn, L.T("Parte do serviço de configurações do GNOME.")),
        new(["org.gnome.software", "gnome-software-service", "gnome-software"], StartupRole.Optional, Gn, L.T("A Loja de apps fica em segundo plano para procurar atualizações."), L.T("os avisos de atualização da Loja (atualize por conta própria)")),
        new(["org.gnome.evolution-alarm-notify", "evolution-alarm-notify", "evolution-*"], StartupRole.Optional, L.T("Agenda"), L.T("Mostra lembretes da agenda (Evolution / Agenda do GNOME)."), L.T("lembretes de agenda")),
        new(["tracker-*", "localsearch-*"], StartupRole.Optional, Gn, Search, L.T("a busca de arquivos do GNOME")),
        new(["org.gnome.dejadup.monitor", "deja-dup-monitor"], StartupRole.Recommended, "Backup", L.T("Faz os backups agendados do Déjà Dup.")),
        new(["gnome-keyring-secrets", "gnome-keyring-daemon"], StartupRole.Required, L.T("Chaveiro"), L.T("Guarda as suas senhas salvas (Wi-Fi, navegador, e-mail).")),
        new(["gnome-keyring-pkcs11", "p11-kit-server"], StartupRole.Recommended, L.T("Chaveiro"), L.T("Disponibiliza certificados digitais guardados no chaveiro.")),
        new(["gnome-keyring-ssh", "gcr-ssh-agent", "ssh-agent", "gpg-agent*"], StartupRole.Optional, L.T("Chaveiro"), L.T("Guarda chaves SSH/GPG para você não digitar a senha delas toda vez."), L.T("SSH (acesso remoto pelo terminal) nem GPG")),
        new(["org.gnome.shell*", "gnome-shell*", "gnome-session*", "gnome-initial-setup*", "org.gnome.*"], StartupRole.System, Gn, L.T("Parte da área de trabalho GNOME.")),

        // KDE Plasma
        new(["org.kde.plasmashell", "plasmashell"], StartupRole.Required, Kde, L.T("Painel, menu e área de trabalho do Plasma.")),
        new(["polkit-kde-authentication-agent-1", "org.kde.polkit-kde-authentication-agent-1"], StartupRole.Required, Kde, Polkit),
        new(["org.kde.powerdevil", "powerdevil"], StartupRole.Required, Kde, Power),
        new(["xembedsniproxy"], StartupRole.Required, Kde, L.T("Faz ícones de programas antigos aparecerem na bandeja.")),
        new(["gmenudbusmenuproxy"], StartupRole.Required, Kde, L.T("Mostra os menus de alguns programas (GTK) no painel.")),
        new(["kaccess", "org.kde.kaccess"], StartupRole.Required, Kde, A11y),
        new(["pam_kwallet_init", "pam_kwallet5_init", "org.kde.kwalletd*"], StartupRole.Required, Kde, L.T("Abre a carteira de senhas do KDE (KWallet) junto com o login.")),
        new(["baloo_file", "org.kde.baloo*"], StartupRole.Optional, Kde, Search, L.T("a busca de arquivos do KDE")),
        new(["org.kde.discover.notifier", "discover-notifier"], StartupRole.Recommended, Kde, Updates),
        new(["org.kde.kdeconnect.daemon", "org.kde.kdeconnect*", "kdeconnectd", "kdeconnect-indicator"], StartupRole.Optional, Kde, L.T("Integra o PC com o celular (notificações, arquivos, controle remoto)."), "KDE Connect"),
        new(["org.kde.klipper", "klipper"], StartupRole.Optional, Kde, L.T("Guarda o histórico da área de transferência."), L.T("o histórico de itens copiados")),
        new(["org.kde.kalendarac", "kalendarac", "org.kde.merkuro*"], StartupRole.Optional, Kde, L.T("Mostra lembretes da agenda."), L.T("lembretes de agenda")),
        new(["org.kde.plasma-welcome", "plasma-welcome"], StartupRole.Optional, Kde, L.T("Tela de boas-vindas do Plasma."), L.T("a tela de boas-vindas")),
        new(["org.kde.*", "plasma-*", "plasma_*", "kde*", "kwin*", "ksmserver*", "kded*"], StartupRole.System, Kde, L.T("Parte da área de trabalho KDE Plasma.")),

        // XFCE
        new(["xfce4-power-manager"], StartupRole.Required, Xf, Power),
        new(["xfce4-notifyd"], StartupRole.Required, Xf, L.T("Mostra as notificações na tela.")),
        new(["xfce4-screensaver", "light-locker"], StartupRole.Required, Xf, Lock),
        new(["xfce4-volumed-pulse", "xfce4-volumed"], StartupRole.Required, Xf, L.T("Faz funcionar as teclas de volume.")),
        new(["xfce4-clipman-plugin-autostart", "xfce4-clipman"], StartupRole.Optional, Xf, L.T("Guarda o histórico da área de transferência."), L.T("o histórico de itens copiados")),
        new(["xiccd"], StartupRole.Optional, Xf, L.T("Aplica perfis de cor ao monitor."), L.T("perfis de cor (calibração de monitor)")),
        new(["xfce-polkit", "xfce4-polkit"], StartupRole.Required, Xf, Polkit),
        new(["xfce4-*", "xfce-*", "xfsettingsd", "xfdesktop", "xfwm4", "thunar*"], StartupRole.System, Xf, L.T("Parte da área de trabalho XFCE.")),

        // MATE
        new(["mate-settings-daemon"], StartupRole.Required, Ma, L.T("Aplica tema, teclado, mouse e teclas especiais no MATE.")),
        new(["mate-power-manager"], StartupRole.Required, Ma, Power),
        new(["mate-screensaver"], StartupRole.Required, Ma, Lock),
        new(["mate-volume-control-status-icon", "mate-volume-control-applet"], StartupRole.Required, Ma, L.T("Ícone de volume no painel.")),
        new(["polkit-mate-authentication-agent-1", "mate-polkit*"], StartupRole.Required, Ma, Polkit),
        new(["mate-*", "caja*"], StartupRole.System, Ma, L.T("Parte da área de trabalho MATE.")),

        // LXQt / LXDE
        new(["lxqt-panel"], StartupRole.Required, Lq, L.T("Painel e menu do LXQt.")),
        new(["lxqt-policykit-agent"], StartupRole.Required, Lq, Polkit),
        new(["lxqt-powermanagement"], StartupRole.Required, Lq, Power),
        new(["lxqt-notifications"], StartupRole.Required, Lq, L.T("Mostra as notificações na tela.")),
        new(["lxqt-globalkeyshortcuts"], StartupRole.Required, Lq, L.T("Faz funcionar os atalhos de teclado do sistema.")),
        new(["lxqt-runner"], StartupRole.Optional, Lq, L.T("Lançador rápido de programas (Alt+F2)."), L.T("o lançador rápido")),
        new(["lxqt-*", "pcmanfm-qt*"], StartupRole.System, Lq, L.T("Parte da área de trabalho LXQt.")),
        new(["lxpolkit"], StartupRole.Required, "LXDE", Polkit),

        // Budgie
        new(["budgie-desktop-screensaver"], StartupRole.Required, Bg, Lock),
        new(["budgie-desktop-nm-applet"], StartupRole.Required, Bg, L.T("Ícone de rede no painel: é por ele que você escolhe o Wi-Fi.")),
        new(["budgie-*", "org.buddiesofbudgie.*"], StartupRole.System, Bg, L.T("Parte da área de trabalho Budgie.")),

        // Shared pieces (any desktop)
        new(["polkit-gnome-authentication-agent-1", "polkit-gnome*", "polkit-*"], StartupRole.Required, L.T("Permissões"), Polkit),
        new(["at-spi-dbus-bus", "at-spi-dbus-bus.service"], StartupRole.Required, L.T("Acessibilidade"), L.T("Ponte de acessibilidade: leitores de tela e muitos programas dependem dela.")),
        new(["xapp-sn-watcher"], StartupRole.Required, L.T("Bandeja"), L.T("Faz os ícones dos programas aparecerem na bandeja do painel.")),
        new(["xdg-user-dirs", "user-dirs-update-gtk", "xdg-user-dirs-gtk"], StartupRole.Required, L.T("Pastas pessoais"), L.T("Mantém as pastas pessoais (Documentos, Downloads…) com os nomes do seu idioma.")),
        new(["nm-applet", "network-manager-applet"], StartupRole.Required, L.T("Rede"), L.T("Ícone de rede no painel: é por ele que você escolhe o Wi-Fi.")),
        new(["blueman", "blueman-applet"], StartupRole.Optional, "Bluetooth", L.T("Ícone do Bluetooth no painel, para parear fones, mouses e caixas de som."), "Bluetooth"),
        new(["obex"], StartupRole.Optional, "Bluetooth", L.T("Envia e recebe arquivos por Bluetooth."), L.T("envio de arquivos por Bluetooth")),
        new(["print-applet", "system-config-printer-applet"], StartupRole.Optional, L.T("Impressão"), L.T("Ícone da fila de impressão."), "impressora"),
        new(["im-launch", "ibus*", "org.freedesktop.ibus*", "fcitx*", "org.fcitx.*"], StartupRole.Optional, L.T("Método de entrada"), InputMethod, InputFeature),
        new(["geoclue-demo-agent", "geoclue*"], StartupRole.Optional, L.T("Localização"), L.T("Libera a localização para apps que pedem (fuso horário automático, luz noturna, clima)."), L.T("localização automática")),
        new(["onboard-autostart", "onboard", "caribou*"], StartupRole.Optional, L.T("Acessibilidade"), L.T("Teclado na tela (só abre se estiver ligado nas opções de acessibilidade)."), L.T("teclado na tela")),
        new(["orca-autostart", "orca"], StartupRole.Optional, L.T("Acessibilidade"), L.T("Leitor de tela para quem não enxerga (só abre se estiver ligado na acessibilidade)."), L.T("leitor de tela")),
        new(["pulseaudio", "pulseaudio.service"], StartupRole.Required, L.T("Som"), L.T("Servidor de som: sem ele os programas ficam sem áudio.")),
        new(["pipewire", "pipewire-pulse"], StartupRole.Required, L.T("Som"), L.T("Servidor de som (PipeWire): sem ele os programas ficam sem áudio.")),
        new(["wireplumber"], StartupRole.Required, L.T("Som"), L.T("Escolhe as saídas e entradas de som (fone, caixa, microfone) para o PipeWire.")),
        new(["filter-chain"], StartupRole.Recommended, L.T("Som"), L.T("Filtros de áudio do PipeWire (por exemplo, cancelamento de eco).")),
        new(["xdg-desktop-portal-rewrite-launchers"], StartupRole.Recommended, L.T("Portais"), L.T("Ajusta os atalhos de apps Flatpak/Snap depois de atualizações.")),
        new(["xdg-desktop-portal*"], StartupRole.Required, L.T("Portais"), L.T("Ponte que deixa apps Flatpak/Snap abrirem arquivos e compartilharem a tela.")),
        new(["dbus*"], StartupRole.Required, L.T("Sistema"), L.T("Canal de comunicação entre os programas da sessão.")),
        new(["gvfs-*"], StartupRole.Required, L.T("Arquivos"), L.T("Acesso a pendrives, celulares e pastas de rede no gerenciador de arquivos.")),
        new(["session-migration"], StartupRole.Recommended, L.T("Sistema"), L.T("Ajusta as suas configurações depois de atualizações do sistema (roda rápido e termina).")),
        new(["snap-userd-autostart", "snapd.session-agent", "io.snapcraft.*"], StartupRole.Recommended, "Snap", L.T("Necessário para apps Snap abrirem links e funcionarem direito.")),
        new(["xscreensaver", "lxqt-xscreensaver-autostart"], StartupRole.Recommended, L.T("Tela"), Lock),
        new(["update-notifier", "org.freedesktop.packagekit*", "gnome-packagekit*"], StartupRole.Recommended, L.T("Atualizações"), Updates),
        new(["zeitgeist-datahub", "zeitgeist*"], StartupRole.Optional, L.T("Sistema"), L.T("Registra a sua atividade recente para sugestões e arquivos recentes."), L.T("a lista de atividades recentes")),
        new(["spice-vdagent", "vmware-user*", "vboxclient*", "open-vm-tools*"], StartupRole.Optional, L.T("Máquina virtual"), Vm, L.T("máquina virtual")),

        // Update notifiers of the Arch family and Fedora (CachyOS: cachy-update ships /etc/xdg/autostart/arch-update-tray.desktop, seen 2026-11-10)
        new(["arch-update-tray", "arch-update"], StartupRole.Recommended, L.T("Atualizações"), Updates + L.T(" Ícone na bandeja do Arch-Update (no CachyOS, Cachy-Update).")),
        new(["pamac-tray", "pamac-tray-plasma"], StartupRole.Recommended, "Manjaro", Updates),
        new(["octopi-notifier"], StartupRole.Recommended, L.T("Atualizações"), Updates),
        new(["dnfdragora-updater"], StartupRole.Recommended, "Fedora", Updates),

        // Distribution tools (Linux Mint)
        new(["mintupdate", "mintupdate-launcher"], StartupRole.Recommended, Mint, Updates),
        new(["mintreport", "mintreport-tray"], StartupRole.Optional, Mint, L.T("Relatórios do sistema: avisa sobre falhas de programas e pacotes de idioma faltando."), L.T("os avisos de relatório do sistema")),
        new(["mintwelcome"], StartupRole.Optional, Mint, L.T("Tela de boas-vindas do Linux Mint."), L.T("a tela de boas-vindas")),
        new(["warpinator-autostart", "warpinator", "org.x.warpinator"], StartupRole.Optional, Mint, L.T("Envia e recebe arquivos de outros PCs da mesma rede (Warpinator)."), L.T("a troca de arquivos pela rede")),
        new(["sticky", "org.x.sticky"], StartupRole.Optional, Mint, L.T("Notas adesivas na área de trabalho."), L.T("notas adesivas")),
        new(["touchegg"], StartupRole.Optional, L.T("Touchpad"), L.T("Gestos com vários dedos no touchpad."), L.T("gestos no touchpad")),

        // GPU vendor tools that come with the driver
        new(["nvidia-prime", "nvidia-prime-applet"], StartupRole.Optional, "NVIDIA", L.T("Ícone para trocar entre a placa integrada e a NVIDIA (PRIME)."), L.T("o ícone de troca de placa de vídeo")),
        new(["nvidia-settings-autostart", "nvidia-settings"], StartupRole.Recommended, "NVIDIA", L.T("Carrega as configurações salvas no painel da NVIDIA ao entrar.")),

        // Apps the user installs (common on Linux desktops)
        new(["steam", "com.valvesoftware.steam"], StartupRole.App, "Steam", L.T("Abre a Steam (loja e biblioteca de jogos) ao entrar.")),
        new(["discord", "com.discordapp.discord", "vesktop", "dev.vencord.vesktop"], StartupRole.App, "Discord", L.T("Abre o Discord (chat de voz e texto) ao entrar.")),
        new(["telegramdesktop", "org.telegram.desktop", "telegram-desktop"], StartupRole.App, "Telegram", L.T("Abre o Telegram ao entrar.")),
        new(["signal-desktop", "org.signal.signal"], StartupRole.App, "Signal", L.T("Abre o Signal ao entrar.")),
        new(["slack", "com.slack.slack"], StartupRole.App, "Slack", L.T("Abre o Slack ao entrar.")),
        new(["skypeforlinux", "com.skype.client"], StartupRole.App, L.T("Skype"), L.T("Abre o Skype ao entrar.")),
        new(["spotify", "com.spotify.client"], StartupRole.App, L.T("Spotify"), L.T("Abre o Spotify ao entrar.")),
        new(["zoom", "us.zoom.zoom"], StartupRole.App, "Zoom", L.T("Abre o Zoom ao entrar.")),
        new(["dropbox"], StartupRole.App, L.T("Dropbox"), L.T("Sincroniza a pasta do Dropbox com a nuvem.")),
        new(["megasync", "nz.mega.megasync"], StartupRole.App, "MEGA", L.T("Sincroniza pastas com a nuvem do MEGA.")),
        new(["nextcloud", "com.nextcloud.desktopclient.nextcloud", "owncloud", "com.owncloud.*"], StartupRole.App, L.T("Nextcloud"), L.T("Sincroniza pastas com o seu servidor Nextcloud/ownCloud.")),
        new(["insync", "onedrive", "pcloud", "com.github.abraunegg.onedrive"], StartupRole.App, L.T("Nuvem"), L.T("Sincroniza pastas com um serviço de nuvem.")),
        new(["syncthing*", "com.github.zocker_160.syncthinggtk"], StartupRole.App, L.T("Syncthing"), L.T("Sincroniza pastas entre os seus aparelhos.")),
        new(["flameshot", "org.flameshot.flameshot"], StartupRole.App, "Flameshot", L.T("Ferramenta de captura de tela (Print Screen).")),
        new(["copyq", "com.github.hluk.copyq"], StartupRole.App, "CopyQ", L.T("Guarda o histórico da área de transferência.")),
        new(["ulauncher", "albert"], StartupRole.App, L.T("Lançador"), L.T("Lançador rápido de programas por teclado.")),
        new(["conky*"], StartupRole.App, "Conky", L.T("Mostra informações do sistema na área de trabalho.")),
        new(["plank", "docky", "cairo-dock"], StartupRole.App, "Dock", L.T("Barra de atalhos (dock) na tela.")),
        new(["redshift*", "gammastep*"], StartupRole.App, L.T("Luz noturna"), L.T("Deixa a tela mais amarelada à noite.")),
        new(["solaar"], StartupRole.App, "Solaar", L.T("Gerencia mouses e teclados sem fio da Logitech.")),
        new(["openrgb", "org.openrgb.openrgb"], StartupRole.App, "OpenRGB", L.T("Controla as luzes RGB das peças.")),
        new(["corectrl", "org.corectrl.corectrl", "lact", "io.github.ilya_zlobintsev.lact"], StartupRole.App, "GPU", L.T("Ajusta perfis da placa de vídeo (ventoinha, clock).")),
        new(["protonvpn*", "nordvpn*", "com.protonvpn.*"], StartupRole.App, "VPN", L.T("Conecta a VPN ao entrar.")),
        new(["keepassxc", "org.keepassxc.keepassxc", "bitwarden", "com.bitwarden.desktop", "1password"], StartupRole.App, L.T("Senhas"), L.T("Gerenciador de senhas.")),
        new(["guake", "tilda"], StartupRole.App, "Terminal", L.T("Terminal que desce do topo da tela com um atalho.")),
        new(["barrier", "input-leap", "com.github.debauchee.barrier"], StartupRole.App, "Barrier", L.T("Compartilha teclado e mouse entre computadores.")),
        new(["teamviewer*", "anydesk", "rustdesk", "com.rustdesk.rustdesk"], StartupRole.App, L.T("Acesso remoto"), L.T("Permite acesso remoto a este PC.")),
        new(["jetbrains-toolbox"], StartupRole.App, "JetBrains", L.T("Gerencia as IDEs da JetBrains.")),
        new(["variety"], StartupRole.App, "Variety", L.T("Troca o papel de parede automaticamente.")),
        new(["ilmenyx*", "io.github.ilmenyx_technology.ilmenyx*", "io.github.bismuth_technology.ilmenyx*", "io.github.bismuthtechnology.ilmenyx*"], StartupRole.App, "Ilmenyx Controller", L.T("O próprio Ilmenyx Controller (monitor ou avisos de dispositivos).")),
    ];

    private static readonly (string Token, string Name)[] Desktops =
    [
        (L.T("X-Cinnamon"), Cin), ("Cinnamon", Cin), ("GNOME", Gn), ("KDE", Kde), ("XFCE", Xf), ("MATE", Ma), ("LXQt", Lq), ("LXDE", "LXDE"),
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
        string where = only.Count > 0 ? L.T("só em ") + Names(only) : not.Count > 0 ? L.T("não abre em ") + Names(not) : "";
        if (current.Count == 0) return (true, where);
        // "Cinnamon" and "X-Cinnamon" are both used in the wild for the same desktop: compare without the X- prefix.
        static string Norm(string t) => t.StartsWith("X-", StringComparison.OrdinalIgnoreCase) ? t[2..] : t;
        bool Has(string t) => current.Any(c => Norm(c).Equals(Norm(t), StringComparison.OrdinalIgnoreCase));
        bool runs = (only.Count == 0 || only.Any(Has)) && !not.Any(Has);
        return (runs, where);
    }

    public static string RecommendationFor(StartupRole role, string? feature) => role switch
    {
        StartupRole.Required => L.T("Necessário — não desative."),
        StartupRole.Recommended => L.T("Recomendado manter ativado."),
        StartupRole.Optional => L.T($"Pode desativar se não usa {feature ?? L.T("este recurso")}."),
        StartupRole.System => L.T("Do sistema — mantenha ativado se não souber o que é."),
        StartupRole.Unknown => L.T("Desconhecido: pesquise antes de desativar."),
        _ => L.T("Do usuário — pode desativar; o programa continua instalado."),
    };

    public static StartupAdvice Classify(StartupFacts f)
    {
        var (runs, where) = f.Kind == "Desktop" ? RunsIn(f.Keys, f.CurrentDesktop) : (true, "");
        StartupAdvice Make(StartupRole role, string component, string explanation, string? feature = null)
        {
            var group = role is StartupRole.App or StartupRole.User or StartupRole.Unknown ? StartupGroup.User : StartupGroup.System;
            // Entries limited to another desktop never start here: the honest advice is that the switch changes nothing.
            string advice = runs ? RecommendationFor(role, feature) : L.T($"Não abre nesta área de trabalho ({where}): ligado ou desligado, não muda nada aqui.");
            return new(group, role, component, explanation, advice, runs, where);
        }
        string described = f.Comment.Length > 0 ? f.Comment.TrimEnd('.') + "." : "";

        if (Find(f.Id, f.Exec) is { } rule) return Make(rule.Role, rule.Component, rule.Explanation, rule.Feature);

        // Standard keys: session phases and auto-restart are only used by pieces of the desktop session.
        string? desktop = DesktopOf(f.Keys.GetValueOrDefault("OnlyShowIn", ""));
        string phase = f.Keys.GetValueOrDefault(L.T("X-GNOME-Autostart-Phase"), "");
        if (SessionPhases.Contains(phase, StringComparer.OrdinalIgnoreCase) || f.Keys.ContainsKey("X-KDE-autostart-phase"))
            return Make(StartupRole.Required, desktop ?? (f.Keys.ContainsKey("X-KDE-autostart-phase") ? Kde : L.T("Área de trabalho")), described.Length > 0 ? described : L.T("Parte da sessão da área de trabalho (abre numa fase inicial do login)."));
        bool autoRestart = f.Keys.Any(k => k.Key is "X-GNOME-AutoRestart" or "X-MATE-AutoRestart" or "X-Cinnamon-AutoRestart" && k.Value.Equals("true", StringComparison.OrdinalIgnoreCase));
        if (f.Keys.Keys.Any(k => k.StartsWith("X-KDE-", StringComparison.Ordinal) || k.StartsWith("X-Cinnamon", StringComparison.Ordinal)) && (f.FromSystemDir || f.OverridesSystem))
            desktop ??= f.Keys.Keys.Any(k => k.StartsWith("X-KDE-", StringComparison.Ordinal)) ? Kde : Cin;
        if (f.Package is { Length: > 0 } pkg && DesktopPackages.FirstOrDefault(p => pkg.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase)) is { Name: { } byPackage })
            return Make(StartupRole.System, byPackage, described.Length > 0 ? described : L.T($"Parte da área de trabalho {byPackage} (pacote {pkg})."));
        if ((f.FromSystemDir || f.OverridesSystem) && (autoRestart || desktop is not null))
            return Make(StartupRole.System, desktop ?? L.T("Área de trabalho"), described.Length > 0 ? described : L.T("Parte da área de trabalho."));

        if (!f.FromSystemDir && !f.OverridesSystem)
            return Make(StartupRole.User, "", described.Length > 0 ? described : L.T("Adicionado por você ou por um programa que você instalou."));
        string package = f.Package is { Length: > 0 } p2 ? L.T($" Instalado pelo pacote {p2}.") : "";
        return Make(StartupRole.Unknown, "", (described.Length > 0 ? described : L.T("Sem descrição no arquivo.")) + package);
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
