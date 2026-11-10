// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// "Jogo não abre?" (JOGOS): read-only checklist of why Proton/Flatpak/a game does not start (GameLaunchChecks). Each card: status, what
// was found, plain explanation, risk, the exact commands to copy and, only for user-level Flatpak changes, a one-click fix with confirmation
// and undo. Root or security-reducing changes are never run by the app.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <param name="Collect">Read-only facts (no root).</param>
/// <param name="RunUserFix">Runs one whitelisted user-level flatpak call; null hides the one-click buttons.</param>
/// <param name="OpenPage">Opens another page of the shell (Otimiza, Driver NVIDIA); null hides the link buttons.</param>
public sealed record GameCheckServices(
    Func<CancellationToken, Task<GameCheckFacts>> Collect,
    Func<IReadOnlyList<string>, CancellationToken, Task<(bool Ok, string Message)>>? RunUserFix,
    Action<string>? OpenPage = null);

public sealed class GameCheckPage : StackPanel
{
    private readonly GameCheckServices services;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly TextBlock status = new() { Name = "GcStatus", Text = "Clique em Verificar.", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock summary = new() { Name = "GcSummary", FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock distro = new() { Name = "GcDistro", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel list = new() { Name = "GcList", Spacing = 12 };
    private readonly Button scan;
    private bool busy;
    public IReadOnlyList<GameCheck> Checks { get; private set; } = [];

    public GameCheckPage(GameCheckServices services, Func<Window?> owner, CancellationToken lifetime)
    {
        this.services = services; this.owner = owner; this.lifetime = lifetime;
        scan = UiKit.Primary("BtnGcScan", "Verificar");
        scan.Click += async (_, _) => await RefreshAsync();
        Children.Add(UiKit.PageHeader("Por que o jogo não abre?", "Confere, sem mudar nada, os motivos mais comuns para a Steam, o Proton, o Flatpak ou um jogo não abrirem neste Linux. Cada item explica o que foi achado e mostra o comando exato.", scan));
        Children.Add(status);
        Children.Add(UiKit.Card(new StackPanel { Spacing = 4, Children = { summary, distro } }));
        Children.Add(list);
        Loaded += async (_, _) => { if (Checks.Count == 0 && !busy) await RefreshAsync(); };
    }

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; scan.IsEnabled = false; status.Text = "Conferindo o sistema (somente leitura)…";
        try
        {
            var facts = await services.Collect(lifetime);
            Checks = GameLaunchChecks.Build(facts);
            summary.Text = GameLaunchChecks.Summary(Checks);
            summary.Foreground = Checks.Any(c => c.Status == CheckStatus.Problem) ? Palette.Error : Checks.Any(c => c.Status == CheckStatus.Attention) ? Palette.Warn : Palette.Success;
            distro.Text = "Sistema: " + (facts.Distro.Name.Length > 0 ? facts.Distro.Name : "não identificado") + ". Itens que não se aplicam a este sistema não aparecem.";
            list.Children.Clear();
            foreach (var c in Checks.OrderByDescending(c => c.Status)) list.Children.Add(Card(c));
            status.Text = $"Verificado às {DateTime.Now:HH:mm:ss}. Nada foi alterado.";
        }
        catch (OperationCanceledException) { status.Text = "Verificação cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException) { status.Text = "Falhou: " + e.Message; }
        finally { busy = false; scan.IsEnabled = true; }
    }

    private static IBrush StatusBrush(CheckStatus s) => s switch { CheckStatus.Ok => Palette.Success, CheckStatus.Problem => Palette.Error, CheckStatus.Attention => Palette.Warn, _ => Palette.Muted };

    private Border Card(GameCheck c)
    {
        var head = new WrapPanel { Children =
        {
            new TextBlock { Text = c.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 10, 6), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap },
            UiKit.Tag(GameLaunchChecks.StatusText(c.Status), StatusBrush(c.Status), "GcState_" + c.Id),
        } };
        var body = new StackPanel { Spacing = 6, Children = { head } };
        body.Children.Add(new TextBlock { Name = "GcFound_" + c.Id, Text = c.Found, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = c.Explanation, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Risco: " + c.Risk, FontSize = 11.5, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        if (c.Warning is { } w)
            body.Children.Add(new Border { Name = "GcWarning_" + c.Id, Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(12, 8), Margin = new Thickness(0, 4, 0, 0),
                Child = new TextBlock { Text = "Atenção: " + w + " O Ilmenyx nunca aplica isto sozinho.", Foreground = Palette.Warn, FontSize = 12, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap } });

        var fixStatus = new TextBlock { Name = "GcFixStatus_" + c.Id, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, IsVisible = false };
        var actions = new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        if (services.RunUserFix is not null)
            for (int i = 0; i < c.Fixes.Count; i++)
                AddFix(actions, fixStatus, c, c.Fixes[i], i);
        if (c.LinkPage is { } page && services.OpenPage is { } open)
        {
            var link = new Button { Name = "GcLink_" + c.Id, Content = c.LinkLabel ?? "Abrir", FontSize = 12, Padding = new Thickness(14, 7) };
            link.Click += (_, _) => open(page);
            actions.Children.Add(link);
        }
        if (actions.Children.Count > 0) body.Children.Add(actions);
        body.Children.Add(fixStatus);

        if (c.Commands.Count > 0)
        {
            var commands = new StackPanel { Name = "GcCommands_" + c.Id, Spacing = 8, Margin = new Thickness(0, 4, 0, 0), IsVisible = c.Status is CheckStatus.Problem or CheckStatus.Attention };
            int k = 0;
            foreach (var (label, command) in c.Commands)
            {
                var box = new TextBox { Name = $"GcCmd_{c.Id}_{k}", Text = command, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = IlmenyxTheme.Mono, FontSize = 11.5 };
                AutomationProperties.SetName(box, label);
                var copy = new Button { Name = $"GcCopy_{c.Id}_{k}", Content = "Copiar", FontSize = 11, Padding = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
                AutomationProperties.SetName(copy, "Copiar: " + label);
                string text = command;
                copy.Click += async (_, _) => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb) { await cb.SetTextAsync(text); fixStatus.Text = "Comando copiado. Cole no terminal e confira antes de executar."; fixStatus.Foreground = Palette.Muted; fixStatus.IsVisible = true; } };
                var row = new Grid { ColumnDefinitions = new("*,Auto") }; Grid.SetColumn(copy, 1); row.Children.Add(box); row.Children.Add(copy);
                commands.Children.Add(new StackPanel { Spacing = 3, Children = { new TextBlock { Text = label, FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap }, row } });
                k++;
            }
            var toggle = new Button { Name = "GcShowCommands_" + c.Id, Content = commands.IsVisible ? "Esconder comandos" : "Ver comandos", FontSize = 11.5, Background = Brushes.Transparent, Foreground = Palette.AccentText, Padding = new Thickness(0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            toggle.Click += (_, _) => { commands.IsVisible = !commands.IsVisible; toggle.Content = commands.IsVisible ? "Esconder comandos" : "Ver comandos"; };
            body.Children.Add(toggle);
            body.Children.Add(commands);
        }
        var card = UiKit.Card(body, new Thickness(0));
        card.Name = "GcCheck_" + c.Id;
        return card;
    }

    private void AddFix(WrapPanel actions, TextBlock fixStatus, GameCheck c, UserFix fix, int index)
    {
        var apply = UiKit.Primary($"GcFix_{c.Id}_{index}", fix.Button);
        var undo = new Button { Name = $"GcUndo_{c.Id}_{index}", Content = fix.UndoButton ?? "Desfazer", FontSize = 12, Padding = new Thickness(14, 7), IsVisible = false };
        apply.Click += async (_, _) =>
        {
            if (owner() is not { } window || services.RunUserFix is not { } runner) return;
            if (!await ConfirmDialog.AskAsync(window, c.Title, fix.Confirm, "Continuar", "Cancelar")) { fixStatus.Text = "Cancelado: nada foi feito."; fixStatus.Foreground = Palette.Muted; fixStatus.IsVisible = true; return; }
            apply.IsEnabled = false; fixStatus.Text = "Aplicando…"; fixStatus.IsVisible = true;
            var (ok, message) = await runner(fix.Arguments, lifetime);
            fixStatus.Text = ok ? "Pronto. Clique em Verificar para conferir." + (c.Id == "steamdrives" ? " Feche e abra a Steam." : "") : message;
            fixStatus.Foreground = ok ? Palette.Success : Palette.Warn;
            apply.IsEnabled = !ok; undo.IsVisible = ok && fix.UndoArguments is not null;
        };
        undo.Click += async (_, _) =>
        {
            if (services.RunUserFix is not { } runner || fix.UndoArguments is not { } back) return;
            undo.IsEnabled = false; fixStatus.Text = "Desfazendo…";
            var (ok, message) = await runner(back, lifetime);
            fixStatus.Text = ok ? "Desfeito." : message; fixStatus.Foreground = ok ? Palette.Muted : Palette.Warn;
            undo.IsEnabled = true; undo.IsVisible = !ok; apply.IsEnabled = ok;
        };
        actions.Children.Add(apply); actions.Children.Add(undo);
    }
}
