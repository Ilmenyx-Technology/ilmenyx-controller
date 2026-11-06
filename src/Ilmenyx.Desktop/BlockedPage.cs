// New Ilmenyx additions: see LICENSING.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>
/// Functions that are not ported or not validated yet, each with a (!) that explains why. When this machine has the hardware a feature was waiting for,
/// the card says "Teste disponível nesta máquina". Reading the machine is passive (sysfs, Vulkan adapter list); nothing is tested or started here.
/// </summary>
public sealed class BlockedPage : StackPanel
{
    private readonly Func<MachineProbe> probe;
    private readonly StackPanel list = new() { Name = "BlockedList", Spacing = 10 };
    private readonly TextBlock summary = new() { Name = "BlockedSummary", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted };
    public MachineProbe? Probe { get; private set; }

    public BlockedPage(Func<MachineProbe> probe)
    {
        this.probe = probe; Spacing = 4;
        Children.Add(UiKit.PageHeader("Bloqueados (!)", "Funções do original que ainda não foram portadas ou validadas, e por quê · clique em (!) para ver o motivo"));
        var overview = UiKit.FeatureCard(summary);
        overview.Margin = new Thickness(0, 0, 0, 14); Children.Add(overview);
        Children.Add(list);
        Refresh();
    }

    public void Refresh()
    {
        Probe = probe();
        int available = BlockedCatalog.AvailableCount(Probe);
        summary.Text = available > 0
            ? $"{BlockedCatalog.TestAvailableText}: {available} item(ns) destravado(s) pelo hardware deste computador. Siga a dica de cada cartão para concluir o port."
            : "Nenhum item pode ser concluído neste computador: o hardware que eles pedem não foi encontrado.";
        list.Children.Clear();
        foreach (var f in BlockedCatalog.All.OrderBy(f => f.State(Probe) == BlockedState.TestAvailable ? 0 : f.State(Probe) == BlockedState.Blocked ? 1 : 2)) list.Children.Add(Card(f, f.State(Probe)));
    }

    private static Control Card(BlockedFeature f, BlockedState state)
    {
        string chipText = state switch { BlockedState.TestAvailable => BlockedCatalog.TestAvailableText.ToUpperInvariant(), BlockedState.NotApplicable => "NÃO SE APLICA", _ => "BLOQUEADO" };
        var chip = new Border { Name = "BlockedChip_" + f.Id, CornerRadius = new CornerRadius(IlmenyxTheme.Pill), Padding = new Thickness(10, 3), Background = state == BlockedState.TestAvailable ? Palette.Accent : Palette.Line, Child = new TextBlock { Text = (state == BlockedState.Blocked ? "🔒 " : "") + chipText, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = state == BlockedState.TestAvailable ? Palette.OnAccent : Palette.Muted } };
        var details = new StackPanel { Name = "BlockedDetails_" + f.Id, Spacing = 3, IsVisible = state == BlockedState.TestAvailable, Margin = new Thickness(0, 8, 0, 0) };
        details.Children.Add(new TextBlock { Text = f.KindLabel.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint });
        details.Children.Add(new TextBlock { Text = f.Reason, TextWrapping = TextWrapping.Wrap });
        if (f.Needs != "—") details.Children.Add(new TextBlock { Text = "Precisa de: " + f.Needs, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Muted, FontSize = 12 });
        if (state == BlockedState.TestAvailable && f.TestHint.Length > 0) details.Children.Add(new TextBlock { Text = "Como seguir: " + f.TestHint, TextWrapping = TextWrapping.Wrap, FontSize = 12, FontWeight = FontWeight.SemiBold });
        details.Children.Add(new TextBlock { Text = "Registro: " + f.Reference, FontSize = 11, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        var why = new Button { Name = "BlockedWhy_" + f.Id, Content = "(!)", FontWeight = FontWeight.Bold, Padding = new Thickness(8, 2), CornerRadius = new CornerRadius(12), Foreground = Palette.Warn };
        AutomationProperties.SetName(why, $"Motivo: {f.Name}");
        ToolTip.SetTip(why, f.KindLabel + ": " + f.Reason);
        why.Click += (_, _) => details.IsVisible = !details.IsVisible;
        var head = new DockPanel { LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { chip, why } };
        DockPanel.SetDock(right, Dock.Right); head.Children.Add(right);
        head.Children.Add(new TextBlock { Text = f.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        return UiKit.Card(new StackPanel { Children = { head, details } }, new Thickness(0));
    }
}
