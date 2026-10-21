using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Silex.Audit.Desktop;

/// <summary>Application-owned local destination dialog: no global input automation or external process.</summary>
public sealed class JsonSaveDialog : Window
{
    public JsonSaveDialog(string suggestedFileName, string extension = ".json")
    {
        if (extension is not (".json" or ".html")) throw new ArgumentException("Formato de relatório não suportado.", nameof(extension));
        Title = extension == ".html" ? "Salvar relatório HTML" : "Salvar relatório JSON"; Width = 660; Height = 260; MinWidth = 440;
        CanResize = true; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        string directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!Directory.Exists(directory)) directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var destination = new TextBox { Name = "DestinationPath", Text = Path.Combine(directory, suggestedFileName), Watermark = "/pasta/relatorio" + extension };
        var error = new TextBlock { Name = "DestinationError", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed };
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
            { error.Text = e.Message; }
        };
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Destino do relatório", FontSize = 18, FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Informe o caminho completo para um arquivo novo. Arquivos existentes serão preservados.", TextWrapping = TextWrapping.Wrap },
                destination, error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { save, cancel } }
            }
        };
        Opened += (_, _) => { destination.Focus(); destination.SelectAll(); };
    }
    public static Task<string?> ChooseAsync(Window owner, string suggestedFileName, string extension = ".json") => new JsonSaveDialog(suggestedFileName, extension).ShowDialog<string?>(owner);
}
