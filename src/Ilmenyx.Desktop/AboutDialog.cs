// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed class AboutDialog : Window
{
    public AboutDialog()
    {
        Icon = AppIcon.Window; Title = L.T("Ilmenyx Controller — Licenças e créditos"); Width = 740; Height = 560;
        MinWidth = 480; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Panel; Foreground = Palette.Fg;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        var close = new Button { Name = "CloseAbout", Content = L.T("Fechar"), IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var documents = new[]
        {
            (Title: L.T("Origem"), Text: ProductIdentity.Attribution + "\n\n" + Read("NOTICE.md")),
            (Title: L.T("MIT (partes do RESET EXPLODE)"), Text: Read("LICENSES/MIT.txt")),
            (Title: L.T("Dependências"), Text: Read("THIRD-PARTY-NOTICES.md")),
            (Title: L.T("Licença Ilmenyx"), Text: LicenseDocuments.OwnTerms(Read))
        };
        var selector = new ComboBox
        {
            Name = "LicenseDocument", ItemsSource = documents.Select(d => d.Title).ToArray(),
            HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 0
        };
        var label = new TextBlock { Text = L.T("Documento"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(selector, L.T("Documento de licença ou crédito"));
        AutomationProperties.SetLabeledBy(selector, label);
        var content = new TextBox
        {
            Name = "LicenseTextContent", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
        };
        void SelectDocument()
        {
            if (selector.SelectedIndex < 0 || selector.SelectedIndex >= documents.Length) return;
            var document = documents[selector.SelectedIndex];
            content.Text = document.Text;
            content.CaretIndex = 0;
            content.SelectionStart = content.SelectionEnd = 0;
            AutomationProperties.SetName(content, document.Title + L.T(" — texto para leitura e cópia"));
        }
        selector.SelectionChanged += (_, _) => SelectDocument();
        SelectDocument();
        var panel = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        var heading = new TextBlock { Text = "Ilmenyx Controller · by Ilmenyx Technology", TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var navigation = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetColumn(selector, 1); navigation.Children.Add(label); navigation.Children.Add(selector);
        DockPanel.SetDock(navigation, Dock.Top); panel.Children.Add(navigation);
        panel.Children.Add(content); Content = new Border { Background = IlmenyxTheme.Ambient(), Child = panel };
        Opened += (_, _) => selector.Focus();
    }
    private static string Read(string file)
    {
        try { return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return L.T("Indisponível: arquivo de atribuição não encontrado no pacote: ") + file + L.T(". Consulte o código-fonte e revise o empacotamento."); }
    }
}
/// <summary>Texts of the Ilmenyx license shown in About: explanation, additional permission and the PolyForm text in LICENSE.</summary>
public static class LicenseDocuments
{
    public const string AdditionalPermission = "LICENSES/LicenseRef-Ilmenyx-Permissao-Adicional.txt";
    public static string OwnTerms(Func<string, string> read) =>
        read("LICENSING.md") + "\n\n" + read(AdditionalPermission) + "\n\n" + read("LICENSE");
}
