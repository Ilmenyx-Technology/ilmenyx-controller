// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions, adapted from the catalog idea of the original AppInstaller (MIT, see NOTICE): see LICENSING.md.
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

/// <summary>App of the post-install catalog, installed from Flathub into the user's own Flatpak installation (no root).</summary>
public sealed class InstallableApp
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string FlatpakId { get; init; } = "";
    /// <summary>Executable name of a native package, so an app installed by the distribution counts as installed.</summary>
    public string NativeBinary { get; init; } = "";
    public bool PreSelect { get; init; } = true;
    /// <summary>Group shown in the list ("Essenciais", "Jogos", "Periféricos", "Streaming").</summary>
    public string Category { get; init; } = "Essenciais";
    /// <summary>License declared on Flathub (checked through flathub.org/api/v2/appstream on 2026-11-08), empty when not listed.</summary>
    public string License { get; init; } = "";
    /// <summary>What else the app needs on the system (root service, udev rule…), in plain pt-BR. Shown before installing.</summary>
    public string Caveat { get; init; } = "";
    /// <summary>Not on Flathub: only a link to the official page is offered (nothing is installed by the app).</summary>
    public string LinkUrl { get; init; } = "";
    /// <summary>Flatpak extension (runtime) whose branch must match the runtime of this installed Flatpak app (e.g. the Gamescope layer for Steam).</summary>
    public string RuntimeFor { get; init; } = "";
    /// <summary>Branch found for <see cref="RuntimeFor"/> (e.g. "25.08"); null when that app is not installed as a Flatpak.</summary>
    public string? Branch { get; set; }
    /// <summary>Reason the item cannot be installed here now (null = it can).</summary>
    public string? Unavailable { get; set; }
    public bool LinkOnly => LinkUrl.Length > 0;
    public string Key => FlatpakId.Length > 0 ? FlatpakId : "link." + new string(Name.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();
    /// <summary>Flatpak ref to install: the id, or id//branch for an extension that must match an app's runtime.</summary>
    public string Ref => RuntimeFor.Length > 0 && Branch is { } b ? FlatpakId + "//" + b : FlatpakId;
    public bool Selected { get; set; }
    public bool Installed { get; set; }
    public string Status { get; set; } = "";
}

public static partial class AppCatalog
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{2,119}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
    /// <summary>Order of the groups on the page.</summary>
    public static readonly string[] Categories = ["Essenciais", "Jogos", "Streaming", "Periféricos"];
    public static bool ValidId(string id) => IdPattern().IsMatch(id) && id.Count(c => c == '.') >= 2;

    public static List<InstallableApp> Default() =>
    [
        new() { Name = "Steam", Description = "Loja e biblioteca de jogos da Valve (com Proton para jogos de Windows).", FlatpakId = "com.valvesoftware.Steam", NativeBinary = "steam" },
        new() { Name = "Google Chrome", Description = "Navegador do Google.", FlatpakId = "com.google.Chrome", NativeBinary = "google-chrome-stable" },
        new() { Name = "Discord", Description = "Chat de voz e texto para jogar com os amigos.", FlatpakId = "com.discordapp.Discord", NativeBinary = "discord" },
        new() { Name = "Flameshot", Description = "Captura de tela com anotações (equivalente ao Lightshot no Linux).", FlatpakId = "org.flameshot.Flameshot", NativeBinary = "flameshot" },
        // WinRAR has no Linux version; PeaZip (LGPL-3.0, 7-Zip engine) opens and extracts .rar, .zip and .7z on any distribution.
        new() { Name = "PeaZip", Description = "Compactador de arquivos: abre e extrai RAR, ZIP e 7Z (no lugar do WinRAR).", FlatpakId = "io.github.peazip.PeaZip", NativeBinary = "peazip" },
        new() { Name = "VLC", Description = "Player de vídeo e música que abre qualquer formato.", FlatpakId = "org.videolan.VLC", NativeBinary = "vlc" },
        new() { Name = "Lutris", Description = "Biblioteca de jogos de várias lojas e emuladores.", FlatpakId = "net.lutris.Lutris", NativeBinary = "lutris", PreSelect = false, Category = "Jogos" },
        new() { Name = "Heroic Games Launcher", Description = "Cliente para jogos da Epic, GOG e Amazon.", FlatpakId = "com.heroicgameslauncher.hgl", NativeBinary = "heroic", PreSelect = false, Category = "Jogos" },
        new() { Name = "ProtonUp-Qt", Description = "Gerencia versões do Proton-GE e do Wine para Steam e Lutris.", FlatpakId = "net.davidotek.pupgui2", NativeBinary = "protonup-qt", PreSelect = false, Category = "Jogos", License = "GPL-3.0" },
        new() { Name = "OBS Studio", Description = "Gravação e transmissão de tela.", FlatpakId = "com.obsproject.Studio", NativeBinary = "obs", PreSelect = false },
        // Gaming extras (2026-11-08). Ids and licenses checked on flathub.org/api/v2/appstream/<id>; none is pre-selected.
        new() { Name = "ProtonPlus", Description = "Instala e atualiza versões do Proton, Wine, DXVK e VKD3D para Steam, Lutris, Heroic e Bottles (alternativa ao ProtonUp-Qt).", FlatpakId = "com.vysp3r.ProtonPlus", NativeBinary = "protonplus", PreSelect = false, Category = "Jogos", License = "GPL-3.0-or-later" },
        new() { Name = "LACT", Description = "Informações, monitoramento, limite de energia, ventoinha e overclock de placas AMD, NVIDIA e Intel.", FlatpakId = "io.github.ilya_zlobintsev.LACT", NativeBinary = "lact", PreSelect = false, Category = "Jogos", License = "MIT",
            Caveat = "Para mudar clock, energia e ventoinha, o LACT usa um serviço do sistema (lactd) que roda como root. Na primeira vez que abrir, o próprio LACT oferece instalar esse serviço fora do Flatpak e pede a sua senha. Só para ver informações, dá para recusar. Placas NVIDIA precisam do driver proprietário. Para desfazer o serviço: sudo systemctl disable --now lactd e apague /etc/systemd/system/lactd.service." },
        new() { Name = "Gamescope para a Steam Flatpak", Description = "Camada do Gamescope (o compositor do Steam Deck: escala, limite de FPS, HDR) para usar nas opções de inicialização da Steam instalada em Flatpak.", FlatpakId = "org.freedesktop.Platform.VulkanLayer.gamescope", RuntimeFor = "com.valvesoftware.Steam", PreSelect = false, Category = "Jogos", License = "BSD-2-Clause",
            Caveat = "Só serve para a Steam em Flatpak. O Ilmenyx instala a versão igual à da base (runtime) da sua Steam; quando a Steam trocar de base, instale de novo. Com a Steam da distribuição, instale o pacote gamescope pelo sistema." },
        new() { Name = "Sunshine", Description = "Transmite os jogos deste PC para outro aparelho com o app Moonlight (TV, celular, notebook).", FlatpakId = "dev.lizardbyte.app.Sunshine", NativeBinary = "sunshine", PreSelect = false, Category = "Streaming", License = "GPL-3.0-only",
            Caveat = "Depois de instalar, rode uma vez no terminal: flatpak run --command=additional-install.sh dev.lizardbyte.app.Sunshine (passos extras do Flatpak, que podem pedir senha). A configuração abre no navegador com um certificado próprio: o aviso de \"não seguro\" é esperado." },
        new() { Name = "GeForce NOW", Description = "Jogos na nuvem da NVIDIA. O app oficial para Linux não está no Flathub: a NVIDIA distribui pelo próprio site.", LinkUrl = "https://www.nvidia.com/en-us/geforce-now/download/", PreSelect = false, Category = "Streaming",
            Caveat = "O Ilmenyx só abre a página oficial; nada é baixado pelo app. Os apps \"GeForce NOW\" do Flathub são de terceiros, não da NVIDIA." },
        new() { Name = "Piper", Description = "Configura mouses gamer: DPI, perfis, botões e LEDs.", FlatpakId = "org.freedesktop.Piper", NativeBinary = "piper", PreSelect = false, Category = "Periféricos", License = "GPL-2.0-or-later",
            Caveat = "Precisa do serviço ratbagd (pacote ratbagd ou libratbag da distribuição) instalado e rodando no sistema; sem ele o Piper não encontra o mouse. Exemplo: sudo apt install ratbagd." },
        new() { Name = "Solaar", Description = "Gerencia teclados e mouses Logitech (receptor Unifying, Bolt, Lightspeed e Bluetooth): bateria, pareamento e ajustes.", FlatpakId = "io.github.pwr_solaar.solaar", NativeBinary = "solaar", PreSelect = false, Category = "Periféricos", License = "GPL-2.0-only",
            Caveat = "Só Logitech. O Flatpak não instala a regra udev que libera o acesso ao receptor; sem ela o Solaar pode não ver o aparelho. O pacote solaar da distribuição já traz a regra; ou copie 42-logitech-unify-permissions.rules para /etc/udev/rules.d (precisa de root)." },
        new() { Name = "input-remapper", Description = "Troca a função de teclas e botões de teclado, mouse e controle.", LinkUrl = "https://github.com/sezanzeb/input-remapper", NativeBinary = "input-remapper-gtk", PreSelect = false, Category = "Periféricos",
            Caveat = "Não está no Flathub porque usa um serviço root. Instale pelo pacote input-remapper da sua distribuição; o Ilmenyx só abre a página do projeto." },
    ];

    /// <summary>Application ids from <c>flatpak list --app --columns=application</c> (one per line).</summary>
    public static IReadOnlySet<string> ParseFlatpakList(string output) =>
        output.Split('\n').Select(l => l.Trim()).Where(ValidId).ToHashSet(StringComparer.Ordinal);

    public static bool HasFlathub(string remotesOutput) =>
        remotesOutput.Split('\n').Any(l => l.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == "flathub");

    public static void MarkInstalled(IEnumerable<InstallableApp> apps, IReadOnlySet<string> flatpaks, Func<string, bool> nativeExists)
    {
        foreach (var a in apps)
        {
            a.Installed = (a.FlatpakId.Length > 0 && a.RuntimeFor.Length == 0 && flatpaks.Contains(a.FlatpakId)) || (a.NativeBinary.Length > 0 && nativeExists(a.NativeBinary));
            if (a.Installed) a.Selected = false;
        }
    }

    /// <summary>Branch of the runtime printed by <c>flatpak info --show-runtime APP</c> ("org.freedesktop.Platform/x86_64/25.08" → "25.08"); null if not a valid branch.</summary>
    public static string? RuntimeBranch(string output)
    {
        string line = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        string[] parts = line.Split('/');
        return parts.Length == 3 && ValidId(parts[0]) && BranchPattern().IsMatch(parts[2]) ? parts[2] : null;
    }

    [GeneratedRegex(@"^[0-9]{2}\.[0-9]{2}(?:\.[0-9]{1,2})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex BranchPattern();

    /// <summary>"id\tbranch" lines of <c>flatpak list --runtime --columns=application,branch</c> → id//branch refs.</summary>
    public static IReadOnlySet<string> ParseRuntimeList(string output) =>
        output.Split('\n').Select(l => l.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length >= 2 && ValidId(p[0]) && BranchPattern().IsMatch(p[1]))
            .Select(p => p[0] + "//" + p[1]).ToHashSet(StringComparer.Ordinal);

    /// <summary>Last useful output line (no progress bars or table drawing).</summary>
    public static string LastLine(string output) =>
        output.Split('\n', '\r').Select(l => l.Trim()).LastOrDefault(l => l.Length > 3 && !l.All(c => "-\\|/█▒=# ".Contains(c)) && !l.Contains("MB/s") && !l.Contains("kB/s")) ?? "";
}
