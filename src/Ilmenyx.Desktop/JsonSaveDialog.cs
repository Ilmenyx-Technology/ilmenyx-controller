using Avalonia;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

/// <summary>Application-owned local destination dialog: no global input automation or external process.</summary>
public sealed class JsonSaveDialog : Window
{
    public JsonSaveDialog(string suggestedFileName, string extension = ".json")
    {
        if (extension is not (".json" or ".html")) throw new ArgumentException("Formato de relatório não suportado.", nameof(extension));
        Icon = AppIcon.Window; Title = extension == ".html" ? "Salvar relatório HTML" : "Salvar relatório JSON"; Width = 660; Height = 390; MinHeight = 360; MinWidth = 440;
        Background = Palette.Panel; Foreground = Palette.Fg;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        CanResize = true; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        string directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!Directory.Exists(directory)) directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var destination = new TextBox { Name = "DestinationPath", Text = Path.Combine(directory, suggestedFileName), PlaceholderText = "/pasta/relatorio" + extension };
        var label = new TextBlock { Text = "Caminho completo do arquivo", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold };
        AutomationProperties.SetName(destination, "Caminho completo do arquivo de relatório");
        AutomationProperties.SetLabeledBy(destination, label);
        AutomationProperties.SetHelpText(destination, "Informe um caminho completo para um arquivo novo com extensão " + extension + ". Arquivos existentes serão preservados.");
        var error = new TextBlock { Name = "DestinationError", TextWrapping = TextWrapping.Wrap, Foreground = Palette.Error };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        var save = new Button { Name = "SaveJson", Content = "Salvar", IsDefault = true };
        var cancel = new Button { Name = "CancelSaveJson", Content = "Cancelar", IsCancel = true };
        save.Click += (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(destination.Text)) throw new ArgumentException("Informe o arquivo de destino.");
                if (!Path.IsPathFullyQualified(destination.Text)) throw new ArgumentException("Informe o caminho completo do arquivo.");
                string path = Path.GetFullPath(destination.Text);
                if (!Directory.Exists(Path.GetDirectoryName(path))) throw new ArgumentException("A pasta de destino não existe.");
                if (File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null)
                    throw new ArgumentException("Esse destino já existe. Escolha um arquivo novo.");
                if (!Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Use a extensão " + extension + ".");
                Close(path);
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                error.Text = e.Message;
                AutomationProperties.SetHelpText(destination, e.Message);
                destination.Focus();
            }
        };
        cancel.Click += (_, _) => Close(null);
        var content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Destino do relatório", TextWrapping = TextWrapping.Wrap, FontSize = 18, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Informe o caminho completo para um arquivo novo. Arquivos existentes serão preservados. " + Ilmenyx.Core.ProductIdentity.SharingNotice, TextWrapping = TextWrapping.Wrap },
                label, destination, error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { save, cancel } }
            }
        };
        Content = new ScrollViewer
        {
            Name = "SaveDialogScroll", Background = IlmenyxTheme.Ambient(), Content = content, IsTabStop = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Opened += (_, _) => { destination.Focus(); destination.SelectAll(); };
    }
    public static Task<string?> ChooseAsync(Window owner, string suggestedFileName, string extension = ".json") => new JsonSaveDialog(suggestedFileName, extension).ShowDialog<string?>(owner);
}
