// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Xml;

namespace Ilmenyx.Core;

/// <summary>
/// Library artwork for Linux games from AppStream (metainfo of the app, or the catalog of a Flatpak remote / the distribution): the
/// default screenshot, as a mid-size thumbnail when there is one. Only https addresses are returned; nothing here goes to the network.
/// </summary>
public static class NativeArt
{
    private const int Preferred = 752; // card and hero both look fine from this width
    private static readonly XmlReaderSettings Safe = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true, IgnoreWhitespace = true };

    /// <summary>Screenshot address of the component with this ID (desktop ID, with or without ".desktop") in a metainfo file or a catalog.</summary>
    public static string? ScreenshotUrl(Stream xml, string id)
    {
        using var r = XmlReader.Create(xml, Safe);
        while (r.ReadToFollowing("component"))
        {
            using var c = r.ReadSubtree();
            if (FromComponent(c, id) is { } url) return url;
        }
        return null;
    }

    private static string? FromComponent(XmlReader c, string id)
    {
        bool match = false, inScreenshot = false, screenshotDefault = false, bestDefault = false, done = false;
        string? best = null; int bestScore = int.MaxValue;
        c.Read();
        while (!c.EOF)
        {
            if (c.NodeType == XmlNodeType.EndElement && c.Name == "screenshot")
            {
                inScreenshot = false;
                if (best is not null && bestDefault) done = true; // the default screenshot wins; later ones are only alternatives
            }
            else if (c.NodeType == XmlNodeType.Element && c.Name == "id" && c.Depth == 1)
            {
                string value = c.ReadElementContentAsString().Trim();
                match = value == id || value == id + ".desktop" || value.EndsWith(".desktop", StringComparison.Ordinal) && value[..^8] == id;
                if (!match) return null;
                continue; // already on the next node
            }
            else if (c.NodeType == XmlNodeType.Element && c.Name == "screenshot" && !done)
            {
                inScreenshot = true; screenshotDefault = c.GetAttribute("type") == "default";
            }
            else if (c.NodeType == XmlNodeType.Element && c.Name == "image" && inScreenshot && !done)
            {
                string type = c.GetAttribute("type") ?? "source";
                int width = int.TryParse(c.GetAttribute("width"), out int w) ? w : 0;
                string url = c.ReadElementContentAsString().Trim();
                // Score: a thumbnail at least as wide as Preferred and closest to it; the source image only when nothing else exists.
                int score = type == "source" ? 100_000 : width >= Preferred ? width - Preferred : 50_000 + (Preferred - width);
                if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(url, UriKind.Absolute, out _)
                    && (best is null || screenshotDefault && !bestDefault || screenshotDefault == bestDefault && score < bestScore))
                { best = url; bestScore = score; bestDefault = screenshotDefault; }
                continue;
            }
            c.Read();
        }
        return match ? best : null;
    }
}
