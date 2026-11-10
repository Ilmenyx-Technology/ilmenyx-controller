// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// New Ilmenyx additions (layout follows the original Network page: TESTAR, progress bar, status, diagnostic card, note; cinematic surfaces 2026-11-06): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed class NetworkPage : StackPanel
{
    private readonly INetworkProbe probe;
    private readonly UsageViewModel usage;
    private readonly Func<Window?> owner;
    private readonly CancellationToken lifetime;
    private readonly Button test = UiKit.Primary("BtnNetTest", L.T("Testar"), 22, 9);
    private readonly Border progress = new() { Name = "NetProgress", Background = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Left, Height = 4, CornerRadius = new CornerRadius(2) };
    private readonly Grid track = new() { Height = 4, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBlock status = new() { Name = "NetStatus", Text = L.T("Pronto. O teste leva cerca de 30 segundos."), Foreground = Palette.Muted, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel cards = new() { Name = "NetCards" };
    private readonly UsageChart chart = new() { Name = "NetChart", Height = 160 };
    private readonly TextBlock live = new() { Name = "NetLine", FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock liveSub = new() { Name = "NetSub", Foreground = Palette.Muted, FontSize = 12 };
    private CancellationTokenSource? running; private double fraction;
    public HealthCard? LastResult { get; private set; }

    public NetworkPage(INetworkProbe probe, UsageViewModel usage, Func<Window?> owner, CancellationToken lifetime)
    {
        this.probe = probe; this.usage = usage; this.owner = owner; this.lifetime = lifetime;
        test.Click += async (_, _) => { if (running is not null) running.Cancel(); else await RunAsync(); };
        Children.Add(UiKit.PageHeader(L.T("Rede"), L.T("Descubra se o lag vem do seu PC, do Wi-Fi ou do provedor"), test));
        track.Children.Add(new Border { Background = Palette.Hover, CornerRadius = new CornerRadius(2) });
        track.Children.Add(progress); track.SizeChanged += (_, _) => progress.Width = track.Bounds.Width * fraction;
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { UiKit.Icon(Icons.Network, 15, Palette.AccentText), UiKit.Label(L.T("DIAGNÓSTICO DE CONEXÃO")) } };
        Children.Add(UiKit.Card(new StackPanel { Children = { head, track, status } }));
        Children.Add(cards);
        Children.Add(new TextBlock { Text = L.T("O teste envia ") + NetworkEndpoints.Description + L.T(". Feche downloads, streaming e jogos antes para um resultado fiel. Nada é gravado em disco."), Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 4) });
        Children.Add(UiKit.SectionHeader(L.T("Tráfego ao vivo"), new TextBlock { Text = L.T("últimos 5 min · soma das interfaces ativas"), FontSize = 11.5, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center }));
        Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = { chart, live, liveSub } }));
        usage.Updated += UiKit.OnScreen(chart, () => { chart.SetValues(usage.NetSeries, null, 300); live.Text = usage.NetLine; liveSub.Text = usage.NetSub; });
        live.Text = usage.NetLine; liveSub.Text = usage.NetSub;
    }

    private void SetProgress(double f) { fraction = Math.Clamp(f, 0, 1); progress.Width = track.Bounds.Width * fraction; }

    public async Task RunAsync()
    {
        if (running is not null || owner() is not { } window) return;
        if (!await ConfirmDialog.AskAsync(window, L.T("Teste de rede"), L.T($"Este teste usa a internet: {NetworkEndpoints.Description}.\n\nPode levar cerca de 30 segundos e consumir bastante dados em conexões rápidas ou limitadas. Iniciar agora?"), L.T("Iniciar teste"), L.T("Cancelar"))) return;
        running = CancellationTokenSource.CreateLinkedTokenSource(lifetime); test.Content = L.T("Cancelar"); cards.Children.Clear(); SetProgress(0);
        try
        {
            var card = await NetworkTestRunner.RunAsync(probe, (text, f) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { status.Text = text; SetProgress(f); }), running.Token);
            LastResult = card; cards.Children.Add(HealthCardView.Build(card));
            status.Text = L.T($"Concluído às {DateTime.Now:HH:mm:ss}."); SetProgress(1);
        }
        catch (OperationCanceledException) { status.Text = L.T("Teste cancelado."); SetProgress(0); }
        catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException or InvalidOperationException) { status.Text = "Falhou: " + e.Message; SetProgress(0); }
        finally { running?.Dispose(); running = null; test.Content = L.T("Testar"); }
    }
}
