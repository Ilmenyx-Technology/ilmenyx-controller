// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>
/// Help Center ("Ajuda"): one card per page with what it is for, how to use it in a few steps and common questions, plus "Refazer o tour".
/// Every text comes from <see cref="HelpLanguage"/>; <see cref="ShowSection"/> scrolls to a page's card (the header "?" uses it).
/// </summary>
public sealed class HelpCenterPage : Grid
{
    private readonly ScrollViewer scroll;
    private readonly StackPanel content;
    private readonly Dictionary<string, Border> cards = new();
    private DispatcherTimer? flash;

    public HelpCenterPage(HelpLanguage text, Action<string> openPage, Action retour)
    {
        var again = UiKit.Primary("HelpRetour", text[HelpKeys.HelpRetour], 18, 8);
        AutomationProperties.SetName(again, text[HelpKeys.HelpRetour]);
        again.Click += (_, _) => retour();
        content = new StackPanel { Margin = new Thickness(0, 4, 8, 24) };
        content.Children.Add(UiKit.PageHeader(text[HelpKeys.HelpTitle], text[HelpKeys.HelpSubtitle], again, "HelpTitle"));

        // Index: one row per sidebar group with a chip per page.
        var index = new StackPanel { Spacing = 8 };
        foreach (var group in text.Groups)
        {
            var sections = text.Sections.Where(s => s.GroupId == group.Id).ToList();
            if (sections.Count == 0) continue;
            var chips = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
            foreach (var s in sections)
            {
                var chip = new Button { Name = "HelpGo_" + s.Key, Content = s.Title, FontSize = 11.5, Padding = new Thickness(12, 4), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
                UiKit.ChipStyle(chip, false);
                var key = s.Key; chip.Click += (_, _) => ShowSection(key);
                chips.Children.Add(chip);
            }
            var label = UiKit.Label(group.Title); label.Width = 110; label.VerticalAlignment = VerticalAlignment.Top; label.Margin = new Thickness(0, 5, 8, 0);
            var row = new DockPanel { Children = { label, chips } }; DockPanel.SetDock(label, Dock.Left);
            index.Children.Add(row);
        }
        content.Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = { UiKit.Label(text[HelpKeys.HelpIndex]), index } }));

        foreach (var group in text.Groups)
        {
            var sections = text.Sections.Where(s => s.GroupId == group.Id).ToList();
            if (sections.Count == 0) continue;
            content.Children.Add(UiKit.SectionHeader(group.Title));
            foreach (var s in sections) content.Children.Add(Section(text, s, openPage));
        }
        scroll = new ScrollViewer { Name = "HelpScroll", Content = content };
        Children.Add(scroll);
    }

    private Border Section(HelpLanguage text, HelpSection s, Action<string> openPage)
    {
        var title = new TextBlock { Text = s.Title, FontSize = 17, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (s.Key != "help")
        {
            var open = new Button { Name = "HelpOpen_" + s.Key, Content = text[HelpKeys.HelpOpenPage], FontSize = 11.5, Padding = new Thickness(12, 5), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(open, text[HelpKeys.HelpOpenPage] + ": " + s.Title);
            var key = s.Key; open.Click += (_, _) => openPage(key);
            DockPanel.SetDock(open, Dock.Right); head.Children.Add(open);
        }
        head.Children.Add(title);

        var body = new StackPanel { Spacing = 6, Children = { head, UiKit.Label(text[HelpKeys.HelpPurpose]),
            new TextBlock { Text = s.Purpose, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Fg, Margin = new Thickness(0, 0, 0, 8) } } };
        body.Children.Add(UiKit.Label(text[HelpKeys.HelpSteps]));
        for (int i = 0; i < s.Steps.Count; i++)
        {
            var number = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Palette.AccentContainer, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock { Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Palette.OnAccentContainer, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var step = new TextBlock { Text = s.Steps[i], FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel { Children = { number, step } }; DockPanel.SetDock(number, Dock.Left);
            AutomationProperties.SetName(row, text.Format(HelpKeys.HelpStepAccessible, i + 1, s.Steps[i]));
            body.Children.Add(row);
        }
        if (s.Faq.Count > 0)
        {
            var label = UiKit.Label(text[HelpKeys.HelpFaq]); label.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(label);
            foreach (var f in s.Faq)
            {
                var expander = new Expander
                {
                    Name = $"HelpFaq_{s.Key}_{f.Id}", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Header = new TextBlock { Text = f.Question, FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    Content = new TextBlock { Text = f.Answer, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted },
                };
                AutomationProperties.SetName(expander, f.Question);
                body.Children.Add(expander);
            }
        }
        var card = UiKit.Card(body); card.Name = "Help_" + s.Key; card.Padding = new Thickness(22, 18);
        AutomationProperties.SetName(card, s.Title);
        cards[s.Key] = card;
        return card;
    }

    /// <summary>Card of a page key, or null (used by tests and by the shell).</summary>
    public Control? SectionCard(string key) => cards.GetValueOrDefault(key);

    /// <summary>Scrolls so the page's card is at the top (after layout, since the page may have just become visible) and outlines it briefly.</summary>
    public void ShowSection(string key)
    {
        if (!cards.TryGetValue(key, out var card)) { scroll.Offset = default; return; }
        void Go()
        {
            if (card.TranslatePoint(default, content) is not { } p) return;
            double max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(0, Math.Clamp(p.Y - 6, 0, max));
        }
        Go();
        Dispatcher.UIThread.Post(Go, DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(Go, DispatcherPriority.Background);
        foreach (var c in cards.Values) c.BorderBrush = Palette.Line;
        card.BorderBrush = Palette.Accent;
        flash?.Stop();
        flash = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        flash.Tick += (_, _) => { flash?.Stop(); card.BorderBrush = Palette.Line; };
        flash.Start();
    }
}
