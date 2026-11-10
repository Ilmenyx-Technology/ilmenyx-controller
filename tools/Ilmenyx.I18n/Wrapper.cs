// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ilmenyx.I18n;

/// <summary>
/// Wraps texts a person reads in L.T(...). A text is left out, and listed in the report, when it is a lookup or comparison value (switch
/// patterns, ==, StartsWith, dictionary keys), sits where only constants are allowed (const, attributes, default parameters) or is a
/// multi-line raw block; those need a decision by hand.
/// </summary>
internal sealed partial class Wrapper(string file, List<string> report) : CSharpSyntaxRewriter
{
    public int Count { get; private set; }

    private static readonly HashSet<string> LookupMethods =
    [
        "StartsWith", "EndsWith", "Contains", "IndexOf", "LastIndexOf", "Equals", "Replace", "Split", "Trim", "TrimStart", "TrimEnd", "IsMatch", "Match",
        "Matches", "GetEnvironmentVariable", "SetEnvironmentVariable", "Combine", "GetProperty", "TryGetProperty", "ContainsKey", "TryGetValue", "Remove",
        "GetValue", "Parse", "TryParse", "Create", "Compare", "CompareOrdinal", "ParseExact", "TryParseExact",
    ];

    private static readonly HashSet<string> IdentityProperties = ["Name", "Tag", "Classes", "Key", "Id", "PageKey", "Code"];

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node) =>
        Program.IsMarker(node) ? node : base.VisitInvocationExpression(node);

    public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.StringLiteralExpression) || !Human(node.Token.ValueText)) return node;
        if (node.Token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken)) return Leave(node, "bloco", node.Token.ValueText);
        return Excluded(node) is { } why ? Leave(node, why, node.Token.ValueText) : Mark(node);
    }

    public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        string key = Program.KeyOf(node)!;
        var visited = (InterpolatedStringExpressionSyntax)base.VisitInterpolatedStringExpression(node)!;
        if (!Human(StripHoles(key))) return visited;
        if (node.StringStartToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken)) return Leave(node, "bloco", key, visited);
        return Excluded(node) is { } why ? Leave(node, why, key, visited) : Mark(visited);
    }

    private SyntaxNode Leave(ExpressionSyntax original, string why, string text, SyntaxNode? visited = null)
    {
        int line = original.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        report.Add($"{why}\t{file}:{line}\t{text.Replace("\n", "\\n").Replace("\t", " ")}");
        return visited ?? original;
    }

    private ExpressionSyntax Mark(ExpressionSyntax e)
    {
        Count++;
        var call = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName("L"), SyntaxFactory.IdentifierName("T")),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(e.WithoutTrivia()))));
        return call.WithLeadingTrivia(e.GetLeadingTrivia()).WithTrailingTrivia(e.GetTrailingTrivia());
    }

    /// <summary>Why this text must stay a plain constant, or null when it can be wrapped.</summary>
    private static string? Excluded(ExpressionSyntax node)
    {
        foreach (var a in node.Ancestors())
        {
            switch (a)
            {
                case AttributeArgumentSyntax: return "atributo";
                case PatternSyntax or CaseSwitchLabelSyntax: return "padrão";
                case ParameterSyntax: return "parâmetro-padrão";
                case InterpolationSyntax: return null; // inside a hole of another text: the inner text is shown as is
                case VariableDeclarationSyntax { Parent: FieldDeclarationSyntax f } when f.Modifiers.Any(SyntaxKind.ConstKeyword): return "const";
                case VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax l } when l.IsConst: return "const";
                case StatementSyntax or MemberDeclarationSyntax: goto use;
            }
        }
    use:
        // Climb through expressions that pass the text along unchanged (parentheses, ?:, ??, concatenation, switch arms).
        SyntaxNode e = node;
        while (e.Parent is ParenthesizedExpressionSyntax
               || e.Parent is ConditionalExpressionSyntax c && c.Condition != e
               || e.Parent is BinaryExpressionSyntax b && (b.IsKind(SyntaxKind.AddExpression) || b.IsKind(SyntaxKind.CoalesceExpression))
               || e.Parent is SwitchExpressionArmSyntax arm && arm.Expression == e
               || e.Parent is SwitchExpressionSyntax)
            e = e.Parent!;
        switch (e.Parent)
        {
            case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.EqualsExpression) || b.IsKind(SyntaxKind.NotEqualsExpression): return "comparação";
            case IsPatternExpressionSyntax: return "comparação";
            case ArgumentSyntax { Parent: BracketedArgumentListSyntax }: return "índice";
            case ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax inv } }:
                string name = inv.Expression switch
                {
                    MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
                    IdentifierNameSyntax i => i.Identifier.ValueText,
                    _ => "",
                };
                return LookupMethods.Contains(name) ? "busca:" + name : null;
            case ArgumentSyntax { Parent: ArgumentListSyntax { Parent: ObjectCreationExpressionSyntax o } } when o.Type.ToString().EndsWith("Regex", StringComparison.Ordinal): return "regex";
            case AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax } asg when asg.Left != e: return null;
            case AssignmentExpressionSyntax { Left: IdentifierNameSyntax id } when IdentityProperties.Contains(id.Identifier.ValueText): return "identificador";
            case AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax ma } when IdentityProperties.Contains(ma.Name.Identifier.ValueText): return "identificador";
        }
        if (e.Parent is ArgumentSyntax { Parent: BracketedArgumentListSyntax { Parent: ImplicitElementAccessSyntax } }) return "índice";
        return null;
    }

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Hole();

    private static string StripHoles(string key) => Hole().Replace(key, "");

    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex Word();

    [GeneratedRegex(@"^[a-z0-9_.:\-/+=@,;#*]+$")]
    private static partial Regex Token();

    [GeneratedRegex(@"\p{Ll}\p{Lu}")]
    private static partial Regex Camel();

    [GeneratedRegex(@"\\[dswbDSWB]|\(\?|\[\^|\.\*|\.\+|\^\(|\$\)")]
    private static partial Regex RegexSyntax();

    [GeneratedRegex(@"[à-öø-ÿÀ-ÖØ-ß]")]
    private static partial Regex Accent();

    [GeneratedRegex(@"^(command -v|echo|rm|mv|cp|if \[|sudo|printf|set -|for |exec|flatpak|pacman|apt-get|dnf|zypper|systemctl|modprobe)\b|\| *sudo|; *then\b|^[a-z0-9_.-]+ = |^[A-Z0-9_]+=")]
    private static partial Regex Shell();

    /// <summary>Text meant for a person: words, not an identifier, path, command option, regex or format.</summary>
    public static List<string>? Missed;

    internal static bool Human(string text)
    {
        if (!Shape(text)) return false;
        if (Vocabulary.IsPortuguese(text)) return true;
        Missed?.Add(text.Replace("\n", "\\n"));
        return false;
    }

    private static bool Shape(string text)
    {
        string t = text.Trim();
        if (!Word().IsMatch(t)) return false;
        if (RegexSyntax().IsMatch(t)) return false;
        if (t.StartsWith('/') || t.StartsWith('~') || t.StartsWith('$') || t.StartsWith('%') || t.StartsWith('#') || t.StartsWith('\\')
            || t.StartsWith("http", StringComparison.Ordinal) || t.StartsWith("file:", StringComparison.Ordinal) || t.StartsWith("--", StringComparison.Ordinal)
            || t.StartsWith('-') && t.Length > 1 && char.IsLetter(t[1])
            || t.StartsWith('.') && t.Length > 1 && t[1] != ' ')
            return false;
        if (t.Contains("&&") || t.Contains("||") || t.Contains("/dev/null") || t.Contains("2>&1") || Shell().IsMatch(t)
            || t.Contains("(+http", StringComparison.Ordinal)) return false; // shell scripts and the HTTP user agent
        if (t.StartsWith('<') && !Accent().IsMatch(t) && !t.Contains(' ')) return false;
        if (t.Any(char.IsWhiteSpace)) return true;
        if (Accent().IsMatch(t)) return true;
        if (Token().IsMatch(t) || Camel().IsMatch(t)) return false;
        if (t.IndexOfAny(['_', '/', '\\', '=', ':', '.', '@']) >= 0) return false;
        if (!t.Any(char.IsLower)) return t.Length > 6; // short acronyms (CPU, NVIDIA, ZRAM) stay as they are
        return true;
    }
}
