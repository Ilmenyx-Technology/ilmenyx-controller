// SPDX-License-Identifier: MIT AND PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Gustavo (GustavoReset)
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// New Ilmenyx additions (layout follows the original Energy page; cinematic header and card flow 2026-11-06): see LICENSING.md.
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
    private readonly TextBlock status = new() { Name = "EnergyStatus", Text = "Bateria, fonte de energia e consumo", Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 10) };
    private bool busy;
    public IReadOnlyList<HealthCard> Cards { get; private set; } = [];

    public EnergyPage(SensorMonitorViewModel sensors, CancellationToken lifetime)
    {
        this.sensors = sensors; this.lifetime = lifetime;
        var refresh = UiKit.Primary("BtnEnergyRefresh", "ATUALIZAR");
        refresh.Click += async (_, _) => await RefreshAsync();
        Children.Add(UiKit.PageHeader("Energia", "Bateria, fonte de energia e consumo", refresh));
        Children.Add(status); Children.Add(cards);
        Children.Add(new TextBlock
        {
            Text = "Os dados vêm de /sys/class/power_supply e do power-profiles-daemon. Alguns notebooks não informam ciclos de carga ou a capacidade de projeto. O consumo mostrado vem dos sensores de potência do kernel (hwmon), não da fonte (PSU) do PC.",
            Foreground = Palette.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 8)
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
            cards.Children.Add(HealthCardView.Columns(Cards.Select(c => HealthCardView.Build(c)).ToList()));
            status.Text = readout.Batteries.Count == 0 ? $"Nenhuma bateria · PC de mesa ou sem dados · lido às {DateTime.Now:HH:mm:ss}" : $"{readout.Batteries.Count} bateria(s) · lido às {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) { status.Text = "Leitura cancelada."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Falhou: " + e.Message; }
        finally { busy = false; }
    }
}
