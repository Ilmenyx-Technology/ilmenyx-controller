using System.Text.RegularExpressions;

namespace Ilmenyx.Platform.Linux;

/// <summary>Conservative minimization of obvious identifiers in bounded free text, not anonymization.</summary>
internal static partial class ReportTextPrivacy
{
    [GeneratedRegex(@"(?i)\b(hostname|username|serial(?:_number)?|uuid|machine-id|mac|ip)\s*[:=]\s*[^\s;,]+", RegexOptions.CultureInvariant)]
    private static partial Regex Labeled();
    [GeneratedRegex(@"(?:/home/[^\s;,]+|/root(?:/[^\s;,]*)?)", RegexOptions.CultureInvariant)]
    private static partial Regex HomePath();
    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b|\b[0-9A-Fa-f]{8}-(?:[0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
    [GeneratedRegex(@"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4();
    public static string Redact(string boundedText)
    {
        var text = Labeled().Replace(boundedText, "$1=[redigido]");
        text = HomePath().Replace(text, "[caminho redigido]");
        text = Identifier().Replace(text, "[identificador redigido]");
        return Ipv4().Replace(text, match => System.Net.IPAddress.TryParse(match.Value, out _) ? "[IP redigido]" : match.Value);
    }
}
