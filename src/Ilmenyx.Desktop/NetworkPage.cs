// New Ilmenyx additions (layout follows the original Network page: TESTAR, progress bar, status, diagnostic card, note): see LICENSING.md.
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
    private readonly Button test = UiKit.Primary("BtnNetTest", "TESTAR", 22, 9);
    private readonly Border progress = new() { Name = "NetProgress", Background = Palette.Fg, HorizontalAlignment = HorizontalAlignment.Left, Height = 3 };
    private readonly Grid track = new() { Height = 3, Margin = new Thickness(0, 0, 0, 8), Background = Palette.Line };
    private readonly TextBlock status = new() { Name = "NetStatus", Text = "Pronto. O teste leva cerca de 30 segundos.", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly StackPanel cards = new() { Name = "NetCards" };
    private readonly UsageChart chart = new() { Name = "NetChart", Height = 160 };
    private readonly TextBlock live = new() { Name = "NetLine", FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock liveSub = new() { Name = "NetSub", Foreground = Palette.Muted, FontSize = 12 };
    private CancellationTokenSource? running; private double fraction;
    public HealthCard? LastResult { get; private set; }

    public NetworkPage(INetworkProbe probe, UsageViewModel usage, Func<Window?> owner, CancellationToken lifetime)
    {
        this.probe = probe; this.usage = usage; this.owner = owner; this.lifetime = lifetime; Spacing = 4;
        test.HorizontalAlignment = HorizontalAlignment.Right; test.VerticalAlignment = VerticalAlignment.Top;
        test.Click += async (_, _) => { if (running is not null) running.Cancel(); else await RunAsync(); };
        var header = new Grid { Margin = new Thickness(0, 4, 0, 20) };
        header.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 160, 0), Children = { new TextBlock { Text = "Rede", FontSize = 32, FontWeight = FontWeight.Bold }, new TextBlock { Text = "Descubra se o lag vem do seu PC, do Wi-Fi ou do provedor", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap } } });
        header.Children.Add(test);
        track.Children.Add(progress); track.SizeChanged += (_, _) => progress.Width = track.Bounds.Width * fraction;
        Children.Add(header); Children.Add(track); Children.Add(status); Children.Add(cards);
        Children.Add(new TextBlock { Text = "O teste envia " + NetworkEndpoints.Description + ". Feche downloads, streaming e jogos antes para um resultado fiel. Nada é gravado em disco.", Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) });
        Children.Add(UiKit.Card(new StackPanel { Spacing = 10, Children = { UiKit.Label("TRÁFEGO AO VIVO · ÚLTIMOS 5 MIN (SOMA DAS INTERFACES ATIVAS)"), chart, live, liveSub } }));
        usage.Updated += () => { chart.SetValues(usage.NetSeries, null, 300); live.Text = usage.NetLine; liveSub.Text = usage.NetSub; };
        live.Text = usage.NetLine; liveSub.Text = usage.NetSub;
    }

    private void SetProgress(double f) { fraction = Math.Clamp(f, 0, 1); progress.Width = track.Bounds.Width * fraction; }

    public async Task RunAsync()
    {
        if (running is not null || owner() is not { } window) return;
        if (!await ConfirmDialog.AskAsync(window, "Teste de rede", $"Este teste usa a internet: {NetworkEndpoints.Description}.\n\nPode levar cerca de 30 segundos e consumir bastante dados em conexões rápidas ou limitadas. Iniciar agora?", "Iniciar teste", "Cancelar")) return;
        running = CancellationTokenSource.CreateLinkedTokenSource(lifetime); test.Content = "CANCELAR"; cards.Children.Clear(); SetProgress(0);
        try
        {
            var card = await NetworkTestRunner.RunAsync(probe, (text, f) => Avalonia.Threading.Dispatcher.UIThread.Post(() => { status.Text = text; SetProgress(f); }), running.Token);
            LastResult = card; cards.Children.Add(HealthCardView.Build(card));
            status.Text = $"Concluído às {DateTime.Now:HH:mm:ss}."; SetProgress(1);
        }
        catch (OperationCanceledException) { status.Text = "Teste cancelado."; SetProgress(0); }
        catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException or InvalidOperationException) { status.Text = "Falhou: " + e.Message; SetProgress(0); }
        finally { running?.Dispose(); running = null; test.Content = "TESTAR"; }
    }
}
