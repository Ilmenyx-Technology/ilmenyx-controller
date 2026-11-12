// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ilmenyx.Core;
using Ilmenyx.Platform.Linux;

namespace Ilmenyx.Desktop;

/// <summary>
/// "Liberar espaço": what is safe to delete (recommended items already ticked) and big files unused for 50+ days (never ticked). The user
/// picks; one confirmation lists everything; what needs root shows its command and asks for the password through polkit.
/// Scanning happens only on this page (on open and on "Analisar de novo"), off the UI thread. "Exibir detalhes" shows what each item holds
/// (each program's cache, each file in the trash, each package…); for the trash and the caches the user can untick parts to keep them.
/// </summary>
public sealed class CleanupPage : StackPanel
{
    private readonly CancellationToken lifetime;
    private readonly Func<Window?> owner;
    private readonly StackPanel safeList = new() { Name = "CleanSafeList" };
    private readonly StackPanel staleList = new() { Name = "CleanStaleList" };
    private readonly TextBlock status = new() { Name = "CleanStatus", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock total = new() { Name = "CleanTotal", FontSize = 16, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock projection = new() { Name = "CleanProjection", FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { IsIndeterminate = true, IsVisible = false, MinHeight = 4 };
    private readonly Button scan, clean;
    private readonly HashSet<CleanupKind> chosen = [];
    private readonly HashSet<string> chosenFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<CleanupKind, HashSet<string>> skipped = []; // parts the user unticked to keep
    private readonly HashSet<CleanupKind> expanded = [];
    private const int PartsShown = 8;
    private IReadOnlyList<CleanupItem> items = [];
    private IReadOnlyList<StaleFile> stale = [];
    private int staleShown = 8;
    private bool busy;

    public CleanupPage(Func<Window?> owner, CancellationToken lifetime)
    {
        this.owner = owner; this.lifetime = lifetime;
        Spacing = 12;
        scan = new Button { Name = "BtnCleanScan", Content = L.T("Analisar de novo"), FontWeight = FontWeight.SemiBold, Padding = new Thickness(16, 7) };
        scan.Click += async (_, _) => await ScanAsync();
        Children.Add(UiKit.PageHeader(L.T("Liberar espaço"), L.T("Nada é apagado sem você confirmar. Caches se refazem sozinhos; o que pede senha mostra antes o comando."), scan));
        Children.Add(status); Children.Add(progress);
        var safeCard = UiKit.Card(new StackPanel { Spacing = 2, Children = { UiKit.Label(L.T("SEGURO DE APAGAR")), safeList } }, new Thickness(0)); safeCard.Padding = new Thickness(20, 16);
        var staleIntro = new TextBlock { Text = L.T($"Arquivos grandes (50 MB ou mais) da sua pasta pessoal que ninguém abre nem muda há mais de {Cleanup.StaleDays} dias. Não vêm marcados: confira um a um."), FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        var staleCard = UiKit.Card(new StackPanel { Spacing = 2, Children = { UiKit.Label(L.T($"SEM USO HÁ MAIS DE {Cleanup.StaleDays} DIAS")), staleIntro, staleList } }, new Thickness(0)); staleCard.Padding = new Thickness(20, 16);
        Children.Add(HealthCardView.Columns([safeCard, staleCard], 900, 12));
        clean = UiKit.Primary("BtnCleanRun", L.T("Limpar selecionados")); clean.IsEnabled = false; clean.VerticalAlignment = VerticalAlignment.Center;
        clean.Click += async (_, _) => await CleanAsync();
        var bar = new Grid { ColumnDefinitions = new("*,Auto") };
        var left = new StackPanel { Children = { total, projection } };
        Grid.SetColumn(clean, 1); bar.Children.Add(left); bar.Children.Add(clean);
        var totalCard = new Border { Name = "CleanTotalCard", Child = bar, Background = Palette.AccentContainer, CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius), Padding = new Thickness(20, 14) };
        total.Foreground = Palette.OnAccentContainer; projection.Foreground = Palette.OnAccentContainer;
        Children.Add(totalCard);
        Loaded += async (_, _) => { if (items.Count == 0 && !busy) await ScanAsync(); };
    }

    private static string Size(long b) => b >= 1L << 30 ? (b / (double)(1L << 30)).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " GB"
        : b >= 1L << 20 ? (b / (double)(1L << 20)).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " MB"
        : (b / 1024.0).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " KB";

    public async Task ScanAsync()
    {
        if (busy) return;
        busy = true; scan.IsEnabled = false; clean.IsEnabled = false; progress.IsVisible = true;
        status.Text = L.T("Medindo o que pode ser liberado (só leitura)…");
        try
        {
            var (id, like) = new LinuxDriverInventory(etcRoot: HostSpawn.InFlatpak ? "/run/host/etc" : "/etc").Distro();
            items = await Cleanup.ScanAsync(id, like, lifetime);
            stale = await Task.Run(() => Cleanup.StaleFiles(Cleanup.Home, DateTime.UtcNow, lifetime), lifetime);
            chosen.Clear(); chosenFiles.Clear(); skipped.Clear();
            foreach (var i in items.Where(i => i.Recommended && i.Unavailable is null && i.Bytes is > 0)) chosen.Add(i.Kind);
            Render();
            status.Text = L.T($"Analisado às {DateTime.Now:HH:mm}. Nada foi apagado.");
        }
        catch (OperationCanceledException) { status.Text = L.T("Análise cancelada."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = L.T("Não foi possível terminar a análise: ") + e.Message; }
        finally { busy = false; scan.IsEnabled = true; progress.IsVisible = false; Totals(); }
    }

    private void Render()
    {
        safeList.Children.Clear();
        foreach (var item in items) safeList.Children.Add(SafeRow(item));
        staleList.Children.Clear();
        if (stale.Count == 0) staleList.Children.Add(new TextBlock { Text = L.T("Nenhum arquivo grande parado há tanto tempo. Ótimo."), FontSize = 12.5, Foreground = Palette.Muted, Margin = new Thickness(0, 6) });
        foreach (var f in stale.Take(staleShown)) staleList.Children.Add(StaleRow(f));
        if (stale.Count > staleShown)
        {
            var more = new Button { Name = "BtnCleanMore", Content = L.T($"Mostrar mais {Math.Min(20, stale.Count - staleShown)}"), FontSize = 12, Background = Brushes.Transparent, Foreground = Palette.AccentText, Padding = new Thickness(0, 6) };
            more.Click += (_, _) => { staleShown += 20; Render(); };
            staleList.Children.Add(more);
        }
    }

    private HashSet<string> Skipped(CleanupKind kind) => skipped.TryGetValue(kind, out var set) ? set : skipped[kind] = new(StringComparer.Ordinal);

    private static bool Choosable(CleanupItem item) => item.PartsChoosable && item.Parts is { Count: > 0 };

    private static long Whole(CleanupItem item) => Choosable(item) ? item.Parts!.Sum(p => p.Bytes) : item.Bytes ?? 0;

    /// <summary>What the item frees with the current choice: the ticked parts when the user picked some, else the whole item.</summary>
    private long ChosenBytes(CleanupItem item) => !chosen.Contains(item.Kind) ? 0
        : Choosable(item) ? item.Parts!.Where(p => !Skipped(item.Kind).Contains(p.Key)).Sum(p => p.Bytes) : item.Bytes ?? 0;

    /// <summary>The parts to delete, or null for the whole item (nothing unticked).</summary>
    private IReadOnlySet<string>? OnlyParts(CleanupItem item) => Choosable(item) && Skipped(item.Kind).Count > 0
        ? item.Parts!.Select(p => p.Key).Where(k => !Skipped(item.Kind).Contains(k)).ToHashSet(StringComparer.Ordinal) : null;

    private Control SafeRow(CleanupItem item)
    {
        bool usable = item.Unavailable is null && (item.Bytes is null or > 0);
        var parts = item.Parts ?? [];
        bool choosable = Choosable(item) && usable;
        var skip = Skipped(item.Kind);
        var box = new CheckBox { Name = "CleanItem_" + item.Kind, IsChecked = chosen.Contains(item.Kind), IsEnabled = usable, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 6, 0) };
        AutomationProperties.SetName(box, item.Title);
        var titleLine = new WrapPanel { ItemSpacing = 8, Children = { new TextBlock { Text = item.Title, FontSize = 13.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } } };
        if (item.Recommended && usable) titleLine.Children.Add(UiKit.Tag(L.T("recomendado"), Palette.Success));
        if (item.NeedsPassword) titleLine.Children.Add(UiKit.Tag(L.T("pede senha"), Palette.Muted));
        var text = new StackPanel { Spacing = 2, Children = { titleLine, new TextBlock { Text = item.Unavailable ?? item.Explanation, FontSize = 12, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap } } };
        if (item.Command.Length > 0 && usable) text.Children.Add(new TextBlock { Text = item.Command, FontSize = 11, FontFamily = IlmenyxTheme.Mono, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap });
        var size = new TextBlock { Name = "CleanSize_" + item.Kind, FontSize = 14, FontWeight = FontWeight.SemiBold, FontFamily = IlmenyxTheme.Mono, HorizontalAlignment = HorizontalAlignment.Right };
        var of = new TextBlock { FontSize = 11, Foreground = Palette.Faint, HorizontalAlignment = HorizontalAlignment.Right, IsVisible = false };
        var sizes = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0), Children = { size, of } };

        var partBoxes = new List<(CleanupPart Part, CheckBox Box)>();
        bool syncing = false;
        void Sync()
        {
            syncing = true;
            bool on = chosen.Contains(item.Kind);
            box.IsChecked = on;
            foreach (var (p, b) in partBoxes) b.IsChecked = on && !skip.Contains(p.Key);
            bool partial = choosable && on && skip.Count > 0;
            size.Text = partial ? Size(ChosenBytes(item)) : item.Bytes is { } b0 ? (b0 == 0 ? L.T("vazio") : Size(Whole(item))) : "—";
            of.Text = partial ? L.T("de ") + Size(Whole(item)) : ""; of.IsVisible = partial;
            syncing = false;
            Totals();
        }
        box.IsCheckedChanged += (_, _) => { if (syncing) return; if (box.IsChecked == true) { chosen.Add(item.Kind); skip.Clear(); } else chosen.Remove(item.Kind); Sync(); };
        void Pick(CleanupPart part, bool on)
        {
            if (syncing) return;
            if (on && !chosen.Contains(item.Kind)) { chosen.Add(item.Kind); skip.Clear(); foreach (var p in parts) if (p.Key != part.Key) skip.Add(p.Key); }
            else if (on) skip.Remove(part.Key);
            else { skip.Add(part.Key); if (parts.All(p => skip.Contains(p.Key))) { chosen.Remove(item.Kind); skip.Clear(); } }
            Sync();
        }

        if (parts.Count > 0)
        {
            var details = new StackPanel { Name = "CleanDetails_" + item.Kind, Spacing = 0, Margin = new Thickness(0, 6, 0, 0) };
            var toggle = new Button { Name = "BtnCleanDetails_" + item.Kind, FontSize = 12, Background = Brushes.Transparent, Foreground = Palette.AccentText, Padding = new Thickness(0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            void Label()
            {
                details.IsVisible = expanded.Contains(item.Kind);
                toggle.Content = details.IsVisible ? L.T("Ocultar detalhes") : L.T($"Exibir detalhes ({parts.Count})");
                AutomationProperties.SetName(toggle, (details.IsVisible ? L.T("Ocultar detalhes de ") : L.T("Exibir detalhes de ")) + item.Title);
            }
            void Fill(int count)
            {
                details.Children.Clear(); partBoxes.Clear();
                string intro = item.Kind switch
                {
                    CleanupKind.Trash or CleanupKind.UserCache when choosable => L.T("Desmarque o que quiser manter. Só o que estiver marcado é apagado."),
                    CleanupKind.UserCache => L.T("O que tem em ~/.cache, do maior para o menor."),
                    CleanupKind.PackageCache => L.T("Instaladores antigos por programa. O comando acima apaga todos de uma vez; os programas instalados não mudam."),
                    CleanupKind.Journal => L.T("O comando acima apaga só os registros com mais de 4 semanas."),
                    CleanupKind.FlatpakUnused => L.T("Bibliotecas instaladas. O Flatpak remove só as que nenhum app usa e confere isso na hora; os primeiros da lista são os candidatos."),
                    _ => "",
                };
                details.Children.Add(new TextBlock { Text = intro, FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
                if (choosable)
                {
                    var all = new Button { Name = "BtnCleanAll_" + item.Kind, Content = L.T("Marcar todos"), FontSize = 11.5, Background = Brushes.Transparent, Foreground = Palette.AccentText, Padding = new Thickness(0, 2, 12, 2) };
                    var none = new Button { Name = "BtnCleanNone_" + item.Kind, Content = L.T("Desmarcar todos"), FontSize = 11.5, Background = Brushes.Transparent, Foreground = Palette.AccentText, Padding = new Thickness(0, 2) };
                    all.Click += (_, _) => { chosen.Add(item.Kind); skip.Clear(); Sync(); };
                    none.Click += (_, _) => { chosen.Remove(item.Kind); skip.Clear(); Sync(); };
                    details.Children.Add(new WrapPanel { Children = { all, none } });
                }
                foreach (var part in parts.Take(count)) details.Children.Add(PartRow(part, choosable, partBoxes, Pick));
                if (parts.Count > count)
                {
                    var more = new Button { Name = "BtnCleanAllParts_" + item.Kind, Content = L.T($"Mostrar todos ({parts.Count})"), FontSize = 12, Background = Brushes.Transparent, Foreground = Palette.AccentText, Padding = new Thickness(0, 6) };
                    more.Click += (_, _) => { Fill(parts.Count); Sync(); };
                    details.Children.Add(more);
                }
            }
            toggle.Click += (_, _) =>
            {
                if (!expanded.Remove(item.Kind)) expanded.Add(item.Kind);
                if (details.Children.Count == 0) { Fill(PartsShown); Sync(); }
                Label();
            };
            if (expanded.Contains(item.Kind)) Fill(PartsShown);
            Label();
            text.Children.Add(toggle); text.Children.Add(details);
        }

        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        Grid.SetColumn(text, 1); Grid.SetColumn(sizes, 2);
        row.Children.Add(box); row.Children.Add(text); row.Children.Add(sizes);
        Sync();
        return new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10), Child = row };
    }

    /// <summary>One part inside "Exibir detalhes": plain name, what it is, size; a checkbox when the part can be kept apart.</summary>
    private static Control PartRow(CleanupPart part, bool choosable, List<(CleanupPart Part, CheckBox Box)> boxes, Action<CleanupPart, bool> pick)
    {
        var name = new TextBlock { Text = part.Name, FontSize = 12.5, FontWeight = FontWeight.Medium, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (part.Key.Length > 0 && part.Key != part.Name) ToolTip.SetTip(name, part.Key);
        var head = new WrapPanel { ItemSpacing = 6, Children = { name } };
        if (part.Redownload) head.Children.Add(UiKit.Tag(L.T("baixa de novo"), Palette.Warn));
        var info = new StackPanel { Children = { head } };
        if (part.Note.Length > 0) info.Children.Add(new TextBlock { Text = part.Note, FontSize = 11.5, Foreground = Palette.Muted, TextWrapping = TextWrapping.Wrap });
        var size = new TextBlock { Text = Size(part.Bytes), FontSize = 12.5, FontFamily = IlmenyxTheme.Mono, Foreground = Palette.Muted, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        if (choosable)
        {
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 6, 0) };
            AutomationProperties.SetName(box, part.Name);
            box.IsCheckedChanged += (_, _) => { if (box.IsChecked is { } on) pick(part, on); };
            boxes.Add((part, box)); row.Children.Add(box);
        }
        Grid.SetColumn(info, 1); Grid.SetColumn(size, 2);
        row.Children.Add(info); row.Children.Add(size);
        return new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6), Child = row };
    }

    private Control StaleRow(StaleFile f)
    {
        string shown = f.Path.StartsWith(Cleanup.Home + "/", StringComparison.Ordinal) ? f.Path[(Cleanup.Home.Length + 1)..] : f.Path;
        var box = new CheckBox { IsChecked = chosenFiles.Contains(f.Path), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetName(box, shown);
        box.IsCheckedChanged += (_, _) => { if (box.IsChecked == true) chosenFiles.Add(f.Path); else chosenFiles.Remove(f.Path); Totals(); };
        var name = new TextBlock { Text = shown, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, f.Path);
        var days = new TextBlock { Text = L.T($"{f.DaysUnused} dias"), FontSize = 11.5, Foreground = Palette.Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) };
        var size = new TextBlock { Text = Size(f.Bytes), FontSize = 13, FontWeight = FontWeight.SemiBold, FontFamily = IlmenyxTheme.Mono, VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto") };
        Grid.SetColumn(name, 1); Grid.SetColumn(days, 2); Grid.SetColumn(size, 3);
        row.Children.Add(box); row.Children.Add(name); row.Children.Add(days); row.Children.Add(size);
        return new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8), Child = row };
    }

    private long Selected() => items.Sum(ChosenBytes) + stale.Where(f => chosenFiles.Contains(f.Path)).Sum(f => f.Bytes);

    /// <summary>Space of the file system of the home folder, before and after the selection.</summary>
    private void Totals()
    {
        long bytes = Selected();
        bool flatpak = chosen.Contains(CleanupKind.FlatpakUnused);
        total.Text = chosen.Count + chosenFiles.Count == 0 ? L.T("Nada selecionado") : Size(bytes) + L.T(" selecionados") + (flatpak ? L.T(" + Flatpak sem uso") : "");
        clean.IsEnabled = !busy && chosen.Count + chosenFiles.Count > 0;
        projection.Text = "";
        try
        {
            var drive = DriveInfo.GetDrives().Where(d => d.IsReady && Cleanup.Home.StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal)).OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
            if (drive is { TotalSize: > 0 })
            {
                double now = 1 - drive.AvailableFreeSpace / (double)drive.TotalSize, after = Math.Max(0, now - bytes / (double)drive.TotalSize);
                projection.Text = bytes > 0 ? L.T($"O disco da sua pasta pessoal passa de {now * 100:0}% para {after * 100:0}% cheio.") : L.T($"O disco da sua pasta pessoal está {now * 100:0}% cheio.");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>": Firefox, pip e mais 3" when only some parts are ticked; empty when the whole item goes.</summary>
    private string Picked(CleanupItem item)
    {
        if (OnlyParts(item) is not { } only) return "";
        var names = item.Parts!.Where(p => only.Contains(p.Key)).Select(p => p.Name).ToList();
        return ": " + string.Join(", ", names.Take(3)) + (names.Count > 3 ? L.T($" e mais {names.Count - 3}") : "");
    }

    private async Task CleanAsync()
    {
        if (owner() is not { } window) return;
        var picked = items.Where(i => chosen.Contains(i.Kind)).ToList();
        var files = stale.Where(f => chosenFiles.Contains(f.Path)).ToList();
        var lines = picked.Select(i => "• " + i.Title + Picked(i) + (i.Bytes is not null ? " (" + Size(ChosenBytes(i)) + ")" : "")).ToList();
        if (files.Count > 0) lines.Add("• " + L.T($"{files.Count} arquivo(s) sem uso ({Size(files.Sum(f => f.Bytes))}), apagados para sempre"));
        string message = string.Join("\n", lines);
        if (picked.Any(i => i.NeedsPassword)) message += "\n\n" + L.T("Os itens marcados \"pede senha\" abrem o pedido de senha do sistema.");
        if (picked.Any(i => i.Kind == CleanupKind.UserCache)) message += "\n\n" + L.T("Feche o navegador e outros programas antes, para os caches deles serem apagados por inteiro.");
        if (!await ConfirmDialog.AskAsync(window, L.T("Apagar o que foi selecionado?"), message, L.T("Apagar"), L.T("Cancelar"))) return;
        busy = true; clean.IsEnabled = false; scan.IsEnabled = false; progress.IsVisible = true;
        long freed = 0; var errors = new List<string>();
        try
        {
            foreach (var item in picked)
            {
                status.Text = L.T("Limpando: ") + item.Title + "…";
                var (ok, msg, bytes) = await Cleanup.RunAsync(item, lifetime, OnlyParts(item));
                freed += bytes;
                if (!ok) errors.Add(item.Title + ": " + msg);
            }
            if (files.Count > 0) { status.Text = L.T("Apagando os arquivos escolhidos…"); freed += await Task.Run(() => Cleanup.DeleteFiles(files, Cleanup.Home, lifetime), lifetime); }
        }
        catch (OperationCanceledException) { errors.Add(L.T("cancelado")); }
        finally { busy = false; progress.IsVisible = false; }
        await ScanAsync();
        status.Text = (freed > 0 ? L.T("Pronto: cerca de ") + Size(freed) + L.T(" liberados.") : L.T("Pronto.")) + (errors.Count > 0 ? " " + L.T("Não deu para limpar: ") + string.Join("; ", errors) : "");
        status.Foreground = errors.Count == 0 ? Palette.Success : Palette.Warn;
    }
}
