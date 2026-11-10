// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Ferramenta de tradução (docs/idiomas.md):
//   wrap <arquivos ou pastas>   marca com L.T(...) os textos visíveis ainda sem marca e lista os que ficaram de fora
//   check                       compara os textos marcados no código com src/Ilmenyx.Core/Locales/*.json (sai com 1 se houver erro)
//   pending <idioma> <saída>    grava os textos que ainda faltam traduzir em <idioma>, um por linha (JSON)
//   merge <idioma> <arquivo>    junta traduções ({"texto pt": "tradução"}) ao catálogo do idioma
//   prune                       tira dos catálogos os textos que o código não usa mais
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ilmenyx.I18n;

internal static partial class Program
{
    private static readonly string[] Sources = ["src/Ilmenyx.Core", "src/Ilmenyx.Platform.Linux", "src/Ilmenyx.Gpu.Vulkan", "src/Ilmenyx.Desktop", "src/Ilmenyx.Cli"];
    private const string LocaleDir = "src/Ilmenyx.Core/Locales";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static int Main(string[] args)
    {
        if (!File.Exists("Ilmenyx.slnx")) { Console.Error.WriteLine("Rode na raiz do repositório."); return 2; }
        switch (args.FirstOrDefault())
        {
            case "wrap": return Wrap(args.Length > 1 ? args[1..] : Sources);
            case "check": return Check();
            case "prune": return Prune();
            case "missed": Vocabulary.Build(); Wrapper.Missed = []; Wrap(Sources, dry: true); File.WriteAllLines("i18n-missed.txt", Wrapper.Missed.Distinct().Order()); return 0;
            case "pending" when args.Length == 3: return Pending(args[1], args[2]);
            case "merge" when args.Length == 3: return Merge(args[1], args[2]);
            default: Console.Error.WriteLine("Uso: wrap [caminhos] | check | prune | pending <idioma> <saída> | merge <idioma> <arquivo>"); return 2;
        }
    }

    private static IEnumerable<string> Files(IEnumerable<string> paths) => paths.SelectMany(p => File.Exists(p) ? [p]
        : Directory.EnumerateFiles(p, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !Skipped.Contains(Path.GetFileName(f))))
        .Order(StringComparer.Ordinal);

    /// <summary>Files with their own language scheme (HelpContent) or no interface text.</summary>
    private static readonly HashSet<string> Skipped = ["HelpContent.PtBr.cs", "Localization.cs", "Icons.cs", "AssemblyInfo.cs"];

    private static int Wrap(IEnumerable<string> paths, bool dry = false)
    {
        var report = new List<string>(); int wrapped = 0, files = 0;
        Vocabulary.Build();
        foreach (var file in Files(paths))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
            var rewriter = new Wrapper(file, report);
            var root = (CompilationUnitSyntax)rewriter.Visit(tree.GetRoot());
            if (rewriter.Count == 0) continue;
            root = AddUsing(root);
            if (!dry) File.WriteAllText(file, root.ToFullString());
            wrapped += rewriter.Count; files++;
        }
        File.WriteAllLines("i18n-wrap-report.tsv", report);
        Console.WriteLine($"{wrapped} textos marcados em {files} arquivos; {report.Count} deixados de fora (i18n-wrap-report.tsv).");
        return 0;
    }

    private static CompilationUnitSyntax AddUsing(CompilationUnitSyntax root)
    {
        string? ns = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        if (ns is not null && (ns == "Ilmenyx.Core" || ns.StartsWith("Ilmenyx.Core.", StringComparison.Ordinal))) return root;
        if (root.Usings.Any(u => u.Name?.ToString() == "Ilmenyx.Core" && u.Alias is null && u.StaticKeyword.IsKind(SyntaxKind.None))) return root;
        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Ilmenyx.Core")).NormalizeWhitespace().WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));
        if (root.Usings.Count == 0)
        {
            // After the license header: the first token keeps its comments, the using goes before the namespace.
            var first = root.Members.First();
            var lead = first.GetLeadingTrivia();
            return root.ReplaceNode(first, first.WithLeadingTrivia(SyntaxFactory.TriviaList())).WithUsings(SyntaxFactory.List([directive.WithLeadingTrivia(lead)]));
        }
        return root.AddUsings(directive);
    }

    /// <summary>Key of a text marked with L.T: the literal itself, or the interpolated text with {0}, {1}… for the values.</summary>
    internal static string? KeyOf(ExpressionSyntax e) => e switch
    {
        LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression) => l.Token.ValueText,
        InterpolatedStringExpressionSyntax s => TemplateKey(s),
        _ => null,
    };

    private static string TemplateKey(InterpolatedStringExpressionSyntax s)
    {
        var sb = new StringBuilder(); int n = 0;
        foreach (var part in s.Contents)
            if (part is InterpolatedStringTextSyntax t) foreach (char c in t.TextToken.ValueText) { if (c is '{' or '}') sb.Append(c); sb.Append(c); }
            else sb.Append('{').Append(n++).Append('}');
        return sb.ToString();
    }

    /// <summary>L.T(text), or L.In(code, text): the text argument, else null.</summary>
    internal static ExpressionSyntax? MarkedText(InvocationExpressionSyntax inv) =>
        inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } m && m.Expression.ToString() is "L" or "Ilmenyx.Core.L"
            ? name switch
            {
                "T" when inv.ArgumentList.Arguments.Count == 1 => inv.ArgumentList.Arguments[0].Expression,
                "In" when inv.ArgumentList.Arguments.Count == 2 => inv.ArgumentList.Arguments[1].Expression,
                _ => null,
            }
            : null;

    internal static bool IsMarker(InvocationExpressionSyntax inv) => MarkedText(inv) is not null;

    private static SortedDictionary<string, SortedSet<string>> Extract()
    {
        var keys = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var file in Files(Sources))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
            foreach (var inv in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Where(IsMarker))
            {
                if (KeyOf(MarkedText(inv)!) is not { } key)
                {
                    Console.Error.WriteLine($"{file}:{inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: L.T precisa de um texto escrito no código, não de uma variável.");
                    continue;
                }
                if (!keys.TryGetValue(key, out var at)) keys[key] = at = new(StringComparer.Ordinal);
                at.Add($"{file}:{inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
            }
        }
        return keys;
    }

    private static string LocalePath(string lang) => Path.Combine(LocaleDir, lang + ".json");

    private static SortedDictionary<string, string> ReadLocale(string lang) =>
        File.Exists(LocalePath(lang)) && JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(LocalePath(lang))) is { } d
            ? new(d, StringComparer.Ordinal) : new(StringComparer.Ordinal);

    private static void WriteLocale(string lang, SortedDictionary<string, string> map) => File.WriteAllText(LocalePath(lang), JsonSerializer.Serialize(map, Json) + "\n");

    [GeneratedRegex(@"(?<!\{)\{(\d+)\}")]
    private static partial Regex Placeholder();

    internal static string Holes(string s) => string.Join(",", Placeholder().Matches(s.Replace("{{", "")).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal));

    private static int Check()
    {
        var keys = Extract(); int errors = 0;
        Console.WriteLine($"{keys.Count} textos marcados no código.");
        foreach (var lang in Directory.EnumerateFiles(LocaleDir, "*.json").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal))
        {
            var map = ReadLocale(lang!);
            int missing = keys.Keys.Count(k => !map.ContainsKey(k));
            var stale = map.Keys.Where(k => !keys.ContainsKey(k)).ToList();
            var holes = map.Where(kv => keys.ContainsKey(kv.Key) && Holes(kv.Key) != Holes(kv.Value)).ToList();
            Console.WriteLine($"{lang}: {map.Count - stale.Count} traduzidos, {missing} faltando, {stale.Count} sem uso, {holes.Count} com valores trocados.");
            foreach (var k in stale.Take(20)) Console.WriteLine($"  sem uso: {k}");
            foreach (var kv in holes) Console.WriteLine($"  valores diferentes: {kv.Key} → {kv.Value}");
            errors += stale.Count + holes.Count;
        }
        return errors == 0 ? 0 : 1;
    }

    /// <summary>Removes from every catalog the texts the code no longer marks.</summary>
    private static int Prune()
    {
        var keys = Extract();
        foreach (var lang in Directory.EnumerateFiles(LocaleDir, "*.json").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal))
        {
            var map = ReadLocale(lang!);
            int before = map.Count;
            foreach (var k in map.Keys.Where(k => !keys.ContainsKey(k)).ToList()) map.Remove(k);
            WriteLocale(lang!, map);
            Console.WriteLine($"{lang}: {before - map.Count} textos sem uso removidos.");
        }
        return 0;
    }

    private static int Pending(string lang, string output)
    {
        var map = ReadLocale(lang);
        var missing = Extract().Where(kv => !map.ContainsKey(kv.Key)).Select(kv => new { key = kv.Key, at = kv.Value.First() }).ToList();
        File.WriteAllText(output, JsonSerializer.Serialize(missing, Json));
        Console.WriteLine($"{missing.Count} textos sem tradução em {lang} gravados em {output}.");
        return 0;
    }

    private static int Merge(string lang, string input)
    {
        var map = ReadLocale(lang); var keys = Extract(); int added = 0, bad = 0;
        foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(input))!)
        {
            if (!keys.ContainsKey(k)) { Console.Error.WriteLine($"  texto não existe no código: {k}"); bad++; continue; }
            if (Holes(k) != Holes(v)) { Console.Error.WriteLine($"  valores diferentes: {k} → {v}"); bad++; continue; }
            map[k] = v; added++;
        }
        WriteLocale(lang, map);
        Console.WriteLine($"{lang}: {added} traduções juntadas, {bad} recusadas; catálogo com {map.Count}.");
        return bad == 0 ? 0 : 1;
    }
}
