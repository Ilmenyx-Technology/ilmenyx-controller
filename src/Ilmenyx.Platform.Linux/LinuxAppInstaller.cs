// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions, adapted from the AppInstaller of the original (MIT, see NOTICE): see LICENSING.md.
using Ilmenyx.Core;

namespace Ilmenyx.Platform.Linux;

public delegate Task<(int Code, string Output)> ProgramRunner(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct);

/// <summary>Flatpak (user installation) based installer. Network and disk writes happen only when a method that installs is called by an explicit click.</summary>
public sealed class LinuxAppInstaller(ProgramRunner? run = null, Func<string, bool>? nativeExists = null)
{
    private readonly ProgramRunner runner = run ?? ((program, args, timeout, ct) => BoundedProcess.RunAsync(program, args, timeout, 262144, ct));
    private readonly Func<string, bool> native = nativeExists ?? (name => BoundedProcess.Find(name) is not null);

    /// <summary>Install command for Flatpak on the running distribution (needs root, so it is only shown). Unknown distribution: lists the common ones.</summary>
    public static string FlatpakInstallHint(string id, string idLike)
    {
        var all = (id + " " + idLike).ToLowerInvariant().Split([' ', '"'], StringSplitOptions.RemoveEmptyEntries);
        bool Any(params string[] n) => n.Any(all.Contains);
        if (Any("arch", "cachyos", "manjaro", "endeavouros")) return "sudo pacman -S flatpak";
        if (Any("debian", "ubuntu", "linuxmint", "pop")) return "sudo apt install flatpak";
        if (Any("fedora", "rhel", "centos", "nobara")) return "sudo dnf install flatpak";
        if (Any("opensuse", "suse")) return "sudo zypper install flatpak";
        return "sudo pacman -S flatpak, sudo apt install flatpak ou sudo dnf install flatpak";
    }

    public static bool FlatpakAvailable => BoundedProcess.Find("flatpak") is not null;

    public async Task RefreshInstalledAsync(IEnumerable<InstallableApp> apps, CancellationToken ct = default)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in new[] { "--user", "--system" })
            try { var (code, output) = await runner("flatpak", ["list", scope, "--app", "--columns=application"], TimeSpan.FromSeconds(10), ct); if (code == 0) ids.UnionWith(AppCatalog.ParseFlatpakList(output)); }
            catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { }
        AppCatalog.MarkInstalled(apps, ids, native);
    }

    public async Task<bool> HasFlathubAsync(CancellationToken ct = default)
    {
        foreach (var scope in new[] { "--user", "--system" })
            try { var (code, output) = await runner("flatpak", ["remotes", scope, "--columns=name"], TimeSpan.FromSeconds(10), ct); if (code == 0 && AppCatalog.HasFlathub(output)) return true; }
            catch (Exception e) when (e is IOException or FileNotFoundException or TimeoutException or System.ComponentModel.Win32Exception) { }
        return false;
    }

    /// <summary>Adds the Flathub remote to the user installation (no root). Explicit click only.</summary>
    public async Task<(bool Ok, string Message)> AddFlathubAsync(CancellationToken ct = default)
    {
        var (code, output) = await runner("flatpak", ["remote-add", "--user", "--if-not-exists", "flathub", "https://flathub.org/repo/flathub.flatpakrepo"], TimeSpan.FromSeconds(60), ct);
        return code == 0 ? (true, "Flathub adicionado ao seu usuário.") : (false, $"Falhou (código {code}). {AppCatalog.LastLine(output)}".Trim());
    }

    public static IReadOnlyList<string> InstallArguments(InstallableApp app, bool upgrade = false)
    {
        if (!AppCatalog.ValidId(app.FlatpakId)) throw new ArgumentException("Identificador Flatpak inválido.");
        return upgrade ? ["update", "--user", "-y", "--noninteractive", app.FlatpakId] : ["install", "--user", "-y", "--noninteractive", "flathub", app.FlatpakId];
    }

    public async Task<(bool Ok, string Message)> InstallAsync(InstallableApp app, CancellationToken ct, bool upgrade = false)
    {
        var (code, output) = await runner("flatpak", InstallArguments(app, upgrade), TimeSpan.FromMinutes(20), ct);
        if (code == 0) return (true, "Instalado.");
        if (output.Contains("already installed", StringComparison.OrdinalIgnoreCase) || output.Contains("já instalado", StringComparison.OrdinalIgnoreCase)) return (true, "Já estava instalado.");
        if (output.Contains("No remote refs found", StringComparison.OrdinalIgnoreCase) || output.Contains("not found", StringComparison.OrdinalIgnoreCase)) return (false, "Pacote não encontrado no Flathub.");
        if (output.Contains("Network", StringComparison.OrdinalIgnoreCase) || output.Contains("Could not resolve", StringComparison.OrdinalIgnoreCase)) return (false, "Falha no download: confira a internet e tente de novo.");
        return (false, $"Falhou (código {code}). {AppCatalog.LastLine(output)}".Trim());
    }
}
