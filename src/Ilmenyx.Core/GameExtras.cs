// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Gaming extras (2026-11-08): a bigger shader cache for the user session and two experimental per-game Proton options.
// Idea list from the LinuxToys survey (docs/pesquisa-linuxtoys-howmanyfps-2026-11-07.md); own implementation, no code or configuration taken
// from it (GPL-3.0). Only public technical facts are used, each from the upstream documentation:
// - Mesa (docs.mesa3d.org/envvars.html): MESA_SHADER_CACHE_MAX_SIZE, number with K/M/G suffix, 1 GB when unset; caches under
//   $XDG_CACHE_HOME/mesa_shader_cache (also _db and _sf variants).
// - NVIDIA README "OpenGL environment variable settings" (driver 570/580): __GL_SHADER_DISK_CACHE_SIZE (integer, bytes), cache in
//   $XDG_CACHE_HOME/nvidia/GLCache; an application run WITHOUT the variable reverts the cache to the default size and wipes it if larger,
//   so NVIDIA recommends setting it for the whole session. __GL_SHADER_DISK_CACHE_SKIP_CLEANUP is not in that README: not used.
// - GE-Proton README (runtime options): PROTON_ENABLE_WAYLAND / PROTON_USE_WAYLAND "Enables the Wayland driver"; GE-Proton 10+ and
//   UMU-Proton (built from GE). Valve's Proton 10 README does not list it, so official Proton is never given the option.
// - Proton READMEs: PROTON_ENABLE_NVAPI enables NVAPI (GE; Valve's Proton enables it by default and only documents PROTON_DISABLE_NVAPI).
//   DXVK-NVAPI README: NVIDIA Reflex is forwarded to DXVK / VKD3D-Proton; with other drivers it needs DXVK_NVAPI_ALLOW_OTHER_DRIVERS and is
//   of "very limited use", so low latency is only offered for the NVIDIA proprietary driver. AMD Anti-Lag/Reflex for Mesa exists only in a
//   third-party Vulkan layer (low_latency_layer, LOW_LATENCY_LAYER=1) that the user must install: not offered (see docs/jogos-extras.md).
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

/// <summary>Bigger shader cache for the user session (systemd environment.d file, no root). Never sets AMD_VULKAN_ICD or any driver choice.</summary>
public static class ShaderCacheTweak
{
    public const string FileName = "90-ilmenyx-shader-cache.conf";
    public const string Marker = "# Ilmenyx: cache de shaders maior.";
    public const string MesaVariable = "MESA_SHADER_CACHE_MAX_SIZE", MesaValue = "10G";
    public const string NvidiaVariable = "__GL_SHADER_DISK_CACHE_SIZE";
    public const long NvidiaBytes = 10L * 1024 * 1024 * 1024;

    public static IReadOnlyList<KeyValuePair<string, string>> Variables { get; } =
        [new(MesaVariable, MesaValue), new(NvidiaVariable, NvidiaBytes.ToString(CultureInfo.InvariantCulture))];

    /// <summary>The whole file the app writes (KEY=VALUE lines, environment.d syntax), starting with <see cref="Marker"/>.</summary>
    public static string FileContent() =>
        Marker + " Vale a partir do próximo login.\n# Para desfazer: desligue a opção na página Otimiza do Ilmenyx ou apague este arquivo.\n" +
        string.Join("", Variables.Select(v => $"{v.Key}={v.Value}\n"));

    /// <summary>True only for a file the app wrote itself (first line is the marker): any other file with the same name is never touched.</summary>
    public static bool IsOurs(string? text) => text is not null && text.StartsWith(Marker, StringComparison.Ordinal);

    /// <summary>KEY=VALUE pairs of an environment.d file (comments and blank lines skipped; quotes around the value removed).</summary>
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[line[..eq].Trim()] = line[(eq + 1)..].Trim().Trim('"', '\'');
        }
        return map;
    }

    /// <summary>"1,2 GB" style size in pt-BR (binary units, like the rest of the app).</summary>
    public static string SizeText(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"]; double v = bytes; int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return (u == 0 ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')) + " " + units[u];
    }

    /// <summary>The Otimiza item: state, explanation (takes effect on the next login, uses disk) and the cache folders found with their size.</summary>
    public static GameTweak Describe(bool on, bool foreignFile, string? sessionValue, IReadOnlyList<(string Label, long? Bytes)> caches, string path)
    {
        var found = caches.Where(c => c.Bytes is not null).ToList();
        string sizes = found.Count == 0 ? "Nenhum cache de shaders encontrado ainda." : "Tamanho atual: " + string.Join(" · ", found.Select(c => $"{c.Label} {SizeText(c.Bytes!.Value)}")) + ".";
        string session = sessionValue is null ? "Nesta sessão o limite do Mesa é o padrão (1 GB)." : $"Nesta sessão: {MesaVariable}={sessionValue}.";
        string state = foreignFile ? $"Já existe um arquivo {FileName} que não foi criado pelo Ilmenyx: ele não será alterado."
            : on ? (sessionValue == MesaValue ? "Ligado e em uso nesta sessão." : "Ligado. Vale a partir do próximo login (saia da sessão e entre de novo).") : "Desligado.";
        return new GameTweak
        {
            Id = "shadercache", Title = "Cache de shaders maior", Badge = "SEM ROOT", Adjustable = !foreignFile, IsOn = on,
            Description = "Aumenta para 10 GB o espaço em disco que os drivers de vídeo usam para guardar shaders já compilados (o padrão do Mesa, de AMD e Intel, é 1 GB). Com o limite cheio, o cache é apagado e o jogo volta a engasgar compilando shaders a cada sessão.",
            Effect = $"Grava {path} ({MesaVariable}={MesaValue} e {NvidiaVariable}={NvidiaBytes}), só para o seu usuário. Vale a partir do próximo login e também nos jogos abertos pelo Ilmenyx. Usa mais espaço em disco, só conforme os jogos precisarem. Não troca o driver de vídeo. A Steam tem o seu próprio cache (shadercache), separado.\n{session}",
            Status = state + " " + sizes,
        };
    }
}

/// <summary>A Proton build named as Steam (config.vdf CompatToolMapping) or umu (compatibilitytools.d folder) names it.</summary>
public sealed record ProtonBuild(string Name, int? Major, bool GeFamily, bool Official);

/// <summary>What the per-game extras depend on. <paramref name="WindowsGame"/>: the game runs with Proton (or may: a Steam game).</summary>
public sealed record ExtrasContext(bool WaylandSession, ProtonBuild? Proton, bool NvidiaProprietaryRenders, bool WindowsGame);

/// <summary>One per-game extra the user turned on: the variables it adds, or why it is left out this time.</summary>
public sealed record ExtraChoice(string Id, string Label, IReadOnlyList<string> Pieces, string? Skipped)
{
    public bool Applies => Skipped is null;
}

public static partial class GameExtras
{
    public const string WaylandId = "wayland", LowLatencyId = "lowlatency";
    public const string WaylandLabel = "Proton nativo no Wayland (experimental)", LowLatencyLabel = "Baixa latência (experimental)";
    public const string WaylandPiece = "PROTON_ENABLE_WAYLAND=1", LowLatencyPiece = "PROTON_ENABLE_NVAPI=1";
    public const int MinWaylandProton = 10;

    [GeneratedRegex(@"^(?:GE-Proton|UMU-Proton-?|Proton-)(\d{1,3})(?:[.\-]|\z)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GeName();
    [GeneratedRegex(@"^proton_(\d{1,3})(?:\z|[._])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OfficialName();

    /// <summary>"GE-Proton10-25", "UMU-Proton-10.0-4", "Proton-9.27-GE" → GE family with the major version; "proton_10", "proton_experimental",
    /// "proton_hotfix" → Valve's official Proton; anything else → unknown build. Null for an empty name.</summary>
    public static ProtonBuild? ParseProton(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        bool ge = name.StartsWith("GE-Proton", StringComparison.OrdinalIgnoreCase) || name.StartsWith("UMU-Proton", StringComparison.OrdinalIgnoreCase) || name.Contains("-GE", StringComparison.OrdinalIgnoreCase);
        if (ge) return new(name, GeName().Match(name) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null, true, false);
        if (name.StartsWith("proton_", StringComparison.OrdinalIgnoreCase))
            return new(name, OfficialName().Match(name) is { Success: true } o ? int.Parse(o.Groups[1].Value, CultureInfo.InvariantCulture) : null, false, true);
        return new(name, null, false, false);
    }

    private const string OnlyWindows = "Só vale para jogos de Windows (rodando com o Proton).";

    private static string? WaylandBlocked(ExtrasContext c)
    {
        if (!c.WindowsGame) return OnlyWindows;
        if (!c.WaylandSession) return "Fora de uso: a sessão atual não é Wayland (é X11). A opção só vale ao entrar numa sessão Wayland.";
        if (c.Proton is not { } p) return "Fora de uso: não deu para saber qual Proton este jogo usa. Precisa de GE-Proton ou UMU-Proton 10 ou mais novo.";
        if (p.Official) return $"Fora de uso: o jogo usa o Proton oficial da Valve ({p.Name}), que não documenta essa opção. Escolha GE-Proton 10 ou mais novo em Steam › Propriedades do jogo › Compatibilidade.";
        if (!p.GeFamily || p.Major is null) return $"Fora de uso: não reconheci a versão do Proton ({p.Name}). Precisa de GE-Proton ou UMU-Proton 10 ou mais novo.";
        if (p.Major < MinWaylandProton) return $"Fora de uso: precisa do GE-Proton/UMU-Proton 10 ou mais novo (este jogo usa {p.Name}).";
        return null;
    }

    private static string? LowLatencyBlocked(ExtrasContext c)
    {
        if (!c.WindowsGame) return OnlyWindows;
        if (!c.NvidiaProprietaryRenders) return "Fora de uso: só existe opção documentada para placas NVIDIA com o driver proprietário (NVIDIA Reflex pelo DXVK-NVAPI), e o jogo não roda numa delas. Para AMD e Intel o Proton não tem uma opção equivalente documentada, então nada é ligado.";
        return null;
    }

    /// <summary>
    /// The extras turned on in the profile, each with its variables or the reason it is left out: wrong session/Proton/GPU, or a conflict with
    /// any other launch piece (<paramref name="otherPieces"/>: the profile's own variables, wrappers and arguments, or a recommended combination),
    /// judged by the same table as the Compatibilidade page (<see cref="LaunchOptionTriage.Conflict"/>). Kept pieces never conflict with them.
    /// </summary>
    public static IReadOnlyList<ExtraChoice> Resolve(GameProfile p, ExtrasContext context, IEnumerable<string> otherPieces)
    {
        var others = otherPieces.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
        var result = new List<ExtraChoice>();
        void Add(string id, string label, string piece, string? blocked)
        {
            string? conflict = blocked is null ? others.Select(o => LaunchOptionTriage.Conflict(piece, o)).FirstOrDefault(r => r is not null) : null;
            result.Add(new(id, label, blocked is null && conflict is null ? [piece] : [], blocked ?? (conflict is null ? null : "Fora de uso: " + LowerFirst(conflict))));
        }
        if (p.ProtonWayland) Add(WaylandId, WaylandLabel, WaylandPiece, WaylandBlocked(context));
        if (p.LowLatency) Add(LowLatencyId, LowLatencyLabel, LowLatencyPiece, LowLatencyBlocked(context));
        return result;
    }

    private static string LowerFirst(string s) => s.Length > 0 ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    /// <summary>The kept pieces as environment variables (KEY, VALUE), in order.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Environment(IEnumerable<ExtraChoice> choices) =>
        choices.SelectMany(c => c.Pieces).Select(LaunchOptionTriage.Env).Where(e => e is not null).Select(e => new KeyValuePair<string, string>(e!.Value.Key, e.Value.Value)).ToList();

    /// <summary>Per-option status line for the profile card ("ligado: VAR=1" or the reason it is out).</summary>
    public static string Summary(IReadOnlyList<ExtraChoice> choices) =>
        string.Join("\n", choices.Select(c => c.Applies ? $"{c.Label}: entra como {string.Join(" ", c.Pieces)}." : $"{c.Label}: {c.Skipped}"));

    /// <summary>For the Compatibilidade page: the profile extras that would conflict with a recommended combination (pieces as reported).</summary>
    public static IReadOnlyList<(string Label, string Reason)> AgainstRecommended(GameProfile p, IEnumerable<string> recommended)
    {
        var list = recommended.ToList(); var hits = new List<(string, string)>();
        void Check(bool on, string label, string piece) { if (on && list.Select(r => LaunchOptionTriage.Conflict(piece, r)).FirstOrDefault(r => r is not null) is { } why) hits.Add((label, why)); }
        Check(p.ProtonWayland, WaylandLabel, WaylandPiece);
        Check(p.LowLatency, LowLatencyLabel, LowLatencyPiece);
        return hits;
    }

    /// <summary>Wayland session: XDG_SESSION_TYPE=wayland or a WAYLAND_DISPLAY socket name.</summary>
    public static bool IsWaylandSession(Func<string, string?> env) =>
        string.Equals(env("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(env("WAYLAND_DISPLAY"));
}
