// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>
/// "Executar instalador do Windows": one disposable run of a setup.exe with Proton (see <see cref="FastExec"/>). The user picks the
/// installer and the folder where the game should live (drive D: inside the installer); afterwards the temporary Windows is deleted
/// and the new game executables can be added to the library with one click.
/// </summary>
public sealed class FastExecDialog : Window
{
    private readonly Func<FastExecPlan, CancellationToken, Task<int>> run;
    private CancellationTokenSource? running;

    /// <param name="addToLibrary">Adds an installed game executable to the library (returns false if it was already there).</param>
    /// <param name="run">Runs the plan (tests inject a fake); default <see cref="FastExec.RunAsync"/>.</param>
    public FastExecDialog(Func<string, Task<bool>> addToLibrary, Func<string, string?> find, Func<FastExecPlan, CancellationToken, Task<int>>? run = null, string? tempRoot = null)
    {
        this.run = run ?? FastExec.RunAsync;
        Icon = AppIcon.Window; Title = "Ilmenyx — Executar instalador do Windows"; Width = 660; MinWidth = 480; SizeToContent = SizeToContent.Height; MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Palette.Panel; Foreground = Palette.Fg; CanResize = false;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);

        var installer = new TextBox { Name = "FxInstaller", PlaceholderText = "/caminho/do/setup.exe" };
        var destination = new TextBox { Name = "FxDestination", Text = FastExec.DefaultDestination() };
        AutomationProperties.SetName(installer, "Instalador do Windows"); AutomationProperties.SetName(destination, "Pasta onde o jogo vai ficar");
        var pickInstaller = new Button { Name = "FxPickInstaller", Content = "PROCURAR…", FontSize = 12, Padding = new Thickness(14, 6) };
        var pickDestination = new Button { Name = "FxPickDestination", Content = "PROCURAR…", FontSize = 12, Padding = new Thickness(14, 6) };
        pickInstaller.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Escolha o instalador do Windows", AllowMultiple = false,
                FileTypeFilter = [new("Instaladores do Windows") { Patterns = ["*.exe", "*.EXE"] }, new("Todos os arquivos") { Patterns = ["*"] }] });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } p) installer.Text = p;
        };
        pickDestination.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta onde o jogo vai ficar", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } p) destination.Text = p;
        };
        DockPanel Field(TextBox box, Button pick) { var d = new DockPanel(); DockPanel.SetDock(pick, Dock.Right); pick.Margin = new Thickness(8, 0, 0, 0); d.Children.Add(pick); d.Children.Add(box); return d; }

        var tips = new Border
        {
            Name = "FxTips", Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(14, 10),
            Child = new StackPanel { Spacing = 6, Children = {
                new TextBlock { Text = "Durante a instalação", FontWeight = FontWeight.SemiBold, Foreground = Palette.Warn },
                new TextBlock { Text = "1. Quando o instalador perguntar onde instalar, use C:\\Jogos (já vem criado). Quando ele terminar, o Ilmenyx move o jogo pronto para a pasta acima e apaga o resto.", Foreground = Palette.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "2. Desmarque DirectX, .NET, Visual C++ e outros \"componentes do Windows\". O Proton já cuida do DirectX e do .NET, e o que o jogo precisar ele instala na hora de jogar.", Foreground = Palette.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "3. Atalhos na área de trabalho e no menu não são criados no seu Linux. Quando o instalador terminar, feche-o normalmente.", Foreground = Palette.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap } } },
        };
        var status = new TextBlock { Name = "FxStatus", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
        var results = new StackPanel { Name = "FxResults", Spacing = 6 };
        var start = UiKit.Primary("FxStart", "EXECUTAR INSTALADOR");
        var close = new Button { Name = "FxClose", Content = "Fechar", IsCancel = true, Padding = new Thickness(18, 8) };
        close.Click += (_, _) => { if (running is not null) running.Cancel(); else Close(); };

        start.Click += async (_, _) =>
        {
            FastExecPlan plan;
            try { plan = FastExec.Plan((installer.Text ?? "").Trim(), (destination.Text ?? "").Trim(), find, tempRoot); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { status.Foreground = Palette.Warn; status.Text = e.Message; return; }
            start.IsEnabled = false; installer.IsEnabled = destination.IsEnabled = pickInstaller.IsEnabled = pickDestination.IsEnabled = false; results.Children.Clear();
            close.Content = "Cancelar"; var cts = running = new CancellationTokenSource();
            var since = DateTime.UtcNow.AddSeconds(-2);
            bool finished = false;
            try
            {
                FastExec.CleanupStale(tempRoot);
                FastExec.Prepare(plan);
                status.Foreground = Palette.Muted;
                status.Text = "Instalador aberto. Na primeira vez o Proton prepara o Windows temporário (leva um minuto). Siga as dicas acima.";
                int code = await this.run(plan, cts.Token);
                status.Text = code == 0 ? "Instalador finalizado." : $"O instalador terminou com o código {code}.";
                finished = code == 0; // a failed installer leaves a half-installed game: do not move it, just clean up
            }
            catch (OperationCanceledException) { status.Text = "Instalação cancelada."; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { status.Foreground = Palette.Warn; status.Text = "Não consegui executar: " + e.Message; }
            // The game was installed inside the temporary Windows (C:\Jogos): move it out before deleting the rest.
            bool keep = false;
            if (finished)
            {
                try
                {
                    var moved = await Task.Run(() => FastExec.MoveToDestination(plan, name => Avalonia.Threading.Dispatcher.UIThread.Post(() => status.Text = $"Movendo \"{name}\" para {plan.Destination}…")));
                    status.Text = moved.Count > 0 ? $"Instalador finalizado. {moved.Count} pasta(s) movida(s) para {plan.Destination}." : "Instalador finalizado.";
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    keep = true; status.Foreground = Palette.Warn;
                    status.Text = "Não consegui mover o jogo: " + e.Message + " O Windows temporário foi mantido em " + plan.TempPrefix + " para você recuperar os arquivos.";
                }
            }
            if (!keep) FastExec.Cleanup(plan);
            running = null; close.Content = "Fechar";
            start.IsEnabled = true; installer.IsEnabled = destination.IsEnabled = pickInstaller.IsEnabled = pickDestination.IsEnabled = true;
            if (keep) return;
            status.Text += " O Windows temporário foi apagado; só ficou o jogo, em " + plan.Destination + ".";
            var found = FastExec.NewExecutables(plan.Destination, since);
            if (found.Count == 0) { results.Children.Add(new TextBlock { Text = "Não encontrei um jogo novo nessa pasta. Se ele foi instalado em outro lugar, adicione o .exe pela página Jogos (PROCURAR…).", Foreground = Palette.Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap }); return; }
            results.Children.Add(new TextBlock { Text = "Jogos encontrados — adicione o executável principal à biblioteca:", FontSize = 12, Foreground = Palette.Muted });
            int i = 0;
            foreach (var exe in found.Take(5))
            {
                var add = new Button { Name = "FxAdd" + i++, Content = "ADICIONAR", FontSize = 11, Padding = new Thickness(12, 5) };
                var row = new DockPanel { Children = { add, new TextBlock { Text = Path.GetRelativePath(plan.Destination, exe), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } } };
                DockPanel.SetDock(add, Dock.Right);
                add.Click += async (_, _) => { add.IsEnabled = false; add.Content = await addToLibrary(exe) ? "ADICIONADO" : "JÁ ESTAVA"; };
                results.Children.Add(row);
            }
        };

        var body = new StackPanel { Spacing = 12, Children = {
            UiKit.PageHeader("Executar instalador do Windows", "Roda o instalador uma vez, com Proton, num Windows descartável. No fim só o jogo instalado fica; o instalador não deixa rastros."),
            UiKit.Label("INSTALADOR (.EXE)"), Field(installer, pickInstaller),
            UiKit.Label("PASTA ONDE O JOGO VAI FICAR NO FINAL"), Field(destination, pickDestination),
            tips, status, results } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { close, start } };
        var dock = new DockPanel { Margin = new Thickness(22, 18) };
        DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = body, MaxHeight = 620 });
        Content = dock;
        Closing += (_, e) => { if (running is not null) { running.Cancel(); e.Cancel = true; } };
    }
}
