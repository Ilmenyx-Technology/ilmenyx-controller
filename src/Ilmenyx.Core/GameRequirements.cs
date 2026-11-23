// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Method and limits: docs/meu-pc-roda.md.
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Ilmenyx.Core;

/// <summary>
/// One block of published system requirements (minimum or recommended) turned into comparable facts. Every field is null/empty when the
/// text does not say it or the parser did not recognise it; <see cref="Unrecognised"/> keeps the lines nothing could be made of.
/// </summary>
public sealed record GameRequirement(string Text, string? Os, IReadOnlyList<string> CpuAlternatives, double? RamGb, IReadOnlyList<string> GpuAlternatives,
    double? VramGb, int? DirectX, string? Vulkan, string? OpenGl, double? StorageGb, IReadOnlyList<string> Unrecognised)
{
    public static readonly GameRequirement Empty = new("", null, [], null, [], null, null, null, null, null, []);
    public bool IsEmpty => Text.Length == 0;
}

/// <summary>Requirements Steam publishes for one app (Windows list and, when the developer gave one, the native Linux list).</summary>
public sealed record GameRequirements(string AppId, string Name, DateTimeOffset FetchedAt, string PcMinimum, string PcRecommended, string LinuxMinimum, string LinuxRecommended)
{
    public bool HasLinux => LinuxMinimum.Length > 0 || LinuxRecommended.Length > 0;
    public bool HasAny => PcMinimum.Length > 0 || PcRecommended.Length > 0 || HasLinux;
}

/// <summary>Pure parser for requirement texts (Steam HTML, pt-BR and English). The HTML is never rendered: it becomes plain text lines.</summary>
public static class GameRequirementsParser
{
    private static readonly Regex Br = new(@"<\s*br\s*/?\s*>|</\s*(li|p|div|ul|tr)\s*>|<\s*(li|p|div|tr)(\s[^>]*)?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Tag = new(@"<[^>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex Header = new(@"^(m[ií]nim[oa]s?|minimum|recomend\w*|recommended|requisitos[\w\s]*)\s*:?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Split = new(@"\s+(?:or|ou|and/or)\s+|\s*/\s*|\s*;\s*|,\s+(?=(?:NVIDIA|AMD|Intel|GeForce|Radeon|Ryzen|Core|GTX|RTX|RX|Arc|Iris|Pentium|Athlon|FX|Xeon|Celeron)\b)|\s+\|\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Size = new(@"(?<![\w.,])(\d{1,5}(?:[.,]\d{1,2})?)\s*(TB|GB|MB|GiB|MiB)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DirectXRx = new(@"Direct\s?X\s*(?:vers[aã]o|version|ver\.?)?\s*:?\s*(\d{1,2})(?:\.\d)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex VulkanRx = new(@"Vulkan\s*(?:API\s*)?(?:vers[aã]o|version|v)?\s*(\d\.\d)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OpenGlRx = new(@"OpenGL\s*(?:vers[aã]o|version|v)?\s*(\d(?:\.\d)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LooseRam = new(@"(\d{1,3}(?:[.,]\d)?)\s*(GB|MB)\s*(?:de\s+)?(?:RAM|mem[oó]ria|memory)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LooseStorage = new(@"(\d{1,4}(?:[.,]\d)?)\s*(GB|MB|TB)\s*(?:de\s+|of\s+)?(?:available\s+|free\s+|dispon[ií]ve\w+\s+|livres?\s+)?(?:space|storage|disk|hard|espa[cç]o|armazenamento)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Plain-text lines of a requirements block: tags dropped, entities decoded, the "Minimum:" title line removed.</summary>
    public static IReadOnlyList<string> Lines(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];
        string s = Tag.Replace(Br.Replace(html, "\n"), "");
        s = WebUtility.HtmlDecode(s).Replace(' ', ' ').Replace('\r', '\n');
        var lines = new List<string>();
        foreach (var raw in s.Split('\n'))
        {
            string l = string.Join(' ', raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
            if (l.Length == 0 || Header.IsMatch(l)) continue;
            lines.Add(l.Length > 400 ? l[..400] : l);
        }
        return lines.Take(40).ToList();
    }

    private enum Kind { Os, Cpu, Ram, Gpu, DirectX, Storage, Other }

    private static string Fold(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Normalize(NormalizationForm.FormD)) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Replace("*", "").Trim();
    }

    private static Kind Classify(string label) => Fold(label) switch
    {
        "os" or "so" or "sistema operacional" or "sistema operativo" or "operating system" => Kind.Os,
        "processor" or "processador" or "cpu" or "procesador" => Kind.Cpu,
        "memory" or "memoria" or "ram" or "memoria ram" or "system memory" or "memoria do sistema" => Kind.Ram,
        "graphics" or "placa de video" or "placa grafica" or "video" or "gpu" or "graficos" or "grafico" or "video card" or "placa de video/grafica" or "graphics card" => Kind.Gpu,
        "directx" => Kind.DirectX,
        "storage" or "armazenamento" or "hard drive" or "hard disk space" or "disk space" or "espaco em disco" or "espaco no disco" or "espaco disponivel" or "espaco livre"
            or "disco rigido" or "hd" or "espaco de armazenamento" or "almacenamiento" => Kind.Storage,
        _ => Kind.Other,
    };

    /// <summary>Largest-first reading of "8 GB", "4096 MB", "1,5 GB", "1 TB" as GB (decimal comma accepted); null when none.</summary>
    public static double? ToGb(string value, bool smallest = false)
    {
        var all = Size.Matches(value).Select(m => Gb(m.Groups[1].Value, m.Groups[2].Value)).Where(v => v is not null).Select(v => v!.Value).ToList();
        return all.Count == 0 ? null : smallest ? all.Min() : all[0];
    }

    private static double? Gb(string num, string unit)
    {
        if (!double.TryParse(num.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v <= 0) return null;
        return unit.ToUpperInvariant() switch { "TB" => v * 1024, "MB" or "MIB" => v / 1024, _ => v };
    }

    private static readonly Regex VendorOr = new(@"\b(Intel|AMD)\s+(?:or|ou|/)\s+(Intel|AMD)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CoresThreads = new(@"(\d+)\s*cores?\s*(?:/|and|e|,)\s*(\d+)\s*threads?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private const string Tail = @"(?:better|higher|faster|superior|melhor|equivalent[e]?|equal|similar|above|up|more|\+)";
    private static readonly Regex TailNoise = new(@"(?:\s+(?:or|ou|and|e)\s+|\s+)?" + Tail + @"\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OnlyNoise = new(@"^(?:" + Tail + @"|equivalent\s+or\s+better|or\s+" + Tail + @")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Splits a processor/graphics line into the alternatives the game accepts ("A or B / C"). Trademark signs, label leftovers (":") and
    /// "/ better", "or equivalent", "ou superior" tails are dropped; "Intel or AMD" without a model and "4 cores / 8 threads" stay one alternative.
    /// </summary>
    public static IReadOnlyList<string> Alternatives(string value)
    {
        string v = Regex.Replace(value.Replace("®", "").Replace("™", "").Replace("©", ""), @"\((?:R|TM)\)", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        v = VendorOr.Replace(v.Trim(' ', ':', ';', '-', '–', '.', ','), "$1 & $2");
        v = CoresThreads.Replace(v, "$1 cores $2 threads");
        var list = new List<string>();
        foreach (var part in Split.Split(v))
        {
            string x = part.Trim(' ', '.', ',', ':', ';', '-', '–');
            if (x.Length < 3 || OnlyNoise.IsMatch(x)) continue;
            x = TailNoise.Replace(x, "").Trim(' ', '.', ',', ':', ';', '-', '–');
            if (x.Length >= 3) list.Add(x);
        }
        return list.Take(8).ToList();
    }

    public static GameRequirement Parse(string html)
    {
        var lines = Lines(html);
        if (lines.Count == 0) return GameRequirement.Empty;
        string? os = null, vulkan = null, opengl = null; int? dx = null; double? ram = null, vram = null, storage = null;
        var cpu = new List<string>(); var gpu = new List<string>(); var unrec = new List<string>();
        foreach (var line in lines)
        {
            int colon = line.IndexOf(':');
            var kind = colon is > 0 and <= 40 ? Classify(line[..colon]) : Kind.Other;
            string value = kind == Kind.Other ? line : line[(colon + 1)..].Trim(' ', ':', ';');
            bool used = true;
            switch (kind)
            {
                case Kind.Os: os ??= value; break;
                case Kind.Cpu: cpu.AddRange(Alternatives(value)); break;
                case Kind.Ram: ram ??= ToGb(value); used = ram is not null; break;
                case Kind.Gpu:
                    gpu.AddRange(Alternatives(StripMemory(value)));
                    vram ??= ToGb(value, smallest: true);
                    break;
                case Kind.DirectX: dx ??= DirectXRx.Match("DirectX " + value) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null; used = dx is not null; break;
                case Kind.Storage: storage ??= ToGb(value); used = storage is not null; break;
                default: used = false; break;
            }
            // API names can sit in any line ("Graphics: ... DirectX 11 compatible", "Additional Notes: Vulkan 1.3"); the line still counts as read only if its own label was.
            if (dx is null && DirectXRx.Match(line) is { Success: true } d) { dx = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture); }
            if (vulkan is null && VulkanRx.Match(line) is { Success: true } v) { vulkan = v.Groups[1].Success ? v.Groups[1].Value : "?"; }
            if (opengl is null && OpenGlRx.Match(line) is { Success: true } g) { opengl = g.Groups[1].Value; }
            if (!used) unrec.Add(line);
        }
        if (ram is null && LooseRam.Match(string.Join(' ', lines)) is { Success: true } lr) { ram = Gb(lr.Groups[1].Value, lr.Groups[2].Value); unrec.RemoveAll(u => LooseRam.Match(u) is { Success: true } x && u.Length <= x.Length + 12); }
        if (storage is null && LooseStorage.Match(string.Join(' ', lines)) is { Success: true } ls) { storage = Gb(ls.Groups[1].Value, ls.Groups[2].Value); unrec.RemoveAll(u => LooseStorage.Match(u) is { Success: true } x && u.Length <= x.Length + 12); }
        return new(string.Join('\n', lines), os, cpu, ram, gpu, vram, dx, vulkan, opengl, storage, unrec);
    }

    /// <summary>"GeForce GTX 1060 6GB" → the model without the memory size, so only the name is looked up.</summary>
    private static string StripMemory(string v) => Regex.Replace(v, @"\(?\s*\d{1,3}\s*(GB|MB)(\s*(VRAM|de\s+VRAM|video\s+memory|mem[oó]ria(\s+de\s+v[ií]deo)?))?\s*\)?", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Steam appdetails JSON → requirements; null when the app does not exist (success=false) or the JSON is not the expected shape.</summary>
    public static GameRequirements? ParseAppDetails(string json, string appId, DateTimeOffset now)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(appId, out var app) || app.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!app.TryGetProperty("success", out var ok) || ok.ValueKind != System.Text.Json.JsonValueKind.True) return null;
            if (!app.TryGetProperty("data", out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            string Get(string list, string tier) => data.TryGetProperty(list, out var o) && o.ValueKind == System.Text.Json.JsonValueKind.Object
                && o.TryGetProperty(tier, out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String ? t.GetString() ?? "" : "";
            string name = data.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() ?? "" : "";
            return new(appId, name.Length > 200 ? name[..200] : name, now, Get("pc_requirements", "minimum"), Get("pc_requirements", "recommended"),
                Get("linux_requirements", "minimum"), Get("linux_requirements", "recommended"));
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>App id from a number or a store link (store.steampowered.com/app/ID/...); null for anything else.</summary>
    public static string? AppIdFrom(string input)
    {
        input = input.Trim();
        if (input.Length is > 0 and <= 10 && input.All(char.IsAsciiDigit)) return input.TrimStart('0').Length == 0 ? null : input.TrimStart('0');
        var m = Regex.Match(input, @"^(?:https?://)?(?:store\.steampowered\.com|steamcommunity\.com)/app/(\d{1,10})(?:[/?#]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success ? m.Groups[1].Value : null;
    }
}
