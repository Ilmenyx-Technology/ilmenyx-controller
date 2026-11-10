// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Não chama L.Use: o idioma é global e os outros testes leem os textos em pt-BR.
using System.Text.RegularExpressions;
using Ilmenyx.Core;
using Xunit;

namespace Ilmenyx.Tests;

public sealed partial class LocalizationTests
{
    private static string Key(L.Template t) => t.Key;
    private static IReadOnlyList<string> Values(L.Template t) => t.Values;

    [Fact]
    public void InterpolatedTextBecomesATemplateWithItsValuesApart()
    {
        int n = 3; double t = 71.25; string game = "Dota 2";
        Assert.Equal("{0} jogos · {1} °C em {2} {{x}}", Key($"{n} jogos · {t:0.0} °C em {game} {{x}}"));
        Assert.Equal(["3", 71.25.ToString("0.0"), "Dota 2"], Values($"{n} jogos · {t:0.0} °C em {game}"));
        Assert.Equal("[  7]", L.T($"[{7,3}]"));
    }

    [Fact]
    public void ReferenceLanguageReturnsTheTextAsWritten()
    {
        Assert.True(L.IsReference);
        Assert.Equal("Fechar", L.T("Fechar"));
        Assert.Equal("2 de 5 {ok}", L.T($"{2} de {5} {{ok}}"));
    }

    [Fact]
    public void FillMovesValuesAndSurvivesBrokenTemplates()
    {
        Assert.Equal("5 of 2", L.Fill("{1} of {0}", ["2", "5"]));
        Assert.Equal("{a} {9} x", L.Fill("{{a}} {9} {0}", ["x"])); // a placeholder without value stays visible
        Assert.Equal("{", L.Fill("{", []));
    }

    [Theory]
    [InlineData(null, null, "pt_BR.UTF-8", "pt-BR")]
    [InlineData(null, null, "es_AR.UTF-8", "es")]
    [InlineData(null, null, "de_DE.UTF-8", "en")]
    [InlineData(null, null, "C.UTF-8", "pt-BR")] // no real locale: the reference language
    [InlineData(null, "en", "pt_BR.UTF-8", "en")] // the saved choice wins over the system
    [InlineData("es", "en", "pt_BR.UTF-8", "es")] // ILMENYX_LANG wins over everything
    [InlineData("xx", "klingon", "en_US.UTF-8", "en")] // unknown codes are ignored
    public void LanguageComesFromEnvironmentThenSettingThenSystem(string? env, string? saved, string lang, string expected) =>
        Assert.Equal(expected, L.Resolve(saved, k => k switch { "ILMENYX_LANG" => env, "LANG" => lang, _ => null }));

    [Theory]
    [InlineData("pt", "pt-BR")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("en-US", "en")]
    [InlineData("ES", "es")]
    public void CodesAreMatchedLoosely(string code, string expected) => Assert.Equal(expected, L.Find(code)!.Code);

    [GeneratedRegex(@"(?<!\{)\{(\d+)\}")]
    private static partial Regex Placeholder();

    private static string Holes(string s) => string.Join(",", Placeholder().Matches(s.Replace("{{", "")).Select(m => m.Groups[1].Value).Order());

    [Fact]
    public void CatalogsKeepEveryValueOfTheirTexts()
    {
        foreach (var language in L.Languages.Where(l => l.Code != L.Reference))
            foreach (var (key, text) in L.Load(language.Code))
            {
                Assert.False(string.IsNullOrWhiteSpace(text), $"{language.Code}: {key}");
                Assert.True(Holes(key) == Holes(text), $"{language.Code}: \"{key}\" → \"{text}\"");
            }
    }
}
