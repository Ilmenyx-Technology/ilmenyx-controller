// New Ilmenyx additions (layout follows the original Stress page: components, limits, start/stop, readouts, temperature chart, log): see LICENSING.md.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private readonly Button start = UiKit.Primary("BtnStress", "INICIAR", 20, 12);
    private readonly TextBlock hint = new() { Name = "StressHint", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12, MaxWidth = 240, Margin = new Thickness(0, 10, 0, 0), TextAlignment = TextAlignment.Center };
    private readonly Border statusCard = new() { Name = "StatusCard" };
    private readonly TextBlock stStatus = new() { Name = "StStatus", Text = "Parado", FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock stTemp = new() { Name = "StTemp", Text = "—", FontSize = 18 }, stTempMax = new() { Name = "StTempMax", Text = "máx. —", FontSize = 11, Foreground = Palette.Muted };
    private readonly TextBlock stLoad = new() { Name = "StLoad", Text = "—", FontSize = 18 }, stRam = new() { Name = "StRam", Text = "—", FontSize = 18 };
    private readonly UsageChart chart = new() { Name = "TempChart", Height = 150 };
    private readonly StackPanel log = new() { Name = "StressLog" };
    private readonly List<double> temps = [];
    private UiKit.ChipGroup<double>? ramChips; private double ramFraction = 0.6;
    private StressLimits? limits; private DateTime startedAt; private double maxTemp;
    private bool cpuOn, ramOn;
    public bool IsRunning => engine.Running;
    public string StatusText => stStatus.Text ?? "";

    public StressPage(IStressEngine engine, UsageViewModel usage, Func<Window?> owner, Func<long> availableMemory, CancellationToken lifetime)
    {
        this.engine = engine; this.usage = usage; this.owner = owner; this.availableMemory = availableMemory; this.lifetime = lifetime; Spacing = 4;
        Children.Add(new StackPanel { Margin = new Thickness(0, 4, 0, 20), Children = { new TextBlock { Text = "Stress test", FontSize = 26, FontWeight = FontWeight.Bold }, new TextBlock { Text = "Carga máxima com corte automático por temperatura", Foreground = Palette.Muted } } });

        ramChips = new("RamFractionPanel", [("40%", 0.4), ("60%", 0.6), ("80%", 0.8)], 0.6, v => ramFraction = v);
        ramChips.Panel.IsEnabled = false;
        chkRam.IsCheckedChanged += (_, _) => ramChips.Panel.IsEnabled = chkRam.IsChecked == true;
        ToolTip.SetTip(chkGpu, "Stress de GPU (estilo FurMark) depende do módulo de GPU em Vulkan (fase 4) e ainda não foi portado.");
        Control Field(string label, Control c) => new StackPanel { Margin = new Thickness(0, 0, 28, 8), Children = { UiKit.Label(label), new Border { Height = 4 }, c } };
        foreach (var c in new Control[] { cpuLimit, gpuLimit, duration }) AutomationProperties.SetName(c, "Campo do stress test");
        var config = new WrapPanel
        {
            Children =
            {
                Field("COMPONENTES", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { chkCpu, chkRam, chkGpu } }),
                Field("USO DA RAM DISPONÍVEL", ramChips.Panel), Field("LIMITE CPU °C", cpuLimit), Field("LIMITE GPU °C", gpuLimit), Field("DURAÇÃO (MIN · 0 = SEM LIMITE)", duration)
            }
        };
        start.MinWidth = 150; start.Click += async (_, _) => { if (engine.Running) Stop("Parado pelo usuário"); else await StartAsync(); };
        var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0), Children = { start, hint } };
        var row = new Grid { ColumnDefinitions = new("*,Auto") }; Grid.SetColumn(side, 1); row.Children.Add(config); row.Children.Add(side);
        Children.Add(UiKit.Card(row));

        Border Stat(string label, params Control[] parts)
        {
            var inner = new StackPanel(); inner.Children.Add(UiKit.Label(label)); foreach (var part in parts) inner.Children.Add(part);
            return new Border { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(14), Margin = new Thickness(4, 0), Child = inner };
        }
        statusCard.Background = Palette.Card; statusCard.BorderBrush = Palette.Line; statusCard.BorderThickness = new Thickness(1); statusCard.CornerRadius = new CornerRadius(16); statusCard.Padding = new Thickness(14); statusCard.Margin = new Thickness(4, 0); statusCard.Child = new StackPanel { Children = { UiKit.Label("STATUS"), stStatus } };
        var stats = new UniformGrid { Columns = 5, Margin = new Thickness(-4, 0, -4, 14) };
        stats.Children.Add(statusCard); stats.Children.Add(Stat("TEMP. CPU", stTemp, stTempMax)); stats.Children.Add(Stat("CARGA CPU", stLoad, new TextBlock { Text = "uso total", FontSize = 11, Foreground = Palette.Muted }));
        stats.Children.Add(Stat("TEMP. / CONSUMO GPU", new TextBlock { Text = "—", FontSize = 18 }, new TextBlock { Text = "fase 4 (Vulkan)", FontSize = 11, Foreground = Palette.Muted }));
        stats.Children.Add(Stat("RAM", stRam, new TextBlock { Text = "passes / erros", FontSize = 11, Foreground = Palette.Muted }));
        Children.Add(stats);
        Children.Add(UiKit.Card(new StackPanel { Children = { UiKit.Label("TEMPERATURA DA CPU — ÚLTIMOS 5 MIN"), new Border { Height = 6 }, chart, new TextBlock { Text = "Consumo da CPU (RAPL) e sensores de GPU não são lidos neste teste.", FontSize = 11, Foreground = Palette.Faint, Margin = new Thickness(0, 6, 0, 0) } } }));
        Children.Add(UiKit.Card(new StackPanel { Children = { UiKit.Label("REGISTRO"), log } }));
        timer.Tick += (_, _) => Tick();
    }

    private void Log(string message)
    {
        log.Children.Add(new TextBlock { Text = $"{DateTime.Now:HH:mm:ss}  {message}", FontFamily = new FontFamily("monospace"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
        if (log.Children.Count > 200) log.Children.RemoveAt(0);
    }

    private void SetStatus(string text, bool alert)
    {
        stStatus.Text = text; statusCard.Background = alert ? Palette.Fg : Palette.Card; stStatus.Foreground = alert ? Brushes.Black : Palette.Fg;
    }

    private void Controls(bool running)
    {
        start.Content = running ? "PARAR" : "INICIAR";
        chkCpu.IsEnabled = chkRam.IsEnabled = !running; cpuLimit.IsEnabled = duration.IsEnabled = !running; ramChips!.Panel.IsEnabled = !running && chkRam.IsChecked == true;
    }

    public async Task StartAsync()
    {
        hint.Text = "";
        bool cpu = chkCpu.IsChecked == true, ram = chkRam.IsChecked == true;
        if (!cpu && !ram) { hint.Text = "Ative CPU ou RAM."; return; }
        if (StressGuard.Validate(cpuLimit.Text ?? "", gpuLimit.Text ?? "", false, duration.Text ?? "", out var parsed) is { } error) { hint.Text = error; return; }
        if (owner() is not { } window) return;
        if ((cpu || ram) && usage.Last?.CpuTempC is null)
        {
            if (!await ConfirmDialog.AskAsync(window, "Stress test", usage.IsRunning
                ? "Nenhum sensor de temperatura da CPU foi encontrado (hwmon coretemp/k10temp), então o corte automático por temperatura NÃO funcionará. Continuar mesmo assim?"
                : "O monitor está pausado e não há leitura de temperatura da CPU, então o corte automático por temperatura NÃO funcionará. Retome o monitor ou continue por sua conta e risco. Continuar mesmo assim?", "Continuar sem corte", "Cancelar")) return;
        }
        long available = availableMemory();
        string what = string.Join(" e ", new[] { cpu ? $"CPU ({Environment.ProcessorCount} threads a 100%)" : null, ram ? $"RAM ({(ramFraction * 100).ToString("0", CultureInfo.InvariantCulture)}% da memória disponível, sempre deixando 1 GiB livre)" : null }.Where(x => x != null));
        string time = parsed!.Duration > TimeSpan.Zero ? $"por {parsed.Duration.TotalMinutes:0.##} min" : "SEM LIMITE DE TEMPO (só para quando você clicar em PARAR, ou ao atingir o limite de temperatura ou erro)";
        if (!await ConfirmDialog.AskAsync(window, "Iniciar stress test", $"Isto vai gerar carga máxima em {what} {time}.\n\nLimite de temperatura da CPU: {parsed.CpuTempLimit:0} °C (o teste é interrompido automaticamente). Salve seu trabalho antes: o sistema pode ficar lento e, com pouca RAM livre, instável.\n\nIniciar agora?", "Iniciar", "Cancelar")) return;
        try { engine.Start(cpu, ram, ramFraction, available); }
        catch (InvalidOperationException e) { hint.Text = e.Message; return; }
        limits = parsed; cpuOn = cpu; ramOn = ram; startedAt = DateTime.Now; maxTemp = 0; temps.Clear();
        Controls(true); SetStatus("Rodando", false); Log($"Iniciado · CPU {(cpu ? $"sim (limite {parsed.CpuTempLimit:0} °C)" : "não")} · RAM {(ram ? $"sim ({(ramFraction * 100).ToString("0", CultureInfo.InvariantCulture)}% da disponível, {engine.MemoryAllocatedBytes / 1048576} MiB)" : "não")} · {(parsed.Duration > TimeSpan.Zero ? $"{parsed.Duration.TotalMinutes:0.##} min" : "sem limite de tempo")}");
        timer.Start();
    }

    public void Stop(string reason)
    {
        if (!engine.Running) return;
        timer.Stop();
        long errors = engine.MemoryErrors, passes = engine.MemoryPasses; bool hadRam = engine.MemoryAllocatedBytes > 0;
        engine.Stop();
        Controls(false); bool aborted = reason.StartsWith("ABORTADO", StringComparison.Ordinal);
        SetStatus(aborted ? "Abortado" : "Parado", aborted);
        Log($"{reason} · duração {DateTime.Now - startedAt:hh\\:mm\\:ss}" + (cpuOn ? $" · CPU temp. máx. {maxTemp:0.#} °C" : "") + (hadRam ? $" · RAM {passes} passes, {errors} erros" : ""));
    }

    /// <summary>One-second tick: readouts and the abort guards. Public so a host can drive it deterministically.</summary>
    public void Tick()
    {
        if (!engine.Running || limits is null) return;
        double? t = usage.Last?.CpuTempC;
        if (t is { } v) { maxTemp = Math.Max(maxTemp, v); temps.Add(v); if (temps.Count > 300) temps.RemoveAt(0); chart.SetValues(temps, 110, 300); }
        stTemp.Text = t is { } x ? x.ToString("0.#", CultureInfo.InvariantCulture) + " °C" : "—"; stTempMax.Text = "máx. " + (maxTemp > 0 ? maxTemp.ToString("0.#", CultureInfo.InvariantCulture) + " °C" : "—");
        stLoad.Text = UsageViewModel.Pct(usage.Last?.CpuPercent);
        stRam.Text = ramOn ? $"{engine.MemoryPasses} / {engine.MemoryErrors}" : "—";
        if (StressGuard.Check(limits, cpuOn, t, false, null, engine.MemoryErrors, false, null, DateTime.Now - startedAt) is { } reason) Stop(reason);
    }
}
