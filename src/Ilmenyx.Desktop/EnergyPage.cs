// New Ilmenyx additions (layout follows the original Energy page): see LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Contracts;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

public sealed class EnergyPage : StackPanel
{
    private readonly LinuxPowerReader reader = new();
    private readonly SensorMonitorViewModel sensors;
    private readonly CancellationToken lifetime;
    private readonly StackPanel cards = new() { Name = "EnergyCards" };
    private readonly TextBlock status = new() { Name = "EnergyStatus", Text = "Bateria, fonte de energia e consumo", Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private bool busy;
    public IReadOnlyList<HealthCard> Cards { get; private set; } = [];

    public EnergyPage(SensorMonitorViewModel sensors, CancellationToken lifetime)
    {
        this.sensors = sensors; this.lifetime = lifetime; Spacing = 4;
        var refresh = new Button { Name = "BtnEnergyRefresh", Content = "ATUALIZAR", Padding = new Thickness(18, 9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        refresh.Click += async (_, _) => await RefreshAsync();
        var header = new Grid { Margin = new Thickness(0, 4, 0, 18) };
        header.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 160, 0), Children = { new TextBlock { Text = "Energia", FontSize = 32, FontWeight = FontWeight.Bold }, status } });
        header.Children.Add(refresh);
        Children.Add(header); Children.Add(cards);
        Children.Add(new TextBlock
        {
            Text = "Os dados vêm de /sys/class/power_supply e do power-profiles-daemon. Alguns notebooks não informam ciclos de carga ou a capacidade de projeto. O consumo mostrado vem dos sensores de potência do kernel (hwmon), não da fonte (PSU) do PC.",
            Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8)
        });
        Loaded += async (_, _) => { if (Cards.Count == 0) await RefreshAsync(); };
    }

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; status.Text = "Lendo as fontes de energia…";
        try
        {
            var readout = await Task.Run(() => reader.Read(), lifetime);
            string? plan = await LinuxPowerReader.ActiveProfileAsync(lifetime);
            var power = (sensors.Snapshot?.Readings ?? []).Where(r => r.Channel.StartsWith("power", StringComparison.Ordinal) && r.Reading.Value is > 0)
                .Select(r => new KeyValuePair<string, string>($"{r.Chip} · {r.Label}", r.Reading.Value!.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " W")).ToList();
            Cards = BatteryEvaluator.Evaluate(readout.Batteries, readout.MainsOnline, plan, power);
            cards.Children.Clear();
            foreach (var card in Cards) cards.Children.Add(HealthCardView.Build(card));
            status.Text = readout.Batteries.Count == 0 ? $"Nenhuma bateria · PC de mesa ou sem dados · lido às {DateTime.Now:HH:mm:ss}" : $"{readout.Batteries.Count} bateria(s) · lido às {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) { status.Text = "Leitura cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Falhou: " + e.Message; }
        finally { busy = false; }
    }
}
