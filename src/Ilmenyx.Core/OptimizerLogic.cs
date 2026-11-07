// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
// New Ilmenyx additions, adapted from the app grouping and suggestions of the original Optimizer (MIT, see NOTICE): see LICENSING.md.
namespace Ilmenyx.Core;

public sealed record RunningProcess(int Pid, string Name, long ResidentBytes, string? DesktopName);

public sealed class BackgroundApp
{
    public string ProcessName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public long MemoryBytes { get; init; }
    public int Count { get; init; }
    public IReadOnlyList<int> Pids { get; init; } = [];
    public bool HasWindow { get; init; }
    public bool Suggested { get; init; }
    public bool Selected { get; set; }
    public string MemoryText => $"{MemoryBytes / 1048576.0:N0} MB".Replace(",", ".");
}

public sealed record CloseResult(IReadOnlyList<string> Closed, IReadOnlyList<string> NotClosed);

public static class OptimizerLogic
{
    /// <summary>Never listed even if they look like applications (session, security, audio, shells, the app itself).</summary>
    public static readonly HashSet<string> Protected = new(StringComparer.Ordinal)
    {
        "systemd", "dbus-daemon", "dbus-broker", "pipewire", "pipewire-pulse", "wireplumber", "pulseaudio", "Xorg", "Xwayland", "kwin_wayland", "kwin_x11", "plasmashell", "gnome-shell",
        "sddm", "gdm", "lightdm", "login", "bash", "zsh", "fish", "sh", "sudo", "ssh-agent", "gpg-agent", "polkitd", "polkit-kde-authentication-agent-1", "kded6", "kglobalacceld",
        "kactivitymanagerd", "ibus-daemon", "fcitx5", "dotnet", "Ilmenyx.Desktop", "ilmenyx", "konsole", "gnome-terminal-server", "kitty", "alacritty", "wezterm-gui", "code", "claude", "xdg-desktop-portal", "xdg-document-portal"
    };

    /// <summary>Background helpers that are safe to close while gaming (pre-selected), the Linux counterpart of the original list.</summary>
    public static readonly IReadOnlyDictionary<string, string> Suggested = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["baloo_file"] = "Indexador de arquivos do KDE (usa disco e CPU)", ["baloo_file_extractor"] = "Indexador de arquivos do KDE (extrator)",
        ["tracker-miner-fs-3"] = "Indexador de arquivos do GNOME", ["tracker-extract-3"] = "Extrator do indexador do GNOME",
        ["dropbox"] = "Sincronização do Dropbox", ["insync"] = "Google Drive (Insync)", ["megasync"] = "Sincronização do MEGA", ["onedrive"] = "Sincronização do OneDrive",
        ["teams-for-linux"] = "Microsoft Teams", ["slack"] = "Slack", ["skypeforlinux"] = "Skype", ["zoom"] = "Zoom", ["akonadi_control"] = "Serviço de dados pessoais do KDE (Akonadi)",
    };

    /// <summary>Groups processes by name. Listed: apps matching an installed desktop entry ("Programa aberto") and suggested helpers; everything else stays out.</summary>
    public static IReadOnlyList<BackgroundApp> Build(IEnumerable<RunningProcess> processes, int selfPid)
    {
        var groups = new Dictionary<string, List<RunningProcess>>(StringComparer.Ordinal);
        foreach (var p in processes)
        {
            if (p.Pid == selfPid || p.Pid <= 1 || p.Name.Length == 0 || Protected.Contains(p.Name)) continue;
            if (!groups.TryGetValue(p.Name, out var list)) groups[p.Name] = list = [];
            list.Add(p);
        }
        var result = new List<BackgroundApp>();
        foreach (var (name, procs) in groups)
        {
            string? desktop = procs.Select(p => p.DesktopName).FirstOrDefault(d => !string.IsNullOrEmpty(d));
            bool hasWindow = desktop is not null, suggested = Suggested.ContainsKey(name);
            if (!hasWindow && !suggested) continue;
            string detail = suggested ? Suggested[name] : "Programa aberto";
            if (procs.Count > 1) detail += $" · {procs.Count} processos";
            result.Add(new BackgroundApp { ProcessName = name, Title = desktop ?? name, Detail = detail, MemoryBytes = procs.Sum(p => p.ResidentBytes), Count = procs.Count, Pids = procs.Select(p => p.Pid).ToList(), HasWindow = hasWindow, Suggested = suggested, Selected = suggested });
        }
        return result.OrderByDescending(a => a.Suggested).ThenByDescending(a => a.MemoryBytes).ToList();
    }
}

/// <summary>A Linux game-tuning item: current state, what it does and, when it needs root, the command the user can run.</summary>
public sealed class GameTweak
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Effect { get; init; } = "";
    public string Badge { get; init; } = "";
    /// <summary>True when the app can toggle it without privileges; otherwise it is read-only and the command is shown.</summary>
    public bool Adjustable { get; init; }
    public bool IsOn { get; set; }
    public string Status { get; set; } = "";
}

public static class GameTweakRules
{
    public const long RecommendedMaxMapCount = 1048576;

    public static GameTweak MaxMapCount(long? current) => new()
    {
        Id = "max_map_count", Title = "vm.max_map_count", Badge = "REQUER ROOT",
        Description = "Limite de mapeamentos de memória por processo. Jogos como CS2, Star Citizen e vários Proton/DXVK travam ou fecham com o valor antigo de 65530.",
        Effect = "Recomendado: pelo menos 1048576. Comando (você executa): sudo sysctl -w vm.max_map_count=2147483642   (persistente: arquivo em /etc/sysctl.d/).",
        IsOn = current is >= RecommendedMaxMapCount, Status = current is { } v ? $"Atual: {v}" + (v >= RecommendedMaxMapCount ? " — adequado." : " — baixo para alguns jogos.") : "Não foi possível ler."
    };

    public static GameTweak Swappiness(long? current) => new()
    {
        Id = "swappiness", Title = "vm.swappiness", Badge = "REQUER ROOT",
        Description = "Quanto o kernel prefere usar swap em vez de descartar cache. Valores altos podem causar engasgos em jogos com pouca RAM livre.",
        Effect = "Sugestão para jogos: 10 a 30 (o padrão 60 funciona bem com bastante RAM). Comando: sudo sysctl -w vm.swappiness=10",
        IsOn = current is <= 30, Status = current is { } v ? $"Atual: {v}" : "Não foi possível ler."
    };

    public static GameTweak Governor(string? governor) => new()
    {
        Id = "governor", Title = "Governador da CPU", Badge = "REQUER ROOT / DAEMON",
        Description = "Política de frequência. Em desktops, 'performance' evita quedas de clock; com power-profiles-daemon o perfil Alto desempenho cuida disso sem root.",
        Effect = "Use o perfil de energia acima (sem privilégios) ou: sudo cpupower frequency-set -g performance",
        IsOn = governor == "performance", Status = governor is { Length: > 0 } ? "Atual: " + governor : "Não informado (VM, contêiner ou driver sem cpufreq)."
    };

    public static GameTweak OpenFiles(long? soft) => new()
    {
        Id = "nofile", Title = "Limite de arquivos abertos", Badge = "REQUER ROOT",
        Description = "Alguns jogos (esync do Proton, Star Citizen) precisam de limite alto de descritores de arquivo.",
        Effect = "Recomendado: 524288 ou mais. Distribuições com systemd recentes já usam 1048576. Ajuste em /etc/security/limits.d/ ou DefaultLimitNOFILE no systemd.",
        IsOn = soft is >= 524288, Status = soft is { } v ? $"Atual (limite soft deste processo): {v}" : "Não foi possível ler."
    };

    public static long? ParseOpenFiles(string limitsText)
    {
        foreach (var line in limitsText.Split('\n'))
            if (line.StartsWith("Max open files", StringComparison.Ordinal))
            {
                var p = line["Max open files".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length > 0 && long.TryParse(p[0], out long soft)) return soft;
            }
        return null;
    }
}
