// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>
/// First run: every blocked sensor the app can unlock, listed together and checked by default, applied with ONE system password prompt
/// (owner request 2026-11-07, "pedir todas as permissões de uma vez, como outros apps"). Declining is fine: the same fixes stay in the
/// sidebar button "Liberar sensores da CPU". Only the fixed <see cref="SensorFix.Apply"/> scripts are run, combined by <see cref="SensorHelp.Combined"/>.
/// </summary>
public sealed class PermissionsOnboardingDialog : Window
{
    /// <summary>True when the user ran the fixes and they all worked.</summary>
    public bool Applied { get; private set; }

    public PermissionsOnboardingDialog(IReadOnlyList<SensorFix> fixes, Func<string, Task<(bool Ok, string Message)>> run)
    {
        Icon = AppIcon.Window; Title = "Ilmenyx Controller — Permissões"; Width = 620; MinWidth = 460; SizeToContent = SizeToContent.Height; MaxHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Palette.Panel; Foreground = Palette.Fg; CanResize = false;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var applicable = fixes.Where(f => f.Apply is { Length: > 0 }).ToList();
        var list = new StackPanel { Spacing = 10 };
        list.Children.Add(UiKit.PageHeader("Antes de começar",
            "O Linux esconde alguns sensores de usuários comuns. Libere tudo agora com uma única senha (o sistema pede; o Ilmenyx nunca a vê)."));
        var boxes = new List<(CheckBox Box, SensorFix Fix)>();
        for (int i = 0; i < applicable.Count; i++)
        {
            var fix = applicable[i];
            var box = new CheckBox { Name = "OnboardFix" + i, IsChecked = true, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
            AutomationProperties.SetName(box, fix.Title);
            var text = new StackPanel { Spacing = 4, Children = {
                new TextBlock { Text = fix.Title, FontSize = 14, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = fix.Why, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap } } };
            if (fix.Warning is { } w) text.Children.Add(new TextBlock { Text = w, Foreground = Palette.Warn, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
            var row = new DockPanel { Children = { box, text } }; DockPanel.SetDock(box, Dock.Left);
            list.Children.Add(UiKit.Card(row, new Thickness(0)));
            boxes.Add((box, fix));
        }
        list.Children.Add(new TextBlock { Text = "Tudo continua valendo depois de reiniciar o PC. Se preferir agora não, dá para liberar depois pelo botão \"Liberar sensores da CPU\" no menu lateral, e também desfazer por lá.",
            FontSize = 11.5, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        var status = new TextBlock { Name = "OnboardStatus", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var apply = UiKit.Primary("OnboardApply", "LIBERAR COM UMA SENHA");
        var skip = new Button { Name = "OnboardSkip", Content = "Agora não", IsCancel = true, Padding = new Thickness(18, 8) };
        void Refresh() => apply.IsEnabled = boxes.Any(b => b.Box.IsChecked == true);
        foreach (var (box, _) in boxes) box.IsCheckedChanged += (_, _) => Refresh();
        skip.Click += (_, _) => Close();
        apply.Click += async (_, _) =>
        {
            var chosen = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Fix).ToList();
            if (SensorHelp.Combined(chosen) is not { } script) return;
            apply.IsEnabled = false; skip.IsEnabled = false; foreach (var (box, _) in boxes) box.IsEnabled = false;
            status.Foreground = Palette.Muted; status.Text = "Aguardando a sua senha na janela do sistema…";
            var (ok, message) = await run(script);
            if (ok)
            {
                Applied = true;
                status.Foreground = Palette.Success; status.Text = "Pronto. As leituras aparecem em poucos segundos.";
                skip.Content = "Continuar"; skip.IsEnabled = true; skip.Focus();
            }
            else
            {
                status.Foreground = Palette.Warn; status.Text = message + " Você pode tentar de novo ou liberar depois pelo menu lateral.";
                skip.IsEnabled = true; foreach (var (box, _) in boxes) box.IsEnabled = true; Refresh();
            }
        };
        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { skip, apply } };
        DockPanel.SetDock(right, Dock.Right); buttons.Children.Add(right); buttons.Children.Add(status);
        status.Margin = new Thickness(0, 0, 12, 0);
        var dock = new DockPanel { Margin = new Thickness(22, 18) };
        DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = list, MaxHeight = 560 });
        Content = dock;
        Opened += (_, _) => apply.Focus();
    }
}
