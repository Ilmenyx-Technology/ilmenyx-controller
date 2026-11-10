// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Triage of launch options suggested by similar-hardware ProtonDB reports: the recommended set never holds options that fight each other.
// Rules come from the Proton README (runtime config options, proton_10.0) and the DXVK/VKD3D-Proton/Mesa docs:
// - PROTON_USE_WINED3D=1 replaces DXVK (Vulkan) with wined3d (OpenGL) for D3D9-11; DXVK_*/VKD3D_*/NVAPI options then do nothing (and D3D12 is VKD3D only);
// - NVAPI (PROTON_ENABLE_NVAPI / DXVK_ENABLE_NVAPI) needs the NVIDIA proprietary driver; PROTON_DISABLE_NVAPI / PROTON_HIDE_NVIDIA_GPU undo it;
// - RADV_* / ACO_* / AMD_VULKAN_ICD are AMD (Mesa RADV); ANV_* is Intel (Mesa ANV); MESA_* / DRI_PRIME only act on Mesa drivers;
// - a game takes one graphics API switch (-dx11, -dx12, -vulkan…); one variable takes one value; mangohud and MANGOHUD=1 are the same overlay;
// - PROTON_ENABLE_WAYLAND / PROTON_USE_WAYLAND (GE-Proton) are the same switch and are not used inside Gamescope (GE-Proton README, HDR section);
// - PROTON_NO_ESYNC=1 and PROTON_NO_FSYNC=1 are compatible (often used together), so they are not a conflict.
// Pure (no I/O): installed tools come in as a predicate.
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

public enum GpuNeed { NvidiaProprietary, Amd, Intel, Mesa }

/// <summary>Options matching <see cref="A"/> conflict with options matching <see cref="B"/>; with an empty B, any two members of A conflict. {0} in the reason is the kept option.</summary>
public sealed record LaunchConflict(string Name, string[] A, string[] B, string Reason);
public sealed record LaunchGpuRule(string Pattern, GpuNeed Need);
public sealed record LaunchToolRule(string Pattern, string Tool, string Package);
public sealed record LaunchManualRule(string Pattern, string Reason);

public sealed record ExcludedOption(string Option, int Count, int Of, string Reason);
public sealed record LaunchTriage(IReadOnlyList<CompatSuggestion> Recommended, IReadOnlyList<ExcludedOption> Alternatives, string? CommandLine,
    CompatSuggestion? Proton, IReadOnlyList<ExcludedOption> OtherProtons);

public static class LaunchOptionTriage
{
    private static readonly string[] DxvkOnly = ["DXVK_*", "VKD3D_*", "PROTON_ENABLE_NVAPI", "DXVK_ENABLE_NVAPI", "DXVK_NVAPI_*", "PROTON_DXVK_D3D8", "-dx12", "-d3d12", "-force-d3d12"];
    private static readonly string[] Apis = ["-dx9", "-d3d9", "-dx11", "-d3d11", "-force-d3d11", "-dx12", "-d3d12", "-force-d3d12", "-vulkan", "-force-vulkan", "-vk", "-opengl", "-gl", "-force-glcore"];

    public static readonly IReadOnlyList<LaunchConflict> Conflicts =
    [
        new("renderer", ["PROTON_USE_WINED3D=1"], DxvkOnly,
            L.T("Não combina com {0}: o PROTON_USE_WINED3D=1 troca o DXVK/VKD3D (Vulkan) pelo OpenGL, e as opções DXVK_, VKD3D_, NVAPI e DirectX 12 só funcionam com eles.")),
        new("api", Apis, [], L.T("Não combina com {0}: os dois escolhem a API gráfica do jogo (DirectX 9/11/12, Vulkan ou OpenGL), e o jogo só usa uma.")),
        new("nod3d11", ["PROTON_NO_D3D11=1"], ["-dx11", "-d3d11", "-force-d3d11"], L.T("Não combina com {0}: um desliga o DirectX 11 e o outro pede ao jogo para usar o DirectX 11.")),
        new("nod3d10", ["PROTON_NO_D3D10=1"], ["-dx10", "-d3d10"], L.T("Não combina com {0}: um desliga o DirectX 10 e o outro pede ao jogo para usar o DirectX 10.")),
        new("nvapi", ["PROTON_ENABLE_NVAPI=1", "DXVK_ENABLE_NVAPI=1"], ["PROTON_DISABLE_NVAPI=1", "PROTON_HIDE_NVIDIA_GPU=1"],
            L.T("Não combina com {0}: um liga o NVAPI (recursos da NVIDIA, como DLSS) e o outro desliga o NVAPI ou esconde a placa NVIDIA do jogo.")),
        new("mangohud", ["mangohud", "MANGOHUD=1"], [], L.T("Faz o mesmo que {0} (liga o MangoHud, o contador de FPS): basta um.")),
        // GE-Proton README: PROTON_USE_WAYLAND and PROTON_ENABLE_WAYLAND both enable the Wayland driver; under Gamescope keep Wine's default XWayland.
        new("wayland", ["PROTON_ENABLE_WAYLAND=1", "PROTON_USE_WAYLAND=1"], [], L.T("Faz o mesmo que {0} (liga o driver Wayland do Proton): basta um.")),
        new("wayland-gamescope", ["PROTON_ENABLE_WAYLAND=1", "PROTON_USE_WAYLAND=1"], ["gamescope"],
            L.T("Não combina com {0}: dentro do Gamescope o GE-Proton recomenda o XWayland padrão, sem a opção Wayland.")),
    ];

    public static readonly IReadOnlyList<LaunchGpuRule> GpuRules =
    [
        new("PROTON_ENABLE_NVAPI", GpuNeed.NvidiaProprietary), new("DXVK_ENABLE_NVAPI", GpuNeed.NvidiaProprietary), new("DXVK_NVAPI_*", GpuNeed.NvidiaProprietary),
        new("PROTON_DISABLE_NVAPI", GpuNeed.NvidiaProprietary), new("PROTON_HIDE_NVIDIA_GPU", GpuNeed.NvidiaProprietary), new("PROTON_ENABLE_NGX_UPDATER", GpuNeed.NvidiaProprietary),
        new("__GL_*", GpuNeed.NvidiaProprietary), new("__NV_*", GpuNeed.NvidiaProprietary), new("__GLX_VENDOR_LIBRARY_NAME=nvidia", GpuNeed.NvidiaProprietary),
        new("__VK_LAYER_NV_optimus", GpuNeed.NvidiaProprietary), new("prime-run", GpuNeed.NvidiaProprietary),
        new("RADV_*", GpuNeed.Amd), new("ACO_*", GpuNeed.Amd), new("AMD_VULKAN_ICD", GpuNeed.Amd), new("AMD_DEBUG", GpuNeed.Amd), new("PROTON_FSR4_*", GpuNeed.Amd),
        new("ANV_*", GpuNeed.Intel), new("INTEL_DEBUG", GpuNeed.Intel),
        new("MESA_*", GpuNeed.Mesa), new("DRI_PRIME", GpuNeed.Mesa),
    ];

    public static readonly IReadOnlyList<LaunchToolRule> ToolRules =
    [
        new("gamemoderun", "gamemoderun", "gamemode"), new("mangohud", "mangohud", "mangohud"), new("MANGOHUD", "mangohud", "mangohud"),
        new("gamescope", "gamescope", "gamescope"), new("prime-run", "prime-run", "nvidia-prime"), new("obs-gamecapture", "obs-gamecapture", "obs-vkcapture"),
        new("strangle", "strangle", "libstrangle"), new("pvkrun", "pvkrun", "primus-vk"),
    ];

    /// <summary>Wrappers whose arguments (screen size, FPS limit) are not kept in the counted pieces: never part of an automatic combination.</summary>
    public static readonly IReadOnlyList<LaunchManualRule> ManualRules =
    [
        new("gamescope", L.T("O gamescope precisa da resolução da sua tela e de \"--\" antes do jogo: configure à mão, olhando os relatos abaixo.")),
        new("strangle", L.T("O strangle precisa do limite de FPS (ex.: strangle 60): configure à mão, olhando os relatos abaixo.")),
        new("dxvk-hud", L.T("Não é um programa padrão das distribuições: veja no relato como a pessoa usou.")),
    ];

    private static readonly Regex EnvPiece = new(@"^([A-Z_][A-Z0-9_]*)=(.*)$", RegexOptions.CultureInvariant);

    public static (string Key, string Value)? Env(string option) => EnvPiece.Match(option) is { Success: true } m ? (m.Groups[1].Value, m.Groups[2].Value) : null;

    /// <summary>"KEY=VALUE" matches exactly (value ignoring case), "KEY" any value of KEY, "PREFIX*" any key starting with it; other patterns (wrappers, game flags) match whole options ignoring case.</summary>
    public static bool Matches(string pattern, string option)
    {
        if (Env(option) is { } env)
        {
            if (pattern.EndsWith('*')) return env.Key.StartsWith(pattern[..^1], StringComparison.Ordinal);
            int eq = pattern.IndexOf('=');
            return eq < 0 ? env.Key == pattern : env.Key == pattern[..eq] && env.Value.Equals(pattern[(eq + 1)..], StringComparison.OrdinalIgnoreCase);
        }
        return pattern.Equals(option, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Any(string[] patterns, string option) => patterns.Any(p => Matches(p, option));

    /// <summary>Reason why <paramref name="candidate"/> cannot join <paramref name="kept"/>, or null when they combine.</summary>
    public static string? Conflict(string candidate, string kept)
    {
        if (Env(candidate) is { } a && Env(kept) is { } b && a.Key == b.Key && !a.Value.Equals(b.Value, StringComparison.OrdinalIgnoreCase))
            return L.T($"Outro valor para {a.Key} (a combinação já usa {kept}): a variável só guarda um valor.");
        foreach (var c in Conflicts)
        {
            bool hit = c.B.Length == 0
                ? Any(c.A, candidate) && Any(c.A, kept) && !candidate.Equals(kept, StringComparison.OrdinalIgnoreCase)
                : Any(c.A, candidate) && Any(c.B, kept) || Any(c.B, candidate) && Any(c.A, kept);
            if (hit) return string.Format(System.Globalization.CultureInfo.InvariantCulture, c.Reason, kept);
        }
        return null;
    }

    /// <summary>What the user's GPU and driver can use. Unknown vendor: nothing is filtered by GPU.</summary>
    public static HashSet<GpuNeed>? Capabilities(CompatHardware hw) => hw.Vendor switch
    {
        "nvidia" => hw.NvidiaProprietary ? [GpuNeed.NvidiaProprietary] : [GpuNeed.Mesa],
        "amd" => [GpuNeed.Amd, GpuNeed.Mesa],
        "intel" => [GpuNeed.Intel, GpuNeed.Mesa],
        _ => null,
    };

    private static string NeedText(GpuNeed n) => n switch
    {
        GpuNeed.NvidiaProprietary => L.T("placas NVIDIA com o driver proprietário"),
        GpuNeed.Amd => L.T("placas AMD (driver RADV do Mesa)"),
        GpuNeed.Intel => L.T("placas Intel (driver ANV do Mesa)"),
        _ => L.T("drivers Mesa (AMD, Intel ou NVIDIA com nouveau)"),
    };

    /// <summary>Why the option does not apply to this machine (GPU, missing tool, manual setup), or null.</summary>
    public static string? Unusable(string option, CompatHardware hw, Func<string, bool> toolInstalled)
    {
        if (ManualRules.FirstOrDefault(r => Matches(r.Pattern, option)) is { } manual) return manual.Reason;
        if (Capabilities(hw) is { } caps && GpuRules.FirstOrDefault(r => Matches(r.Pattern, option)) is { } gpu && !caps.Contains(gpu.Need))
            return L.T($"Só serve para {NeedText(gpu.Need)}; a sua é outra ({CompatText.Clean(hw.GpuText, 60) ?? L.T("GPU desconhecida")}).");
        if (ToolRules.FirstOrDefault(r => Matches(r.Pattern, option)) is { } tool && !toolInstalled(tool.Tool))
            return L.T($"Precisa do programa {tool.Tool} (pacote {tool.Package}), que não foi encontrado neste PC.");
        return null;
    }

    /// <summary>
    /// Greedy by popularity: each option (most used first) joins the recommended set unless it does not apply to this machine or conflicts with an option
    /// already in it; one Proton version (the most used) is recommended and the others are alternatives.
    /// </summary>
    public static LaunchTriage Triage(IReadOnlyList<CompatSuggestion> options, IReadOnlyList<CompatSuggestion> protons, CompatHardware hw, Func<string, bool> toolInstalled, int maxRecommended = 8)
    {
        var kept = new List<CompatSuggestion>(); var excluded = new List<ExcludedOption>();
        foreach (var o in options.OrderByDescending(o => o.Count).ThenBy(o => o.Option, StringComparer.Ordinal))
        {
            if (kept.Any(k => k.Option.Equals(o.Option, StringComparison.Ordinal))) continue;
            string? reason = Unusable(o.Option, hw, toolInstalled) ?? kept.Select(k => Conflict(o.Option, k.Option)).FirstOrDefault(r => r is not null);
            if (reason is null && kept.Count >= maxRecommended) reason = L.T("Menos usada; a combinação já tem as opções mais citadas.");
            if (reason is null) kept.Add(o); else excluded.Add(new(o.Option, o.Count, o.Of, reason));
        }
        string? line = Compose(kept.Select(k => k.Option));
        if (line is not null && LaunchOptionSafety.Check(line) is { Applicable: false }) { line = null; } // defence: never offer a line Steam would run unsafely
        var ordered = protons.OrderByDescending(p => p.Count).ThenBy(p => p.Option, StringComparer.Ordinal).ToList();
        var proton = ordered.FirstOrDefault();
        var others = ordered.Skip(1).Select(p => new ExcludedOption(p.Option, p.Count, p.Of, L.T($"A Steam usa uma versão do Proton por jogo; {proton!.Option} foi a mais usada."))).ToList();
        return new(kept, excluded, line, proton, others);
    }

    /// <summary>Steam launch line: variables, then wrappers, then %command%, then the game's own arguments. Null when empty.</summary>
    public static string? Compose(IEnumerable<string> pieces)
    {
        var list = pieces.ToList();
        if (list.Count == 0) return null;
        var env = list.Where(p => Env(p) is not null).ToList();
        var wrappers = list.Where(p => Env(p) is null && LaunchOptionSafety.IsWrapper(p)).ToList();
        var args = list.Where(p => Env(p) is null && !LaunchOptionSafety.IsWrapper(p)).ToList();
        return string.Join(' ', env.Concat(wrappers).Append("%command%").Concat(args));
    }
}
