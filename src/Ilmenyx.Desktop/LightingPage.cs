// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original Integração tab: connections, device list, effect, colour, brightness, speed, take/return control): see LICENSING.md.
using System.Diagnostics;
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

public sealed record LightingServices(Func<string, int, CancellationToken, Task<OpenRgbClient>> Connect, Func<double?> GpuTemp, Func<string, CancellationToken, Task> OpenUrl, Func<bool> OpenRgbInstalled, Func<int, CancellationToken, Task<IAsyncDisposable>>? StartServer = null);

/// <summary>
/// RGB lighting through the local OpenRGB SDK server (Corsair, Razer, MSI, ASUS, Gigabyte… are supported by OpenRGB itself; the vendor SDKs of the
/// original are Windows-only). Nothing connects at startup; lights change only after ATIVAR CONTROLE and a confirmation, and are always handed back.
/// </summary>
public sealed class LightingPage : StackPanel
{
    public sealed class DeviceRow { public LightDevice Device { get; init; } = null!; public bool Selected { get; set; } = true; }

    private readonly LightingServices services;
    private readonly UsageViewModel usage;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly TextBox port = new() { Name = "LightPort", Text = OpenRgbClient.DefaultPort.ToString(CultureInfo.InvariantCulture), Width = 90 };
    private readonly Button connect = new() { Name = "BtnLightConnect", Content = "CONECTAR AO OPENRGB" };
    private readonly Button startServer = new() { Name = "BtnLightServer", Content = "INICIAR SERVIDOR OPENRGB" };
    private readonly Button openSite = new() { Name = "BtnOrgbOpen", Content = "SITE DO OPENRGB" };
    private readonly Button run = UiKit.Primary("BtnLightRun", "ATIVAR CONTROLE", 22, 11);
    private readonly TextBlock status = new() { Name = "LightStatus", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
    private readonly TextBlock hint = new() { Name = "LightRunHint", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Palette.Muted };
    private readonly TextBlock live = new() { Name = "LightLive", FontSize = 12, Foreground = Palette.Muted };
    private readonly StackPanel list = new() { Name = "LightDevices", Spacing = 2 };
    private readonly TextBlock diag = new() { Name = "LightDiag", FontFamily = new FontFamily("monospace"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Slider r = new() { Name = "LightR", Minimum = 0, Maximum = 255, Value = 255, Width = 200 }, g = new() { Name = "LightG", Minimum = 0, Maximum = 255, Value = 255, Width = 200 }, b = new() { Name = "LightB", Minimum = 0, Maximum = 255, Value = 255, Width = 200 };
    private readonly Slider bright = new() { Name = "LightBright", Minimum = 0, Maximum = 100, Value = 100, Width = 200 }, speed = new() { Name = "LightSpeed", Minimum = 1, Maximum = 10, Value = 3, Width = 200 };
    private readonly Border preview = new() { Name = "LightPreview", Width = 40, Height = 40, CornerRadius = new CornerRadius(20) };
    private readonly Queue<string> log = new();
    private readonly Dictionary<string, ((byte, byte, byte)[] Colors, long At)> last = [];
    private OpenRgbClient? client;
    private IAsyncDisposable? server;
    private List<DeviceRow> rows = [];
    private List<DeviceRow> controlled = [];
    private string effect = "static";
    private double smooth = double.NaN;
    private bool running, busy, sending;
    private CancellationTokenSource? cts;
    public bool IsControlling => running;
    public IReadOnlyList<DeviceRow> Devices => rows;
    public string StatusText => status.Text ?? "";

    public LightingPage(LightingServices services, UsageViewModel usage, Func<Window?> owner, CancellationToken lifetime)
    {
        this.services = services; this.usage = usage; this.owner = owner; this.lifetime = lifetime; Spacing = 4;
        Children.Add(UiKit.PageHeader("Integração", "Iluminação RGB e efeitos pelo OpenRGB (servidor SDK local) · nada conecta sozinho"));
        AutomationProperties.SetName(port, "Porta do servidor SDK do OpenRGB");
        connect.Click += async (_, _) => await ConnectAsync();
        openSite.Click += async (_, _) => { try { await services.OpenUrl("https://openrgb.org/", lifetime); Log("Site do OpenRGB aberto no navegador."); } catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or ArgumentException) { Log("Não foi possível abrir o navegador: " + e.Message); } };
        Children.Add(UiKit.Card(new StackPanel { Spacing = 8, Children = { UiKit.Label("OPENRGB"),
            new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "O OpenRGB controla as luzes de placas-mãe, memórias, ventoinhas, teclados e mouses de várias marcas (incluindo Corsair e Razer). O OpenRGB acompanha o Ilmenyx: INICIAR SERVIDOR sobe o servidor SDK embutido (ou o do sistema, se existir) só em 127.0.0.1 e o fecha ao sair. Se o seu OpenRGB já está com o servidor ativo, basta CONECTAR." },
            new WrapPanel { Children = { Field("PORTA", port), new WrapPanel { ItemSpacing = 8, LineSpacing = 8, VerticalAlignment = VerticalAlignment.Bottom, Children = { startServer, connect, openSite } } } }, status } }));
        Children.Add(UiKit.Card(new StackPanel { Spacing = 6, Children = { UiKit.Label("DISPOSITIVOS"), list } }));
        var fx = new UiKit.ChipGroup<string>("LightFxPanel", [("ESTÁTICO", "static"), ("RESPIRAR", "breath"), ("ARCO-ÍRIS", "rainbow"), ("TEMP. CPU", "cpu"), ("TEMP. GPU", "gpu"), ("CARGA CPU", "load"), ("APAGAR", "off")], "static", v => { effect = v; smooth = double.NaN; last.Clear(); });
        // Keep buttons as direct children: the UI smoke and keyboard traversal rely on this panel.
        fx.Panel.Orientation = Orientation.Vertical;
        foreach (var button in fx.Panel.Children) button.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(hint, 1); hint.Margin = new Thickness(0, 8, 0, 0);
        foreach (var s in new[] { r, g, b, bright, speed }) s.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) { RefreshPreview(); last.Clear(); } };
        var swatches = new WrapPanel();
        foreach (var (name, cr, cg, cb) in new[] { ("Branco", 255, 255, 255), ("Vermelho", 255, 0, 0), ("Laranja", 255, 110, 0), ("Amarelo", 255, 210, 0), ("Verde", 0, 255, 60), ("Ciano", 0, 220, 255), ("Azul", 0, 80, 255), ("Roxo", 150, 0, 255), ("Rosa", 255, 0, 160) })
        {
            var sw = new Button { Content = "", Width = 28, Height = 28, Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb((byte)cr, (byte)cg, (byte)cb)) };
            ToolTip.SetTip(sw, name); AutomationProperties.SetName(sw, "Cor " + name);
            sw.Click += (_, _) => { r.Value = cr; g.Value = cg; b.Value = cb; };
            swatches.Children.Add(sw);
        }
        startServer.IsVisible = services.StartServer is not null;
        startServer.Click += async (_, _) => await StartServerAsync();
        run.Click += async (_, _) => { if (running) await StopAsync(true); else await StartAsync(); };
        Children.Add(UiKit.Card(new StackPanel { Spacing = 8, Children = { UiKit.Label("EFEITO"), fx.Panel,
            new WrapPanel { Children = { Field("VERMELHO", r), Field("VERDE", g), Field("AZUL", b), Field("BRILHO %", bright), Field("VELOCIDADE", speed), Field("COR", preview) } }, swatches,
            new Grid { RowDefinitions = new("Auto,Auto"), Children = { run, hint } }, live } }));
        Children.Add(UiKit.Card(new StackPanel { Children = { UiKit.Label("DIAGNÓSTICO"), diag } }));
        timer.Tick += async (_, _) => await TickAsync();
        lifetime.Register(() => Dispatcher.UIThread.Post(async () => await ShutdownAsync()));
        RefreshPreview();
        status.Text = services.OpenRgbInstalled() ? "OpenRGB encontrado neste computador. Inicie o servidor SDK e clique em CONECTAR." : "OpenRGB não encontrado no PATH. Instale pela sua distribuição (ou use o site) e inicie o servidor SDK.";
    }

    private static Control Field(string name, Control c) => new StackPanel { Margin = new Thickness(0, 0, 24, 6), Children = { UiKit.Label(name), new Border { Height = 4 }, c } };

    private (byte R, byte G, byte B) Chosen => ((byte)r.Value, (byte)g.Value, (byte)b.Value);
    private void RefreshPreview() => preview.Background = new SolidColorBrush(Color.FromRgb(Chosen.R, Chosen.G, Chosen.B));

    private void Log(string line)
    {
        log.Enqueue($"{DateTime.Now:HH:mm:ss}  {line}"); while (log.Count > 80) log.Dequeue();
        diag.Text = string.Join("\n", log);
    }

    public async Task StartServerAsync()
    {
        if (busy || running || services.StartServer is null) return;
        if (server is not null) { await StopServerAsync(); return; }
        if (!int.TryParse(port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int p)) { status.Text = "Porta inválida."; return; }
        busy = true; startServer.IsEnabled = false;
        try
        {
            status.Text = "Iniciando o servidor OpenRGB (a primeira vez extrai o pacote embutido; pode levar alguns segundos)…";
            server = await services.StartServer(p, lifetime);
            startServer.Content = "PARAR SERVIDOR OPENRGB";
            Log($"Servidor OpenRGB iniciado em 127.0.0.1:{p}.");
        }
        catch (Exception e) when (e is IOException or TimeoutException or FileNotFoundException or ArgumentException or System.ComponentModel.Win32Exception) { status.Text = e.Message; Log(e.Message); return; }
        catch (OperationCanceledException) { status.Text = "Início cancelado."; return; }
        finally { busy = false; startServer.IsEnabled = true; }
        await ConnectAsync();
    }

    private async Task StopServerAsync()
    {
        if (running) await StopAsync(true);
        if (client is not null) { await client.DisposeAsync(); client = null; rows = []; BuildList(); }
        if (server is { } s) { server = null; await s.DisposeAsync(); }
        startServer.Content = "INICIAR SERVIDOR OPENRGB"; status.Text = "Servidor OpenRGB encerrado."; Log(status.Text);
    }

    public async Task ConnectAsync()
    {
        if (busy || running) return;
        busy = true; connect.IsEnabled = false;
        try
        {
            if (!int.TryParse(port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int p)) { status.Text = "Porta inválida."; return; }
            if (client is not null) { await client.DisposeAsync(); client = null; }
            status.Text = "Conectando…";
            client = await services.Connect("127.0.0.1", p, lifetime);
            var found = await client.ListAsync(lifetime);
            rows = found.Select(d => new DeviceRow { Device = d }).ToList(); BuildList();
            status.Text = $"Conectado (protocolo {client.Protocol}) · {rows.Count} dispositivo(s) com LEDs.";
            Log(status.Text);
        }
        catch (OpenRgbException e) { status.Text = e.Message; Log(e.Message); if (client is not null) { await client.DisposeAsync(); client = null; } rows = []; BuildList(); }
        catch (OperationCanceledException) { status.Text = "Conexão cancelada."; }
        finally { busy = false; connect.IsEnabled = true; }
    }

    private void BuildList()
    {
        list.Children.Clear();
        if (rows.Count == 0) { list.Children.Add(new TextBlock { Text = "Nenhum dispositivo ainda. Conecte ao OpenRGB.", Foreground = Palette.Muted }); return; }
        foreach (var row in rows)
        {
            var cb = new CheckBox { Name = "LightDev_" + row.Device.Index, Content = $"{row.Device.Name} — {row.Device.Detail}", IsChecked = row.Selected };
            cb.IsCheckedChanged += (_, _) => row.Selected = cb.IsChecked == true;
            list.Children.Add(cb);
        }
    }

    public async Task StartAsync()
    {
        if (running || client is null || owner() is not { } window) { if (client is null) hint.Text = "Conecte ao OpenRGB primeiro."; return; }
        var chosen = rows.Where(x => x.Selected).ToList();
        if (chosen.Count == 0) { hint.Text = "Marque pelo menos um dispositivo."; return; }
        hint.Text = "";
        if (!await ConfirmDialog.AskAsync(window, "Ativar controle das luzes", $"O Ilmenyx vai assumir {chosen.Count} dispositivo(s) via OpenRGB e enviar o efeito escolhido. Ao clicar em DEVOLVER AO ORIGINAL (ou fechar o aplicativo), cada dispositivo volta ao modo que estava ativo na conexão.\n\nAtivar agora?", "Ativar", "Cancelar")) return;
        cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var problems = new List<string>();
        foreach (var row in chosen)
        {
            try { await client.AcquireAsync(row.Device, cts.Token); Log($"Controle de \"{row.Device.Name}\": ok"); }
            catch (IOException e) { problems.Add($"{row.Device.Name}: {e.Message}"); Log($"Controle de \"{row.Device.Name}\": {e.Message}"); }
        }
        controlled = chosen; last.Clear(); smooth = double.NaN; running = true; run.Content = "DEVOLVER AO ORIGINAL";
        hint.Text = problems.Count == 0 ? $"Controlando {chosen.Count} dispositivo(s)." : "Controle ativo, com avisos: " + string.Join("; ", problems);
        timer.Start();
    }

    public async Task StopAsync(bool release)
    {
        timer.Stop(); running = false; run.Content = "ATIVAR CONTROLE"; live.Text = "";
        while (sending) await Task.Delay(10);
        if (release && client is not null)
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            foreach (var row in controlled)
            {
                try { await client.ReleaseAsync(row.Device, limit.Token); Log($"\"{row.Device.Name}\" devolvido ao modo original."); }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { Log($"Não foi possível devolver \"{row.Device.Name}\": {e.Message}"); }
            }
            hint.Text = "Controle devolvido: o OpenRGB reassume as luzes.";
        }
        cts?.Cancel(); cts?.Dispose(); cts = null; controlled = [];
    }

    private async Task ShutdownAsync()
    {
        try { await StopAsync(true); } catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
        if (client is not null) { try { await client.DisposeAsync(); } catch (IOException) { } client = null; }
        if (server is { } s) { server = null; await s.DisposeAsync(); }
    }

    private double? LiveValue()
    {
        double? raw = effect switch { "cpu" => usage.Last?.CpuTempC, "gpu" => services.GpuTemp(), "load" => usage.Last?.CpuPercent, _ => null };
        if (raw is not double v) return null;
        smooth = LightEffects.Smooth(smooth, v); return smooth;
    }

    /// <summary>One frame (~30/s): effect colours per device, resent only if changed or every 2 s so the vendor driver does not take over.</summary>
    public async Task TickAsync()
    {
        if (!running || client is null || sending) return;
        sending = true;
        try
        {
            double? value = LiveValue();
            live.Text = effect switch { "cpu" or "gpu" => value is double t ? $"{(effect == "cpu" ? "CPU" : "GPU")} {t.ToString("0", CultureInfo.InvariantCulture)} °C" : "Sensor indisponível.", "load" => value is double l ? $"CPU {l.ToString("0", CultureInfo.InvariantCulture)}% de carga" : "Sensor indisponível.", _ => "" };
            long now = Environment.TickCount64;
            foreach (var row in controlled)
            {
                var d = row.Device;
                bool have = last.TryGetValue(d.Id, out var previous);
                if (have && now - previous.At < client.MinIntervalMs) continue;
                var colors = LightEffects.Frame(effect, d.LedX(), clock.Elapsed.TotalSeconds, speed.Value, Chosen, bright.Value / 100.0, value);
                if (have && previous.Colors.SequenceEqual(colors) && now - previous.At < 2000) continue;
                try { await client.SendAsync(d, colors, cts?.Token ?? lifetime); last[d.Id] = (colors, now); }
                catch (Exception e) when (e is IOException or ObjectDisposedException)
                {
                    Log($"Perda de comunicação com o OpenRGB: {e.Message}"); hint.Text = "Conexão com o OpenRGB perdida. Reconecte.";
                    timer.Stop(); running = false; run.Content = "ATIVAR CONTROLE"; return;
                }
            }
        }
        finally { sending = false; }
    }
}
