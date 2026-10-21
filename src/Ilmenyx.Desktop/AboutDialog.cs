using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed class AboutDialog : Window
{
    public AboutDialog()
    {
        Title = "Ilmenyx — Licenças e créditos"; Width = 740; Height = 560;
        MinWidth = 480; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var close = new Button { Name = "CloseAbout", Content = "Fechar", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var tabs = new TabControl
        {
            ItemsSource = new[]
            {
                Tab("Origem", ProductIdentity.Attribution + "\n\n" + Read("NOTICE.md"), "AttributionText"),
                Tab("MIT", Read("LICENSE"), "LicenseText"),
                Tab("Dependências", Read("THIRD-PARTY-NOTICES.md"), "DependenciesText")
            }
        };
        var panel = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        var heading = new TextBlock { Text = "Ilmenyx · by Bismuth Technology", FontSize = 20, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading); panel.Children.Add(tabs); Content = panel;
    }
    private static TabItem Tab(string title, string text, string name) => new()
    {
        Header = title,
        Content = new TextBox { Name = name, Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap }
    };
    private static string Read(string file)
    {
        try { return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return "Indisponível: arquivo de atribuição não encontrado no pacote: " + file + ". Consulte o código-fonte e revise o empacotamento."; }
    }
}
