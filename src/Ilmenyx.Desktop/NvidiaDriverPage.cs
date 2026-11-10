// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// "Driver NVIDIA" tab (FERRAMENTAS): read-only diagnosis, a plan in plain language, and one-click actions that run a FIXED script through
// polkit only after the real apt-get -s simulation was accepted and the user confirmed exactly what will be done (AGENTS.md pattern).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <param name="Diagnose">Read-only diagnosis (no root).</param>
/// <param name="Simulate">Real `apt-get -s` for an action that has simulation arguments (no root).</param>
/// <param name="RunPrivileged">Runs the fixed script as root through pkexec, streaming lines; null when polkit is missing.</param>
public sealed record NvidiaDriverServices(
    Func<CancellationToken, Task<NvidiaDiagnosis>> Diagnose,
    Func<NvidiaAction, CancellationToken, Task<AptSimulation>> Simulate,
    Func<string, Action<string>, CancellationToken, Task<(bool Ok, string Message)>>? RunPrivileged)
{
    public static NvidiaDriverServices Real()
    {
        var probe = new NvidiaDriverProbe();
        return new(probe.DiagnoseAsync, probe.SimulateAsync, NvidiaDriverRunner.Available ? NvidiaDriverRunner.RunAsync : null);
    }
}

public sealed class NvidiaDriverPage : StackPanel
{
    private readonly NvidiaDriverServices services;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly TextBlock status = new() { Name = "NvStatus", Text = L.T("Clique em Verificar."), Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock gpuName = new() { Name = "NvGpuName", Text = "—", FontSize = 24, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock stateText = new() { Name = "NvState", Text = L.T("Verificando…"), FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock summary = new() { Name = "NvSummary", FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly WrapPanel tags = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly Border hero;
    private readonly Border empty;
    private readonly Grid tiles = new() { Name = "NvTiles", Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock tDriver = new() { Name = "NvTileDriver", Text = "—" }, tVersion = new() { Name = "NvTileVersion", Text = "—" }, tKernel = new() { Name = "NvTileKernel", Text = "—" },
        tSecure = new() { Name = "NvTileSecureBoot", Text = "—" }, tPrime = new() { Name = "NvTilePrime", Text = "—" };
    private readonly TextBlock nDriver = new(), nVersion = new(), nKernel = new(), nSecure = new(), nPrime = new();
    private readonly StackPanel planPanel = new() { Name = "NvPlan", Spacing = 12 };
    private readonly StackPanel findings = new() { Name = "NvFindings", Spacing = 5 };
    private readonly SelectableTextBlock log = new() { Name = "NvLog", Text = "", FontFamily = IlmenyxTheme.Mono, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
    private readonly ScrollViewer logScroll;
    private readonly Border logCard;
    private readonly TextBlock verify = new() { Name = "NvVerify", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, IsVisible = false };
    private readonly Border rebootCard;
    private readonly TextBlock rebootHelp = new() { Name = "NvRebootHelp", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 12, IsVisible = false };
    private readonly Button scan;
    private bool busy;
    private NvidiaDiagnosis? diagnosis;
    public NvidiaPlan? Plan { get; private set; }

    public NvidiaDriverPage(NvidiaDriverServices services, Func<Window?> owner, CancellationToken lifetime)
    {
        this.services = services; this.owner = owner; this.lifetime = lifetime; Spacing = 0;
        scan = UiKit.Primary("BtnNvScan", L.T("Verificar"));
        scan.Click += async (_, _) => await RefreshAsync();
        Children.Add(UiKit.PageHeader("Driver NVIDIA", L.T("Instala, verifica, repara e atualiza o driver NVIDIA certo para esta placa e este sistema. Tudo é simulado antes; nada muda sem o seu clique e a senha do sistema."), scan));
        Children.Add(status);

        var heroText = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { UiKit.Label(L.T("PLACA NVIDIA")), gpuName, stateText, summary, tags } };
        hero = UiKit.Hero(heroText, height: double.NaN, name: "NvHero"); hero.MinHeight = 170;
        Children.Add(hero);
        empty = UiKit.Card(new StackPanel { Spacing = 6, Children =
        {
            new TextBlock { Text = L.T("Nenhuma placa NVIDIA neste computador"), FontSize = 15, FontWeight = FontWeight.SemiBold },
            new TextBlock { Name = "NvEmpty", Text = L.T("Esta aba instala e cuida do driver de placas NVIDIA. Não encontramos nenhuma no barramento PCI; para Intel e AMD o driver já vem com o kernel e o Mesa da sua distribuição (veja a página Drivers)."), Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap },
        } });
        empty.IsVisible = false; Children.Add(empty);

        var cells = new[]
        {
            UiKit.StatTile(L.T("DRIVER EM USO"), tDriver, nDriver, Icons.Gpu), UiKit.StatTile(L.T("VERSÃO"), tVersion, nVersion, Icons.Drivers),
            UiKit.StatTile(L.T("KERNEL · CABEÇALHOS"), tKernel, nKernel, Icons.System), UiKit.StatTile("SECURE BOOT", tSecure, nSecure, Icons.Compat),
            UiKit.StatTile("PRIME", tPrime, nPrime, Icons.Energy),
        };
        foreach (var t in new[] { tDriver, tVersion, tKernel, tSecure, tPrime }) { t.FontSize = 17; t.TextTrimming = TextTrimming.CharacterEllipsis; }
        foreach (var c in cells) tiles.Children.Add(c);
        tiles.SizeChanged += (_, _) => Flow(cells);
        Children.Add(tiles);

        var rebootButton = UiKit.Primary("BtnNvReboot", L.T("Como reiniciar"));
        rebootButton.Click += (_, _) =>
        {
            rebootHelp.Text = L.T("Salve o que estiver aberto e reinicie pelo menu do sistema (Menu → Sair → Reiniciar). O Ilmenyx não reinicia o computador sozinho. Depois do boot, abra esta aba: ela mostra \"Funcionando\" quando o nvidia-smi responder.");
            rebootHelp.IsVisible = true;
        };
        rebootCard = new Border
        {
            Name = "NvRebootCard", Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(18, 14), Margin = new Thickness(0, 14, 0, 0), IsVisible = false,
            Child = new StackPanel { Spacing = 8, Children =
            {
                new TextBlock { Name = "NvReboot", Text = L.T("Reinicie o computador para ativar o driver."), Foreground = Palette.Warn, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                rebootHelp, rebootButton,
            } },
        };
        Children.Add(rebootCard);

        Children.Add(UiKit.SectionHeader(L.T("Plano")));
        Children.Add(planPanel);
        Children.Add(UiKit.SectionHeader(L.T("Diagnóstico (somente leitura)")));
        Children.Add(UiKit.Card(findings));
        logScroll = new ScrollViewer { MaxHeight = 300, Content = log, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        logCard = UiKit.Card(new StackPanel { Spacing = 8, Children = { verify, logScroll } });
        logCard.IsVisible = false;
        Children.Add(UiKit.SectionHeader(L.T("Registro")));
        Children.Add(logCard);
        Loaded += async (_, _) => { if (diagnosis is null) await RefreshAsync(); };
    }

    private void Flow(Border[] cells)
    {
        double w = tiles.Bounds.Width;
        int cols = w >= 980 ? 5 : w >= 560 ? 3 : 2;
        if (tiles.ColumnDefinitions.Count == cols * 2 - 1) return;
        tiles.ColumnDefinitions.Clear(); tiles.RowDefinitions.Clear();
        for (int c = 0; c < cols; c++) { if (c > 0) tiles.ColumnDefinitions.Add(new ColumnDefinition(12, GridUnitType.Pixel)); tiles.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star)); }
        int rows = (cells.Length + cols - 1) / cols;
        for (int r = 0; r < rows; r++) { if (r > 0) tiles.RowDefinitions.Add(new RowDefinition(12, GridUnitType.Pixel)); tiles.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
        for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], (i % cols) * 2); Grid.SetRow(cells[i], (i / cols) * 2); }
    }

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; scan.IsEnabled = false; status.Text = L.T("Lendo placa, sistema, kernel e pacotes (somente leitura)…");
        try
        {
            var d = await services.Diagnose(lifetime);
            Show(d);
            status.Text = L.T($"Verificado às {DateTime.Now:HH:mm:ss}. Nada foi alterado.");
        }
        catch (OperationCanceledException) { status.Text = L.T("Verificação cancelada."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException) { status.Text = "Falhou: " + e.Message; }
        finally { Lock(false); }
    }

    private void Show(NvidiaDiagnosis d)
    {
        diagnosis = d; var plan = Plan = NvidiaPlanner.Plan(d);
        bool none = plan.State == NvidiaDriverState.NoNvidiaGpu;
        hero.IsVisible = !none; tiles.IsVisible = !none; empty.IsVisible = none;
        var gpu = d.Primary;
        gpuName.Text = gpu is null ? "—" : NvidiaGenerations.MarketingName(gpu.Name);
        stateText.Text = plan.Headline;
        stateText.Foreground = plan.State switch
        {
            NvidiaDriverState.Working => Palette.Success, NvidiaDriverState.Broken or NvidiaDriverState.Unsupported => Palette.Error,
            NvidiaDriverState.NeedsReboot or NvidiaDriverState.LoadedButSmiFails => Palette.Warn, _ => Palette.Fg,
        };
        summary.Text = plan.Summary;
        tags.Children.Clear();
        if (gpu is not null)
        {
            tags.Children.Add(UiKit.Tag(NvidiaGenerations.Name(gpu.Generation) + (gpu.Chip.Length > 0 ? " · " + gpu.Chip : "")));
            tags.Children.Add(UiKit.Tag(gpu.Support.Branch is { } b ? $"ramo {b}{(gpu.Support.Legacy ? L.T(" (legado)") : "")}" : L.T("sem driver suportado")));
            tags.Children.Add(UiKit.Tag(d.Distro.Name));
            if (d.Hybrid) tags.Children.Add(UiKit.Tag(L.T("híbrido (PRIME)")));
        }
        tDriver.Text = d.KernelDriver; nDriver.Text = d.KernelDriver.StartsWith("nvidia", StringComparison.Ordinal) ? L.T("driver proprietário NVIDIA") : d.KernelDriver == "nouveau" ? L.T("driver livre do kernel") : L.T("nenhum driver ligado à placa");
        tVersion.Text = d.SmiVersion ?? d.LoadedVersion ?? (d.InstalledDriver is { } i ? i.Version.Split('-')[0] : "—");
        nVersion.Text = d.SmiVersion is not null ? L.T("nvidia-smi responde") : d.LoadedVersion is not null ? L.T("módulo carregado") : d.InstalledDriver is not null ? L.T("instalado, não carregado") : L.T("não instalado");
        tKernel.Text = d.RunningKernel.Length > 0 ? d.RunningKernel : "—";
        nKernel.Text = d.RunningHeaders is { } h ? h.Complete ? L.T("cabeçalhos completos") : h.Present ? L.T("cabeçalhos INCOMPLETOS") : L.T("sem cabeçalhos") : "—";
        nKernel.Foreground = d.RunningHeaders is { Complete: true } ? Palette.Faint : Palette.Warn;
        tSecure.Text = NvidiaPlanner.SecureBootText(d.SecureBoot); nSecure.Text = d.SecureBoot == SecureBootState.Enabled ? L.T("exige registrar chave MOK") : L.T("sem chave extra");
        tPrime.Text = d.PrimeMode ?? (d.Hybrid ? "—" : L.T("não híbrido")); nPrime.Text = d.Hybrid ? (d.PrimeSelectAvailable ? L.T("prime-select disponível") : L.T("sem prime-select")) : L.T("uma GPU só");
        rebootCard.IsVisible = plan.State == NvidiaDriverState.NeedsReboot || rebootCard.IsVisible && plan.State != NvidiaDriverState.Working;

        findings.Children.Clear();
        foreach (var f in plan.Findings) findings.Children.Add(new TextBlock { Text = f, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });

        planPanel.Children.Clear();
        if (plan.Guidance is { } g) planPanel.Children.Add(UiKit.Card(new TextBlock { Name = "NvGuidance", Text = g, TextWrapping = TextWrapping.Wrap }, new Thickness(0)));
        if (plan.Actions.Count == 0 && plan.Guidance is null)
            planPanel.Children.Add(UiKit.Card(new TextBlock { Name = "NvNothing", Text = none ? L.T("Nada a fazer.") : plan.State == NvidiaDriverState.Working ? L.T("Nada a fazer: o driver está funcionando e em dia.") : L.T("Nada a fazer agora."), Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap }, new Thickness(0)));
        foreach (var a in plan.Actions) planPanel.Children.Add(ActionCard(a));
    }

    private Border ActionCard(NvidiaAction a)
    {
        string key = a.Kind.ToString();
        var head = new WrapPanel { Children = { new TextBlock { Text = a.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 10, 6), VerticalAlignment = VerticalAlignment.Center } } };
        if (!a.Tested) head.Children.Add(UiKit.Tag(L.T("NÃO TESTADO"), Palette.Warn));
        if (a.SimulationArguments is not null) head.Children.Add(UiKit.Tag(L.T("simulado antes (apt-get -s)")));
        var body = new StackPanel { Spacing = 5, Children = { head } };
        if (a.Blocked is { } blocked) body.Children.Add(new TextBlock { Text = blocked, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted });
        foreach (var s in a.Steps) body.Children.Add(new TextBlock { Text = "•  " + s, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
        foreach (var r in a.Risks) body.Children.Add(new TextBlock { Text = "!  " + r, FontSize = 12, Foreground = Palette.Warn, TextWrapping = TextWrapping.Wrap });
        var script = new SelectableTextBlock { Name = "NvScript" + key, Text = a.Script, FontFamily = IlmenyxTheme.Mono, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        var run = UiKit.Primary("BtnNv" + key, a.Button);
        run.IsEnabled = a.Blocked is null && a.Script.Length > 0 && services.RunPrivileged is not null && !busy;
        if (services.RunPrivileged is null && a.Blocked is null) ToolTip.SetTip(run, L.T("O pedido de senha do sistema (pkexec/polkit) não está instalado."));
        run.Click += async (_, _) => await RunAsync(a);
        var show = new Button { Name = "BtnNvScript" + key, Content = L.T("Ver o script"), FontSize = 11.5, Background = Brushes.Transparent, Foreground = Palette.AccentText, IsVisible = a.Script.Length > 0 };
        show.Click += (_, _) => { script.IsVisible = !script.IsVisible; show.Content = script.IsVisible ? L.T("Esconder o script") : L.T("Ver o script"); };
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0), Children = { run, show } });
        body.Children.Add(script);
        var card = UiKit.Card(body, new Thickness(0));
        card.Name = "NvAction" + key;
        return card;
    }

    private void Log(string line)
    {
        void Append() { log.Text = (log.Text is { Length: > 60000 } t ? t[^40000..] : log.Text) + line + "\n"; logScroll.ScrollToEnd(); }
        if (Dispatcher.UIThread.CheckAccess()) Append(); else Dispatcher.UIThread.Post(Append);
    }

    private void Lock(bool locked)
    {
        busy = locked; scan.IsEnabled = !locked;
        foreach (var b in planPanel.GetLogicalDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("BtnNv", StringComparison.Ordinal) == true && !b.Name.StartsWith("BtnNvScript", StringComparison.Ordinal)))
            b.IsEnabled = !locked && Plan?.Actions.FirstOrDefault(a => "BtnNv" + a.Kind == b.Name) is { Blocked: null } a && a.Script.Length > 0 && services.RunPrivileged is not null;
    }

    public async Task RunAsync(NvidiaAction a)
    {
        if (busy || diagnosis is null || services.RunPrivileged is not { } runner || a.Blocked is not null) return;
        Lock(true); logCard.IsVisible = true; verify.IsVisible = false; log.Text = "";
        try
        {
            string simulated = "";
            if (a.SimulationArguments is { } args)
            {
                status.Text = L.T("Simulando com apt-get -s (sem root, nada muda)…");
                Log("$ apt-get -s " + string.Join(' ', args));
                var sim = await services.Simulate(a, lifetime);
                foreach (var x in sim.Actions.Where(x => x.Kind != "Conf")) Log($"  {x.Kind} {x.Package} {x.Version}");
                if (sim.Summary.Length > 0) Log("  " + sim.Summary);
                var problems = NvidiaPlanner.CheckSimulation(a, sim, diagnosis.RunningKernel);
                if (problems.Count > 0)
                {
                    foreach (var p in problems) Log("RECUSADO: " + p);
                    status.Text = L.T("Plano recusado pela simulação: nada foi feito."); Verified(false, L.T("Plano recusado pela simulação. ") + problems[0]);
                    return;
                }
                Log(L.T("Simulação aceita: nenhum kernel instalado ou removido."));
                var ins = sim.Installs.Select(x => x.Package).ToList(); var rem = sim.Removals.Select(x => x.Package).ToList();
                simulated = L.T($"\n\nSimulação (apt-get -s): {ins.Count} pacote(s) a instalar/atualizar, {rem.Count} a remover.") +
                    (rem.Count > 0 ? "\nRemover: " + string.Join(", ", rem.Take(14)) + (rem.Count > 14 ? L.T($" e mais {rem.Count - 14}") : "") : "");
            }
            if (owner() is not { } window) return;
            string message = string.Join("\n", a.Steps.Select(s => "• " + s)) + simulated +
                (a.Risks.Count > 0 ? "\n\nRiscos:\n" + string.Join("\n", a.Risks.Select(r => "! " + r)) : "") +
                L.T("\n\nO sistema vai pedir a sua senha (o Ilmenyx não a vê). O script exato está em \"Ver o script\".") + (a.Tested ? "" : L.T("\n\nNÃO TESTADO nesta distribuição."));
            if (!await ConfirmDialog.AskAsync(window, a.Title + "?", message, a.Button, L.T("Cancelar"))) { status.Text = L.T("Cancelado: nada foi feito."); Log(L.T("Cancelado pelo usuário.")); return; }
            status.Text = L.T("Executando (pode levar alguns minutos; não desligue o computador)…");
            Log("$ pkexec /bin/sh -c <script fixo: " + a.Title + ">");
            var (ok, msg) = await runner(a.Script, Log, lifetime);
            Log("== " + msg);
            status.Text = L.T("Conferindo o resultado…");
            var after = await services.Diagnose(lifetime);
            var (verified, text) = ok ? NvidiaPlanner.Verify(a.Kind, after) : (false, msg);
            Show(after);
            Verified(verified, text);
            if (verified) { rebootCard.IsVisible = true; ((TextBlock)((StackPanel)rebootCard.Child!).Children[0]).Text = a.Kind == NvidiaActionKind.Revert ? L.T("Reinicie o computador para voltar ao nouveau.") : L.T("Reinicie o computador para ativar o driver NVIDIA."); }
            status.Text = verified ? L.T("Concluído e conferido.") : L.T("Não concluído: veja o registro.");
        }
        catch (OperationCanceledException) { status.Text = "Interrompido."; }
        catch (Exception e) when (e is IOException or TimeoutException or InvalidOperationException or ArgumentException or FileNotFoundException or System.ComponentModel.Win32Exception)
        { status.Text = "Falhou: " + e.Message; Log("Falhou: " + e.Message); }
        finally { Lock(false); }
    }

    private void Verified(bool ok, string text)
    {
        verify.Text = text; verify.Foreground = ok ? Palette.Success : Palette.Error; verify.IsVisible = true;
    }
}
