// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;

namespace Ilmenyx.Platform.Linux;

/// <summary>Conservative minimization of obvious identifiers in bounded free text, not anonymization.</summary>
internal static partial class ReportTextPrivacy
{
    [GeneratedRegex(@"(?i)\b(hostname|username|serial(?:_number)?|uuid|machine-id|mac|ip(?:v[46])?)\s*[:=]\s*[^\s;,]+", RegexOptions.CultureInvariant)]
    private static partial Regex Labeled();
    [GeneratedRegex(@"(?:/home/[^\s;,]+|/root(?:/[^\s;,]*)?)", RegexOptions.CultureInvariant)]
    private static partial Regex HomePath();
    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b|\b[0-9A-Fa-f]{8}-(?:[0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
    [GeneratedRegex(@"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4();
    // Match whole candidate tokens, then validate syntax; never resolve a zone/interface name.
    [GeneratedRegex(@"(?<![\p{L}\p{N}_:.%])[0-9A-Fa-f:.]+(?:%[\p{L}\p{N}_.~+\-]+)?(?![\p{L}\p{N}_.%]|:[\p{L}\p{N}_:.%])", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6Candidate();
    private static string RedactIpv6(Match match)
    {
        var candidate = match.Value.TrimEnd('.');
        int zone = candidate.IndexOf('%');
        var address = zone < 0 ? candidate : candidate[..zone];
        bool Valid(string value) => value.Contains(':') && IPAddress.TryParse(value, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetworkV6;
        if (!Valid(address))
        {
            // A prose delimiter may follow an unbracketed address; remove only one
            // colon, and only when doing so yields a complete IPv6 address.
            if (zone >= 0 || !address.EndsWith(':') || !Valid(address[..^1])) return match.Value;
            candidate = candidate[..^1];
        }
        return "[IP redigido]" + match.Value[candidate.Length..];
    }
    public static string Redact(string boundedText)
    {
        var text = Labeled().Replace(boundedText, "$1=[redigido]");
        text = HomePath().Replace(text, "[caminho redigido]");
        text = Ipv6Candidate().Replace(text, RedactIpv6);
        text = Identifier().Replace(text, "[identificador redigido]");
        return Ipv4().Replace(text, match => System.Net.IPAddress.TryParse(match.Value, out _) ? "[IP redigido]" : match.Value);
    }
}
