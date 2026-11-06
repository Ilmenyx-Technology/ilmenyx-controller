// New Ilmenyx additions (layout follows the original RESET OC tab: GPU adjustments, stability verifier, recent errors, BIOS guide): see LICENSING.md.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed record OcServices(IStressEngine Engine, Func<long> AvailableMemory, Func<CancellationToken, Task<double?>> CpuScore,
    Func<DateTimeOffset, CancellationToken, Task<KernelHardwareErrors>> Errors, Func<(string Vendor, string Name)> Board,
    Func<CancellationToken, Task<IReadOnlyList<NvidiaTuningState>>> Nvidia, Func<string> CpuName,
    IGpuStressEngine? Gpu = null, Func<CancellationToken, Task<double?>>? GpuScore = null, Func<double?>? GpuTemp = null);

/// <summary>
/// Overclock helper. Nothing here changes hardware: GPU limits are validated and printed as commands for the user's terminal,
/// the BIOS guide is text, and the verifier is a bounded CPU + RAM stress that starts only after a visible confirmation.
/// </summary>
public sealed class OcPage : StackPanel
{
    private readonly OcServices services;
    private readonly UsageViewModel usage;
    private readonly Func<Window?> owner;
    private readonly IlmenyxSettings settings;
    private readonly string settingsPath;
    private readonly CancellationToken lifetime;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly TextBlock gpuState = new() { Name = "OcGpuState", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
    private readonly TextBox power = new() { Name = "OcPower", Width = 90 };
    private readonly TextBox commands = new() { Name = "OcCommands", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("monospace"), IsVisible = false };
    private readonly TextBlock gpuResult = new() { Name = "OcGpuResult", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted };
    private readonly Button gen = new() { Name = "BtnOcCommands", Content = "GERAR COMANDOS" }, reset = new() { Name = "BtnOcResetCommands", Content = "COMANDOS DE RESTAURAÇÃO" };
    private readonly TextBox label = new() { Name = "OcLabel", PlaceholderText = "ex.: Curve Optimizer -15", Width = 280, MaxLength = 80 };
    private readonly TextBox tempLimit = new() { Name = "OcTempLimit", Text = "95", Width = 80 };
    private readonly Button start = UiKit.Primary("BtnOcCheck", "INICIAR VERIFICAÇÃO", 22, 11);
    private readonly TextBlock hint = new() { Name = "OcHint", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock status = new() { Name = "OcStatus", Text = "Parado", FontSize = 26, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar bar = new() { Name = "OcProgress", Minimum = 0, Maximum = 1, Height = 6, MinHeight = 6, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Border verdictCard = new() { Name = "OcVerdictCard", IsVisible = false, CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius), Padding = new Thickness(18, 14), Margin = new Thickness(0, 16, 0, 0), Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
    private readonly TextBlock verdict = new() { Name = "OcVerdict", FontSize = 28, FontWeight = FontWeight.Bold };
    private readonly TextBlock verdictWhy = new() { Name = "OcVerdictWhy", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock verdictRows = new() { Name = "OcResult", TextWrapping = TextWrapping.Wrap, FontFamily = IlmenyxTheme.Mono, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock recent = new() { Name = "OcRecentErrors", TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel history = new() { Name = "OcHistory" };
    private readonly StackPanel guide = new() { Name = "OcGuide", Spacing = 8 };
    private readonly TextBlock guideTitle = new() { Name = "OcGuideTitle", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock guideIntro = new() { Name = "OcGuideIntro", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
    private NvidiaTuningState? gpu;
    private uint? clockCap;
    private TimeSpan duration = TimeSpan.FromMinutes(15);
    private string targetChoice = "cpu";
    private CancellationTokenSource? cts;
    public bool IsRunning => cts is not null;
    public string VerdictText => verdict.Text ?? "";

    public OcPage(OcServices services, UsageViewModel usage, Func<Window?> owner, string settingsPath, CancellationToken lifetime,
        Func<DateTimeOffset>? now = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.services = services; this.usage = usage; this.owner = owner; this.settingsPath = settingsPath; this.lifetime = lifetime; Spacing = 4;
        this.now = now ?? (() => DateTimeOffset.Now); this.delay = delay ?? Task.Delay;
        settings = IlmenyxSettings.Load(settingsPath);
        Children.Add(UiKit.PageHeader("OC", "Verificador de estabilidade, ajustes da GPU e guia de overclock na BIOS · o Ilmenyx não altera hardware nem BIOS"));
        BuildVerifier(); BuildGpuCard(); BuildRecentAndHistory(); BuildGuide();
        _ = LoadAsync();
    }

    private static Control Field(string name, Control c) => new StackPanel { Margin = new Thickness(0, 0, 24, 6), Children = { UiKit.Label(name), new Border { Height = 4 }, c } };

    private void BuildGpuCard()
    {
        AutomationProperties.SetName(power, "Limite de energia da GPU em watts");
        var chips = new UiKit.ChipGroup<uint?>("OcClockPanel", [("Sem teto", null)], null, v => clockCap = v);
        gen.Click += (_, _) => ShowCommands(false); reset.Click += (_, _) => ShowCommands(true);
        gen.IsEnabled = reset.IsEnabled = power.IsEnabled = false;
        foreach (var b in new[] { gen, reset }) { b.FontSize = 12; b.Padding = new Thickness(14, 7); b.Margin = new Thickness(0, 0, 8, 6); b.CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius); }
        gpuState.FontSize = 12.5;
        Children.Add(UiKit.SectionHeader("GPU NVIDIA · limite de energia e teto de clock"));
        Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = { gpuState,
            new WrapPanel { Children = { Field("LIMITE DE ENERGIA (W)", power), Field("TETO DE CLOCK", chips.Panel) } },
            new WrapPanel { Children = { gen, reset } }, commands, gpuResult } }));
        gpuChips = chips;
    }

    private UiKit.ChipGroup<uint?>? gpuChips;

    private async Task LoadAsync()
    {
        IReadOnlyList<NvidiaTuningState> found = [];
        try { found = await services.Nvidia(lifetime); } catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { }
        gpu = found.FirstOrDefault();
        if (gpu is null) gpuState.Text = "Ajustes disponíveis só para placas NVIDIA com driver oficial (nvidia-smi). Nenhuma foi encontrada; AMD e Intel não são ajustados por este módulo. NVIDIA: testes em hardware real não realizados.";
        else
        {
            string F(double? v) => v is double d ? d.ToString("0", CultureInfo.InvariantCulture) + " W" : "—";
            gpuState.Text = $"{gpu.Name} · limite atual {F(gpu.PowerLimitW)} (padrão {F(gpu.PowerDefaultW)}, faixa {F(gpu.PowerMinW)}–{F(gpu.PowerMaxW)}) · clock máximo {(gpu.MaxClockMhz?.ToString(CultureInfo.InvariantCulture) ?? "—")} MHz";
            power.Text = gpu.PowerLimitW?.ToString("0", CultureInfo.InvariantCulture) ?? ""; power.IsEnabled = gen.IsEnabled = reset.IsEnabled = true;
            if (gpu.MaxClockMhz is uint max)
            {
                var panel = gpuChips!.Panel;
                foreach (var (name, cut) in new[] { ("Leve", 300u), ("Equilibrado", 500u), ("Econômico", 800u) })
                {
                    if (max <= cut + 300) continue;
                    uint cap = max - cut; var b = new Button { Content = $"{name} · {cap} MHz", FontSize = 11, Padding = new Thickness(13, 5), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
                    UiKit.ChipStyle(b, false);
                    b.Click += (_, _) => { clockCap = cap; foreach (var other in panel.Children.OfType<Button>()) UiKit.ChipStyle(other, other == b); };
                    panel.Children.Add(b);
                }
                foreach (var first in panel.Children.OfType<Button>().Take(1)) first.Click += (_, _) => { foreach (var other in panel.Children.OfType<Button>()) UiKit.ChipStyle(other, other == first); };
            }
        }
        await RefreshRecentAsync();
    }

    private void ShowCommands(bool restore)
    {
        if (gpu is null) return;
        try
        {
            IReadOnlyList<string> list;
            if (restore) list = NvidiaTuning.ResetCommands(gpu);
            else
            {
                if (!double.TryParse(power.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double watts) && !double.TryParse(power.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out watts))
                { gpuResult.Text = "Informe o limite de energia em watts."; return; }
                list = NvidiaTuning.Commands(gpu, watts, clockCap);
            }
            commands.Text = string.Join("\n", list); commands.IsVisible = true;
            gpuResult.Text = "Copie e execute no terminal: alterar limites exige privilégios e o Ilmenyx nunca usa sudo por conta própria. Rode o verificador depois para confirmar a estabilidade. O ajuste volta ao padrão ao reiniciar o driver/PC ou com os comandos de restauração.";
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or InvalidOperationException) { gpuResult.Text = e.Message; commands.IsVisible = false; }
    }

    private void BuildVerifier()
    {
        bool gpuUsable = services.Gpu is { UnavailableReason: null };
        var choices = new List<(string, string)> { ("CPU + RAM", "cpu") };
        if (gpuUsable) { choices.Add(("GPU", "gpu")); choices.Add(("CPU + RAM + GPU", "both")); }
        var target = new UiKit.ChipGroup<string>("OcTargetPanel", choices, "cpu", t => targetChoice = t);
        if (!gpuUsable)
        {
            var gpuChip = new Button { Name = "OcTargetGpu", Content = "GPU", FontSize = 11, Padding = new Thickness(13, 5), CornerRadius = new CornerRadius(IlmenyxTheme.Pill), BorderBrush = Palette.Line, BorderThickness = new Thickness(1), IsEnabled = false };
            ToolTip.SetTip(gpuChip, "Teste de GPU indisponível: " + (services.Gpu?.UnavailableReason ?? "módulo Vulkan não carregado") + ".");
            target.Panel.Children.Add(gpuChip);
        }
        var dur = new UiKit.ChipGroup<int>("OcDurPanel", [("5 MIN", 5), ("15 MIN", 15), ("30 MIN", 30), ("60 MIN", 60)], 15, m => duration = TimeSpan.FromMinutes(m));
        AutomationProperties.SetName(label, "Descrição da configuração testada"); AutomationProperties.SetName(tempLimit, "Limite de temperatura da CPU");
        label.HorizontalAlignment = tempLimit.HorizontalAlignment = power.HorizontalAlignment = HorizontalAlignment.Left;
        start.Click += async (_, _) => { if (cts is not null) cts.Cancel(); else await StartAsync(); };
        verdictWhy.FontSize = 12.5; verdictWhy.Margin = new Thickness(0, 2, 0, 0);
        verdictCard.Child = new StackPanel { Children = { UiKit.Label("VEREDITO"), verdict, verdictWhy, verdictRows } };
        // Hero: the verifier state (stopped, running, stable/unstable) dominates; limits go to a compact card below.
        start.MinWidth = 190; start.HorizontalContentAlignment = HorizontalAlignment.Center;
        hint.MaxWidth = 240; hint.Margin = new Thickness(0, 8, 0, 0); hint.TextAlignment = TextAlignment.Right; hint.HorizontalAlignment = HorizontalAlignment.Right;
        var state = new StackPanel { Spacing = 4, Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { UiKit.Icon(Icons.Oc, 14, Palette.AccentText), UiKit.Label("VERIFICADOR DE ESTABILIDADE") } }, status,
            new TextBlock { Text = "Mede a nota, roda stress pelo tempo escolhido e confere o log do kernel; para no primeiro erro ou no limite de temperatura.", FontSize = 12.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap },
            bar, verdictCard } };
        var action = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 0, 0, 0), Children = { start, hint } };
        var hero = new Grid { ColumnDefinitions = new("*,Auto") }; Grid.SetColumn(action, 1); hero.Children.Add(state); hero.Children.Add(action);
        Children.Add(BenchPage.Themed(UiKit.Hero(hero, height: double.NaN, name: "OcHero")));
        Children.Add(UiKit.SectionHeader("Teste e limites"));
        Children.Add(UiKit.Card(new WrapPanel { Children = { Field("TESTAR", target.Panel), Field("DURAÇÃO", dur.Panel), Field("DESCRIÇÃO", label), Field("LIMITE °C", tempLimit) } }));
        Controls = [dur.Panel, target.Panel];
    }

    private Control[] Controls = [];

    /// <summary>Recent kernel errors and the local history side by side (two equal columns, still readable at the 640 px minimum).</summary>
    private void BuildRecentAndHistory()
    {
        var refresh = new Button { Name = "BtnOcRefreshErrors", Content = "ATUALIZAR", FontSize = 11.5, Padding = new Thickness(12, 5), Background = Palette.Card, Foreground = Palette.Muted, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Margin = new Thickness(0, 4, 0, 0) };
        refresh.Click += async (_, _) => await RefreshRecentAsync();
        recent.FontSize = 13;
        var errors = UiKit.Card(new StackPanel { Spacing = 6, Children = { UiKit.Label("ERROS DE HARDWARE RECENTES (LOG DO KERNEL · 7 DIAS)"), recent, new TextBlock { Text = "Leitura somente do journal (journalctl -k). Conta linhas de MCE, EDAC, PCIe AER e reinícios de GPU; não prova a causa.", FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap }, refresh } }, new Thickness(0, 0, 6, 0));
        var past = UiKit.Card(new StackPanel { Spacing = 6, Children = { UiKit.Label("HISTÓRICO DE VERIFICAÇÕES (LOCAL)"), history } }, new Thickness(6, 0, 0, 0));
        Grid.SetColumn(past, 1);
        Children.Add(UiKit.SectionHeader("Saúde e histórico"));
        Children.Add(new Grid { ColumnDefinitions = new("*,*"), Children = { errors, past } });
        BuildHistory();
    }

    public async Task RefreshRecentAsync()
    {
        recent.Text = "Lendo…";
        KernelHardwareErrors e;
        try { e = await services.Errors(now().AddDays(-7), lifetime); } catch (OperationCanceledException) { return; }
        recent.Text = e.Error is not null ? "Não foi possível ler o log do kernel: " + e.Error + "."
            : e.Total == 0 ? "Nenhum erro de hardware nem reinício de GPU nos últimos 7 dias."
            : $"{e.Total} ocorrência(s): CPU/MCE {e.Cpu}, memória/EDAC {e.Memory}, PCIe {e.Pcie}, GPU {e.Gpu}. Última: {e.Last}";
    }


    private void BuildHistory()
    {
        history.Children.Clear();
        var runs = settings.BenchRuns.Where(r => r.Kind == "OC").Take(20).ToList();
        if (runs.Count == 0) { history.Children.Add(new TextBlock { Text = "Nenhuma verificação concluída ainda.", Foreground = Palette.Muted, FontSize = 12 }); return; }
        bool first = true;
        foreach (var r in runs)
        {
            history.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Padding = new Thickness(0, 6), Child = new StackPanel { Spacing = 1, Children = {
                new TextBlock { Text = r.At.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), FontSize = 11, Foreground = Palette.Faint },
                new TextBlock { Text = r.Summary, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = r.Summary.StartsWith("INSTÁVEL", StringComparison.Ordinal) ? Palette.Error : Palette.Fg } } } });
            first = false;
        }
    }

    private void BuildGuide()
    {
        var (vendor, name) = services.Board();
        var (title, intro, steps) = OcGuideBuilder.Build(vendor, name, services.CpuName());
        guideTitle.Text = title; guideIntro.Text = intro;
        foreach (var s in steps)
            guide.Children.Add(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8), Child = new StackPanel { Spacing = 2, Children = {
                new TextBlock { Text = $"{s.Number} · {s.Title}", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = s.Where, FontFamily = IlmenyxTheme.Mono, FontSize = 12, Foreground = Palette.AccentText, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = s.Text, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 12 } } } });
        guideIntro.FontSize = 12.5;
        Children.Add(UiKit.SectionHeader("Guia de overclock na BIOS"));
        Children.Add(UiKit.Card(new StackPanel { Spacing = 6, Children = { guideTitle, guideIntro, guide } }));
    }

    public async Task StartAsync()
    {
        if (cts is not null || owner() is not { } window) return;
        hint.Text = "";
        if (!double.TryParse(tempLimit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double limit) && !double.TryParse(tempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out limit)) { hint.Text = "Limite de temperatura inválido."; return; }
        bool wantCpu = targetChoice is "cpu" or "both", wantGpu = targetChoice is "gpu" or "both";
        var options = new OcCheckOptions(wantCpu, duration, (label.Text ?? "").Trim(), limit, wantGpu, 88);
        if (OcCheckOptions.Validate(options) is { } why) { hint.Text = why; return; }
        long available = services.AvailableMemory();
        if (wantCpu && StressMemory.Plan(available, 0.6) == 0) { hint.Text = "Pouca memória disponível: é preciso sobrar pelo menos 1 GiB livre."; return; }
        string guard = usage.Last?.CpuTempC is null
            ? "ATENÇÃO: nenhum sensor de temperatura da CPU está sendo lido; o corte automático por temperatura NÃO funcionará."
            : string.Create(CultureInfo.InvariantCulture, $"O teste é interrompido se a CPU chegar a {limit:0} °C.");
        if (!await ConfirmDialog.AskAsync(window, "Iniciar verificação", $"Primeiro mede a nota{(wantCpu ? " da CPU (render multi-core, alguns minutos)" : "")}{(wantCpu && wantGpu ? " e" : "")}{(wantGpu ? $" da GPU ({services.Gpu!.AdapterName}, cena de jogo)" : "")} e depois roda stress de {(wantCpu && wantGpu ? "CPU + RAM + GPU" : wantGpu ? "GPU" : "CPU + RAM")} por {duration.TotalMinutes:0} min{(wantCpu ? " (60% da memória disponível, sempre deixando 1 GiB livre)" : "")}. Também para ao primeiro erro de memória ou erro de hardware no log do kernel.\n\n{guard}\n\nSalve seu trabalho antes: o sistema pode ficar lento.\n\nIniciar agora?", "Iniciar", "Cancelar")) return;
        await RunAsync(options, available);
    }

    private async Task RunAsync(OcCheckOptions options, long available)
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        start.Content = "CANCELAR"; foreach (var c in Controls) c.IsEnabled = false; label.IsEnabled = tempLimit.IsEnabled = false; verdictCard.IsVisible = false;
        var progress = new Progress<(string Status, double Fraction)>(p => { status.Text = p.Status; bar.Value = p.Fraction; });
        var kernel = new Func<DateTimeOffset, Task<KernelHardwareErrors>>(since => services.Errors(since, cts?.Token ?? CancellationToken.None));
        OcCheckResult result;
        try { result = await OcCheckRunner.RunAsync(options, services.Engine, available, services.CpuScore, () => usage.Last?.CpuTempC, kernel, delay, now, cts.Token, progress, services.Gpu, services.GpuScore, services.GpuTemp); }
        finally { cts.Dispose(); cts = null; start.Content = "INICIAR VERIFICAÇÃO"; foreach (var c in Controls) c.IsEnabled = true; Controls[1].IsEnabled = true; label.IsEnabled = tempLimit.IsEnabled = true; bar.Value = 0; }
        if (result.Status == OcCheckStatus.Cancelled) { status.Text = "Verificação cancelada: nenhum veredito é publicado."; return; }
        if (result.Status == OcCheckStatus.Unavailable) { status.Text = "Não executada: " + result.FailureReason; return; }
        bool stable = result.Stable == true;
        status.Text = $"Concluído em {result.Elapsed:mm\\:ss}.";
        verdict.Text = stable ? "ESTÁVEL" : "INSTÁVEL";
        verdictWhy.Text = stable ? $"Nenhum erro em {options.Duration.TotalMinutes:0} min de stress. Para Curve Optimizer, use o PC normalmente por um dia e confira os erros recentes." : "Motivo: " + result.FailureReason + ". Volte um passo no ajuste e teste de novo.";
        verdictCard.Background = stable ? Palette.Card : Palette.WarnBg; verdict.Foreground = stable ? Palette.Success : Palette.Error;
        verdictWhy.Foreground = stable ? Palette.Muted : Palette.Fg; verdictRows.Foreground = Palette.Muted;
        var rows = new List<string> { "Configuração: " + (options.Label.Length > 0 ? options.Label : "sem descrição") };
        if (result.CpuScore is double score) rows.Add("Nota CPU (render): " + score.ToString("N0", CultureInfo.InvariantCulture) + " pts (experimental)");
        if (result.GpuScore is double gscore) rows.Add("Nota GPU (cena): " + gscore.ToString("N0", CultureInfo.InvariantCulture) + " pts (experimental)");
        if (result.MaxGpuTemp is double gt) rows.Add("Temp. máx. GPU: " + gt.ToString("0", CultureInfo.InvariantCulture) + " °C");
        if (result.MaxCpuTemp is double t) rows.Add("Temp. máx. CPU: " + t.ToString("0", CultureInfo.InvariantCulture) + " °C");
        rows.Add($"RAM: {result.MemoryPasses} passes, {result.MemoryErrors} erros");
        rows.Add(result.Errors.Error is null ? $"Erros de hardware no log do kernel: {result.Errors.Total}" : "Erros de hardware no log do kernel: NÃO VERIFICADO");
        if (result.Note is not null) rows.Add(result.Note);
        verdictRows.Text = string.Join("\n", rows); verdictCard.IsVisible = true;
        settings.AddBenchRun(new(now(), "OC", (stable ? "estável" : "INSTÁVEL") + " · " + (options.Label.Length > 0 ? options.Label : "sem descrição") + (result.CpuScore is double s2 ? " · CPU " + s2.ToString("N0", CultureInfo.InvariantCulture) : "") + (result.GpuScore is double gs2 ? " · GPU " + gs2.ToString("N0", CultureInfo.InvariantCulture) : ""), stable ? 1.0 : 0.15));
        try { settings.Save(settingsPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text += " · Histórico não salvo: " + e.Message; }
        BuildHistory(); await RefreshRecentAsync();
    }
}
