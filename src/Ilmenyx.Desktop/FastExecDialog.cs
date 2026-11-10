// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Ilmenyx.Platform.Linux;
using Ilmenyx.Core;

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
    public FastExecDialog(Func<string, Task<bool>> addToLibrary, Func<string, string?> find, Func<FastExecPlan, CancellationToken, Task<int>>? run = null, string? tempRoot = null, string? classicRoot = null)
    {
        this.run = run ?? FastExec.RunAsync;
        Icon = AppIcon.Window; Title = L.T("Ilmenyx Controller — Executar instalador do Windows"); Width = 660; MinWidth = 480; SizeToContent = SizeToContent.Height; MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Palette.Panel; Foreground = Palette.Fg; CanResize = false;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);

        var installer = new TextBox { Name = "FxInstaller", PlaceholderText = L.T("/caminho/do/setup.exe") };
        var destination = new TextBox { Name = "FxDestination", Text = FastExec.DefaultDestination() };
        AutomationProperties.SetName(installer, L.T("Instalador do Windows")); AutomationProperties.SetName(destination, L.T("Pasta onde o jogo vai ficar"));
        var pickInstaller = new Button { Name = "FxPickInstaller", Content = L.T("Procurar…"), FontSize = 12, Padding = new Thickness(14, 6) };
        var pickDestination = new Button { Name = "FxPickDestination", Content = L.T("Procurar…"), FontSize = 12, Padding = new Thickness(14, 6) };
        pickInstaller.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = L.T("Escolha o instalador do Windows"), AllowMultiple = false,
                FileTypeFilter = [new(L.T("Instaladores do Windows")) { Patterns = ["*.exe", "*.EXE"] }, new(L.T("Todos os arquivos")) { Patterns = ["*"] }] });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } p) installer.Text = p;
        };
        pickDestination.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.T("Pasta onde o jogo vai ficar"), AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } p) destination.Text = p;
        };
        DockPanel Field(TextBox box, Button pick) { var d = new DockPanel(); DockPanel.SetDock(pick, Dock.Right); pick.Margin = new Thickness(8, 0, 0, 0); d.Children.Add(pick); d.Children.Add(box); return d; }

        // Short line on screen; the full guidance stays in the tooltip (owner 2026-11-08: professional, no walls of text).
        var tipsText = new TextBlock { Text = L.T("Mantenha a pasta C:\\Jogos sugerida e desmarque DirectX, .NET e Visual C++."), Foreground = Palette.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var tipsMore = new TextBlock { Text = L.T("Detalhes"), Foreground = Palette.Warn, FontSize = 12, TextDecorations = TextDecorations.Underline, Cursor = new Cursor(StandardCursorType.Help), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(tipsMore, L.T("• A maioria dos instaladores já abre com a pasta C:\\Jogos\\<nome do jogo>. Se o seu pedir a pasta, use C:\\Jogos. No fim, o Ilmenyx move o jogo para a pasta acima e apaga o resto; um jogo instalado em outra pasta do C: também é encontrado.\n") +
            L.T("• Desmarque DirectX, .NET, Visual C++ e outros componentes do Windows: o Proton já cuida deles.\n") +
            L.T("• Atalhos do instalador não são criados no seu Linux. Quando ele terminar, feche-o normalmente.\n") +
            L.T("• Às vezes a janela do instalador some, não volta pela barra de tarefas ou parece travada (limitação do Wine). Ele continua trabalhando enquanto a barra do Ilmenyx se move.\n") +
            L.T("• Instaladores grandes de 32 bits podem esgotar a memória que conseguem usar (4 GB) e parar logo no início, mesmo com RAM livre. A falha é intermitente: tentar de novo costuma resolver. Se o instalador tiver a opção de limitar a memória (por exemplo, 2 GB), ative."));
        ToolTip.SetShowDelay(tipsMore, 150);
        var tipsLines = new StackPanel { Spacing = 2, Children = { tipsText,
            new TextBlock { Text = L.T("Se a janela do instalador sumir, ele continua rodando."), Foreground = Palette.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Name = "FxTipStuck", Text = L.T("Se travar no início, tente de novo; se o instalador oferecer limite de memória, ative."), Foreground = Palette.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap } } };
        var tipsRow = new DockPanel(); DockPanel.SetDock(tipsMore, Dock.Right); tipsRow.Children.Add(tipsMore); tipsRow.Children.Add(tipsLines);
        var tips = new Border { Name = "FxTips", Background = Palette.WarnBg, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(14, 8), Child = tipsRow };
        // Which Proton runs the installer. Classic (Proton 9, 32-bit Wine layout) is what large 32-bit installers need (see ClassicProton).
        var mode = new ComboBox { Name = "FxProtonMode", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] {
            L.T("Proton padrão"),
            L.T("Proton clássico (32 bits)"),
            L.T("GE-Proton mais recente"),
            L.T("Wine comum (Soda)") } };
        string[] modeHelp = [
            L.T("Proton já instalado no sistema. Serve para a maioria dos instaladores."),
            L.T("GE-Proton 9 com Wine de 32 bits clássico. Para instaladores grandes de 32 bits. Baixa cerca de 470 MB na primeira vez."),
            L.T("GE-Proton mais novo, baixado na primeira vez."),
            L.T("Wine comum, o mesmo que o Bottles usa (Soda). Para instaladores que falham com erro de descompactação no Proton.")];
        void ModeTip() => ToolTip.SetTip(mode, modeHelp[Math.Clamp(mode.SelectedIndex, 0, modeHelp.Length - 1)]);
        ModeTip(); mode.SelectionChanged += (_, _) => ModeTip();
        AutomationProperties.SetName(mode, L.T("Proton usado para rodar o instalador"));
        var ge = new StackPanel { Spacing = 4, Children = { UiKit.Label(L.T("COMPATIBILIDADE")), mode } };
        var status = new TextBlock { Name = "FxStatus", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
        // Visual feedback while anything runs: percentage while downloading, moving bar while the installer or the move works.
        var progress = new ProgressBar { Name = "FxProgress", Height = 4, MinHeight = 4, Minimum = 0, Maximum = 1, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        void Busy(bool on, double? value = null) { progress.IsVisible = on; progress.IsIndeterminate = on && value is null; if (value is { } v) progress.Value = v; }
        var results = new StackPanel { Name = "FxResults", Spacing = 6 };
        var start = UiKit.Primary("FxStart", L.T("Executar instalador"));
        var close = new Button { Name = "FxClose", Content = L.T("Fechar"), IsCancel = true, Padding = new Thickness(18, 8) };
        close.Click += (_, _) => { if (running is not null) running.Cancel(); else Close(); };

        start.Click += async (_, _) =>
        {
            FastExecPlan plan;
            if (mode.SelectedIndex == 1 && !ClassicProton.IsInstalled(classicRoot))
            {
                if (!await ConfirmDialog.AskAsync(this, L.T("Proton clássico"), L.T($"Baixar o {ClassicProton.Version} (cerca de 470 MB) do GitHub do projeto GE-Proton?\n\nÉ feito uma vez só e fica em ~/.local/share/ilmenyx/runners. O arquivo é conferido pela soma oficial antes de ser usado."), L.T("Baixar"), L.T("Cancelar"))) return;
                start.IsEnabled = false; status.Foreground = Palette.Muted; Busy(true, 0);
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("IlmenyxController (+https://github.com/Ilmenyx-Technology/ilmenyx)");
                    await ClassicProton.EnsureAsync(http, new Progress<double>(v => { status.Text = L.T($"Baixando o {ClassicProton.Version}… {v:P0}"); Busy(true, v); }), CancellationToken.None, classicRoot);
                }
                catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                { status.Foreground = Palette.Warn; status.Text = L.T("Não consegui preparar o Proton clássico: ") + e.Message; start.IsEnabled = true; return; }
                finally { start.IsEnabled = true; Busy(false); }
            }
            string? wine = null;
            if (mode.SelectedIndex == 3 && (wine = PlainWine.Find()) is null)
            {
                // No Wine of our own and none downloaded by Bottles: fetch the same Soda build that installed the repacks (once).
                if (!await ConfirmDialog.AskAsync(this, L.T("Wine comum (Soda)"), L.T($"Baixar o Wine {PortableWine.Version} (cerca de 170 MB) do GitHub do projeto Bottles?\n\nÉ feito uma vez só e fica em ~/.local/share/ilmenyx/runners. O arquivo é conferido pela soma oficial (SHA-256). Não precisa instalar o Bottles."), L.T("Baixar"), L.T("Cancelar"))) return;
                start.IsEnabled = false; status.Foreground = Palette.Muted; Busy(true, 0);
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("IlmenyxController (+https://github.com/Ilmenyx-Technology/ilmenyx)");
                    await PortableWine.EnsureAsync(http, new Progress<double>(v => { status.Text = L.T($"Baixando o Wine {PortableWine.Version}… {v:P0}"); Busy(true, v); }), CancellationToken.None);
                }
                catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                { status.Foreground = Palette.Warn; status.Text = L.T("Não consegui preparar o Wine comum: ") + e.Message; start.IsEnabled = true; return; }
                finally { start.IsEnabled = true; Busy(false); }
                if ((wine = PlainWine.Find()) is null) { status.Foreground = Palette.Warn; status.Text = L.T("O Wine comum foi baixado, mas não ficou utilizável. Escolha um Proton."); return; }
            }
            try { plan = FastExec.Plan((installer.Text ?? "").Trim(), (destination.Text ?? "").Trim(), find, tempRoot, geProton: mode.SelectedIndex == 2, protonPath: mode.SelectedIndex == 1 ? ClassicProton.Folder(classicRoot) : null, wineBin: wine); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { status.Foreground = Palette.Warn; status.Text = e.Message; return; }
            start.IsEnabled = false; installer.IsEnabled = destination.IsEnabled = pickInstaller.IsEnabled = pickDestination.IsEnabled = false; results.Children.Clear(); ToolTip.SetTip(status, null);
            close.Content = L.T("Cancelar"); var cts = running = new CancellationTokenSource();
            var since = DateTime.UtcNow.AddSeconds(-2);
            bool finished = false; Busy(true);
            try
            {
                FastExec.CleanupStale(tempRoot);
                FastExec.Prepare(plan);
                status.Foreground = Palette.Muted; status.Text = L.T("Preparando o Windows temporário…");
                await Task.Run(() => FastExec.StageInstaller(plan));
                status.Foreground = Palette.Muted;
                status.Text = L.T("Instalador em execução") + (wine is null ? "" : $" ({PlainWine.Name(wine)})") + L.T(". A primeira janela pode levar até um minuto.");
                int code = await this.run(plan, cts.Token);
                status.Text = code == 0 ? L.T("Instalador finalizado.") : L.T($"O instalador terminou com o código {code}.") + (mode.SelectedIndex != 3 ? L.T(" Em erro de descompactação (ISDone, CRC), tente \"Wine comum (Soda)\".") : "");
                if (code != 0 && FastExec.LatestLog(tempRoot) is { } log) ToolTip.SetTip(status, "Registro: " + log);
                finished = code == 0; // a failed installer leaves a half-installed game: do not move it, just clean up
            }
            catch (OperationCanceledException) { status.Text = L.T("Instalação cancelada."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { status.Foreground = Palette.Warn; status.Text = L.T("Não consegui executar: ") + e.Message; }
            // The game was installed inside the temporary Windows (C:\Jogos): move it out before deleting the rest.
            bool keep = false; IReadOnlyList<string> moved = [];
            if (finished)
            {
                try
                {
                    moved = await Task.Run(() => FastExec.MoveToDestination(plan, name => Avalonia.Threading.Dispatcher.UIThread.Post(() => status.Text = L.T($"Movendo \"{name}\" para {plan.Destination}…"))));
                    status.Text = moved.Count > 0 ? L.T($"Instalador finalizado. {moved.Count} pasta(s) movida(s) para {plan.Destination}.") : L.T("Instalador finalizado.");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    keep = true; status.Foreground = Palette.Warn;
                    status.Text = L.T("Não consegui mover o jogo: ") + e.Message; ToolTip.SetTip(status, L.T("O Windows temporário foi mantido em ") + plan.TempPrefix + L.T(" para você recuperar os arquivos."));
                }
            }
            if (!keep) await Task.Run(() => FastExec.Cleanup(plan));
            Busy(false); running = null; close.Content = L.T("Fechar");
            start.IsEnabled = true; installer.IsEnabled = destination.IsEnabled = pickInstaller.IsEnabled = pickDestination.IsEnabled = true;
            if (keep) return;
            ToolTip.SetTip(status, L.T("O Windows temporário foi apagado; só ficou o jogo, em ") + plan.Destination + ".");
            var found = moved.Count > 0 ? FastExec.ExecutablesIn(moved) : FastExec.NewExecutables(plan.Destination, since);
            if (found.Count == 0) { results.Children.Add(new TextBlock { Text = L.T("Nenhum jogo novo encontrado nessa pasta. Adicione o .exe pela página Jogos."), Foreground = Palette.Faint, FontSize = 12, TextWrapping = TextWrapping.Wrap }); return; }
            results.Children.Add(new TextBlock { Text = L.T("Adicione o executável principal à biblioteca:"), FontSize = 12, Foreground = Palette.Muted });
            int i = 0;
            foreach (var exe in found.Take(5))
            {
                var add = new Button { Name = "FxAdd" + i++, Content = L.T("Adicionar"), FontSize = 11, Padding = new Thickness(12, 5) };
                var row = new DockPanel { Children = { add, new TextBlock { Text = Path.GetRelativePath(plan.Destination, exe), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } } };
                DockPanel.SetDock(add, Dock.Right);
                add.Click += async (_, _) => { add.IsEnabled = false; add.Content = await addToLibrary(exe) ? L.T("ADICIONADO") : L.T("JÁ ESTAVA"); };
                results.Children.Add(row);
            }
        };

        var body = new StackPanel { Spacing = 12, Children = {
            UiKit.PageHeader(L.T("Executar instalador do Windows"), L.T("Roda o instalador num Windows descartável. No fim, só o jogo fica.")),
            UiKit.Label(L.T("INSTALADOR (.EXE)")), Field(installer, pickInstaller),
            UiKit.Label(L.T("PASTA ONDE O JOGO VAI FICAR NO FINAL")), Field(destination, pickDestination),
            tips, ge, progress, status, results } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Children = { close, start } };
        var dock = new DockPanel { Margin = new Thickness(22, 18) };
        DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(new ScrollViewer { Content = body, MaxHeight = 620 });
        Content = dock;
        Closing += (_, e) => { if (running is not null) { running.Cancel(); e.Cancel = true; } };
    }
}
