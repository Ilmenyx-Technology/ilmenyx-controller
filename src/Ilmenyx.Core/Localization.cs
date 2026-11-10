// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Ilmenyx.Core;

/// <summary>A language the interface can show: <see cref="Code"/> is what settings and ILMENYX_LANG store, <see cref="Name"/> is written in the language itself.</summary>
public sealed record UiLanguage(string Code, string Name, string Culture);

/// <summary>
/// Interface translation, gettext style: the pt-BR text written in the code is the key, and Locales/&lt;code&gt;.json maps it to the text of
/// another language. <c>L.T("Fechar")</c> translates a plain text; <c>L.T($"{n} jogos")</c> translates the template "{0} jogos" and fills in
/// the values, so a translation can move them around. A text missing from the catalog stays in pt-BR, never blank.
/// The language is chosen once at startup (<see cref="Use"/>) and a change applies after a restart: tables built in static fields keep
/// the language they were built in. Keys are extracted and checked by tools/Ilmenyx.I18n (docs/idiomas.md).
/// </summary>
public static class L
{
    public const string Reference = "pt-BR";

    public static readonly IReadOnlyList<UiLanguage> Languages =
    [
        new(Reference, "Português (Brasil)", "pt-BR"),
        new("en", "English", "en-US"),
        new("es", "Español", "es-419"),
    ];

    private static IReadOnlyDictionary<string, string> catalog = new Dictionary<string, string>();

    /// <summary>Code of the language in use (pt-BR until <see cref="Use"/> is called, so tests and tools keep the reference texts).</summary>
    public static string Current { get; private set; } = Reference;

    public static bool IsReference => Current == Reference;

    /// <summary>
    /// The language to use: ILMENYX_LANG, then the saved choice, then the system language (LC_ALL, LC_MESSAGES, LANG). Portuguese and
    /// Spanish locales get their language; any other locale gets English.
    /// </summary>
    public static string Resolve(string? saved, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        foreach (var choice in new[] { env("ILMENYX_LANG"), saved })
            if (Find(choice) is { } known) return known.Code;
        string? system = new[] { "LC_ALL", "LC_MESSAGES", "LANG" }.Select(env).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && v is not ("C" or "POSIX" or "C.UTF-8"));
        return system is null ? Reference : system.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? Reference
            : system.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
    }

    /// <summary>The language with this code ("en", "es", "pt-BR"; also "pt", "pt_BR", "en-US"…), or null.</summary>
    public static UiLanguage? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string c = code.Trim().Replace('_', '-');
        return Languages.FirstOrDefault(l => l.Code.Equals(c, StringComparison.OrdinalIgnoreCase))
            ?? Languages.FirstOrDefault(l => c.StartsWith(l.Code[..2], StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Switches texts, number and date formats to <paramref name="code"/>. Call before any window or table is built.</summary>
    public static void Use(string code)
    {
        var language = Find(code) ?? Languages[0];
        catalog = language.Code == Reference ? new Dictionary<string, string>() : Load(language.Code);
        Current = language.Code;
        var culture = CultureInfo.GetCultureInfo(language.Culture);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>The catalog of <paramref name="code"/> as shipped (pt-BR key → text). Empty for pt-BR or an unknown code.</summary>
    public static IReadOnlyDictionary<string, string> Load(string code)
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream($"Ilmenyx.Core.Locales.{code}.json");
        if (stream is null) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) is { } map
            ? map.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
            : new Dictionary<string, string>();
    }

    /// <summary>The text in <paramref name="code"/>, whatever the current language (e.g. to confirm a language change in that language).</summary>
    public static string In(string code, string text) => Find(code) is { } l && l.Code != Reference && Load(l.Code).TryGetValue(text, out var t) ? t : text;

    /// <summary>The text in the current language.</summary>
    public static string T(string text) => catalog.Count > 0 && catalog.TryGetValue(text, out var t) ? t : text;

    /// <summary>The template in the current language with its values filled in (values formatted with the current culture).</summary>
    public static string T(Template template) => template.Fill(catalog);

    /// <summary>
    /// Fills {0}, {1}… of <paramref name="template"/> with <paramref name="values"/>; "{{" and "}}" are literal braces. A placeholder with no
    /// value (a broken translation) is left as written instead of throwing.
    /// </summary>
    public static string Fill(string template, IReadOnlyList<string> values)
    {
        var sb = new StringBuilder(template.Length + 16);
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if ((c is '{' or '}') && i + 1 < template.Length && template[i + 1] == c) { sb.Append(c); i++; continue; }
            if (c == '{')
            {
                int end = template.IndexOf('}', i + 1);
                if (end > i + 1 && int.TryParse(template.AsSpan(i + 1, end - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n < values.Count)
                { sb.Append(values[n]); i = end; continue; }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Builds the key of an interpolated text ("{0} de {1}", braces of the text doubled) and keeps the formatted values apart.</summary>
    [InterpolatedStringHandler]
    public ref struct Template
    {
        private readonly StringBuilder key;
        private readonly List<string> values;

        public Template(int literalLength, int formattedCount)
        {
            key = new StringBuilder(literalLength + formattedCount * 3);
            values = new List<string>(formattedCount);
        }

        public readonly string Key => key.ToString();

        public readonly IReadOnlyList<string> Values => values;

        public readonly void AppendLiteral(string s)
        {
            foreach (char c in s) { if (c is '{' or '}') key.Append(c); key.Append(c); }
        }

        public readonly void AppendFormatted<TValue>(TValue value) => Add(value, 0, null);
        public readonly void AppendFormatted<TValue>(TValue value, string? format) => Add(value, 0, format);
        public readonly void AppendFormatted<TValue>(TValue value, int alignment) => Add(value, alignment, null);
        public readonly void AppendFormatted<TValue>(TValue value, int alignment, string? format) => Add(value, alignment, format);

        private readonly void Add<TValue>(TValue value, int alignment, string? format)
        {
            string s = value is IFormattable f ? f.ToString(format, CultureInfo.CurrentCulture) : value?.ToString() ?? "";
            if (alignment != 0) s = alignment > 0 ? s.PadLeft(alignment) : s.PadRight(-alignment);
            key.Append('{').Append(values.Count.ToString(CultureInfo.InvariantCulture)).Append('}');
            values.Add(s);
        }

        internal readonly string Fill(IReadOnlyDictionary<string, string> catalog)
        {
            string k = Key;
            return L.Fill(catalog.Count > 0 && catalog.TryGetValue(k, out var t) ? t : k, values);
        }
    }
}
