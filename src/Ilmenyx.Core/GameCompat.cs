// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Game compatibility from public sources that allow automated use:
// - ProtonDB summaries (protondb.com) and ProtonDB report exports (github.com/bdefore/protondb-data, ODbL 1.0 / DbCL 1.0);
// - Steam Deck compatibility report from the Steam store (Valve);
// - AreWeAntiCheatYet games.json (MIT).
// SteamDB is not queried: its FAQ forbids scraping and it has no public API; the app only offers a link the user opens.
// Everything here is pure (no I/O): parsing, privacy filtering, hardware matching, suggestions and launch-option safety.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

public sealed record ProtonSummary(string Tier, string? TrendingTier, string? BestReportedTier, string Confidence, double? Score, int Total)
{
    public static string TierName(string? tier) => tier switch
    {
        "platinum" => "Platina", "gold" => "Ouro", "silver" => "Prata", "bronze" => "Bronze", "borked" => "Quebrado", "native" => "Nativo", "pending" => "Pendente", _ => "—",
    };
    public static string TierMeaning(string? tier) => tier switch
    {
        "platinum" => "roda perfeitamente, sem ajustes", "gold" => "roda perfeitamente depois de ajustes", "silver" => "roda com pequenos problemas",
        "bronze" => "roda, mas trava ou tem problemas que atrapalham", "borked" => "não abre ou não dá para jogar", "pending" => "poucos relatos ainda", _ => "",
    };
    public static string ConfidenceName(string? c) => c switch { "strong" => "alta", "good" => "boa", "moderate" => "moderada", "low" => "baixa", "inadequate" => "insuficiente", _ => c ?? "—" };

    /// <summary>protondb.com/api/v1/reports/summaries/{appid}.json. Null when the body is not a summary.</summary>
    public static ProtonSummary? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || Str(r, "tier") is not { } tier) return null;
            double? score = r.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetDouble(out double d) && double.IsFinite(d) ? d : null;
            int total = r.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out int n) && n >= 0 ? n : 0;
            return new(tier, Str(r, "trendingTier"), Str(r, "bestReportedTier"), Str(r, "confidence") ?? "", score, total);
        }
        catch (JsonException) { return null; }
    }

    internal static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 and < 200 } s ? s : null;
}

/// <summary>Steam Deck compatibility from the Steam store: 3 verified, 2 playable, 1 unsupported, 0 unknown.</summary>
public sealed record DeckStatus(int Category, IReadOnlyList<string> Notes)
{
    public string Name => Category switch { 3 => "Verificado", 2 => "Jogável", 1 => "Não suportado", _ => "Desconhecido" };

    private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal)
    {
        ["ControllerGlyphsDoNotMatchDeckDevice"] = "Os botões mostrados no jogo não são os do controle do Deck",
        ["ControllerGlyphsMatchDeckDevice"] = "O jogo mostra os botões do controle do Deck",
        ["InterfaceTextIsLegible"] = "O texto é legível na tela do Deck",
        ["Unsupported_Other"] = "Não funciona no Deck (motivo informado pela Valve: outro)",
        ["UnsupportedAntiCheat"] = "O anti-cheat não é compatível",
        ["DefaultConfigurationIsNotPerformant"] = "Desempenho fraco na configuração padrão",
        ["ExternalControllersNotSupportedLocalMultiplayer"] = "Controles externos não funcionam no multijogador local",
        ["InterfaceTextIsNotLegible"] = "Parte do texto é pequena demais para ler na tela do Deck",
        ["DefaultControllerConfigFullyFunctional"] = "O controle funciona na configuração padrão",
        ["DefaultConfigurationIsPerformant"] = "Bom desempenho na configuração padrão",
        ["GameStartupFunctional"] = "O jogo abre corretamente",
        ["ExternalControllersNotSupportedPrimaryPlayer"] = "Controles externos não funcionam para o jogador principal",
        ["TextInputDoesNotAutomaticallyInvokesKeyboard"] = "O teclado virtual não abre sozinho em campos de texto",
        ["LauncherInteractionIssues"] = "O launcher do jogo exige toque ou mouse",
        ["RequiresManualKeyboardInvocation"] = "É preciso abrir o teclado virtual manualmente",
        ["SingleplayerGameplayFunctional"] = "O modo para um jogador funciona",
        ["MultiplayerGameplayFunctional"] = "O multijogador funciona",
        ["AntiCheatUnsupported"] = "O anti-cheat não é compatível",
        ["UnsupportedAntiCheat_Other"] = "O anti-cheat não é compatível",
    };

    public static DeckStatus? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("results", out var res) || res.ValueKind != JsonValueKind.Object) return null;
            int cat = res.TryGetProperty("resolved_category", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int v) && v is >= 0 and <= 3 ? v : 0;
            var notes = new List<string>();
            if (res.TryGetProperty("resolved_items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray().Take(16))
                    if (ProtonSummary.Str(item, "loc_token") is { } token)
                    {
                        string key = Regex.Replace(token, @"^#?[A-Za-z]+_TestResult_", "");
                        notes.Add(Known.TryGetValue(key, out var pt) ? pt : Humanize(key));
                    }
            return new(cat, notes);
        }
        catch (JsonException) { return null; }
    }

    private static string Humanize(string camel) => Regex.Replace(camel, "(?<=[a-z])(?=[A-Z])", " ");
}

/// <summary>One AreWeAntiCheatYet entry (MIT data). Status: Supported, Running, Planned, Broken, Denied.</summary>
public sealed record AntiCheatInfo(string Name, string Status, IReadOnlyList<string> AntiCheats, bool Native, string? LastUpdate, string? LastUpdateDate)
{
    public string StatusName => Status switch
    {
        "Supported" => "Suportado — o anti-cheat foi liberado para Linux/Proton",
        "Running" => "Roda — funciona, sem confirmação oficial do suporte",
        "Planned" => "Planejado — o estúdio disse que vai liberar",
        "Broken" => "Quebrado — o anti-cheat impede o jogo online no Linux",
        "Denied" => "Negado — o estúdio bloqueia o Linux de propósito",
        _ => Status,
    };
    public bool BlocksOnline => Status is "Broken" or "Denied";

    /// <summary>Index by Steam app id. Entries without a Steam id are skipped (not matched by name to avoid wrong games).</summary>
    public static Dictionary<string, AntiCheatInfo> ParseAll(string json)
    {
        var map = new Dictionary<string, AntiCheatInfo>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;
            foreach (var g in doc.RootElement.EnumerateArray().Take(20_000))
            {
                if (g.ValueKind != JsonValueKind.Object || !g.TryGetProperty("storeIds", out var ids) || ids.ValueKind != JsonValueKind.Object
                    || ProtonSummary.Str(ids, "steam") is not { } steam || !steam.All(char.IsAsciiDigit) || ProtonSummary.Str(g, "status") is not { } status) continue;
                var acs = g.TryGetProperty("anticheats", out var a) && a.ValueKind == JsonValueKind.Array
                    ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length is > 0 and < 60).Take(8).ToList() : [];
                string? upd = null, date = null;
                if (g.TryGetProperty("updates", out var u) && u.ValueKind == JsonValueKind.Array && u.GetArrayLength() > 0 && u[u.GetArrayLength() - 1] is { ValueKind: JsonValueKind.Object } last)
                { upd = CompatText.Clean(ProtonSummary.Str(last, "name"), 300); date = ProtonSummary.Str(last, "date"); }
                bool native = g.TryGetProperty("native", out var nv) && nv.ValueKind == JsonValueKind.True;
                map[steam] = new(CompatText.Clean(ProtonSummary.Str(g, "name"), 120) ?? steam, status, acs, native, upd, date);
            }
        }
        catch (JsonException) { }
        return map;
    }
}

/// <summary>Compact, privacy-filtered ProtonDB report as stored in the local index (one JSON line each).</summary>
public sealed record CompatReport
{
    [JsonPropertyName("a")] public string AppId { get; init; } = "";
    [JsonPropertyName("n")] public string? Title { get; init; }
    [JsonPropertyName("t")] public long Timestamp { get; init; }
    [JsonPropertyName("v")] public string? Verdict { get; init; }
    [JsonPropertyName("vo")] public string? VerdictOutOfBox { get; init; }
    [JsonPropertyName("pv")] public string? ProtonVersion { get; init; }
    [JsonPropertyName("cp")] public string? CustomProton { get; init; }
    [JsonPropertyName("lo")] public string? LaunchOptions { get; init; }
    [JsonPropertyName("f")] public List<string> Faults { get; init; } = [];
    [JsonPropertyName("fn")] public Dictionary<string, string> FaultNotes { get; init; } = [];
    [JsonPropertyName("vn")] public string? VerdictNote { get; init; }
    [JsonPropertyName("cn")] public string? ConcludingNotes { get; init; }
    [JsonPropertyName("g")] public string? Gpu { get; init; }
    [JsonPropertyName("d")] public string? GpuDriver { get; init; }
    [JsonPropertyName("o")] public string? Os { get; init; }
    [JsonPropertyName("k")] public string? Kernel { get; init; }
    [JsonPropertyName("c")] public string? Cpu { get; init; }
    [JsonPropertyName("r")] public string? Ram { get; init; }

    public bool Works => Verdict == "yes";
    public DateTimeOffset Date => DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(Timestamp, 0, 4_102_444_800));
    public string Proton => CustomProton ?? ProtonVersion ?? "";

    public static readonly string[] FaultKeys = ["stabilityFaults", "performanceFaults", "graphicalFaults", "audioFaults", "inputFaults", "windowingFaults", "saveGameFaults", "significantBugs"];
    public static string FaultName(string key) => key switch
    {
        "stabilityFaults" => "Travamentos / fecha sozinho", "performanceFaults" => "Desempenho", "graphicalFaults" => "Gráficos", "audioFaults" => "Áudio",
        "inputFaults" => "Controles / entrada", "windowingFaults" => "Janela / tela cheia", "saveGameFaults" => "Saves", "significantBugs" => "Bugs graves", _ => key,
    };

    /// <summary>One element of reports_piiremoved.json → compact report; null when it has no Steam app id. Free text is length-limited and home paths are hidden.</summary>
    public static CompatReport? FromDump(JsonElement x)
    {
        // appId is a string in most reports and a number in older ones (30 859 of 400 906 in the November 2026 export).
        if (x.ValueKind != JsonValueKind.Object || !x.TryGetProperty("app", out var app) || !app.TryGetProperty("steam", out var steam) || !steam.TryGetProperty("appId", out var idEl)) return null;
        string? id = idEl.ValueKind switch { JsonValueKind.String => idEl.GetString(), JsonValueKind.Number when idEl.TryGetUInt64(out ulong num) => num.ToString(CultureInfo.InvariantCulture), _ => null };
        if (id is null || id.Length is 0 or > 12 || !id.All(char.IsAsciiDigit)) return null;
        x.TryGetProperty("responses", out var r); x.TryGetProperty("systemInfo", out var s);
        string? R(string k, int max = 200) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? CompatText.Clean(v.GetString(), max) : null;
        string? S(string k) => s.ValueKind == JsonValueKind.Object && s.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? CompatText.Clean(v.GetString(), 200) : null;
        var faults = FaultKeys.Where(k => R(k) == "yes").ToList();
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);
        string? verdictNote = null;
        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.Object)
            foreach (var p in n.EnumerateObject().Take(16))
                if (p.Value.ValueKind == JsonValueKind.String && CompatText.Clean(p.Value.GetString(), 1200) is { } text)
                {
                    if (p.Name == "verdict") verdictNote = text; else if (FaultKeys.Contains(p.Name)) notes[p.Name] = text;
                }
        long ts = x.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out long l) ? l : 0;
        return new CompatReport
        {
            AppId = id, Title = CompatText.Clean(ProtonSummary.Str(app, "title"), 160), Timestamp = ts, Verdict = R("verdict", 8), VerdictOutOfBox = R("verdictOob", 8),
            ProtonVersion = R("protonVersion", 60), CustomProton = R("customProtonVersion", 60), LaunchOptions = R("launchOptions", 600), Faults = faults, FaultNotes = notes,
            VerdictNote = verdictNote, ConcludingNotes = R("concludingNotes", 1500), Gpu = S("gpu"), GpuDriver = S("gpuDriver"), Os = S("os"), Kernel = S("kernel"), Cpu = S("cpu"), Ram = S("ram"),
        };
    }
}

public static class CompatText
{
    private static readonly Regex Home = new(@"/home/[^/\s""']+|/Users/[^/\s""']+|/var/home/[^/\s""']+", RegexOptions.CultureInvariant);

    /// <summary>Plain text for display: control characters removed (except new lines), home folders shown as ~, bounded length. Never interpreted as markup.</summary>
    public static string? Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var sb = new StringBuilder(Math.Min(text.Length, max + 1));
        foreach (char c in text) { if (sb.Length > max) break; if (!char.IsControl(c) || c == '\n') sb.Append(c); }
        string s = Home.Replace(sb.ToString(), "~").Trim();
        if (s.Length > max) s = s[..max].TrimEnd() + "…";
        return s.Length == 0 ? null : s;
    }
}

/// <summary>The user's machine, as far as matching reports goes.</summary>
public sealed record CompatHardware(string GpuText, string? Distro, bool NvidiaProprietary)
{
    public string Vendor => CompatMatcher.VendorOf(GpuText);
}

public sealed record RankedReport(CompatReport Report, int Score, IReadOnlyList<string> Matches);
public sealed record CompatSuggestion(string Option, int Count, int Of);

public static class CompatMatcher
{
    private static readonly Regex Model = new(
        @"\b(?:rtx|gtx|gt|mx|quadro\s*[a-z]?)\s*-?\s*\d{3,4}\s*(?:ti|super)?\b|\brx\s*-?\s*\d{3,4}\s*(?:xtx|xt|gre|m|s)?\b|\bradeon\s+(?:pro\s+)?\d{3}m\b|\b\d{3}m\b(?=.*radeon)|\barc\s*[abc]\d{3}m?\b|\bvega\s*\d{1,2}\b|\biris\s*xe\b|\buhd\s*(?:graphics\s*)?\d{3}\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Codename = new(
        @"\b(?:navi\s?\d{2}|gfx\d{3,4}|polaris\s?\d{2}|vega\d{0,2}|vangogh|van gogh|mendocino|raphael|rembrandt|phoenix\d?|strix\d?|hawk ?point|renoir|cezanne|lucienne|picasso|raven\d?|barcelo|tigerlake|alderlake|meteorlake|lunarlake|battlemage|alchemist|turing|ampere|ada|blackwell|pascal)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly string[] UbuntuFamily = ["ubuntu", "mint", "pop", "zorin", "elementary", "kubuntu", "xubuntu", "neon", "debian", "pikaos"];
    private static readonly string[] ArchFamily = ["arch", "manjaro", "endeavouros", "cachyos", "garuda", "steamos", "artix", "chimeraos"];
    private static readonly string[] FedoraFamily = ["fedora", "nobara", "bazzite", "ultramarine", "rhel", "centos"];

    public static string VendorOf(string? gpu)
    {
        string g = (gpu ?? "").ToLowerInvariant();
        return g.Contains("nvidia") || g.Contains("geforce") || g.Contains("nouveau") ? "nvidia" : g.Contains("amd") || g.Contains("radeon") || g.Contains("radv") || g.Contains("ati ") ? "amd" : g.Contains("intel") ? "intel" : "";
    }

    /// <summary>Normalised GPU model tokens ("rtx 3060 ti", "rx 7900 xt", "610m") and chip codenames ("navi31", "mendocino").</summary>
    public static (HashSet<string> Models, HashSet<string> Codenames) Tokens(string? gpu)
    {
        string g = (gpu ?? "").ToLowerInvariant().Replace('_', ' ');
        static string N(string s) => Regex.Replace(s.Replace("-", " "), @"\s+", " ").Trim().Replace("radeon ", "").Replace("pro ", "").Replace("graphics ", "");
        var models = Model.Matches(g).Select(m => N(m.Value)).ToHashSet();
        foreach (var m in models.ToList()) if (Regex.Match(m, @"^(rtx|gtx|rx) (\d{3,4})") is { Success: true } b) models.Add("base:" + b.Groups[1].Value + " " + b.Groups[2].Value);
        var codes = Codename.Matches(g).Select(m => m.Value.Replace(" ", "")).ToHashSet();
        return (models, codes);
    }

    /// <summary>Same GPU model, series or chip codename (used to keep older reports of this machine's GPU when the index is built).</summary>
    public static bool SameGpu(CompatHardware hw, string? reportGpu)
    {
        var (mine, mineCodes) = Tokens(hw.GpuText); var (theirs, theirCodes) = Tokens(reportGpu);
        return mine.Overlaps(theirs) || mineCodes.Overlaps(theirCodes);
    }

    public static string DistroFamily(string? os)
    {
        string o = (os ?? "").ToLowerInvariant();
        string? first = Regex.Matches(o, "[a-z]+").Select(m => m.Value).FirstOrDefault(w => w is not "linux" and not "gnu");
        if (first is null) return "";
        return UbuntuFamily.Contains(first) ? "ubuntu" : ArchFamily.Contains(first) ? "arch" : FedoraFamily.Contains(first) ? "fedora" : first;
    }

    private static string? DistroName(string? os) => Regex.Matches((os ?? "").ToLowerInvariant(), "[a-z]+").Select(m => m.Value).FirstOrDefault(w => w is not "linux" and not "gnu");

    /// <summary>
    /// Similarity of a report to this machine: same exact GPU model (+5) or same series (+3), same chip codename (+3), same vendor (+2),
    /// same driver kind (Mesa or NVIDIA proprietary, +1), same distro (+2) or distro family (+1), reported in the last 12 months (+1).
    /// </summary>
    public static RankedReport Rank(CompatReport r, CompatHardware hw, DateTimeOffset now)
    {
        var matches = new List<string>(); int score = 0;
        var (mine, mineCodes) = Tokens(hw.GpuText); var (theirs, theirCodes) = Tokens(r.Gpu);
        if (mine.Where(t => !t.StartsWith("base:", StringComparison.Ordinal)).Intersect(theirs).Any()) { score += 5; matches.Add("mesma GPU"); }
        else if (mine.Where(t => t.StartsWith("base:", StringComparison.Ordinal)).Intersect(theirs).Any()) { score += 3; matches.Add("GPU da mesma série"); }
        if (mineCodes.Intersect(theirCodes).Any()) { score += 3; if (!matches.Contains("mesma GPU")) matches.Add("mesmo chip"); }
        if (hw.Vendor.Length > 0 && hw.Vendor == VendorOf(r.Gpu)) { score += 2; if (matches.Count == 0) matches.Add("mesmo fabricante de GPU"); }
        string driver = (r.GpuDriver ?? "").ToLowerInvariant();
        bool theirNvidiaProp = driver.Contains("nvidia") && !driver.Contains("mesa");
        if (driver.Length > 0 && theirNvidiaProp == hw.NvidiaProprietary) score += 1;
        if (hw.Distro is { Length: > 0 } d)
        {
            if (DistroName(d) is { } dn && dn == DistroName(r.Os)) { score += 2; matches.Add("mesma distro"); }
            else if (DistroFamily(d) is { Length: > 0 } fam && fam == DistroFamily(r.Os)) { score += 1; matches.Add("distro da mesma família"); }
        }
        if (now - r.Date < TimeSpan.FromDays(365)) score += 1;
        return new(r, score, matches);
    }

    /// <summary>Most similar first, then newest. Reports that only share nothing but recency are kept at the end.</summary>
    public static List<RankedReport> RankAll(IEnumerable<CompatReport> reports, CompatHardware hw, DateTimeOffset now) =>
        reports.Select(r => Rank(r, hw, now)).OrderByDescending(x => x.Score).ThenByDescending(x => x.Report.Timestamp).ToList();

    /// <summary>
    /// Launch-option pieces (ENV=value, wrappers) and Proton versions used by people with similar hardware whose game worked.
    /// Only reports with at least the same GPU vendor count; each report counts once per option.
    /// </summary>
    public static (List<CompatSuggestion> Options, List<CompatSuggestion> Protons) Suggest(IReadOnlyList<RankedReport> ranked, int take = 6)
    {
        var similar = ranked.Where(x => x.Score >= 3 && x.Report.Works).Take(40).ToList();
        var options = new Dictionary<string, int>(StringComparer.Ordinal); var protons = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in similar)
        {
            foreach (var piece in LaunchOptionSafety.Pieces(x.Report.LaunchOptions).Distinct()) options[piece] = options.GetValueOrDefault(piece) + 1;
            if (x.Report.Proton is { Length: > 0 } p) protons[p] = protons.GetValueOrDefault(p) + 1;
        }
        List<CompatSuggestion> Top(Dictionary<string, int> d) => d.Where(kv => kv.Value >= 2 || similar.Count < 5).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(take).Select(kv => new CompatSuggestion(kv.Key, kv.Value, similar.Count)).ToList();
        return (Top(options), Top(protons));
    }
}

/// <summary>
/// Launch options come from anonymous reports. Applying one to Steam runs it as a shell command line, so only a strict subset is accepted:
/// ENV=value assignments, a few well-known wrappers with their flags, %command% and game arguments after it. Anything else stays read-only.
/// </summary>
public static class LaunchOptionSafety
{
    private static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal) { "gamemoderun", "mangohud", "gamescope", "prime-run", "pvkrun", "dxvk-hud", "obs-gamecapture", "strangle", "env" };
    private static readonly Regex Env = new(@"^[A-Z_][A-Z0-9_]{0,63}=[A-Za-z0-9_.,:+=/@%\-]{0,200}$", RegexOptions.CultureInvariant);
    private static readonly Regex Flag = new(@"^-{1,2}[A-Za-z0-9][A-Za-z0-9_\-]{0,40}(=[A-Za-z0-9_.,:x+\-]{0,40})?$|^[0-9]{1,5}(x[0-9]{1,5})?$|^--$", RegexOptions.CultureInvariant);
    private static readonly Regex GameArg = new(@"^(?:--?|\+)?[A-Za-z0-9_][A-Za-z0-9_.,:=+\-]{0,60}$", RegexOptions.CultureInvariant);

    private static readonly char[] ShellCharacters = [';', '|', '&', '`', '$', '>', '<', '(', ')', '{', '}', '\\', '"', '\'', '\n', '~', '*', '?', '!', '#'];

    public sealed record Verdict(bool Applicable, string? Reason);

    public static Verdict Check(string? options)
    {
        string o = (options ?? "").Trim();
        if (o.Length == 0) return new(false, "Sem opções de inicialização.");
        if (o.Length > 400) return new(false, "Longa demais para aplicar com segurança.");
        if (o.IndexOfAny(ShellCharacters) >= 0)
            return new(false, "Tem caracteres de shell (; | & $ aspas…): pode executar outros comandos. Só para leitura.");
        var tokens = o.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int cmd = Array.IndexOf(tokens, "%command%");
        // Without %command% Steam appends the whole text to the game's own arguments: accepted only when every token is a game argument.
        if (cmd < 0)
            return tokens.All(GameArg.IsMatch) ? new(true, null) : new(false, "Sem %command% e com algo que não é argumento do jogo. Só para leitura.");
        if (tokens.Count(t => t == "%command%") > 1) return new(false, "Tem %command% mais de uma vez.");
        bool afterWrapper = false;
        for (int i = 0; i < cmd; i++)
        {
            string t = tokens[i];
            if (Env.IsMatch(t) && !afterWrapper) continue;
            if (Wrappers.Contains(t)) { afterWrapper = true; continue; }
            if (afterWrapper && (Flag.IsMatch(t) || Env.IsMatch(t))) continue;
            return new(false, $"\"{CompatText.Clean(t, 40)}\" não é variável de ambiente nem programa conhecido (gamemoderun, mangohud, gamescope…). Só para leitura.");
        }
        for (int i = cmd + 1; i < tokens.Length; i++)
            if (!GameArg.IsMatch(tokens[i])) return new(false, $"Argumento do jogo \"{CompatText.Clean(tokens[i], 40)}\" fora do formato aceito. Só para leitura.");
        return new(true, null);
    }

    /// <summary>Reusable pieces for the "most used" list: ENV=value assignments and wrapper names before %command%, plus game flags after it.</summary>
    public static IEnumerable<string> Pieces(string? options)
    {
        if (string.IsNullOrWhiteSpace(options) || Check(options) is { Applicable: false }) yield break;
        var tokens = options.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int cmd = Array.IndexOf(tokens, "%command%"); // -1: only game arguments, all yielded below
        for (int i = 0; i < tokens.Length; i++)
        {
            string t = tokens[i];
            if (i < cmd && Env.IsMatch(t)) yield return t;
            else if (i < cmd && Wrappers.Contains(t) && t != "env") yield return t;
            else if (i > cmd) yield return t;
        }
    }
}
