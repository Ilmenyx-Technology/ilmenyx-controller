// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// ZRAM card of the Otimiza page: read-only detection (ZramPlanner), a plan in plain words and, only after a click and a confirmation
// that lists every step, the FIXED root script through polkit (pkexec). Undo removes only what the Ilmenyx created.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <param name="Collect">Read-only facts (no root).</param>
/// <param name="RunPrivileged">Runs a fixed script as root through pkexec, streaming lines; null when polkit is missing.</param>
public sealed record ZramServices(
    Func<CancellationToken, Task<ZramFacts>> Collect,
    Func<string, Action<string>, CancellationToken, Task<(bool Ok, string Message)>>? RunPrivileged);

public sealed class ZramPanel : StackPanel
{
    private readonly ZramServices services;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly TextBlock headline = new() { Name = "ZramHeadline", Text = "Verificando…", FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock summary = new() { Name = "ZramSummary", FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel findings = new() { Name = "ZramFindings", Spacing = 3 };
    private readonly StackPanel actions = new() { Name = "ZramActions", Spacing = 10 };
    private readonly TextBlock result = new() { Name = "ZramResult", FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly SelectableTextBlock log = new() { Name = "ZramLog", FontFamily = IlmenyxTheme.Mono, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
    private readonly ScrollViewer logScroll;
    private readonly Button refresh = new() { Name = "BtnZramRefresh", Content = "Verificar", Padding = new Thickness(10, 4), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
    private bool busy;
    public ZramPlan? Plan { get; private set; }

    public ZramPanel(ZramServices services, Func<Window?> owner, CancellationToken lifetime)
    {
        this.services = services; this.owner = owner; this.lifetime = lifetime; Spacing = 6;
        refresh.Click += async (_, _) => await RefreshAsync();
        logScroll = new ScrollViewer { MaxHeight = 220, Content = log, IsVisible = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Children.Add(UiKit.SectionHeader("MEMÓRIA COMPRIMIDA (ZRAM)", refresh));
        Children.Add(new TextBlock { Text = "Cria uma área de troca comprimida dentro da própria RAM, pelo caminho oficial da sua distribuição. Ajuda PCs com até 16 GB que travam quando a memória enche (jogo + navegador).", Foreground = Palette.Faint, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
        Children.Add(headline); Children.Add(summary); Children.Add(findings); Children.Add(actions); Children.Add(result); Children.Add(logScroll);
        Loaded += async (_, _) => { if (Plan is null && !busy) await RefreshAsync(); };
    }

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; refresh.IsEnabled = false;
        try { Show(ZramPlanner.Plan(await services.Collect(lifetime))); }
        catch (OperationCanceledException) { headline.Text = "Verificação cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException) { headline.Text = "Falhou: " + e.Message; }
        finally { busy = false; refresh.IsEnabled = true; }
    }

    private void Show(ZramPlan plan)
    {
        Plan = plan;
        headline.Text = plan.Headline;
        headline.Foreground = plan.State switch { ZramState.Active or ZramState.ActiveOurs or ZramState.NotNeeded => Palette.Success, ZramState.Recommended => Palette.AccentText, ZramState.ConfiguredInactive or ZramState.ForeignConfig => Palette.Warn, _ => Palette.Fg };
        summary.Text = plan.Summary;
        findings.Children.Clear();
        foreach (var f in plan.Findings) findings.Children.Add(new TextBlock { Text = "•  " + f, FontSize = 11.5, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        actions.Children.Clear();
        if (plan.Actions.Count == 0) return;
        var row = new WrapPanel { ItemSpacing = 10, LineSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var scripts = new StackPanel { Spacing = 8 };
        foreach (var a in plan.Actions)
        {
            var b = a.Kind == ZramActionKind.Apply ? UiKit.Primary("BtnZram" + a.Kind, a.Button) : new Button { Name = "BtnZram" + a.Kind, Content = a.Button, FontSize = 12, Padding = new Thickness(14, 7) };
            b.IsEnabled = services.RunPrivileged is not null;
            if (services.RunPrivileged is null) ToolTip.SetTip(b, "O pedido de senha do sistema (pkexec/polkit) não está instalado.");
            b.Click += async (_, _) => await RunAsync(a);
            row.Children.Add(b);
            scripts.Children.Add(new SelectableTextBlock { Name = "ZramScript" + a.Kind, Text = a.Script, FontFamily = IlmenyxTheme.Mono, FontSize = 11, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        }
        scripts.IsVisible = false;
        var show = new Button { Name = "BtnZramScript", Content = "Ver o script", FontSize = 11.5, Background = Brushes.Transparent, Foreground = Palette.AccentText };
        show.Click += (_, _) => { scripts.IsVisible = !scripts.IsVisible; show.Content = scripts.IsVisible ? "Esconder o script" : "Ver o script"; };
        row.Children.Add(show);
        if (plan.Actions.Any(a => !a.Tested)) row.Children.Add(UiKit.Tag("NÃO TESTADO", Palette.Warn));
        actions.Children.Add(row); actions.Children.Add(scripts);
    }

    private void Log(string line)
    {
        void Append() { log.Text = (log.Text is { Length: > 40000 } t ? t[^30000..] : log.Text) + line + "\n"; logScroll.ScrollToEnd(); }
        if (Dispatcher.UIThread.CheckAccess()) Append(); else Dispatcher.UIThread.Post(Append);
    }

    public async Task RunAsync(ZramAction a)
    {
        if (busy || services.RunPrivileged is not { } runner || owner() is not { } window) return;
        string message = string.Join("\n", a.Steps.Select(s => "• " + s)) + (a.Risks.Count > 0 ? "\n\nRiscos:\n" + string.Join("\n", a.Risks.Select(r => "! " + r)) : "") +
            "\n\nO sistema vai pedir a sua senha (o Ilmenyx não a vê). O script exato está em \"Ver o script\"." + (a.Tested ? "" : "\n\nNÃO TESTADO nesta distribuição.");
        if (!await ConfirmDialog.AskAsync(window, a.Title + "?", message, a.Button, "Cancelar")) { result.Text = "Cancelado: nada foi feito."; result.Foreground = Palette.Muted; result.IsVisible = true; return; }
        busy = true; refresh.IsEnabled = false;
        foreach (var b in actions.GetLogicalDescendantsOfType()) b.IsEnabled = false;
        log.Text = ""; logScroll.IsVisible = true; result.IsVisible = true; result.Foreground = Palette.Muted; result.Text = "Executando (pode levar alguns minutos se precisar baixar o pacote)…";
        try
        {
            Log("$ pkexec /bin/sh -c <script fixo: " + a.Title + ">");
            var (ok, msg) = await runner(a.Script, Log, lifetime);
            Log("== " + msg);
            var after = ZramPlanner.Plan(await services.Collect(lifetime));
            bool verified = ok && (a.Kind == ZramActionKind.Apply ? after.State is ZramState.ActiveOurs : after.State is not (ZramState.ActiveOurs or ZramState.ConfiguredInactive));
            busy = false; Show(after); busy = true;
            result.Text = verified ? (a.Kind == ZramActionKind.Apply ? "Concluído e conferido: ZRAM ativo." : "Desfeito e conferido.") : ok ? "O script terminou, mas a conferência não bateu; veja o registro." : msg;
            result.Foreground = verified ? Palette.Success : Palette.Warn;
        }
        catch (OperationCanceledException) { result.Text = "Interrompido."; }
        catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception) { result.Text = "Falhou: " + e.Message; Log(result.Text); }
        finally { busy = false; refresh.IsEnabled = true; foreach (var b in actions.GetLogicalDescendantsOfType()) b.IsEnabled = services.RunPrivileged is not null || b.Name == "BtnZramScript"; }
    }
}

internal static class ZramPanelExtensions
{
    public static IEnumerable<Button> GetLogicalDescendantsOfType(this Control c) => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(c).OfType<Button>();
}
