// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original Stress page: components, limits, start/stop, readouts, temperature chart, log): see LICENSING.md.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>Stress test (CPU and RAM). Real load starts only after the user clicks INICIAR and confirms; temperature, error and time guards stop it automatically.</summary>
public sealed class StressPage : StackPanel
{
    private readonly IStressEngine engine;
    private readonly UsageViewModel usage;
    private readonly Func<Window?> owner;
    private readonly Func<long> availableMemory;
    private readonly CancellationToken lifetime;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CheckBox chkCpu = new() { Name = "ChkCpu", Content = "CPU", IsChecked = true };
    private readonly CheckBox chkRam = new() { Name = "ChkRam", Content = "RAM" };
    private readonly CheckBox chkGpu = new() { Name = "ChkGpu", Content = "GPU", IsEnabled = false };
    private readonly TextBox cpuLimit = new() { Name = "TempLimit", Text = "90", Width = 80 };
    private readonly TextBox gpuLimit = new() { Name = "GpuTempLimit", Text = "85", Width = 80, IsEnabled = false };
    private readonly TextBox duration = new() { Name = "TxtDuration", Text = "10", Width = 80 };
    private readonly Button start = UiKit.Primary("BtnStress", L.T("Iniciar"), 20, 12);
    private readonly TextBlock hint = new() { Name = "StressHint", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12, MaxWidth = 220, Margin = new Thickness(0, 8, 0, 0), TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Border statusCard;
    private readonly Ellipse statusDot = new() { Width = 9, Height = 9, Fill = Palette.Faint, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock stStatus = new() { Name = "StStatus", Text = L.T("Parado"), FontSize = 30, FontWeight = FontWeight.Bold };
    private readonly TextBlock stTemp = new() { Name = "StTemp", Text = "—", FontSize = 30, FontWeight = FontWeight.Bold }, stTempMax = new() { Name = "StTempMax", Text = L.T("máx. —"), FontSize = 12, Foreground = Palette.Muted };
    private readonly TextBlock stGpu = new() { Name = "StGpu", Text = "—" };
    private readonly TextBlock stLoad = new() { Name = "StLoad", Text = "—" }, stRam = new() { Name = "StRam", Text = "—" };
    private readonly UsageChart chart = new() { Name = "TempChart", Height = 150 };
    private readonly StackPanel log = new() { Name = "StressLog" };
    private readonly TextBlock logEmpty = new() { Text = L.T("Nenhum evento ainda."), FontSize = 12, Foreground = Palette.Faint };
    private readonly List<double> temps = [];
    private UiKit.ChipGroup<double>? ramChips; private double ramFraction = 0.6;
    private StressLimits? limits; private DateTime startedAt; private double maxTemp;
    private bool cpuOn, ramOn;
    public bool IsRunning => running;
    public string StatusText => stStatus.Text ?? "";

    private readonly IGpuStressEngine? gpuEngine;
    private readonly Func<double?>? gpuTemperature;
    private bool running, gpuOn;

    /// <param name="gpuEngine">Optional GPU load (Vulkan). Null or with an UnavailableReason keeps the GPU box disabled.</param>
    /// <param name="gpuTemperature">Latest GPU temperature (null = unknown). For a GPU that shares the CPU package the CPU temperature is used.</param>
    public StressPage(IStressEngine engine, UsageViewModel usage, Func<Window?> owner, Func<long> availableMemory, CancellationToken lifetime, IGpuStressEngine? gpuEngine = null, Func<double?>? gpuTemperature = null)
    {
        this.gpuEngine = gpuEngine; this.gpuTemperature = gpuTemperature; this.engine = engine; this.usage = usage; this.owner = owner; this.availableMemory = availableMemory; this.lifetime = lifetime; Spacing = 4;
        Children.Add(UiKit.PageHeader("Stress test", L.T("Carga máxima com corte automático por temperatura")));

        ramChips = new("RamFractionPanel", [("40%", 0.4), ("60%", 0.6), ("80%", 0.8)], 0.6, v => ramFraction = v);
        ramChips.Panel.IsEnabled = false;
        chkRam.IsCheckedChanged += (_, _) => ramChips.Panel.IsEnabled = chkRam.IsChecked == true;
        bool gpuUsable = gpuEngine is { UnavailableReason: null };
        chkGpu.IsEnabled = gpuUsable;
        ToolTip.SetTip(chkGpu, gpuUsable ? L.T($"Stress de GPU (estilo FurMark, Vulkan) em {gpuEngine!.AdapterName}. Carga máxima na GPU.") : L.T("Stress de GPU indisponível: ") + (gpuEngine?.UnavailableReason ?? L.T("módulo Vulkan não carregado")) + ".");
        chkGpu.IsCheckedChanged += (_, _) => gpuLimit.IsEnabled = chkGpu.IsChecked == true && !running;
        Control Field(string label, Control c) => new StackPanel { Margin = new Thickness(0, 0, 24, 8), Children = { UiKit.Label(label), new Border { Height = 4 }, c } };
        foreach (var c in new Control[] { cpuLimit, gpuLimit, duration }) { AutomationProperties.SetName(c, L.T("Campo do stress test")); c.HorizontalAlignment = HorizontalAlignment.Left; }
        start.MinWidth = 150; start.HorizontalContentAlignment = HorizontalAlignment.Center; start.Click += async (_, _) => { if (running) Stop(L.T("Parado pelo usuário")); else await StartAsync(); };

        // Hero: state and CPU temperature dominate, with the single start/stop action.
        var state = new StackPanel { Spacing = 2, Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { statusDot, UiKit.Label("STATUS") } }, stStatus } };
        var temp = new StackPanel { Spacing = 2, Margin = new Thickness(0, 14, 0, 0), Children = {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { UiKit.Icon(Icons.Temperature, 14, Palette.DataTemperature), UiKit.Label(L.T("TEMP. CPU")) } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { stTemp, stTempMax } } } };
        stTempMax.VerticalAlignment = VerticalAlignment.Bottom; stTempMax.Margin = new Thickness(0, 0, 0, 6);
        var action = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 0, 0, 0), Children = { start, hint } };
        var heroGrid = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("Auto,Auto") };
        Grid.SetRow(temp, 1); Grid.SetColumn(action, 1); Grid.SetRowSpan(action, 2);
        heroGrid.Children.Add(state); heroGrid.Children.Add(temp); heroGrid.Children.Add(action);
        statusCard = BenchPage.Themed(UiKit.Hero(heroGrid, height: double.NaN, name: "StatusCard"));
        Children.Add(statusCard);

        var stats = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 12, -5, 0) };
        foreach (var (label, value, note, icon) in new (string, TextBlock, string, string)[] { (L.T("CARGA CPU"), stLoad, L.T("uso total"), Icons.Cpu), (L.T("TEMP. / FPS GPU"), stGpu, L.T("Vulkan · offscreen"), Icons.Gpu), ("RAM", stRam, L.T("passes / erros"), Icons.Memory) })
        {
            value.FontSize = 20;
            var tile = UiKit.StatTile(label, value, new TextBlock { Text = note, Foreground = Palette.Faint }, icon); tile.Margin = new Thickness(5, 0); stats.Children.Add(tile);
        }
        Children.Add(stats);

        Children.Add(UiKit.SectionHeader(L.T("Temperatura da CPU · últimos 5 min")));
        Children.Add(UiKit.Card(new StackPanel { Children = { chart, new TextBlock { Text = L.T("Consumo da CPU (RAPL) e sensores de GPU não são lidos neste teste."), FontSize = 11, Foreground = Palette.Faint, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap } } }));

        // Limits: one compact card.
        var config = new WrapPanel
        {
            Children =
            {
                Field(L.T("COMPONENTES"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { chkCpu, chkRam, chkGpu } }),
                Field(L.T("USO DA RAM DISPONÍVEL"), ramChips.Panel), Field(L.T("LIMITE CPU °C"), cpuLimit), Field(L.T("LIMITE GPU °C"), gpuLimit), Field(L.T("DURAÇÃO (MIN · 0 = SEM LIMITE)"), duration)
            }
        };
        Children.Add(UiKit.SectionHeader(L.T("Componentes e limites")));
        Children.Add(UiKit.Card(config));
        Children.Add(UiKit.SectionHeader(L.T("Registro")));
        Children.Add(UiKit.Card(new StackPanel { Children = { logEmpty, log } }));
        timer.Tick += (_, _) => Tick();
    }

    private void Log(string message)
    {
        logEmpty.IsVisible = false;
        log.Children.Add(new TextBlock { Text = $"{DateTime.Now:HH:mm:ss}  {message}", FontFamily = IlmenyxTheme.Mono, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2) });
        if (log.Children.Count > 200) log.Children.RemoveAt(0);
    }

    private void SetStatus(string text, bool alert)
    {
        stStatus.Text = text;
        var tone = alert ? Palette.Error : running ? Palette.Success : Palette.Fg;
        stStatus.Foreground = tone; statusDot.Fill = alert ? Palette.Error : running ? Palette.Success : Palette.Faint;
    }

    private void Controls(bool running)
    {
        start.Content = running ? L.T("Parar") : L.T("Iniciar");
        chkCpu.IsEnabled = chkRam.IsEnabled = !running; chkGpu.IsEnabled = !running && gpuEngine is { UnavailableReason: null }; gpuLimit.IsEnabled = !running && chkGpu.IsChecked == true; cpuLimit.IsEnabled = duration.IsEnabled = !running; ramChips!.Panel.IsEnabled = !running && chkRam.IsChecked == true;
    }

    public async Task StartAsync()
    {
        hint.Text = "";
        bool cpu = chkCpu.IsChecked == true, ram = chkRam.IsChecked == true;
        bool gpu = chkGpu.IsChecked == true && gpuEngine is { UnavailableReason: null };
        if (!cpu && !ram && !gpu) { hint.Text = L.T("Ative CPU, RAM ou GPU."); return; }
        if (StressGuard.Validate(cpuLimit.Text ?? "", gpuLimit.Text ?? "", gpu, duration.Text ?? "", out var parsed) is { } error) { hint.Text = error; return; }
        if (owner() is not { } window) return;
        if ((cpu || ram || gpu && gpuEngine!.SharesCpuThermals) && usage.Last?.CpuTempC is null)
        {
            if (!await ConfirmDialog.AskAsync(window, "Stress test", usage.IsRunning
                ? L.T("Nenhum sensor de temperatura da CPU foi encontrado (hwmon coretemp/k10temp), então o corte automático por temperatura NÃO funcionará. Continuar mesmo assim?")
                : L.T("O monitor está pausado e não há leitura de temperatura da CPU, então o corte automático por temperatura NÃO funcionará. Retome o monitor ou continue por sua conta e risco. Continuar mesmo assim?"), L.T("Continuar sem corte"), L.T("Cancelar"))) return;
        }
        long available = availableMemory();
        string what = string.Join(" e ", new[] { gpu ? L.T($"GPU ({gpuEngine!.AdapterName}, carga máxima de renderização)") : null, cpu ? $"CPU ({Environment.ProcessorCount} threads a 100%)" : null, ram ? L.T($"RAM ({(ramFraction * 100).ToString("0", CultureInfo.InvariantCulture)}% da memória disponível, sempre deixando 1 GiB livre)") : null }.Where(x => x != null));
        string gpuText = gpu ? L.T($" e da GPU: {parsed!.GpuTempLimit:0} °C") + (gpuEngine!.SharesCpuThermals ? L.T(" (esta GPU divide o chip com a CPU: vale a temperatura da CPU)") : "") : "";
        string time = parsed!.Duration > TimeSpan.Zero ? L.T($"por {parsed.Duration.TotalMinutes:0.##} min") : L.T("SEM LIMITE DE TEMPO (só para quando você clicar em Parar, ou ao atingir o limite de temperatura ou erro)");
        if (!await ConfirmDialog.AskAsync(window, L.T("Iniciar stress test"), L.T($"Isto vai gerar carga máxima em {what} {time}.\n\nLimite de temperatura da CPU: {parsed.CpuTempLimit:0} °C{gpuText} (o teste é interrompido automaticamente). Salve seu trabalho antes: o sistema pode ficar lento e, com pouca RAM livre, instável.\n\nIniciar agora?"), L.T("Iniciar"), L.T("Cancelar"))) return;
        try { if (cpu || ram) engine.Start(cpu, ram, ramFraction, available); }
        catch (InvalidOperationException e) { hint.Text = e.Message; return; }
        if (gpu) gpuEngine!.Start();
        running = true; gpuOn = gpu; limits = parsed; cpuOn = cpu; ramOn = ram; startedAt = DateTime.Now; maxTemp = 0; temps.Clear();
        Controls(true); SetStatus(L.T("Rodando"), false); Log(L.T($"Iniciado · GPU {(gpu ? "sim" : L.T("não"))} · CPU {(cpu ? L.T($"sim (limite {parsed.CpuTempLimit:0} °C)") : L.T("não"))} · RAM {(ram ? L.T($"sim ({(ramFraction * 100).ToString("0", CultureInfo.InvariantCulture)}% da disponível, {engine.MemoryAllocatedBytes / 1048576} MiB)") : L.T("não"))} · {(parsed.Duration > TimeSpan.Zero ? $"{parsed.Duration.TotalMinutes:0.##} min" : L.T("sem limite de tempo"))}"));
        timer.Start();
    }

    public void Stop(string reason)
    {
        if (!running) return;
        running = false; timer.Stop();
        long errors = engine.MemoryErrors, passes = engine.MemoryPasses; bool hadRam = engine.MemoryAllocatedBytes > 0;
        if (engine.Running) engine.Stop();
        if (gpuOn) gpuEngine!.Stop();
        Controls(false); bool aborted = reason.StartsWith(L.T("ABORTADO"), StringComparison.Ordinal);
        SetStatus(aborted ? L.T("Abortado") : L.T("Parado"), aborted);
        Log(L.T($"{reason} · duração {DateTime.Now - startedAt:hh\\:mm\\:ss}") + (cpuOn ? L.T($" · CPU temp. máx. {maxTemp:0.#} °C") : "") + (hadRam ? L.T($" · RAM {passes} passes, {errors} erros") : ""));
    }

    /// <summary>One-second tick: readouts and the abort guards. Public so a host can drive it deterministically.</summary>
    public void Tick()
    {
        if (!running || limits is null) return;
        double? t = usage.Last?.CpuTempC;
        if (t is { } v) { maxTemp = Math.Max(maxTemp, v); temps.Add(v); if (temps.Count > 300) temps.RemoveAt(0); chart.SetValues(temps, 110, 300); }
        stTemp.Text = t is { } x ? x.ToString("0.#", CultureInfo.InvariantCulture) + " °C" : "—"; stTempMax.Text = L.T("máx. ") + (maxTemp > 0 ? maxTemp.ToString("0.#", CultureInfo.InvariantCulture) + " °C" : "—");
        stLoad.Text = UsageViewModel.Pct(usage.Last?.CpuPercent);
        stRam.Text = ramOn ? $"{engine.MemoryPasses} / {engine.MemoryErrors}" : "—";
        double? g = gpuOn ? (gpuEngine!.SharesCpuThermals ? t : gpuTemperature?.Invoke()) : null;
        if (gpuOn) stGpu.Text = (g is { } gv ? gv.ToString("0.#", CultureInfo.InvariantCulture) + " °C" : "—") + " · " + gpuEngine!.Fps.ToString("0", CultureInfo.InvariantCulture) + " FPS";
        bool gpuStopped = gpuOn && !gpuEngine!.Running;
        if (StressGuard.Check(limits, cpuOn, t, gpuOn, g, engine.MemoryErrors, gpuStopped, gpuEngine?.Error, DateTime.Now - startedAt) is { } reason) Stop(reason);
    }
}
