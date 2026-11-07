// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using System.Reflection;
using System.Text.RegularExpressions;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public class HelpContentTests
{
    /// <summary>Page keys registered by the shell (pages["key"] = …), read from the source so a new page without help fails here.</summary>
    private static IReadOnlyList<string> ShellPageKeys()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ilmenyx.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var desktop = Path.Combine(dir!.FullName, "src", "Ilmenyx.Desktop");
        var keys = Directory.EnumerateFiles(desktop, "Shell*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"pages\[""([a-z]+)""\]\s*=").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.True(keys.Count >= 25, "page registry not found: " + string.Join(",", keys));
        return keys;
    }

    public static TheoryData<string> AllLanguages() { var d = new TheoryData<string>(); foreach (var l in HelpContent.Languages) d.Add(l); return d; }

    [Fact]
    public void EveryRegisteredPageHasExactlyOneHelpSectionAndNoOrphans()
    {
        var keys = ShellPageKeys();
        var text = HelpContent.For(HelpContent.DefaultLanguage);
        Assert.Contains("help", keys);
        foreach (var key in keys) Assert.Single(text.Sections, s => s.Key == key);
        foreach (var s in text.Sections) Assert.Contains(s.Key, keys);
    }

    [Theory, MemberData(nameof(AllLanguages))]
    public void NoTextIsEmptyAndStepsAreShort(string language)
    {
        var text = HelpContent.For(language);
        Assert.All(text.Ui, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
        Assert.All(text.Groups, g => Assert.False(string.IsNullOrWhiteSpace(g.Title)));
        foreach (var s in text.Sections)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title), s.Key);
            Assert.False(string.IsNullOrWhiteSpace(s.Purpose), s.Key);
            Assert.InRange(s.Steps.Count, 2, 5);
            Assert.All(s.Steps, st => Assert.False(string.IsNullOrWhiteSpace(st), s.Key));
            Assert.Contains(text.Groups, g => g.Id == s.GroupId);
            Assert.All(s.Faq, f => { Assert.False(string.IsNullOrWhiteSpace(f.Question), s.Key + "/" + f.Id); Assert.False(string.IsNullOrWhiteSpace(f.Answer), s.Key + "/" + f.Id); });
            Assert.Equal(s.Faq.Count, s.Faq.Select(f => f.Id).Distinct().Count());
        }
        Assert.Equal(text.Sections.Count, text.Sections.Select(s => s.Key).Distinct().Count());
        // Questions also go into the global search, so each must be unique.
        var questions = text.Sections.SelectMany(s => s.Faq).Select(f => f.Question).ToList();
        Assert.Equal(questions.Count, questions.Distinct().Count());
    }

    [Theory, MemberData(nameof(AllLanguages))]
    public void TourStepsReferenceExistingPagesAndStayShort(string language)
    {
        var keys = ShellPageKeys();
        var tour = HelpContent.For(language).Tour;
        Assert.InRange(tour.Count, 8, 10);
        Assert.Equal(tour.Count, tour.Select(t => t.Id).Distinct().Count());
        foreach (var step in tour)
        {
            Assert.Contains(step.PageKey, keys);
            Assert.NotEmpty(step.Highlights);
            Assert.All(step.Highlights, h => Assert.True(keys.Contains(h) || HelpContent.Anchors.Contains(h), step.Id + ": " + h));
            Assert.False(string.IsNullOrWhiteSpace(step.Title), step.Id);
            // 2–3 short sentences: count sentence ends (., ? or ! followed by a space or the end).
            int sentences = Regex.Matches(step.Body.Trim(), @"[.?!](\s|$)").Count;
            Assert.InRange(sentences, 1, 3);
        }
    }

    [Fact]
    public void TheTourCoversTheMainAreas()
    {
        var pagesVisited = HelpContent.For().Tour.SelectMany(t => t.Highlights.Append(t.PageKey)).ToHashSet();
        foreach (var key in new[] { "dash", "sensorfix", "system", "launcher", "compat", "fps", "bench", "stress", "overlay", "optimize", "driver", "apps", "search", "theme", "help" })
            Assert.Contains(key, pagesVisited);
    }

    [Fact]
    public void EveryKeyTheViewsUseExistsInEveryLanguage()
    {
        var used = typeof(HelpKeys).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.True(used.Count > 20);
        foreach (var language in HelpContent.Languages)
        {
            var text = HelpContent.For(language);
            foreach (var key in used) Assert.True(text.Ui.ContainsKey(key), language + " lacks " + key);
            Assert.Equal(used.Count, text.Ui.Count); // no stale keys either
        }
    }

    [Fact]
    public void EveryLanguageSetHasExactlyTheReferenceEntries()
    {
        var reference = HelpContent.For(HelpContent.DefaultLanguage).Keys().ToHashSet();
        Assert.NotEmpty(reference);
        foreach (var language in HelpContent.Languages)
        {
            var other = HelpContent.For(language).Keys().ToHashSet();
            Assert.True(reference.SetEquals(other), $"{language}: missing [{string.Join(", ", reference.Except(other))}] extra [{string.Join(", ", other.Except(reference))}]");
            Assert.Equal(reference.Count, HelpContent.For(language).Keys().Count()); // no duplicates
        }
    }

    [Fact]
    public void UnknownOrMissingLanguageFallsBackToPortuguese()
    {
        Assert.Equal("pt-BR", HelpContent.For(null).Code);
        Assert.Equal("pt-BR", HelpContent.For("xx-YY").Code);
        Assert.Same(HelpContent.For("pt-BR"), HelpContent.For("PT-br"));
        var text = HelpContent.For();
        Assert.Equal("Passo 2 de 10", text.Format(HelpKeys.TourCounter, 2, 10));
        Assert.Equal("chave.inexistente", text["chave.inexistente"]);
    }

    [Fact]
    public void ContentAnswersTheCommonQuestionsTheOwnerListed()
    {
        var questions = HelpContent.For().Sections.SelectMany(s => s.Faq.Select(f => (s.Key, f.Question))).ToList();
        Assert.Contains(("dash", "Por que a temperatura da CPU não aparece?"), questions);
        Assert.Contains(("launcher", "Por que o jogo de Windows demora na primeira vez?"), questions);
        Assert.Contains(("launcher", "Como instalo um jogo a partir do instalador do Windows?"), questions);
        Assert.Contains(("launcher", "O que é a proteção contra travamento em tela cheia?"), questions);
        Assert.Contains(("launcher", "O que é GPU dedicada?"), questions);
        Assert.Contains(("launcher", "O que é o MangoHud?"), questions);
        Assert.Contains(("stress", "É seguro rodar o stress test?"), questions);
    }
}
