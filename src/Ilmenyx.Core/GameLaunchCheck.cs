// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// "Jogo não abre?" page: a READ-ONLY checklist of the usual reasons why Proton, Flatpak or a game does not start on Linux. Each item says
// what was found, explains it in plain words, gives the risk and the exact command to copy. Only user-level Flatpak changes (no root:
// Flathub remote for the user, a Steam Flatpak filesystem override) are offered as one-click fixes; anything that needs root, and above
// all anything that lowers security (AppArmor, SELinux, user namespaces), is explain + copy only. Independent implementation; the idea
// list comes from public technical facts (docs/pesquisa-linuxtoys-howmanyfps-2026-11-07.md), no third-party code or configuration.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

public enum CheckStatus { Ok, Info, Attention, Problem }

public enum FlathubState { Missing, Ok, Disabled, WrongUrl }

/// <summary>A user-level fix: always `flatpak` with these arguments (validated by <see cref="GameLaunchChecks.IsAllowedFix"/>), no shell, no root.</summary>
public sealed record UserFix(string Id, string Button, string Confirm, IReadOnlyList<string> Arguments, IReadOnlyList<string>? UndoArguments = null, string? UndoButton = null);

/// <param name="Commands">(label, command) pairs the user may copy and run; "sudo" marks the ones that need the password.</param>
/// <param name="Warning">Security warning shown in a highlighted box (fixes that lower protection).</param>
/// <param name="LinkPage">Another page of the app that already handles this item (e.g. "optimize" for vm.max_map_count).</param>
public sealed record GameCheck(string Id, string Title, CheckStatus Status, string Found, string Explanation, string Risk,
    IReadOnlyList<(string Label, string Command)> Commands, IReadOnlyList<UserFix> Fixes, string? Warning = null, string? LinkPage = null, string? LinkLabel = null);

/// <summary>One flatpak remote line of `flatpak remotes --columns=name,url,options`.</summary>
public sealed record FlatpakRemote(string Name, string Url, bool Disabled);

/// <summary>A mounted real filesystem outside the home folder that could hold games (/mnt, /media, /run/media).</summary>
public sealed record GameDrive(string MountPoint, bool HasSteamLibrary);

/// <summary>Everything the checklist needs, collected read-only by the Linux probe (or built from fixtures in tests).</summary>
public sealed record GameCheckFacts
{
    public DistroInfo Distro { get; init; } = DistroInfo.Parse("");
    public long? AppArmorRestrictUserns { get; init; }
    /// <summary>AppArmor profile files (top-level names under /etc/apparmor.d) that mention bwrap/flatpak/steam/pressure-vessel.</summary>
    public IReadOnlyList<string> UsernsProfiles { get; init; } = [];
    public long? UnprivilegedUsernsClone { get; init; }
    public long? MaxUserNamespaces { get; init; }
    /// <summary>SELinux enforcing (1), permissive (0) or absent (null).</summary>
    public long? SelinuxEnforce { get; init; }
    public bool? SelinuxExecmod { get; init; }
    public string? PacmanConf { get; init; }
    public bool? RpmFusionFree { get; init; }
    public bool? RpmFusionNonFree { get; init; }
    public bool FlatpakInstalled { get; init; }
    public string FlatpakInstallHint { get; init; } = "";
    public IReadOnlyList<FlatpakRemote>? SystemRemotes { get; init; }
    public IReadOnlyList<FlatpakRemote>? UserRemotes { get; init; }
    public bool SteamFlatpak { get; init; }
    /// <summary>Filesystem entries granted to com.valvesoftware.Steam: app metadata, then system and user overrides, in that order.</summary>
    public IReadOnlyList<string> SteamFilesystems { get; init; } = [];
    public string Home { get; init; } = "/home/user";
    public IReadOnlyList<GameDrive> Drives { get; init; } = [];
    public bool SteamNative { get; init; }
    /// <summary>Kernel drivers bound to the DRM cards (i915, xe, amdgpu, radeon, nvidia, nouveau, virtio-pci…).</summary>
    public IReadOnlyList<string> GpuDrivers { get; init; } = [];
    /// <summary>Base names of the library_path of every Vulkan ICD manifest found (libvulkan_intel.so, libGLX_nvidia.so.0…).</summary>
    public IReadOnlyList<string> IcdLibraries { get; init; } = [];
    /// <summary>`vulkaninfo --summary` output, or null when vulkaninfo is missing or failed.</summary>
    public string? VulkanSummary { get; init; }
    public bool VulkaninfoInstalled { get; init; }
    /// <summary>32-bit libraries found (base names) in the distro's 32-bit library folder.</summary>
    public IReadOnlyList<string> Lib32Present { get; init; } = [];
    public long? MaxMapCount { get; init; }
    public long? SplitLockMitigate { get; init; }
}

public static partial class GameLaunchChecks
{
    public const string SteamAppId = "com.valvesoftware.Steam";
    public const string FlathubRepoFile = "https://flathub.org/repo/flathub.flatpakrepo";
    public const string AppArmorProfilePath = "/etc/apparmor.d/ilmenyx-bwrap";

    [GeneratedRegex(@"^/[^\0\n\r;:!=\\""'`$]{1,240}$", RegexOptions.CultureInvariant)] private static partial Regex SafePathRegex();

    /// <summary>An absolute path that can go into `flatpak override --filesystem=` (no separators ; : ! = , no quotes, no "..").</summary>
    public static bool SafeOverridePath(string path) =>
        SafePathRegex().IsMatch(path) && !path.Split('/').Any(p => p is ".." or ".") && !path.EndsWith('/');

    /// <summary>Only these user-level flatpak calls can be run by a click; everything else is refused before any process starts.</summary>
    public static bool IsAllowedFix(IReadOnlyList<string> a)
    {
        if (a.Count == 5 && a[0] == "remote-add" && a[1] == "--user" && a[2] == "--if-not-exists" && a[3] == "flathub" && a[4] == FlathubRepoFile) return true;
        if (a.Count == 4 && a[0] == "remote-modify" && a[1] == "--user" && a[2] == "--enable" && a[3] == "flathub") return true;
        if (a.Count == 4 && a[0] == "override" && a[1] == "--user" && a[3] == SteamAppId)
        {
            foreach (var prefix in new[] { "--filesystem=", "--nofilesystem=" })
                if (a[2].StartsWith(prefix, StringComparison.Ordinal) && SafeOverridePath(a[2][prefix.Length..])) return true;
        }
        return false;
    }

    // ---- parsers (pure, fixture-tested) ----

    /// <summary>True when an uncommented [multilib] section with an Include line exists in pacman.conf.</summary>
    public static bool MultilibEnabled(string pacmanConf)
    {
        bool inMultilib = false;
        foreach (var raw in pacmanConf.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { inMultilib = line == "[multilib]"; continue; }
            if (inMultilib && Regex.IsMatch(line, @"^Include\s*=")) return true;
        }
        return false;
    }

    /// <summary>enabled=1 (or no enabled= line, the dnf default) in the first section of a .repo file.</summary>
    public static bool RepoEnabled(string repoFile)
    {
        bool first = false;
        foreach (var raw in repoFile.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { if (first) break; first = true; continue; }
            if (first && line.StartsWith("enabled", StringComparison.Ordinal) && line.Split('=', 2) is [_, var v]) return v.Trim() is "1" or "true" or "yes";
        }
        return first;
    }

    public static IReadOnlyList<FlatpakRemote> ParseRemotes(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r').Split('\t')).Where(p => p.Length >= 1 && p[0].Trim().Length > 0)
            .Select(p => new FlatpakRemote(p[0].Trim(), p.Length > 1 ? p[1].Trim() : "", p.Length > 2 && p[2].Split(',').Any(o => o.Trim() == "disabled"))).ToList();

    public static FlathubState Flathub(IReadOnlyList<FlatpakRemote>? remotes)
    {
        var r = remotes?.FirstOrDefault(x => x.Name == "flathub");
        if (r is null) return FlathubState.Missing;
        if (r.Disabled) return FlathubState.Disabled;
        return r.Url.Length == 0 || Regex.IsMatch(r.Url, @"^https://(dl\.)?flathub\.org/repo/?$") ? FlathubState.Ok : FlathubState.WrongUrl;
    }

    /// <summary>The "filesystems=" entries of a keyfile (flatpak info --show-permissions / flatpak override --show), in order.</summary>
    public static IReadOnlyList<string> ParseFilesystems(string keyfile)
    {
        var list = new List<string>(); bool context = false;
        foreach (var raw in keyfile.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) { context = line == "[Context]"; continue; }
            if (context && line.StartsWith("filesystems=", StringComparison.Ordinal))
                list.AddRange(line["filesystems=".Length..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return list;
    }

    /// <summary>
    /// Whether the Steam Flatpak can write <paramref name="path"/>: entries are applied in order ("!x" removes x), read-only grants do not count,
    /// "host" covers everything, "home"/"~" the home folder, an absolute entry its subtree.
    /// </summary>
    public static bool Covers(IReadOnlyList<string> entries, string path, string home)
    {
        var granted = new List<string>();
        foreach (var e in entries)
        {
            bool negate = e.StartsWith('!');
            string body = negate ? e[1..] : e;
            int colon = body.LastIndexOf(':');
            string mode = colon > 0 ? body[(colon + 1)..] : "rw";
            if (colon > 0) body = body[..colon];
            body = body == "~" ? home : body.StartsWith("~/", StringComparison.Ordinal) ? home + body[1..] : body == "home" ? home : body;
            if (negate) { granted.RemoveAll(g => g == body); continue; }
            if (mode == "ro") { granted.RemoveAll(g => g == body); continue; }
            granted.Add(body);
        }
        string p = path.TrimEnd('/');
        return granted.Any(g => g == "host" || g.StartsWith('/') && (p == g.TrimEnd('/') || p.StartsWith(g.TrimEnd('/') + "/", StringComparison.Ordinal)));
    }

    /// <summary>(name, driver) of each physical device in `vulkaninfo --summary`; CPU renderers (llvmpipe/lavapipe) included with the type.</summary>
    public static IReadOnlyList<(string Name, string Driver, bool Cpu)> ParseVulkanSummary(string text)
    {
        var list = new List<(string, string, bool)>(); string? name = null, driver = null; bool cpu = false;
        void Flush() { if (name is not null) list.Add((name, driver ?? "", cpu)); name = null; driver = null; cpu = false; }
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (Regex.IsMatch(line, @"^GPU\d+:$")) { Flush(); continue; }
            if (line.Split('=', 2) is not [var k, var v]) continue;
            k = k.Trim(); v = v.Trim();
            if (k == "deviceName") name = v;
            else if (k == "driverName") driver = v;
            else if (k == "deviceType") cpu = v.Contains("CPU", StringComparison.Ordinal);
        }
        Flush();
        return list;
    }

    /// <summary>The 32-bit library folder of the family: Debian/Ubuntu multiarch, Arch lib32, Fedora/openSUSE /usr/lib.</summary>
    public static string Lib32Folder(DistroFamily family) => family switch
    {
        DistroFamily.Ubuntu or DistroFamily.Debian => "/usr/lib/i386-linux-gnu",
        DistroFamily.Arch => "/usr/lib32",
        _ => "/usr/lib",
    };

    /// <summary>32-bit libraries Steam (native) needs for the GPU drivers in use: the Vulkan loader, OpenGL and each driver's ICD.</summary>
    public static IReadOnlyList<string> Lib32Needed(IReadOnlyList<string> drivers)
    {
        var list = new List<string> { "libvulkan.so.1", "libGL.so.1" };
        foreach (var d in drivers.Distinct())
            switch (d)
            {
                case "i915" or "xe": list.Add("libvulkan_intel.so"); break;
                case "amdgpu": list.Add("libvulkan_radeon.so"); break;
                case "nvidia": list.Add("libGLX_nvidia.so.0"); break;
            }
        return list.Distinct().ToList();
    }

    private static readonly Dictionary<string, (string Vendor, string[] Libraries)> IcdByDriver = new(StringComparer.Ordinal)
    {
        ["i915"] = ("Intel", ["libvulkan_intel.so", "libvulkan_intel_hasvk.so"]),
        ["xe"] = ("Intel", ["libvulkan_intel.so"]),
        ["amdgpu"] = ("AMD", ["libvulkan_radeon.so", "amdvlk64.so", "amdvlk.so"]),
        ["nvidia"] = (L.T("NVIDIA (driver proprietário)"), ["libGLX_nvidia.so.0", "libEGL_nvidia.so.0"]),
        ["nouveau"] = ("NVIDIA (nouveau/NVK)", ["libvulkan_nouveau.so"]),
    };

    private static string Sudo(DistroFamily f, string apt, string dnf, string pacman, string zypper) => f switch
    {
        DistroFamily.Ubuntu or DistroFamily.Debian => apt,
        DistroFamily.Fedora => dnf,
        DistroFamily.Arch => pacman,
        DistroFamily.OpenSuse => zypper,
        _ => "",
    };

    private static IReadOnlyList<(string, string)> Cmds(params (string Label, string Command)[] c) => c.Where(x => x.Command.Length > 0).ToList();

    public static IReadOnlyList<GameCheck> Build(GameCheckFacts f)
    {
        var list = new List<GameCheck>();
        var fam = f.Distro.Family;

        // 1. AppArmor userns restriction (Ubuntu 23.10+/24.04+ and derivatives).
        if (f.AppArmorRestrictUserns is { } restrict)
        {
            bool bwrapProfile = f.UsernsProfiles.Any(p => p.Contains("bwrap", StringComparison.OrdinalIgnoreCase) || p.Contains("flatpak", StringComparison.OrdinalIgnoreCase));
            string profiles = f.UsernsProfiles.Count > 0 ? string.Join(", ", f.UsernsProfiles) : "nenhum";
            var status = restrict == 0 ? CheckStatus.Ok : bwrapProfile ? CheckStatus.Info : CheckStatus.Attention;
            list.Add(new("apparmor", L.T("AppArmor: restrição de namespaces de usuário"), status,
                restrict == 0 ? L.T("Restrição desligada (kernel.apparmor_restrict_unprivileged_userns = 0).") : L.T($"Restrição LIGADA (kernel.apparmor_restrict_unprivileged_userns = 1). Perfis de AppArmor encontrados para bwrap/Flatpak/Steam: {profiles}."),
                L.T("O Flatpak e a Steam (pressure-vessel, o \"contêiner\" do Proton) usam o bubblewrap (bwrap), que cria um namespace de usuário. Desde o Ubuntu 23.10/24.04 o AppArmor só deixa isso para programas com um perfil próprio. Sem perfil, o sintoma é o jogo ou o app Flatpak fechar na hora, com erros como \"bwrap: setting up uid map: Permission denied\" ou \"No permissions to create new namespace\". Se a Steam e o Flatpak abrem normalmente, não mude nada."),
                L.T("A correção afrouxa uma proteção do sistema: o bwrap passa a poder criar namespaces sem restrição. É o que a própria Steam/Flatpak precisa, mas também amplia o que um programa malicioso poderia explorar. Por isso o Ilmenyx só mostra os comandos."),
                restrict == 0 ? [] : Cmds(
                    (L.T("Ver se o kernel bloqueou algo (procure userns_create)"), "sudo journalctl -k -g 'apparmor=\"DENIED\"' --since today"),
                    (L.T("Criar um perfil só para o bwrap (permanente)"), "printf '%s\\n' 'abi <abi/4.0>,' 'include <tunables/global>' 'profile ilmenyx-bwrap /usr/bin/bwrap flags=(unconfined) {' '  userns,' '}' | sudo tee " + AppArmorProfilePath + " && sudo apparmor_parser -r " + AppArmorProfilePath),
                    (L.T("Desfazer o perfil"), "sudo apparmor_parser -R " + AppArmorProfilePath + " && sudo rm " + AppArmorProfilePath),
                    (L.T("Alternativa só até reiniciar (desliga a restrição para TODOS os programas)"), "sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0")),
                [], restrict == 0 ? null : L.T("Reduz a segurança. Só aplique se a Steam/Flatpak realmente não abre e o log mostra o bloqueio. O perfil vale apenas para /usr/bin/bwrap; a alternativa com sysctl desliga a proteção do sistema inteiro.")));
        }

        // 2. Unprivileged user namespaces disabled outright.
        if (f.UnprivilegedUsernsClone is not null || f.MaxUserNamespaces is not null)
        {
            bool cloneOff = f.UnprivilegedUsernsClone == 0, maxOff = f.MaxUserNamespaces == 0;
            string found = string.Join(" · ", new[] { f.UnprivilegedUsernsClone is { } c ? L.T($"kernel.unprivileged_userns_clone = {c}") : null, f.MaxUserNamespaces is { } m ? L.T($"user.max_user_namespaces = {m}") : null }.OfType<string>());
            list.Add(new("userns", L.T("Namespaces de usuário sem privilégio"), cloneOff || maxOff ? CheckStatus.Problem : CheckStatus.Ok, found + (cloneOff || maxOff ? L.T(" — desligados.") : L.T(" — permitidos.")),
                L.T("O Flatpak, a Steam (pressure-vessel) e o Proton precisam criar namespaces de usuário sem ser root. Se o kernel estiver configurado para negar, nada disso abre."),
                L.T("Religar volta ao padrão da maioria das distribuições, mas desfaz um endurecimento que alguém (ou um script de segurança) fez de propósito. Confira antes por que estava desligado."),
                cloneOff || maxOff ? Cmds(
                    (L.T("Religar até reiniciar"), (cloneOff ? "sudo sysctl -w kernel.unprivileged_userns_clone=1" : "") + (cloneOff && maxOff ? " && " : "") + (maxOff ? "sudo sysctl -w user.max_user_namespaces=63000" : "")),
                    (L.T("Procurar quem desligou (arquivos de sysctl)"), "grep -rn 'userns\\|max_user_namespaces' /etc/sysctl.conf /etc/sysctl.d/ /usr/lib/sysctl.d/ 2>/dev/null")) : [],
                [], cloneOff || maxOff ? L.T("Reduz o endurecimento do sistema. Não aplique em servidores ou PCs gerenciados por outra pessoa.") : null));
        }

        // 3. SELinux execmod (openSUSE Tumbleweed/Fedora): Wine/Proton need executable memory mappings.
        if (f.SelinuxEnforce is 1 && f.SelinuxExecmod is { } execmod)
            list.Add(new("selinux", L.T("SELinux: permissão execmod (Wine/Proton)"), execmod ? CheckStatus.Ok : CheckStatus.Attention,
                L.T($"SELinux em modo enforcing · selinuxuser_execmod = {(execmod ? "ligado" : "desligado")}."),
                L.T("Wine e Proton precisam de memória que é escrita e depois executada. Com o SELinux aplicando as regras e a permissão selinuxuser_execmod desligada, alguns jogos fecham ao abrir ou aparecem negações \"execmod\" no log de auditoria."),
                L.T("Ligar a permissão deixa qualquer programa do usuário fazer esse tipo de mapeamento, o que reduz uma proteção contra exploração de falhas."),
                execmod ? [] : Cmds((L.T("Ver negações recentes"), "sudo ausearch -m avc -ts recent | grep execmod"), (L.T("Ligar (permanente)"), "sudo setsebool -P selinuxuser_execmod 1"), (L.T("Desfazer"), "sudo setsebool -P selinuxuser_execmod 0")),
                [], execmod ? null : L.T("Reduz a segurança. Só ligue se o log mostrar negações execmod de Wine/Proton/Steam.")));

        // 4. Arch multilib (needed by the native Steam package).
        if (fam == DistroFamily.Arch && f.PacmanConf is { } pacman)
        {
            bool on = MultilibEnabled(pacman);
            list.Add(new("multilib", L.T("Repositório multilib (Arch e derivadas)"), on ? CheckStatus.Ok : f.SteamNative ? CheckStatus.Problem : CheckStatus.Attention,
                on ? L.T("[multilib] ativo em /etc/pacman.conf.") : L.T("[multilib] desativado (comentado) em /etc/pacman.conf."),
                L.T("A Steam nativa do Arch e as bibliotecas de 32 bits (lib32-*) vêm do repositório multilib. Sem ele, o pacote steam não instala e jogos de 32 bits não abrem. A Steam Flatpak não precisa dele."),
                L.T("Baixo: é um repositório oficial do Arch. Ative e atualize o sistema inteiro em seguida (nunca só um pacote)."),
                on ? [] : Cmds((L.T("Abrir o arquivo para tirar o # das linhas [multilib] e Include"), "sudo nano /etc/pacman.conf"), (L.T("Depois, atualizar o sistema"), "sudo pacman -Syu")), []));
        }

        // 5. Fedora RPM Fusion (native Steam, codecs, NVIDIA).
        if (fam == DistroFamily.Fedora && (f.RpmFusionFree is not null || f.RpmFusionNonFree is not null))
        {
            bool free = f.RpmFusionFree == true, nonFree = f.RpmFusionNonFree == true;
            list.Add(new("rpmfusion", "RPM Fusion (Fedora)", free && nonFree ? CheckStatus.Ok : CheckStatus.Attention,
                L.T($"RPM Fusion free: {(free ? "ativo" : L.T("ausente ou desativado"))} · nonfree: {(nonFree ? "ativo" : L.T("ausente ou desativado"))}."),
                L.T("O Fedora não traz a Steam nativa, os codecs completos de vídeo nem o driver NVIDIA. Eles vêm do RPM Fusion (free e nonfree). Sem ele, vídeos de abertura de jogos podem ficar pretos e a Steam nativa não instala. Com a Steam Flatpak, a falta é menor."),
                L.T("Baixo a médio: é um repositório de terceiros muito usado e recomendado pela comunidade do Fedora, mas passa a fornecer pacotes ao seu sistema."),
                free && nonFree ? [] : Cmds((L.T("Instalar o RPM Fusion free e nonfree (comando oficial)"), "sudo dnf install https://mirrors.rpmfusion.org/free/fedora/rpmfusion-free-release-$(rpm -E %fedora).noarch.rpm https://mirrors.rpmfusion.org/nonfree/fedora/rpmfusion-nonfree-release-$(rpm -E %fedora).noarch.rpm")), []));
        }

        // 6. Flathub remote (system and user).
        if (!f.FlatpakInstalled)
            list.Add(new("flathub", L.T("Flatpak e Flathub"), CheckStatus.Info, L.T("Flatpak não está instalado."),
                L.T("Sem o Flatpak, não há Steam Flatpak, Heroic Flatpak nem os apps da página Instalar apps. Se você usa a Steam nativa, pode ignorar este item."),
                L.T("Baixo: é o pacote oficial da sua distribuição."), f.FlatpakInstallHint.Length > 0 ? Cmds((L.T("Instalar o Flatpak"), f.FlatpakInstallHint)) : [], []));
        else
        {
            var sys = Flathub(f.SystemRemotes); var user = Flathub(f.UserRemotes);
            string Text(FlathubState s) => s switch { FlathubState.Ok => "ok", FlathubState.Disabled => L.T("DESATIVADO"), FlathubState.WrongUrl => L.T("endereço diferente do oficial"), _ => "ausente" };
            bool any = sys == FlathubState.Ok || user == FlathubState.Ok;
            var fixes = new List<UserFix>();
            var cmds = new List<(string, string)>();
            if (!any && user == FlathubState.Missing)
            {
                fixes.Add(new("flathub-add", L.T("Adicionar Flathub (só para mim)"), L.T("Adicionar o Flathub para o seu usuário?\n\nRoda: flatpak remote-add --user --if-not-exists flathub ") + FlathubRepoFile + L.T("\n\nNão precisa de senha e baixa a lista do Flathub (flathub.org)."),
                    ["remote-add", "--user", "--if-not-exists", "flathub", FlathubRepoFile]));
                cmds.Add((L.T("Adicionar o Flathub para o seu usuário"), "flatpak remote-add --user --if-not-exists flathub " + FlathubRepoFile));
            }
            if (user == FlathubState.Disabled)
            {
                fixes.Add(new("flathub-enable", L.T("Reativar Flathub (só para mim)"), L.T("Reativar o Flathub do seu usuário?\n\nRoda: flatpak remote-modify --user --enable flathub\n\nNão precisa de senha."), ["remote-modify", "--user", "--enable", "flathub"]));
                cmds.Add((L.T("Reativar o Flathub do usuário"), "flatpak remote-modify --user --enable flathub"));
            }
            if (sys == FlathubState.Disabled) cmds.Add((L.T("Reativar o Flathub do sistema (pede senha)"), "sudo flatpak remote-modify --enable flathub"));
            if (sys == FlathubState.WrongUrl) cmds.Add((L.T("Corrigir o endereço do Flathub do sistema"), "sudo flatpak remote-modify --url=https://dl.flathub.org/repo/ flathub"));
            if (user == FlathubState.WrongUrl) cmds.Add((L.T("Corrigir o endereço do Flathub do usuário"), "flatpak remote-modify --user --url=https://dl.flathub.org/repo/ flathub"));
            if (!any && sys == FlathubState.Missing) cmds.Add((L.T("Ou adicionar para todos os usuários (pede senha)"), "sudo flatpak remote-add --if-not-exists flathub " + FlathubRepoFile));
            var status = any && sys is not (FlathubState.Disabled or FlathubState.WrongUrl) && user is not (FlathubState.Disabled or FlathubState.WrongUrl) ? CheckStatus.Ok : any ? CheckStatus.Attention : CheckStatus.Problem;
            list.Add(new("flathub", L.T("Flatpak: remoto Flathub"), status, L.T($"Flathub do sistema: {Text(sys)} · do usuário: {Text(user)}."),
                L.T("O Flathub é a loja de onde vêm a Steam Flatpak, o Heroic, o ProtonPlus e os runtimes de jogos. Se ele faltar ou estiver desativado, instalar e atualizar esses apps falha (\"No remote refs found\", \"Unable to load summary\")."),
                L.T("Baixo: o remoto do usuário não precisa de senha e só afeta a sua conta. Para desfazer: flatpak remote-delete --user flathub (os apps instalados por ele também saem)."),
                cmds, fixes));
        }

        // 7. Steam Flatpak access to extra game drives.
        if (f.SteamFlatpak)
        {
            var blocked = f.Drives.Where(d => SafeOverridePath(d.MountPoint) && !Covers(f.SteamFilesystems, d.MountPoint, f.Home)).ToList();
            var allowed = f.Drives.Where(d => Covers(f.SteamFilesystems, d.MountPoint, f.Home)).ToList();
            var fixes = blocked.Take(8).Select(d => new UserFix("steam-fs:" + d.MountPoint, L.T("Liberar ") + Short(d.MountPoint) + L.T(" para a Steam"),
                L.T($"Deixar a Steam Flatpak usar {d.MountPoint}?\n\nRoda: flatpak override --user --filesystem={d.MountPoint} {SteamAppId}\n\nNão precisa de senha. Feche e abra a Steam depois. Dá para desfazer aqui."),
                ["override", "--user", "--filesystem=" + d.MountPoint, SteamAppId], ["override", "--user", "--nofilesystem=" + d.MountPoint, SteamAppId], L.T("Desfazer"))).ToList();
            bool library = blocked.Any(d => d.HasSteamLibrary);
            string found = f.Drives.Count == 0 ? L.T("Nenhum disco extra montado em /mnt, /media ou /run/media.")
                : (blocked.Count > 0 ? L.T("Sem acesso: ") + string.Join(", ", blocked.Select(d => d.MountPoint + (d.HasSteamLibrary ? L.T(" (tem biblioteca Steam)") : ""))) + "." : "")
                + (allowed.Count > 0 ? (blocked.Count > 0 ? " " : "") + L.T("Com acesso: ") + string.Join(", ", allowed.Select(d => d.MountPoint)) + "." : "");
            list.Add(new("steamdrives", L.T("Steam Flatpak: acesso aos discos de jogos"), library ? CheckStatus.Problem : blocked.Count > 0 ? CheckStatus.Info : CheckStatus.Ok, found,
                L.T("A Steam Flatpak roda isolada e só enxerga a sua pasta pessoal. Uma biblioteca em outro disco (HD/SSD extra em /mnt ou /media) some da Steam ou os jogos de lá não abrem, até você liberar o acesso àquela pasta."),
                L.T("Baixo: libera só aquela pasta, só para a Steam e só para o seu usuário. Desfazer: flatpak override --user --nofilesystem=<pasta> ") + SteamAppId + ".",
                Cmds((L.T("Liberar outra pasta (troque o caminho)"), "flatpak override --user --filesystem=/caminho/do/disco " + SteamAppId), (L.T("Ver o que a Steam Flatpak pode acessar"), "flatpak info --show-permissions " + SteamAppId)), fixes));
        }

        // 8. Vulkan driver (ICD) for the GPUs in use.
        {
            var drivers = f.GpuDrivers.Distinct().ToList();
            var devices = f.VulkanSummary is { } vs ? ParseVulkanSummary(vs) : [];
            var missing = new List<string>(); var notes = new List<string>();
            foreach (var d in drivers)
            {
                if (IcdByDriver.TryGetValue(d, out var e)) { if (!e.Libraries.Any(l => f.IcdLibraries.Contains(l))) missing.Add(e.Vendor + " (" + d + ")"); }
                else if (d == "radeon") notes.Add(L.T("Placa AMD antiga no driver radeon: ele não tem Vulkan. Placas GCN 1.0/2.0 podem usar o amdgpu (parâmetro do kernel); mais antigas não rodam DXVK."));
                else if (d is "virtio-pci" or "virtio_gpu" or "vmwgfx" or "qxl" or "bochs-drm" or "bochs") notes.Add(L.T("Placa de vídeo virtual (máquina virtual): o Vulkan depende do hipervisor."));
            }
            bool onlyCpu = devices.Count > 0 && devices.All(x => x.Cpu);
            var status = drivers.Count == 0 ? CheckStatus.Info : missing.Count > 0 || onlyCpu ? CheckStatus.Problem : CheckStatus.Ok;
            string found = (drivers.Count > 0 ? L.T("Drivers de vídeo em uso: ") + string.Join(", ", drivers) + ". " : L.T("Nenhuma placa de vídeo encontrada em /sys/class/drm. "))
                + (missing.Count > 0 ? L.T("Sem driver Vulkan para: ") + string.Join(", ", missing) + ". " : drivers.Count > 0 ? L.T("Driver Vulkan presente para cada uma. ") : "")
                + (devices.Count > 0 ? "vulkaninfo: " + string.Join("; ", devices.Select(x => x.Name + (x.Driver.Length > 0 ? " (" + x.Driver + ")" : "") + (x.Cpu ? " [CPU]" : ""))) + "." : f.VulkaninfoInstalled ? L.T("vulkaninfo não respondeu.") : L.T("vulkaninfo não instalado (pacote vulkan-tools), então só os arquivos foram conferidos."));
            string explain = L.T("O Proton traduz DirectX para Vulkan (DXVK/VKD3D). Sem o driver Vulkan da sua placa, os jogos não abrem ou caem para a renderização pela CPU (llvmpipe), que é lentíssima.") + (notes.Count > 0 ? " " + string.Join(" ", notes) : "") + (onlyCpu ? L.T(" O vulkaninfo só achou a renderização pela CPU.") : "");
            bool needNvidia = missing.Any(m => m.Contains("(nvidia)"));
            var cmds = Cmds(
                (L.T("Instalar o driver Vulkan do Mesa (Intel/AMD/nouveau)"), missing.Any(m => !m.Contains("(nvidia)")) || onlyCpu ? Sudo(fam, "sudo apt install mesa-vulkan-drivers", "sudo dnf install mesa-vulkan-drivers", "sudo pacman -S --needed " + string.Join(' ', drivers.Select(d => d switch { "i915" or "xe" => "vulkan-intel", "amdgpu" => "vulkan-radeon", "nouveau" => "vulkan-nouveau", _ => "" }).Where(x => x.Length > 0).DefaultIfEmpty(L.T("vulkan-intel vulkan-radeon")).Distinct()), "sudo zypper install libvulkan_intel libvulkan_radeon") : ""),
                (L.T("Ferramenta para conferir (vulkaninfo)"), f.VulkaninfoInstalled ? "" : Sudo(fam, "sudo apt install vulkan-tools", "sudo dnf install vulkan-tools", "sudo pacman -S --needed vulkan-tools", "sudo zypper install vulkan-tools")),
                (L.T("Conferir as placas que o Vulkan enxerga"), f.VulkaninfoInstalled ? L.T("vulkaninfo --summary") : ""));
            list.Add(new("vulkan", L.T("Vulkan para a sua placa de vídeo"), status, found.Trim(), explain,
                L.T("Baixo: são pacotes oficiais da distribuição."), cmds, [], LinkPage: needNvidia ? "nvdriver" : null, LinkLabel: needNvidia ? L.T("Abrir Driver NVIDIA") : null));
        }

        // 9. 32-bit Vulkan/GL libraries for the native Steam.
        if (f.SteamNative && fam != DistroFamily.Other)
        {
            var needed = Lib32Needed(f.GpuDrivers);
            var missing = needed.Where(n => !f.Lib32Present.Contains(n)).ToList();
            bool nvidia = f.GpuDrivers.Contains("nvidia");
            string install = Sudo(fam,
                "sudo dpkg --add-architecture i386 && sudo apt update && sudo apt install libvulkan1:i386 libgl1:i386 mesa-vulkan-drivers:i386" + (nvidia ? L.T(" libnvidia-gl-XXX:i386   (troque XXX pelo ramo do seu driver NVIDIA, ex.: 580)") : ""),
                "sudo dnf install vulkan-loader.i686 mesa-libGL.i686 mesa-vulkan-drivers.i686" + (nvidia ? " xorg-x11-drv-nvidia-libs.i686" : ""),
                "sudo pacman -S --needed lib32-vulkan-icd-loader lib32-mesa" + string.Concat(f.GpuDrivers.Distinct().Select(d => d switch { "i915" or "xe" => " lib32-vulkan-intel", "amdgpu" => " lib32-vulkan-radeon", "nvidia" => " lib32-nvidia-utils", _ => "" })),
                "sudo zypper install libvulkan1-32bit Mesa-libGL1-32bit" + string.Concat(f.GpuDrivers.Distinct().Select(d => d switch { "i915" or "xe" => " libvulkan_intel-32bit", "amdgpu" => " libvulkan_radeon-32bit", _ => "" })));
            list.Add(new("lib32", L.T("Bibliotecas de 32 bits (Steam nativa)"), missing.Count == 0 ? CheckStatus.Ok : CheckStatus.Problem,
                missing.Count == 0 ? L.T($"Encontradas em {Lib32Folder(fam)}: {string.Join(", ", needed)}.") : L.T($"Faltando em {Lib32Folder(fam)}: {string.Join(", ", missing)}."),
                L.T("A Steam nativa e muitos jogos (e partes do Proton) ainda são de 32 bits. Eles precisam das versões de 32 bits do carregador Vulkan, do OpenGL e do driver da sua placa. Sem elas, a Steam não abre ou mostra \"glXChooseVisual failed\" / \"You are missing the following 32-bit libraries\". A Steam Flatpak já traz tudo isso."),
                L.T("Baixo: pacotes oficiais da distribuição (no Ubuntu/Debian é preciso ativar a arquitetura i386 antes)."),
                missing.Count == 0 ? [] : Cmds((L.T("Instalar as bibliotecas de 32 bits"), install)), []));
        }

        // 10. vm.max_map_count: handled by the Otimiza page (link, no duplicate).
        if (f.MaxMapCount is { } mmc)
            list.Add(new("maxmap", "vm.max_map_count", mmc >= GameTweakRules.RecommendedMaxMapCount ? CheckStatus.Ok : CheckStatus.Attention, L.T($"Atual: {mmc.ToString(CultureInfo.InvariantCulture)}."),
                L.T("Limite de mapeamentos de memória por processo. Com o valor antigo (65530), jogos como CS2 e vários títulos com Proton fecham sozinhos ou travam no carregamento. A página Otimiza explica e mostra o comando."),
                "Baixo.", [], [], LinkPage: "optimize", LinkLabel: L.T("Abrir Otimiza")));

        // 11. Split-lock mitigation (information only).
        if (f.SplitLockMitigate is { } sl)
            list.Add(new("splitlock", L.T("Mitigação de split-lock"), CheckStatus.Info, sl == 0 ? L.T("Desligada (kernel.split_lock_mitigate = 0).") : L.T("Ligada (kernel.split_lock_mitigate = 1), o padrão do kernel."),
                L.T("Quando um programa faz um acesso \"split-lock\" (comum em alguns jogos antigos de Windows), o kernel o atrasa de propósito para proteger o resto do sistema. Em poucos jogos isso aparece como quedas fortes de FPS. Só vale mexer se um jogo específico estiver com esse problema; o log do kernel mostra \"split lock detection\" com o nome do processo."),
                L.T("Desligar tira uma proteção contra programas que travam o barramento da memória (afeta outros usuários e VMs no mesmo PC). Vale só até reiniciar."),
                sl == 0 ? Cmds((L.T("Voltar ao padrão"), "sudo sysctl -w kernel.split_lock_mitigate=1")) : Cmds((L.T("Ver se algum jogo disparou"), "sudo journalctl -k -g 'split lock' --since today"), (L.T("Desligar até reiniciar"), "sudo sysctl -w kernel.split_lock_mitigate=0")), []));

        return list;
    }

    private static string Short(string mount)
    {
        string name = mount.TrimEnd('/').Split('/').LastOrDefault() ?? mount;
        name = name.Length > 18 ? name[..18] + "…" : name;
        return name.ToUpperInvariant();
    }

    public static string StatusText(CheckStatus s) => s switch { CheckStatus.Ok => "OK", CheckStatus.Info => L.T("INFORMAÇÃO"), CheckStatus.Attention => L.T("ATENÇÃO"), _ => L.T("PROBLEMA") };

    /// <summary>One line for the top of the page.</summary>
    public static string Summary(IReadOnlyList<GameCheck> checks)
    {
        int problems = checks.Count(c => c.Status == CheckStatus.Problem), attention = checks.Count(c => c.Status == CheckStatus.Attention);
        if (problems == 0 && attention == 0) return L.T($"Nenhum problema encontrado nas {checks.Count} verificações. Se um jogo ainda não abre, veja a página Compatibilidade.");
        return (problems > 0 ? L.T($"{problems} problema(s)") : "") + (problems > 0 && attention > 0 ? " e " : "") + (attention > 0 ? L.T($"{attention} ponto(s) de atenção") : "") + L.T($" em {checks.Count} verificações. Leia cada item: nada foi alterado.");
    }
}
