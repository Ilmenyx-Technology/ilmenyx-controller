// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Globalization;

namespace Ilmenyx.Core;

/// <summary>A common question about one area of the app. <see cref="Id"/> is stable across languages.</summary>
public sealed record HelpFaq(string Id, string Question, string Answer);

/// <summary>Sidebar group as shown in the Help Center (DIAGNÓSTICO, JOGOS…). <see cref="Id"/> is stable across languages.</summary>
public sealed record HelpGroup(string Id, string Title);

/// <summary>
/// Help for one page of the shell. <see cref="Key"/> is the page key registered in the shell (pages["key"]) and never translated;
/// every other text is in the language of the set.
/// </summary>
public sealed record HelpSection(string Key, string GroupId, string Title, string Purpose, IReadOnlyList<string> Steps, IReadOnlyList<HelpFaq> Faq);

/// <summary>
/// One step of the welcome tour. <see cref="PageKey"/> is the page the tour opens; <see cref="Highlights"/> are page keys (their sidebar
/// entries) or one of <see cref="HelpContent.Anchors"/> (header controls). Both are identifiers, the same in every language.
/// </summary>
public sealed record TourStep(string Id, string PageKey, IReadOnlyList<string> Highlights, string Title, string Body);

/// <summary>Every user-visible text of the tour and of the Help Center in one language.</summary>
public sealed record HelpLanguage(string Code, IReadOnlyDictionary<string, string> Ui, IReadOnlyList<HelpGroup> Groups, IReadOnlyList<HelpSection> Sections, IReadOnlyList<TourStep> Tour)
{
    /// <summary>Interface text by key (see <see cref="HelpKeys"/>). A missing key shows the key itself, so a gap is visible instead of blank.</summary>
    public string this[string key] => Ui.TryGetValue(key, out var v) ? v : key;

    /// <summary>Interface text with placeholders ({0}, {1}…) filled in, culture-independent.</summary>
    public string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, this[key], args);

    public HelpSection? Section(string key) => Sections.FirstOrDefault(s => s.Key == key);

    public string GroupTitle(string id) => Groups.FirstOrDefault(g => g.Id == id)?.Title ?? id;

    /// <summary>
    /// Flat list of every translatable entry ("ui.tour.next", "section.dash.step.1", "tour.games.body"…), used to check that a new
    /// language set has exactly the entries of the reference set.
    /// </summary>
    public IEnumerable<string> Keys()
    {
        foreach (var k in Ui.Keys) yield return "ui." + k;
        foreach (var g in Groups) yield return "group." + g.Id;
        foreach (var s in Sections)
        {
            yield return $"section.{s.Key}.group.{s.GroupId}";
            yield return $"section.{s.Key}.title";
            yield return $"section.{s.Key}.purpose";
            for (int i = 0; i < s.Steps.Count; i++) yield return $"section.{s.Key}.step.{i}";
            foreach (var f in s.Faq) { yield return $"section.{s.Key}.faq.{f.Id}.q"; yield return $"section.{s.Key}.faq.{f.Id}.a"; }
        }
        foreach (var t in Tour)
        {
            yield return $"tour.{t.Id}.page.{t.PageKey}";
            foreach (var h in t.Highlights) yield return $"tour.{t.Id}.highlight.{h}";
            yield return $"tour.{t.Id}.title";
            yield return $"tour.{t.Id}.body";
        }
    }
}

/// <summary>Keys of the interface texts the tour, the welcome dialog and the Help Center use. Views only use these constants.</summary>
public static class HelpKeys
{
    public const string WelcomeWindow = "welcome.window", WelcomeTitle = "welcome.title", WelcomeBody = "welcome.body", WelcomeNote = "welcome.note",
        WelcomeStart = "welcome.start", WelcomeLater = "welcome.later";
    public const string TourName = "tour.name", TourCounter = "tour.counter", TourBack = "tour.back", TourNext = "tour.next", TourFinish = "tour.finish",
        TourSkip = "tour.skip", TourKeys = "tour.keys", TourAccessible = "tour.accessible";
    public const string HelpNav = "help.nav", HelpTitle = "help.title", HelpSubtitle = "help.subtitle", HelpRetour = "help.retour", HelpIndex = "help.index",
        HelpPurpose = "help.purpose", HelpSteps = "help.steps", HelpFaq = "help.faq", HelpOpenPage = "help.openpage", HelpContext = "help.context",
        HelpSearchPrefix = "help.searchprefix", HelpStepAccessible = "help.stepaccessible";
}

/// <summary>
/// Tour and Help Center texts per language. pt-BR is the reference set (HelpContent.PtBr.cs). To add a language, write another
/// partial file with a method returning a <see cref="HelpLanguage"/> and register it in <see cref="sets"/>; the views do not change.
/// See docs/tutorial.md.
/// </summary>
public static partial class HelpContent
{
    public const string DefaultLanguage = "pt-BR";

    /// <summary>Header controls a tour step can highlight besides sidebar pages.</summary>
    public static readonly IReadOnlyList<string> Anchors = ["search", "theme", "pagehelp", "sensorfix"];

    private static readonly Dictionary<string, Func<HelpLanguage>> sets = new(StringComparer.OrdinalIgnoreCase)
    {
        [DefaultLanguage] = PtBr,
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HelpLanguage> cache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> Languages => sets.Keys;

    /// <summary>The set for <paramref name="code"/> ("pt-BR", later "en", "es"); unknown or null falls back to pt-BR.</summary>
    public static HelpLanguage For(string? code = null)
    {
        string key = code is not null && sets.ContainsKey(code) ? code : DefaultLanguage;
        return cache.GetOrAdd(key, k => sets[k]());
    }
}
