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
    public bool Selected { get; set; }
    public bool Installed { get; set; }
    public string Status { get; set; } = "";
}

public static partial class AppCatalog
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{2,119}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
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
        new() { Name = "Lutris", Description = "Biblioteca de jogos de várias lojas e emuladores.", FlatpakId = "net.lutris.Lutris", NativeBinary = "lutris", PreSelect = false },
        new() { Name = "Heroic Games Launcher", Description = "Cliente para jogos da Epic, GOG e Amazon.", FlatpakId = "com.heroicgameslauncher.hgl", NativeBinary = "heroic", PreSelect = false },
        new() { Name = "ProtonUp-Qt", Description = "Gerencia versões do Proton-GE e do Wine para Steam e Lutris.", FlatpakId = "net.davidotek.pupgui2", NativeBinary = "protonup-qt", PreSelect = false },
        new() { Name = "OBS Studio", Description = "Gravação e transmissão de tela.", FlatpakId = "com.obsproject.Studio", NativeBinary = "obs", PreSelect = false },
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
            a.Installed = flatpaks.Contains(a.FlatpakId) || (a.NativeBinary.Length > 0 && nativeExists(a.NativeBinary));
            if (a.Installed) a.Selected = false;
        }
    }

    /// <summary>Last useful output line (no progress bars or table drawing).</summary>
    public static string LastLine(string output) =>
        output.Split('\n', '\r').Select(l => l.Trim()).LastOrDefault(l => l.Length > 3 && !l.All(c => "-\\|/█▒=# ".Contains(c)) && !l.Contains("MB/s") && !l.Contains("kB/s")) ?? "";
}
