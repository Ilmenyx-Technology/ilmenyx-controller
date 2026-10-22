using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Ilmenyx.Desktop;
using Ilmenyx.Core;
using Ilmenyx.Contracts;

internal static class Program
{
    internal static string OutputDirectory { get; private set; } = "";
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Supply an absolute directory for this opt-in graphical smoke test.");
        var assemblyDirectory = Path.GetDirectoryName(typeof(DashboardWindow).Assembly.Location)!;
        Console.WriteLine("DESKTOP ASSEMBLY DIRECTORY " + assemblyDirectory);
        if (Environment.GetEnvironmentVariable("ILMENYX_EXPECT_DESKTOP_DIR") is { } expected &&
            Path.GetFullPath(expected) != assemblyDirectory)
            throw new InvalidOperationException("Smoke did not load the expected packaged desktop assembly.");
        OutputDirectory = Path.Combine(args[0], "ilmenyx-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(OutputDirectory);
        return DesktopBackend.Configure(AppBuilder.Configure<UiSmokeApp>(), DesktopBackend.Parse(DesktopBackend.ReadSelection())).StartWithClassicDesktopLifetime(Array.Empty<string>());
    }
}

public sealed class UiSmokeApp : Application
{
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new DashboardWindow { Width = 640, Height = 480 };
        desktop.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                await ExerciseAsync(window, desktop);
                File.WriteAllText(Path.Combine(Program.OutputDirectory, "result.txt"), "PASS: application-owned UI controls and real exports; no global keyboard/mouse events; no full benchmark.\n");
                Console.WriteLine("PASS " + Program.OutputDirectory);
                window.Close(); desktop.Shutdown(0);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Program.OutputDirectory, "failure.txt"), e.ToString());
                Console.Error.WriteLine(e); desktop.Shutdown(1);
            }
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Window window, string name)
    {
        var button = Find<Button>(window, name);
        if (!button.IsEffectivelyEnabled) throw new InvalidOperationException("Button disabled: " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task Until(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate()) { if (timer.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException("UI condition timed out."); await Task.Delay(20); }
    }
    private static async Task<JsonSaveDialog> Dialog(IClassicDesktopStyleApplicationLifetime desktop)
    {
        await Until(() => desktop.Windows.OfType<JsonSaveDialog>().Any(w => w.IsVisible));
        var dialog = desktop.Windows.OfType<JsonSaveDialog>().Single(w => w.IsVisible);
        await Task.Delay(60);
        if (!dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(ProductIdentity.SharingNotice) == true))
            throw new InvalidOperationException("Sharing notice missing from export dialog.");
        return dialog;
    }
    private static void Capture(Window window, string file)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        image.Render(window); image.Save(Path.Combine(Program.OutputDirectory, file), PngBitmapEncoderOptions.Default);
    }
    private static async Task SaveAsync(Window owner, IClassicDesktopStyleApplicationLifetime desktop, string button, string path)
    {
        Click(owner, button);
        var dialog = await Dialog(desktop);
        Find<TextBox>(dialog, "DestinationPath").Text = path;
        Click(dialog, "SaveJson");
        await Until(() => !dialog.IsVisible && File.Exists(path));
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Console.WriteLine("EXPORTED " + Path.GetFileName(path));
    }
    private static async Task HtmlResultAsync(Window owner, IClassicDesktopStyleApplicationLifetime desktop, string button, string jsonPath, string stem)
    {
        await Until(() => Find<Button>(owner, button).IsEnabled);
        Click(owner, button); var dialog = await Dialog(desktop);
        var cancelled = Path.Combine(Program.OutputDirectory, stem + "-cancelled.html");
        Find<TextBox>(dialog, "DestinationPath").Text = cancelled; Click(dialog, "CancelSaveJson");
        await Until(() => !dialog.IsVisible);
        if (File.Exists(cancelled)) throw new InvalidOperationException("Cancelled validation HTML dialog wrote a file.");
        Click(owner, button); dialog = await Dialog(desktop);
        Find<TextBox>(dialog, "DestinationPath").Text = Path.Combine(Program.OutputDirectory, stem + "-wrong.json");
        Click(dialog, "SaveJson");
        if (!dialog.IsVisible || Find<TextBlock>(dialog, "DestinationError").Text != "Use a extensão .html.")
            throw new InvalidOperationException("Validation HTML extension not enforced.");
        var preserved = Path.Combine(Program.OutputDirectory, stem + "-preserved.html"); File.WriteAllText(preserved, "preserve-result-html");
        Find<TextBox>(dialog, "DestinationPath").Text = preserved; Click(dialog, "SaveJson");
        if (!dialog.IsVisible || File.ReadAllText(preserved) != "preserve-result-html")
            throw new InvalidOperationException("Validation HTML destination overwritten.");
        var path = Path.Combine(Program.OutputDirectory, stem + ".html");
        Find<TextBox>(dialog, "DestinationPath").Text = path; Capture(dialog, stem + "-html-dialog.png"); Click(dialog, "SaveJson");
        await Until(() => !dialog.IsVisible && File.Exists(path));
        var json = File.ReadAllText(jsonPath);
        var expected = stem switch
        {
            "cpu" => ValidationHtmlReport.Render(JsonSerializer.Deserialize<CpuBenchmarkReport>(json, SnapshotReport.Options)!),
            "memory" => ValidationHtmlReport.Render(JsonSerializer.Deserialize<MemoryValidationReport>(json, SnapshotReport.Options)!),
            "sensors" => SensorHtmlReport.Render(JsonSerializer.Deserialize<SensorSnapshot>(json, SnapshotReport.Options)!),
            _ => ValidationHtmlReport.Render(JsonSerializer.Deserialize<GameSimValidationReport>(json, SnapshotReport.Options)!)
        };
        if (File.ReadAllText(path) != expected) throw new InvalidOperationException("Validation HTML changed the selected report or recollected context.");
        await Until(() => Find<Button>(owner, button).IsEnabled);
        FocusIs(owner, Find<Button>(owner, button));
        Console.WriteLine("PASS " + stem + " HTML UI: same JSON report, cancellation, extension, overwrite refusal and real export");
    }
    // Routed events stay within this Avalonia process; never inject desktop-wide input.
    private static void Key(Window window, Avalonia.Input.Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var target = window.FocusManager?.GetFocusedElement() as Control ?? window;
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers });
    }
    private static void FocusIs(Window window, Control expected)
    {
        if (!ReferenceEquals(window.FocusManager?.GetFocusedElement(), expected))
            throw new InvalidOperationException("Unexpected keyboard focus; expected " + expected.Name + ", actual " + (window.FocusManager?.GetFocusedElement() as Control)?.Name);
    }
    private static async Task KeyboardAsync(DashboardWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        await Until(() => Find<TextBlock>(window, "HardwareStatus").Text?.StartsWith("Snapshot:") == true);
        var opener = Find<Button>(window, "ExportHardware"); opener.Focus();
        Key(window, Avalonia.Input.Key.Space);
        var dialog = await Dialog(desktop);
        var path = Find<TextBox>(dialog, "DestinationPath");
        FocusIs(dialog, path);
        var peer = ControlAutomationPeer.CreatePeerForElement(path)!;
        if (!peer.GetName().Contains("Caminho completo") || AutomationProperties.GetLabeledBy(path) is null ||
            !(AutomationProperties.GetHelpText(path) ?? "").Contains(".json"))
            throw new InvalidOperationException("Destination automation label/help missing.");
        Key(dialog, Avalonia.Input.Key.Tab); FocusIs(dialog, Find<Button>(dialog, "SaveJson"));
        Key(dialog, Avalonia.Input.Key.Tab); FocusIs(dialog, Find<Button>(dialog, "CancelSaveJson"));
        Key(dialog, Avalonia.Input.Key.Tab); FocusIs(dialog, path);
        Key(dialog, Avalonia.Input.Key.Tab, KeyModifiers.Shift); FocusIs(dialog, Find<Button>(dialog, "CancelSaveJson"));
        path.Focus(); path.Text = "relative.json"; Key(dialog, Avalonia.Input.Key.Enter);
        FocusIs(dialog, path);
        if (!dialog.IsVisible || !(AutomationProperties.GetHelpText(path) ?? "").Contains("caminho completo") ||
            AutomationProperties.GetLiveSetting(Find<TextBlock>(dialog, "DestinationError")) != AutomationLiveSetting.Polite)
            throw new InvalidOperationException("Keyboard validation error not associated with destination.");
        Capture(dialog, "keyboard-error.png");
        Key(dialog, Avalonia.Input.Key.Escape); await Until(() => !dialog.IsVisible);
        FocusIs(window, opener);
        Key(window, Avalonia.Input.Key.Space); dialog = await Dialog(desktop);
        var exported = Path.Combine(Program.OutputDirectory, "keyboard-hardware.json");
        Find<TextBox>(dialog, "DestinationPath").Text = exported;
        Key(dialog, Avalonia.Input.Key.Enter);
        await Until(() => !dialog.IsVisible && File.Exists(exported));
        using var json = JsonDocument.Parse(File.ReadAllText(exported));
        FocusIs(window, opener);
        Console.WriteLine("PASS keyboard save dialog: Space opens, Enter saves, Tab/Shift+Tab cycle, Escape cancels, error help/focus, automation peer name, focus restored");

        var aboutOpener = Find<Button>(window, "About"); aboutOpener.Focus(); Key(window, Avalonia.Input.Key.Space);
        await Until(() => desktop.Windows.OfType<AboutDialog>().Any(w => w.IsVisible));
        var about = desktop.Windows.OfType<AboutDialog>().Single(w => w.IsVisible);
        await Task.Delay(80);
        var selector = Find<ComboBox>(about, "LicenseDocument");
        selector.Focus();
        Key(about, Avalonia.Input.Key.Down); await Task.Delay(60);
        if (selector.SelectedIndex != 1 || !ControlAutomationPeer.CreatePeerForElement(Find<TextBox>(about, "LicenseTextContent"))!.GetName().StartsWith("MIT"))
            throw new InvalidOperationException("About keyboard document selection or accessible text name failed.");
        Key(about, Avalonia.Input.Key.Tab); FocusIs(about, Find<TextBox>(about, "LicenseTextContent"));
        Key(about, Avalonia.Input.Key.Tab); FocusIs(about, Find<Button>(about, "CloseAbout"));
        Key(about, Avalonia.Input.Key.Tab); FocusIs(about, selector);
        Key(about, Avalonia.Input.Key.F4); await Until(() => selector.IsDropDownOpen);
        Key(about, Avalonia.Input.Key.Escape); await Until(() => !selector.IsDropDownOpen);
        if (!about.IsVisible || selector.SelectedIndex != 1) throw new InvalidOperationException("Escape did not preserve dialog/document after closing document list.");
        Key(about, Avalonia.Input.Key.Escape); await Until(() => !about.IsVisible); FocusIs(window, aboutOpener);
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => !string.IsNullOrEmpty(b.Name)))
            if (string.IsNullOrWhiteSpace(ControlAutomationPeer.CreatePeerForElement(button)?.GetName()))
                throw new InvalidOperationException("Unnamed button automation peer: " + button.Name);
        Console.WriteLine("PASS keyboard About: arrow selects document; Tab cycles selector/reader/close; F4 opens list; Escape closes list then dialog; named text and restored focus");
    }
    private static TextBox VerifyLicenseDocument(AboutDialog about, int index)
    {
        var (name, expected) = index switch
        {
            0 => ("AttributionText", ProductIdentity.Attribution + "\n\n" + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NOTICE.md"))),
            1 => ("LicenseText", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LICENSE"))),
            2 => ("DependenciesText", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"))),
            _ => ("OwnTermsText", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LICENSING.md")) + "\n\n" + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LICENSE-ILMENYX.md")))
        };
        var text = Find<TextBox>(about, "LicenseTextContent");
        if (text.Text != expected || !text.IsReadOnly) throw new InvalidOperationException("License document changed or is editable: " + name);
        text.Focus(); Key(about, Avalonia.Input.Key.A, KeyModifiers.Control);
        if (text.SelectedText != expected) throw new InvalidOperationException("License text cannot be selected in full: " + name);
        text.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "must-not-edit" });
        if (text.Text != expected) throw new InvalidOperationException("Read-only license text was changed by input.");
        Key(about, Avalonia.Input.Key.End, KeyModifiers.Control);
        return text;
    }
    private static Dictionary<Control, double> ScaleText(Window window, double factor)
    {
        // Snapshot all effective values before assigning any: inherited fonts must not compound.
        var fonts = window.GetVisualDescendants().OfType<Control>()
            .Where(c => c is TextBlock or TextBox or Button or TabItem or ComboBox)
            .ToDictionary(c => c, c => c.GetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty));
        foreach (var (control, size) in fonts) control.SetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty, size * factor);
        return fonts;
    }
    private static async Task Reachable(Window window, Control control)
    {
        control.BringIntoView();
        var timer = Stopwatch.StartNew();
        Point point;
        do
        {
            await Task.Delay(30);
            point = control.TranslatePoint(default, window) ?? throw new InvalidOperationException("Detached layout control.");
            if (point.X >= -1 && point.Y >= -1 && point.X + control.Bounds.Width <= window.Bounds.Width + 1 &&
                point.Y + control.Bounds.Height <= window.Bounds.Height + 1 && control.Bounds.Width > 0 && control.Bounds.Height > 0) return;
        } while (timer.Elapsed < TimeSpan.FromSeconds(2));
        throw new InvalidOperationException($"Clipped control {control.Name ?? control.GetType().Name}: {point}, {control.Bounds.Size}; window {window.Bounds.Size}");
    }
    private static async Task MinimumSize(Window window)
    {
        // Initial native configure events can overtake a requested resize. Reissue a
        // distinct size then the exact minimum, bounded, without relaxing geometry.
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            window.Width = window.MinWidth + 2;
            window.Height = window.MinHeight + 2;
            await Task.Delay(150);
            window.Width = window.MinWidth;
            await Task.Delay(150);
            window.Height = window.MinHeight;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(3))
            {
                await Task.Delay(80);
                if (Math.Abs(window.Bounds.Width - window.MinWidth) < 1 &&
                    Math.Abs(window.Bounds.Height - window.MinHeight) < 1)
                {
                    await Task.Delay(120);
                    if (Math.Abs(window.Bounds.Width - window.MinWidth) < 1 &&
                        Math.Abs(window.Bounds.Height - window.MinHeight) < 1) return;
                }
            }
            Console.WriteLine($"RESIZE retry {attempt}: {window.GetType().Name} actual={window.Bounds.Size} required={window.MinWidth}x{window.MinHeight}");
        }
        throw new InvalidOperationException($"Minimum resize unconfirmed: {window.GetType().Name} actual={window.Bounds.Size} required={window.MinWidth}x{window.MinHeight}");
    }

    private static void TextFits(Window window, List<string> errors)
    {
        foreach (var text in window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0))
            if (text.TextLayout.Width > text.Bounds.Width + 2 || text.TextLayout.Height > text.Bounds.Height + 2)
                errors.Add($"Text clipped in {window.GetType().Name}/{text.Name ?? "TextBlock"}: layout={text.TextLayout.Width}x{text.TextLayout.Height}, bounds={text.Bounds.Size}, text=\"{(text.Text is { Length: > 60 } t ? t[..60] : text.Text)}\"");
    }
    private static async Task RequirementsAsync(DashboardWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var opener = Find<Button>(window, "ShowRequirements"); opener.Focus(); Key(window, Avalonia.Input.Key.Space);
        await Until(() => desktop.Windows.OfType<RequirementsDialog>().Any(w => w.IsVisible));
        var dialog = desktop.Windows.OfType<RequirementsDialog>().Single(w => w.IsVisible);
        var report = Find<TextBox>(dialog, "RequirementsReport");
        if (!report.IsReadOnly || !report.Text!.Contains("Nenhuma verificação")) throw new InvalidOperationException("Requirements must start idle and read-only.");
        FocusIs(dialog, Find<Button>(dialog, "VerifyRequirements"));
        Key(dialog, Avalonia.Input.Key.Space);
        await Until(() => report.Text!.Contains("Ilmenyx doctor") && Find<Button>(dialog, "VerifyRequirements").IsEnabled);
        using var expected = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        RuntimeRequirementsPresentation.Write(new Ilmenyx.Platform.Linux.LinuxRuntimeDependencies().InspectReport(
            Ilmenyx.Platform.Linux.LinuxRuntimeDependencies.IsInvariantModeRequested(),
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))), expected);
        if (report.Text != expected.ToString()) throw new InvalidOperationException("GUI/doctor report mismatch.");
        Key(dialog, Avalonia.Input.Key.Tab); FocusIs(dialog, Find<Button>(dialog, "CloseRequirements"));
        Key(dialog, Avalonia.Input.Key.Tab); FocusIs(dialog, report);
        Key(dialog, Avalonia.Input.Key.Tab); FocusIs(dialog, Find<Button>(dialog, "VerifyRequirements"));
        Key(dialog, Avalonia.Input.Key.Escape); await Until(() => !dialog.IsVisible); FocusIs(window, opener);

        int calls = 0;
        var gate = new TaskCompletionSource<RuntimeRequirements>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new RequirementsDialog(() => { Interlocked.Increment(ref calls); return gate.Task.GetAwaiter().GetResult(); });
        var shown = pending.ShowDialog(window); await Task.Delay(80);
        if (calls != 0) throw new InvalidOperationException("Requirements probed on open.");
        Click(pending, "VerifyRequirements"); await Until(() => calls == 1);
        Click(pending, "CloseRequirements"); await shown;
        gate.SetResult(new(true, true, true, true, true, false, false, false)); await Task.Delay(80);
        if (!Find<TextBox>(pending, "RequirementsReport").Text!.Contains("Nenhuma verificação"))
            throw new InvalidOperationException("Closed dialog received a late result.");
        Console.WriteLine("PASS requirements: idle on open; explicit real probe equals doctor; read-only; keyboard cycle/Escape/focus; close during pending probe suppresses result");
    }

    private static async Task LayoutAsync(DashboardWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var errors = new List<string>();
        foreach (var factor in new[] { 1.0, 1.5, 2.0 })
        {
            var fonts = ScaleText(window, factor);
            await MinimumSize(window);
            foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => !string.IsNullOrEmpty(b.Name) && !b.Name.StartsWith("PART_", StringComparison.Ordinal) && b.IsEffectivelyVisible))
                try { await Reachable(window, button); } catch (InvalidOperationException e) { errors.Add(e.Message); }
            TextFits(window, errors);
            Find<ScrollViewer>(window, "DashboardScroll").ScrollToHome(); await Task.Delay(60);
            Capture(window, $"layout-dashboard-{factor:0.0}.png");
            foreach (var (control, size) in fonts) control.SetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty, size);
            Console.WriteLine($"LAYOUT dashboard factor={factor} actual={window.Bounds.Size} renderScaling={window.RenderScaling}");

            Click(window, "ExportHardware"); var dialog = await Dialog(desktop);
            await MinimumSize(dialog);
            ScaleText(dialog, factor); await Task.Delay(180);
            Find<TextBox>(dialog, "DestinationPath").Text = "relative.json"; Key(dialog, Avalonia.Input.Key.Enter);
            foreach (var name in new[] { "DestinationPath", "DestinationError", "SaveJson", "CancelSaveJson" })
                try { await Reachable(dialog, Find<Control>(dialog, name)); } catch (InvalidOperationException e) { errors.Add(e.Message); }
            TextFits(dialog, errors);
            Capture(dialog, $"layout-save-{factor:0.0}.png");
            Key(dialog, Avalonia.Input.Key.Escape); await Until(() => !dialog.IsVisible);
            Console.WriteLine($"LAYOUT save factor={factor} actual={dialog.Bounds.Size} renderScaling={dialog.RenderScaling}");

            Click(window, "About"); await Until(() => desktop.Windows.OfType<AboutDialog>().Any(w => w.IsVisible));
            var about = desktop.Windows.OfType<AboutDialog>().Single(w => w.IsVisible);
            await MinimumSize(about);
            var selector = Find<ComboBox>(about, "LicenseDocument");
            ScaleText(about, factor); await Task.Delay(100);
            foreach (var index in Enumerable.Range(0, 4))
            {
                selector.SelectedIndex = index; await Task.Delay(60);
                try { await Reachable(about, selector); } catch (InvalidOperationException e) { errors.Add(e.Message); }
                var text = VerifyLicenseDocument(about, index);
                text.FontSize = 14 * factor;
                await Task.Delay(60);
                var readerScroll = text.GetVisualDescendants().OfType<ScrollViewer>().Single();
                if (readerScroll.Extent.Height > readerScroll.Viewport.Height + 1)
                    await Until(() => readerScroll.Offset.Y > 0);
                if (text.Bounds.Height < 120) errors.Add($"License reading area too short: {text.Bounds.Height} DIP at factor {factor}");
                Console.WriteLine($"LAYOUT license document={index} factor={factor} readingHeight={text.Bounds.Height}");
                try { await Reachable(about, text); await Reachable(about, Find<Button>(about, "CloseAbout")); }
                catch (InvalidOperationException e) { errors.Add(e.Message); }
            }
            var heading = about.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Ilmenyx · by Bismuth Technology");
            try { await Reachable(about, heading); } catch (InvalidOperationException e) { errors.Add(e.Message); }
            Console.WriteLine($"LAYOUT About heading origin={heading.TranslatePoint(default, about)} bounds={heading.Bounds.Size} text={heading.TextLayout.Width}x{heading.TextLayout.Height}");
            TextFits(about, errors);
            Capture(about, $"layout-about-{factor:0.0}.png");
            Key(about, Avalonia.Input.Key.Escape); await Until(() => !about.IsVisible);
            Console.WriteLine($"LAYOUT about factor={factor} actual={about.Bounds.Size} renderScaling={about.RenderScaling}");
            Click(window, "ShowRequirements"); await Until(() => desktop.Windows.OfType<RequirementsDialog>().Any(w => w.IsVisible));
            var requirements = desktop.Windows.OfType<RequirementsDialog>().Single(w => w.IsVisible);
            await MinimumSize(requirements); ScaleText(requirements, factor); await Task.Delay(100);
            Click(requirements, "VerifyRequirements");
            await Until(() => Find<TextBox>(requirements, "RequirementsReport").Text!.Contains("Ilmenyx doctor"));
            foreach (var name in new[] { "VerifyRequirements", "CloseRequirements", "RequirementsReport" })
                try { await Reachable(requirements, Find<Control>(requirements, name)); } catch (InvalidOperationException e) { errors.Add(e.Message); }
            if (Find<TextBox>(requirements, "RequirementsReport").Bounds.Height < 120) errors.Add("Requirements reader below 120 DIP.");
            TextFits(requirements, errors); Capture(requirements, $"layout-requirements-{factor:0.0}.png");
            Key(requirements, Avalonia.Input.Key.Escape); await Until(() => !requirements.IsVisible);
            Console.WriteLine($"PASS requirements minimum layout factor={factor} actual={requirements.Bounds.Size}");
        }
        window.Width = 1060; window.Height = 780; await Task.Delay(120);
        File.WriteAllLines(Path.Combine(Program.OutputDirectory, "layout-errors.txt"), errors);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
        Console.WriteLine("PASS layout minimum sizes, text factors 1/1.5/2: dashboard buttons, save field/error/actions, About selector/text/close reachable; reading area >=120 DIP");
    }
    private static async Task MonitorAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var shell = new ShellWindow { Width = 1280, Height = 820 };
        shell.Show();
        await Until(() => shell.IsVisible && shell.GetVisualDescendants().OfType<UsageChart>().Any(c => c.Name == "OverviewChart" && c.Count >= 3));
        if (Find<TextBlock>(shell, "ProductName").Text != "Ilmenyx" || Find<TextBlock>(shell, "MaintainerSignature").Text != "by Bismuth Technology")
            throw new InvalidOperationException("Identity missing from shell window.");
        if (Find<TextBlock>(shell, "OverviewGpuNotice").Text != ValidationNotices.AmdNvidia) throw new InvalidOperationException("AMD/NVIDIA notice missing from overview.");
        if (!Find<TextBlock>(shell, "OverviewValue").Text!.Contains('%')) throw new InvalidOperationException("Overview CPU load not shown.");
        if (Find<TextBlock>(shell, "SideVersion").Text!.Length < 4) throw new InvalidOperationException("Sidebar version missing.");
        await Task.Delay(3500); Capture(shell, "shell-overview.png");
        var chips = shell.GetVisualDescendants().OfType<Button>().Where(b => b.Content?.ToString() is "Temperatura" or "15 MIN").ToList();
        foreach (var chip in chips) chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Find<TextBlock>(shell, "OverviewValue").Text!.Contains("°C") || Find<TextBlock>(shell, "OverviewValue").Text == "—");
        if (Find<Button>(shell, "DashMetricPower").IsEnabled) throw new InvalidOperationException("Unavailable power metric must stay disabled.");
        Click(shell, "GoMemory"); await Until(() => shell.CurrentPage == "system");
        var expected = new (string Key, string Control)[] { ("dash", "OverviewChart"), ("system", "SysRefresh"), ("sensors", "RefreshSensors"), ("gpu", "GpuValidationNotice"), ("disks", "BtnDiskRefresh"),
            ("energy", "BtnEnergyRefresh"), ("net", "BtnNetTest"), ("procs", "ProcessRows"), ("launcher", "BtnScanGames"), ("fps", "BtnApiKey"), ("stress", "BtnStress"), ("optimize", "BtnOptimize"), ("bottleneck", "BtnBnSession"), ("validate", "ValidateCpu"), ("makerports", "BtnMakerPortScan"), ("makerdrv", "BtnMakerDrvScan"), ("startup", "BtnStartupScan"), ("driver", "BtnDrvScan"), ("tools", "About") };
        foreach (var (key, control) in expected)
        {
            var nav = Find<Button>(shell, "Nav" + key);
            nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => shell.CurrentPage == key && shell.GetVisualDescendants().Any(c => c is Control { Name: var n, IsVisible: true } && n == control));
            if (Find<TextBlock>(shell, "TitlePage").Text!.Length == 0) throw new InvalidOperationException("Page title missing for " + key);
            if (key == "procs") { await Until(() => Find<StackPanel>(shell, "ProcessRows").Children.Count > 0); Capture(shell, "shell-processes.png"); }
            if (key == "gpu" && Find<TextBlock>(shell, "GpuValidationNotice").Text != ValidationNotices.AmdNvidia) throw new InvalidOperationException("AMD/NVIDIA notice missing from GPU page.");
            if (key == "net") await Until(() => Find<UsageChart>(shell, "NetChart").Count >= 2);
        }
        // System page (original layout): categories, drive rings, offline RAM inventory import.
        Find<Button>(shell, "Navsystem").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "SysDetails" && p.Children.Count > 3));
        foreach (var category in new[] { "Sistema", "Placa-mãe", "Processador", "Memória", "Placa", "Armazenamento" })
        {
            Click(shell, "SysCat" + category);
            await Until(() => Find<StackPanel>(shell, "SysDetails").Children.Count > 0);
        }
        if (!Find<StackPanel>(shell, "SysDrives").IsVisible || shell.GetVisualDescendants().OfType<RingGauge>().Count() < 1) throw new InvalidOperationException("Storage drive rings missing.");
        await Task.Delay(300); Capture(shell, "shell-system-storage.png");
        Click(shell, "SysCatMemória");
        await Until(() => Find<StackPanel>(shell, "SysRamImport").IsVisible);
        var dmiFile = Path.Combine(Program.OutputDirectory, "dmidecode-fixture.txt");
        File.WriteAllText(dmiFile, "Handle 0x0040, DMI type 16, 23 bytes\nPhysical Memory Array\n\tMaximum Capacity: 64 GB\n\tNumber Of Devices: 2\n\nHandle 0x0041, DMI type 17, 40 bytes\nMemory Device\n\tSize: 16 GB\n\tLocator: DIMM_A2\n\tBank Locator: BANK 0\n\tType: DDR5\n\tSpeed: 4800 MT/s\n\tPart Number: F5-6000J3038F16GX2\n\tSerial Number: FIXTURE-SERIAL-NOT-REAL\n\tConfigured Memory Speed: 4800 MT/s\n");
        Find<TextBox>(shell, "SysDmiPath").Text = "relativo.txt"; Click(shell, "SysImportDmi");
        await Until(() => Find<TextBlock>(shell, "SysImportStatus").Text!.Contains("recusada"));
        Find<TextBox>(shell, "SysDmiPath").Text = dmiFile; Click(shell, "SysImportDmi");
        await Until(() => Find<TextBlock>(shell, "SysImportStatus").Text!.Contains("importado"));
        await Until(() => shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("DESLIGADO") == true));
        if (shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("FIXTURE-SERIAL") == true)) throw new InvalidOperationException("RAM serial leaked into the UI.");
        Console.WriteLine("PASS system page: category list, drive rings, offline dmidecode import (relative path refused), EXPO estimate, no serial in UI");
        // Disks page: verdict cards, bounded SMART query (fails closed without privilege), offline import with raw metrics.
        Find<Button>(shell, "Navdisks").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "DiskCards" && p.Children.Count > 0));
        var verdicts = Find<StackPanel>(shell, "DiskCards").GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name?.StartsWith("Verdict_") == true).ToList();
        if (verdicts.Count == 0 || verdicts.Any(v => v.Text is not ("SAUDÁVEL" or "ATENÇÃO" or "CRÍTICO" or "SEM DADOS"))) throw new InvalidOperationException("Disk verdict badges invalid.");
        var smartable = verdicts.Select(v => v.Name!["Verdict_".Length..]).FirstOrDefault(n => System.Text.RegularExpressions.Regex.IsMatch(n, "^(sd[a-z]+|nvme[0-9]+n[0-9]+)$"));
        if (smartable is not null)
        {
            Click(shell, "SmartQuery_" + smartable);
            await Until(() => Find<TextBlock>(shell, "DiskHealthStatus").Text!.Contains("indisponível") || Find<TextBlock>(shell, "DiskHealthStatus").Text!.Contains("lido"));
            string fixture = smartable.StartsWith("nvme")
                ? "{\"smartctl\":{\"exit_status\":0},\"device\":{\"name\":\"/dev/" + smartable + "\"},\"smart_status\":{\"passed\":true},\"temperature\":{\"current\":40},\"nvme_smart_health_information_log\":{\"percentage_used\":3,\"media_errors\":2}}"
                : "{\"smartctl\":{\"exit_status\":0},\"device\":{\"name\":\"/dev/" + smartable + "\"},\"smart_status\":{\"passed\":true},\"temperature\":{\"current\":35},\"ata_smart_attributes\":{\"table\":[{\"id\":197,\"raw\":{\"value\":1}}]}}";
            var smartFile = Path.Combine(Program.OutputDirectory, "disk-smart-fixture.json"); File.WriteAllText(smartFile, fixture);
            Find<TextBox>(shell, "SmartImportDevice").Text = smartable; Find<TextBox>(shell, "SmartImportPath").Text = smartFile; Click(shell, "ImportSmart");
            await Until(() => Find<StackPanel>(shell, "DiskCards").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "Verdict_" + smartable && t.Text == "ATENÇÃO"));
            await Task.Delay(200); Capture(shell, "shell-disks.png");
            Console.WriteLine("PASS disks page: verdict cards, bounded SMART query without privilege, offline SMART import raises the verdict with raw metrics");
        }
        else Console.WriteLine("SKIP disks SMART import: no sd*/nvme* disk visible in this environment");
        Find<Button>(shell, "Navenergy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "EnergyCards" && p.Children.Count > 0));
        if (!shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "Verdict_energia" && t.Text == "EM USO")) throw new InvalidOperationException("Energy card missing.");
        await Task.Delay(200); Capture(shell, "shell-energy.png");
        Console.WriteLine("PASS energy page: power card always present, battery cards when the kernel reports batteries");
        Find<Button>(shell, "Navdriver").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "DrvList" && p.Children.Count > 0));
        if (!Find<TextBlock>(shell, "DrvStatus").Text!.StartsWith("Pronto")) throw new InvalidOperationException("Driver scan did not finish.");
        if (!shell.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "DrvInstallHint" && t.IsReadOnly)) throw new InvalidOperationException("Install hint (never executed) missing.");
        Click(shell, "BtnDrvFwupd");
        await Until(() => { var t = Find<TextBlock>(shell, "DrvStatus").Text!; return t.Contains("fwupd") || t.Contains("dispositivo"); });
        await Task.Delay(200); Capture(shell, "shell-drivers.png");
        Console.WriteLine("PASS driver page: offline inventory, GPU/board cards, never-executed install hint, read-only fwupd listing (network and browser actions not clicked)");
        Find<Button>(shell, "Navgpu").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "GpuSections" && p.Children.Count >= 6));
        if (Find<TextBlock>(shell, "GpuName").Text!.Length == 0 || Find<TextBlock>(shell, "GpuVendor").Text!.Length == 0) throw new InvalidOperationException("GPU highlight card empty.");
        if (Find<TextBlock>(shell, "GpuValidationNotice").Text != ValidationNotices.AmdNvidia) throw new InvalidOperationException("AMD/NVIDIA notice missing from GPU page.");
        await Until(() => Find<StackPanel>(shell, "GpuLive").Children.Count > 0 || Find<StackPanel>(shell, "GpuExtras").Children.Count > 0);
        await Task.Delay(200); Capture(shell, "shell-gpu.png");
        Console.WriteLine("PASS gpu page: chips, highlight card, 8 GPU-Z style sections, live sensors poll or honest 'no counters' text, AMD/NVIDIA notice kept");
        Find<Button>(shell, "Navbottleneck").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var bottleneck = shell.GetVisualDescendants().OfType<BottleneckPage>().Single();
        await Until(() => bottleneck.Session is null && Find<TextBlock>(shell, "BnLiveTitle").Text != "—");
        if (!Find<TextBlock>(shell, "BnBalanceText").Text!.Contains("GPU") && !Find<TextBlock>(shell, "BnBalanceText").Text!.Contains("CPU") && !Find<TextBlock>(shell, "BnBalanceText").Text!.Contains("equilibrado")) throw new InvalidOperationException("Balance text missing.");
        await Until(() => Find<MeterView>(shell, "Bn_cpu").Text.Contains('%'));
        bottleneck.StartSession(TimeSpan.FromSeconds(3));
        await Until(() => shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "BnResultTitle" && t.Text!.Length > 0));
        if (!Find<TextBlock>(shell, "BnResultSummary").Text!.Contains("amostra")) throw new InvalidOperationException("Session summary missing.");
        await Task.Delay(200); Capture(shell, "shell-bottleneck.png");
        Console.WriteLine("PASS bottleneck page: balance, live verdict and meters, timed session produces a distribution and advice (FPS honestly unavailable)");
        await StartupAsync(shell, desktop);
        Find<Button>(shell, "Navmakerports").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "MakerPortList" && p.Children.Count > 0));
        Find<Button>(shell, "Navmakerdrv").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "MakerChipList" && p.Children.Count == 6));
        if (!shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "MakerChipState_ch341" && t.Text!.Length > 0)) throw new InvalidOperationException("Chip family state missing.");
        Console.WriteLine("PASS maker pages: serial ports listed (or honest empty state) without opening any port; chip family modules, environment checks and official pages (not opened)");
        if (OperatingSystem.IsLinux()) await LauncherAsync(shell, desktop);
        await StressAsync(shell, desktop);
        await NetworkAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await OptimizeAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await FpsAsync(shell, desktop);
        Click(shell, "SortMemory"); await Until(() => Find<Button>(shell, "SortMemory").Content!.ToString()!.Contains('▼'));
        Click(shell, "ToggleMonitor");
        await Until(() => Find<Button>(shell, "ToggleMonitor").Content!.ToString() == "Retomar monitor" && Find<TextBlock>(shell, "SideMonitor").Text == "monitor parado");
        var frozen = Find<TextBlock>(shell, "OverviewValue").Text; await Task.Delay(1500);
        if (Find<TextBlock>(shell, "OverviewValue").Text != frozen) throw new InvalidOperationException("Paused monitor kept sampling.");
        Click(shell, "ToggleMonitor");
        await Until(() => Find<Button>(shell, "ToggleMonitor").Content!.ToString() == "Pausar monitor");
        Click(shell, "OpenClassic");
        await Until(() => desktop.Windows.OfType<DashboardWindow>().Count(w => w.IsVisible) == 2);
        desktop.Windows.OfType<DashboardWindow>().Single(w => w.IsVisible && w != desktop.MainWindow).Close();
        Click(shell, "About");
        await Until(() => desktop.Windows.OfType<AboutDialog>().Any(w => w.IsVisible));
        desktop.Windows.OfType<AboutDialog>().Single(w => w.IsVisible).Close();
        Console.WriteLine("PASS shell: grouped sidebar, 10 pages reachable, overview chart/chips/components, live process table, GPU notice, pause/resume without sampling, classic diagnostics and About");
        shell.Close();
    }
    private static async Task StartupAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Real startup entries are never touched: a standalone page over temporary XDG directories is exercised instead.
        string root = Path.Combine(Program.OutputDirectory, "startup-fixture"), cfg = Path.Combine(root, "cfg"), sys = Path.Combine(root, "sys");
        Directory.CreateDirectory(Path.Combine(cfg, "autostart")); Directory.CreateDirectory(sys);
        File.WriteAllText(Path.Combine(cfg, "autostart", "mine.desktop"), "[Desktop Entry]\nType=Application\nName=Mine\nExec=/bin/true\n");
        File.WriteAllText(Path.Combine(sys, "sysapp.desktop"), "[Desktop Entry]\nType=Application\nName=SysApp\nExec=/bin/true\n");
        var manager = new Ilmenyx.Platform.Linux.LinuxStartupManager(cfg, Path.Combine(root, "data"), [sys], null);
        Window? host = null;
        var page = new StartupPage(manager, () => host, CancellationToken.None);
        host = new Window { Width = 900, Height = 600, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => page.Entries.Count == 2);
        Click(host, "BtnStartupScan");
        if (host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "StartupRemove_sysapp.desktop")) throw new InvalidOperationException("System entries must not offer REMOVER.");
        var toggle = Find<CheckBox>(host, "StartupToggle_sysapp.desktop"); toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => File.Exists(Path.Combine(cfg, "autostart", "sysapp.desktop")) && Find<TextBlock>(host, "StartupState_sysapp.desktop").Text == "DESATIVADO");
        if (File.ReadAllText(Path.Combine(sys, "sysapp.desktop")).Contains("Hidden")) throw new InvalidOperationException("System file was modified.");
        Click(host, "StartupRemove_mine.desktop");
        await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var noDialog = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); Click(noDialog, "ConfirmNo");
        await Until(() => !noDialog.IsVisible);
        if (!File.Exists(Path.Combine(cfg, "autostart", "mine.desktop"))) throw new InvalidOperationException("Declined removal still removed the entry.");
        Click(host, "StartupRemove_mine.desktop");
        await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var yesDialog = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); Click(yesDialog, "ConfirmYes");
        await Until(() => !File.Exists(Path.Combine(cfg, "autostart", "mine.desktop")) && page.Entries.All(e => e.Name != "Mine"));
        await Until(() => Find<Button>(host, "BtnStartupRestore").IsEnabled);
        Click(host, "BtnStartupRestore");
        await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => File.Exists(Path.Combine(cfg, "autostart", "mine.desktop")) && page.Entries.Any(e => e.Name == "Mine"));
        host.Close();
        // The real shell page only lists (no clicks): entries come from the user's actual session.
        Find<Button>(shell, "Navstartup").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "StartupSummary" && (t.Text!.Contains("PROGRAMAS") || t.Text.Contains("NENHUM"))));
        await Task.Delay(300); Capture(shell, "shell-startup.png");
        Console.WriteLine("PASS startup page: system entry disabled by user override (system file untouched), declined removal keeps entry, confirmed removal + restore round trip; real list read-only");
    }
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task LauncherAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Fixture library: nothing real is launched, written or closed (the launcher is a recording stub and Steam files are temporary).
        string root = Path.Combine(Program.OutputDirectory, "launcher-fixture"), home = Path.Combine(root, "home"), steam = Path.Combine(home, ".local/share/Steam"), apps = Path.Combine(steam, "steamapps");
        Directory.CreateDirectory(Path.Combine(apps, "common/Game One")); Directory.CreateDirectory(Path.Combine(steam, "userdata/1/config"));
        File.WriteAllText(Path.Combine(apps, "libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam + "\"\n\t}\n}\n");
        File.WriteAllText(Path.Combine(apps, "appmanifest_100.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"100\"\n\t\"name\"\t\t\"Game One\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"Game One\"\n}\n");
        string localConfig = Path.Combine(steam, "userdata/1/config/localconfig.vdf");
        File.WriteAllText(localConfig, "\"UserLocalConfigStore\"\n{\n}\n");
        string manual = Path.Combine(root, "manualgame.sh"); File.WriteAllText(manual, "#!/bin/sh\n"); File.SetUnixFileMode(manual, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string settingsPath = Path.Combine(root, "cfg/ilmenyx/settings.json"); string? launched = null;
        Window? host = null;
        var page = new LauncherPage(new Ilmenyx.Platform.Linux.LinuxGameLibrary(home, (_, _) => Task.FromResult((0, "[]"))), new Ilmenyx.Platform.Linux.SteamInfo(home, Path.Combine(root, "noproc")), settingsPath,
            (g, p, v, status, learned, ct) => { launched = g.Key + "|" + p.WinMode; status("stub launched"); return Task.CompletedTask; }, () => host, "AMD", true, CancellationToken.None);
        host = new Window { Width = 1100, Height = 800, Content = page }; host.Show();
        await Until(() => page.Games.Count == 1);
        Click(host, "BtnAddExe"); Find<TextBox>(host, "LAddPath").Text = "relative/path"; Click(host, "BtnAddExeConfirm");
        await Until(() => Find<TextBlock>(host, "LAddStatus").Text!.Contains("caminho completo"));
        Find<TextBox>(host, "LAddPath").Text = manual; Click(host, "BtnAddExeConfirm");
        await Until(() => page.Games.Count == 2 && page.Profile is not null && host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "LTitle" && t.Text == "manualgame"));
        Click(host, "LGame_Steam_100");
        await Until(() => host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "LTitle" && t.Text == "Game One") && Find<StackPanel>(host, "LSteamBox").IsVisible);
        var plan = Find<CheckBox>(host, "LChkPlan"); plan.IsChecked = true;
        var gpu = Find<CheckBox>(host, "LChkGpu"); if (!gpu.IsEnabled) throw new InvalidOperationException("GPU checkbox should be enabled with multiple GPUs."); gpu.IsChecked = true;
        foreach (var chip in host.GetVisualDescendants().OfType<Button>().Where(b => b.Content?.ToString() is "Tela cheia" or "1920x1080" or "144")) chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Find<TextBlock>(host, "LPreview").Text == "DRI_PRIME=1 %command% -fullscreen -w 1920 -h 1080 -freq 144");
        if (!File.Exists(settingsPath) || !File.ReadAllText(settingsPath).Contains("\"HighPerfPlan\": true")) throw new InvalidOperationException("Profile was not persisted.");
        Click(host, "BtnSteamApply");
        await Until(() => Find<TextBlock>(host, "LStatus").Text!.Contains("Opções gravadas na Steam"));
        if (!File.ReadAllText(localConfig).Contains("DRI_PRIME=1 %command% -fullscreen") || !File.Exists(localConfig + ".ilmenyx.bak")) throw new InvalidOperationException("Steam launch options not written with backup.");
        Find<TextBox>(host, "LHelperPath").Text = "/bin/true -q"; Click(host, "BtnAddHelper");
        await Until(() => Find<StackPanel>(host, "LHelpers").Children.Count == 1);
        Click(host, "BtnPlay");
        await Until(() => launched == "Steam:100|full" && Find<TextBlock>(host, "LStatus").Text == "stub launched" && Find<Button>(host, "BtnPlay").Content!.ToString() == "JOGAR");
        host.Close();
        // The real shell page reads the real libraries (Steam, Lutris, Heroic) without launching, writing or closing anything.
        Find<Button>(shell, "Navlauncher").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "LGameCount" && !t.Text!.StartsWith("Procurando")));
        await Task.Delay(200); Capture(shell, "shell-launcher.png");
        Console.WriteLine("PASS launcher page: library scan, manual executable validation, profile persistence, Steam launch options with backup, helper, stubbed launch; real library read-only");
    }
    private sealed class FakeStress : Ilmenyx.Core.IStressEngine
    {
        public bool Running { get; private set; } public long MemoryErrors { get; set; } public long MemoryPasses { get; set; } public long MemoryAllocatedBytes { get; private set; }
        public int Starts, Stops;
        public void Start(bool cpu, bool ram, double fraction, long available) { Running = true; Starts++; MemoryAllocatedBytes = ram ? 1L << 30 : 0; }
        public void Stop() { Running = false; Stops++; }
    }
    private sealed class FakeUsage : IUsageProvider
    {
        public double? Temp = 60;
        public Task<UsageSample> SampleAsync(CancellationToken ct = default) => Task.FromResult(new UsageSample(DateTimeOffset.UtcNow, "Fixture", 50, 2000, Temp, 8L << 30, 2L << 30, null, null, null, null, null, 1, []));
    }

    private static async Task StressAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // A fake engine drives the real page: no CPU/RAM load is ever generated by the smoke test.
        var engine = new FakeStress(); var provider = new FakeUsage(); using var usage = new UsageViewModel(provider);
        await usage.SampleOnceAsync(CancellationToken.None);
        Window? host = null; var page = new StressPage(engine, usage, () => host, () => 8L << 30, CancellationToken.None);
        host = new Window { Width = 1100, Height = 800, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnStress"));
        if (Find<CheckBox>(host, "ChkGpu").IsEnabled) throw new InvalidOperationException("GPU stress must stay disabled until the Vulkan module exists.");
        Find<TextBox>(host, "TempLimit").Text = "10"; Click(host, "BtnStress");
        await Until(() => Find<TextBlock>(host, "StressHint").Text!.Contains("40 e 110")); if (engine.Starts != 0) throw new InvalidOperationException("Invalid limit started the load.");
        Find<TextBox>(host, "TempLimit").Text = "90"; Find<TextBox>(host, "TxtDuration").Text = "10";
        Click(host, "BtnStress"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var d1 = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); Click(d1, "ConfirmNo"); await Until(() => !d1.IsVisible);
        if (engine.Starts != 0) throw new InvalidOperationException("Declined confirmation still started the load.");
        Click(host, "BtnStress"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var d2 = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); Click(d2, "ConfirmYes"); await Until(() => !d2.IsVisible);
        await Until(() => engine.Running && page.StatusText == "Rodando" && Find<Button>(host, "BtnStress").Content!.ToString() == "PARAR");
        provider.Temp = 95; await usage.SampleOnceAsync(CancellationToken.None); page.Tick();
        await Until(() => !engine.Running && page.StatusText == "Abortado" && Find<Button>(host, "BtnStress").Content!.ToString() == "INICIAR");
        if (!host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("ABORTADO: CPU a 95") == true)) throw new InvalidOperationException("Abort reason not logged.");
        // Time limit through the real one-second timer.
        provider.Temp = 60; await usage.SampleOnceAsync(CancellationToken.None); Find<TextBox>(host, "TxtDuration").Text = 0.02.ToString(System.Globalization.CultureInfo.CurrentCulture);
        Click(host, "BtnStress"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => engine.Running); await Until(() => !engine.Running && host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Concluído (tempo esgotado)") == true));
        // Without a temperature sensor the user is warned and can cancel.
        provider.Temp = null; await usage.SampleOnceAsync(CancellationToken.None); int before = engine.Starts;
        Click(host, "BtnStress"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var warn = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); if (!Find<TextBlock>(warn, "ConfirmText").Text!.Contains("NÃO funcionará")) throw new InvalidOperationException("No-sensor warning missing.");
        Click(warn, "ConfirmNo"); await Until(() => !warn.IsVisible); if (engine.Starts != before) throw new InvalidOperationException("Cancelled warning still started the load.");
        host.Close();
        Find<Button>(shell, "Navstress").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnStress" && b.Content?.ToString() == "INICIAR"));
        Console.WriteLine("PASS stress page: invalid limits refused, declined confirmation keeps the load off, CPU temperature abort, timed completion, no-sensor warning; GPU stress disabled; no real load generated");
    }
    private sealed class FakeNetProbe : Ilmenyx.Core.INetworkProbe
    {
        public int Calls;
        public Ilmenyx.Core.AdapterInfo? ActiveAdapter() => new("eth0", "Ethernet (cabo)", "1000 Mbps", "192.168.0.10", "192.168.0.1", "1.1.1.1", System.Net.IPAddress.Parse("192.168.0.1"));
        public Task<(List<double>, int)> PingLoopAsync(System.Net.IPAddress a, int count, int intervalMs, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.FromResult((new List<double> { 10, 11, 10, 12, 11, 10 }, 6)); }
        public Task<double?> DownloadAsync(int s, CancellationToken ct) => Task.FromResult<double?>(200);
        public Task<double?> UploadAsync(int s, CancellationToken ct) => Task.FromResult<double?>(40);
        public Task<double?> DnsAsync(string h, CancellationToken ct) => Task.FromResult<double?>(15);
    }

    private static async Task NetworkAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // A fake probe drives the real page: the smoke test never sends a packet or opens a connection.
        var probe = new FakeNetProbe(); using var usage = new UsageViewModel(new FakeUsage());
        Window? host = null; var page = new NetworkPage(probe, usage, () => host, CancellationToken.None);
        host = new Window { Width = 1000, Height = 800, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnNetTest"));
        Click(host, "BtnNetTest"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var dialog = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible);
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("speed.cloudflare.com")) throw new InvalidOperationException("Confirmation must list the endpoints.");
        Click(dialog, "ConfirmNo"); await Until(() => !dialog.IsVisible); if (probe.Calls != 0) throw new InvalidOperationException("Declined confirmation still used the network.");
        Click(host, "BtnNetTest"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => page.LastResult is not null && Find<StackPanel>(host, "NetCards").Children.Count == 1 && Find<Button>(host, "BtnNetTest").Content!.ToString() == "TESTAR");
        if (page.LastResult!.VerdictText != "ESTÁVEL") throw new InvalidOperationException("Healthy fake network should be ESTÁVEL.");
        host.Close();
        Find<Button>(shell, "Navnet").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnNetTest" && b.Content?.ToString() == "TESTAR"));
        Console.WriteLine("PASS network page: confirmation lists endpoints, declined test uses no network, fake probe yields the verdict card; real probe never clicked");
    }
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task OptimizeAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Fixture processes and fake services: no real process is signalled and the real power profile is never touched.
        string root = Path.Combine(Program.OutputDirectory, "optimize-fixture"), proc = Path.Combine(root, "proc");
        Directory.CreateDirectory(Path.Combine(proc, "self")); File.WriteAllText(Path.Combine(proc, "self", "status"), "Uid:\t1000\t1\t1\t1\nGid:\t1000\t1\t1\t1\nGroups:\t1000\n");
        void P(string pid, string comm) { string d = Path.Combine(proc, pid); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "comm"), comm + "\n"); File.WriteAllText(Path.Combine(d, "status"), "Uid:\t1000\t1\t1\t1\n"); File.WriteAllText(Path.Combine(d, "statm"), "5000 2000 0 0 0 0 0\n"); File.WriteAllText(Path.Combine(d, "cmdline"), comm + "\0"); }
        P("201", "baloo_file"); P("202", "myeditor"); P("203", "systemd");
        var signalled = new List<int>(); string power = "balanced"; var sets = new List<string>();
        var optimizer = new Ilmenyx.Platform.Linux.LinuxOptimizer(proc, Path.Combine(root, "sys"), root, (_, _) => Task.FromResult((0, "'adaptive'")), (pids, _) => { signalled.AddRange(pids); foreach (var pid in pids) Directory.Delete(Path.Combine(proc, pid.ToString()), true); return Task.CompletedTask; });
        var services = new OptimizeServices(_ => Task.FromResult<string?>(power), (id, _) => { power = id; sets.Add(id); return Task.FromResult(true); });
        using var usage = new UsageViewModel(new FakeUsage()); await usage.SampleOnceAsync(CancellationToken.None);
        string settingsPath = Path.Combine(root, "cfg/settings.json"); Window? host = null;
        // The desktop-app map is injected through ~/.local/share/applications of the fixture home.
        string apps = Path.Combine(root, ".local/share/applications"); Directory.CreateDirectory(apps); File.WriteAllText(Path.Combine(apps, "e.desktop"), "[Desktop Entry]\nType=Application\nName=My Editor\nExec=/usr/bin/myeditor %F\n");
        var page = new OptimizePage(optimizer, services, usage, settingsPath, () => host, CancellationToken.None);
        host = new Window { Width = 1200, Height = 900, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => page.Apps.Count == 2 && host.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "Tweak_power"));
        if (Find<CheckBox>(host, "OptApp_baloo_file").IsChecked != true || Find<CheckBox>(host, "OptApp_myeditor").IsChecked == true) throw new InvalidOperationException("Suggested helper must be pre-selected; ordinary apps must not.");
        if (Find<CheckBox>(host, "Tweak_max_map_count").IsEnabled) throw new InvalidOperationException("Root-only tweaks must be read-only.");
        var tp = Find<CheckBox>(host, "Tweak_power"); tp.IsChecked = true; tp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => power == "performance" && page.Settings.TweakBackup.GetValueOrDefault("power") == "balanced");
        Click(host, "BtnOptimize"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var no = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); Click(no, "ConfirmNo"); await Until(() => !no.IsVisible);
        if (signalled.Count != 0) throw new InvalidOperationException("Declined confirmation still signalled processes.");
        Click(host, "BtnOptimize"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "OptResult").Text!.Contains("Fechados: baloo_file") && signalled.SequenceEqual([201]));
        Click(host, "BtnTweaksRestore"); await Until(() => power == "balanced" && page.Settings.TweakBackup.Count == 0);
        host.Close();
        Find<Button>(shell, "Navoptimize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "OptTweaks" && p.Children.Count >= 4));
        await Task.Delay(300); Capture(shell, "shell-optimize.png");
        Console.WriteLine("PASS optimize page: only own desktop apps and suggested helpers listed, SIGTERM only after confirmation, reversible power profile with backup/restore, root-only tweaks read-only with commands; real page read-only");
    }
    private sealed class FakeHttp : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"avgFps\":111,\"min1Fps\":80}") }); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task FpsAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Fake HTTP handler: the real HowManyFPS service is never contacted by the smoke test.
        string root = Path.Combine(Program.OutputDirectory, "fps-fixture"); Directory.CreateDirectory(root);
        var http = new FakeHttp(); var client = new Ilmenyx.Core.HowManyFpsClient(Path.Combine(root, "cache.json"), http);
        string settingsPath = Path.Combine(root, "settings.json"); Window? host = null;
        var page = new GamesFpsPage(client, new Ilmenyx.Core.IlmenyxSettings(), settingsPath, () => host, () => ("AMD Ryzen 7 7700X 8-Core Processor", "NVIDIA GeForce RTX 4070"), CancellationToken.None);
        host = new Window { Width = 1100, Height = 900, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => page.Rows.Count > 20 && page.Rows.Any(r => r.Fps > 0));
        if (page.GpuIndex != 100 || page.CpuIndex != 100) throw new InvalidOperationException("Hardware indices not resolved from the catalog tables.");
        string CardFps(string name) => host.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Game_" + LauncherPage.SafeName(name)).GetVisualDescendants().OfType<TextBlock>().First(t => t.FontSize == 28).Text!;
        await Until(() => host.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "Game_" + LauncherPage.SafeName("Counter-Strike 2"))); if (CardFps("Counter-Strike 2") != "340") throw new InvalidOperationException("CS2 estimate at 1080p/Alto should be 340.");
        foreach (var chip in host.GetVisualDescendants().OfType<Button>().Where(b => b.Content?.ToString() == "1440p")) chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => CardFps("Counter-Strike 2") != "340");
        if (Find<Button>(host, "BtnFpsQuery").IsEnabled) throw new InvalidOperationException("API query must be disabled without a key.");
        Find<TextBox>(host, "ApiKeyBox").Text = "fixture-key"; Click(host, "BtnApiKey");
        await Until(() => Find<Button>(host, "BtnFpsQuery").IsEnabled && File.Exists(settingsPath));
        Click(host, "BtnFpsQuery"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var dlg = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); if (!Find<TextBlock>(dlg, "ConfirmText").Text!.Contains("howmanyfps.com")) throw new InvalidOperationException("Confirmation must name the endpoint.");
        Click(dlg, "ConfirmNo"); await Until(() => !dlg.IsVisible); if (http.Calls != 0) throw new InvalidOperationException("Declined confirmation still used the network.");
        Click(host, "BtnFpsQuery"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => http.Calls >= 20 && page.Rows.All(r => r.Source == "HOWMANYFPS") && CardFps("Counter-Strike 2") == "111");
        host.Close();
        Find<Button>(shell, "Navfps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnFpsQuery" && !b.IsEnabled || b.Name == "BtnApiKey"));
        Console.WriteLine("PASS fps page: local estimate from catalog tables, option chips recompute, API query only after key + confirmation naming the endpoint, fake handler results shown; real service never contacted");
    }
    private static async Task ExerciseAsync(DashboardWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var output = Program.OutputDirectory;
        await MonitorAsync(desktop);
        await KeyboardAsync(window, desktop);
        await RequirementsAsync(window, desktop);
        if (window.Title != ProductIdentity.Name || Find<TextBlock>(window, "ProductName").Text != "Ilmenyx" ||
            Find<TextBlock>(window, "MaintainerSignature").Text != "by Bismuth Technology")
            throw new InvalidOperationException("Ilmenyx identity missing from live window.");
        Click(window, "About");
        await Until(() => desktop.Windows.OfType<AboutDialog>().Any(w => w.IsVisible));
        var about = desktop.Windows.OfType<AboutDialog>().Single(w => w.IsVisible);
        await Task.Delay(80);
        var attribution = Find<TextBox>(about, "LicenseTextContent").Text!;
        if (!attribution.Contains("Gustavo (GustavoReset)") || !attribution.Contains("não oficial") || !attribution.Contains("sem afiliação"))
            throw new InvalidOperationException("About attribution incomplete.");
        var selector = Find<ComboBox>(about, "LicenseDocument");
        selector.SelectedIndex = 1; await Task.Delay(60);
        if (!Find<TextBox>(about, "LicenseTextContent").Text!.Contains("Copyright (c) 2026 Gustavo (GustavoReset)"))
            throw new InvalidOperationException("MIT license missing from About.");
        selector.SelectedIndex = 2; await Task.Delay(60);
        if (!Find<TextBox>(about, "LicenseTextContent").Text!.Contains("Avalonia.Wayland"))
            throw new InvalidOperationException("Third-party notices missing from About.");
        selector.SelectedIndex = 3; await Task.Delay(60);
        if (!Find<TextBox>(about, "LicenseTextContent").Text!.Contains("Usos permitidos, inclusive em trabalho remunerado"))
            throw new InvalidOperationException("Own usage and commercialization terms missing from About.");
        selector.SelectedIndex = 0; await Task.Delay(60); Capture(about, "about.png");
        Click(about, "CloseAbout"); await Until(() => !about.IsVisible);
        Console.WriteLine("PASS Ilmenyx identity, by Bismuth Technology, live About/attribution/MIT/dependency documents and close");
        var implementation = window.PlatformImpl?.GetType().FullName ?? "unavailable";
        var requested = DesktopBackend.Parse(DesktopBackend.ReadSelection());
        if (requested == DisplayBackend.Wayland && !implementation.StartsWith("Avalonia.Wayland.", StringComparison.Ordinal))
            throw new InvalidOperationException("Native Wayland was requested but another backend was selected: " + implementation);
        if (requested == DisplayBackend.X11 && !implementation.StartsWith("Avalonia.X11.", StringComparison.Ordinal))
            throw new InvalidOperationException("X11 was requested but another backend was selected: " + implementation);
        Console.WriteLine("ACTUAL WINDOW IMPLEMENTATION " + implementation);
        var handle = window.TryGetPlatformHandle();
        Console.WriteLine("DISPLAY BACKEND requested=" + (DesktopBackend.ReadSelection() ?? "auto") + "; handle=" + handle?.HandleDescriptor);
        if (DesktopBackend.Parse(DesktopBackend.ReadSelection()) == DisplayBackend.Wayland &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            throw new InvalidOperationException("For a conclusive native smoke test, unset DISPLAY.");
        await Until(() => Find<Button>(window, "ExportHardware").IsEnabled);
        var existing = Path.Combine(output, "preserved.json");
        File.WriteAllText(existing, "preserve-this-file");
        Click(window, "ExportHardware");
        var dialog = await Dialog(desktop);
        Find<TextBox>(dialog, "DestinationPath").Text = existing;
        Click(dialog, "SaveJson");
        if (!dialog.IsVisible || !Find<TextBlock>(dialog, "DestinationError").Text!.Contains("já existe")) throw new InvalidOperationException("Existing destination was not rejected in the dialog.");
        if (File.ReadAllText(existing) != "preserve-this-file") throw new InvalidOperationException("Existing file modified.");
        Find<TextBox>(dialog, "DestinationPath").Text = Path.Combine(output, "hardware.json");
        Capture(dialog, "save-dialog.png");
        Click(dialog, "SaveJson");
        await Until(() => File.Exists(Path.Combine(output, "hardware.json")));
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "hardware.json"))))
        {
            if (json.RootElement.GetProperty("schemaVersion").GetString() != "1.3") throw new InvalidOperationException("Hardware schema incorrect.");
            var gpus = json.RootElement.GetProperty("gpus").GetProperty("devices");
            if (gpus.GetArrayLength() > 0 && !Find<TextBlock>(window, "GpuSummary").Text!.Contains(gpus[0].GetProperty("device").GetString()!))
                throw new InvalidOperationException("GPU identification not presented in dashboard.");
        }
        var validationNotice = Find<TextBlock>(window, "GpuValidationNotice");
        if (!validationNotice.IsEffectivelyVisible || validationNotice.Text != "AMD/NVIDIA · Testes não realizados em hardware real")
            throw new InvalidOperationException("AMD/NVIDIA hardware validation notice missing.");
        Console.WriteLine("PASS visible AMD/NVIDIA notice: Testes não realizados em hardware real");
        Console.WriteLine("PASS hardware UI export and overwrite refusal");

        if (Find<Button>(window, "ExportMemory").IsEnabled || !Find<TextBlock>(window, "MemorySummary").Text!.StartsWith("Nenhuma"))
            throw new InvalidOperationException("Memory validation ran automatically.");
        if (Find<Button>(window, "ExportSim").IsEnabled || !Find<TextBlock>(window, "SimSummary").Text!.StartsWith("Nenhuma"))
            throw new InvalidOperationException("GameSim ran automatically.");
        foreach (var button in new[] { "ExportCpuHtml", "ExportMemoryHtml", "ExportSimHtml" })
            if (Find<Button>(window, button).IsEnabled) throw new InvalidOperationException("Validation HTML enabled before a result.");
        Click(window, "ExportHardwareHtml");
        dialog = await Dialog(desktop);
        var cancelledHtml = Path.Combine(output, "cancelled.html");
        Find<TextBox>(dialog, "DestinationPath").Text = cancelledHtml;
        Click(dialog, "CancelSaveJson"); await Until(() => !dialog.IsVisible);
        if (File.Exists(cancelledHtml)) throw new InvalidOperationException("Cancelled HTML dialog wrote a file.");
        Click(window, "ExportHardwareHtml"); dialog = await Dialog(desktop);
        Find<TextBox>(dialog, "DestinationPath").Text = Path.Combine(output, "wrong-extension.json");
        Click(dialog, "SaveJson");
        if (!dialog.IsVisible || Find<TextBlock>(dialog, "DestinationError").Text != "Use a extensão .html.")
            throw new InvalidOperationException("HTML extension was not validated.");
        var existingHtml = Path.Combine(output, "preserved.html"); File.WriteAllText(existingHtml, "preserve-html");
        Find<TextBox>(dialog, "DestinationPath").Text = existingHtml; Click(dialog, "SaveJson");
        if (!dialog.IsVisible || File.ReadAllText(existingHtml) != "preserve-html") throw new InvalidOperationException("HTML overwrite not refused.");
        var htmlPath = Path.Combine(output, "hardware.html");
        Find<TextBox>(dialog, "DestinationPath").Text = htmlPath; Capture(dialog, "html-dialog.png"); Click(dialog, "SaveJson");
        await Until(() => File.Exists(htmlPath));
        var selected = JsonSerializer.Deserialize<HardwareSnapshot>(File.ReadAllText(Path.Combine(output, "hardware.json")), SnapshotReport.Options)!;
        if (File.ReadAllText(htmlPath) != HardwareHtmlReport.Render(selected)) throw new InvalidOperationException("HTML export did not preserve the selected snapshot.");
        Console.WriteLine("PASS HTML UI export, same snapshot, cancel, extension validation and overwrite refusal");

        Click(window, "ValidateCpu");
        Click(window, "CancelCpu");
        await Until(() => Find<Button>(window, "ValidateCpu").IsEnabled);
        if (!Find<TextBlock>(window, "CpuStatus").Text!.Contains("ancel")) throw new InvalidOperationException("CPU cancellation not shown.");
        Console.WriteLine("PASS CPU cancel control");
        Click(window, "ValidateCpu");
        await Until(() => Find<TextBlock>(window, "CpuSummary").Text?.StartsWith("Concluída") == true);
        if (Find<Image>(window, "CpuPreview").Source is null) throw new InvalidOperationException("CPU preview missing.");
        Click(window, "ExportCpu");
        dialog = await Dialog(desktop);
        var cancelled = Path.Combine(output, "cancelled.json");
        Find<TextBox>(dialog, "DestinationPath").Text = cancelled;
        Click(dialog, "CancelSaveJson");
        await Until(() => !dialog.IsVisible);
        if (File.Exists(cancelled)) throw new InvalidOperationException("Cancelled dialog wrote a file.");
        var cpuPath = Path.Combine(output, "cpu.json");
        await SaveAsync(window, desktop, "ExportCpu", cpuPath);
        using (var json = JsonDocument.Parse(File.ReadAllText(cpuPath)))
        {
            var result = json.RootElement.GetProperty("benchmark");
            if (result.GetProperty("completedSamples").GetInt64() != 4608 || result.GetProperty("experimentalScore").ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException("Reduced CPU result invalid.");
        }
        Console.WriteLine("PASS CPU real render, cancelled picker and UI export");
        await HtmlResultAsync(window, desktop, "ExportCpuHtml", cpuPath, "cpu");

        Click(window, "ValidateMemory"); Click(window, "CancelMemory");
        await Until(() => Find<Button>(window, "ValidateMemory").IsEnabled);
        if (!Find<TextBlock>(window, "MemoryStatus").Text!.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Memory cancellation not shown.");
        Click(window, "ValidateMemory");
        await Until(() => Find<TextBlock>(window, "MemorySummary").Text!.StartsWith("Concluída"));
        Click(window, "ExportMemory"); dialog = await Dialog(desktop);
        var cancelledMemory = Path.Combine(output, "cancelled-memory.json");
        Find<TextBox>(dialog, "DestinationPath").Text = cancelledMemory; Click(dialog, "CancelSaveJson");
        await Until(() => !dialog.IsVisible);
        if (File.Exists(cancelledMemory)) throw new InvalidOperationException("Cancelled memory export wrote a file.");
        var memoryPath = Path.Combine(output, "memory.json");
        await SaveAsync(window, desktop, "ExportMemory", memoryPath);
        using (var json = JsonDocument.Parse(File.ReadAllText(memoryPath)))
        {
            var result = json.RootElement.GetProperty("validation");
            if (result.GetProperty("completedTraversalSteps").GetInt32() != 32768 || result.GetProperty("score").ValueKind != JsonValueKind.Null ||
                result.GetProperty("readSum").GetInt64() != 32768 || result.GetProperty("copySum").GetInt64() != 32768 || result.GetProperty("performanceStatus").GetString() != "not-measured" ||
                result.GetProperty("timeoutSeconds").GetDouble() != 5 || result.GetProperty("readCopyRepetitions").GetInt32() != 1)
                throw new InvalidOperationException("Memory UI report invalid.");
        }
        Console.WriteLine("PASS memory validation: no autostart, real bounded result, cancel control, cancelled dialog and JSON export");
        await HtmlResultAsync(window, desktop, "ExportMemoryHtml", memoryPath, "memory");

        Click(window, "ValidateSim"); Click(window, "CancelSim");
        await Until(() => Find<Button>(window, "ValidateSim").IsEnabled);
        if (!Find<TextBlock>(window, "SimStatus").Text!.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("GameSim cancellation not shown.");
        Click(window, "ValidateSim");
        await Until(() => Find<TextBlock>(window, "SimSummary").Text!.StartsWith("Concluída"));
        var preview = window.GetVisualDescendants().OfType<GameSimPreview>().Single();
        if (preview.State?.Entities.Count != 256 || preview.State.CompletedSteps != 16)
            throw new InvalidOperationException("GameSim final preview state missing.");
        Click(window, "ExportSim"); dialog = await Dialog(desktop);
        var cancelledSim = Path.Combine(output, "cancelled-sim.json");
        Find<TextBox>(dialog, "DestinationPath").Text = cancelledSim; Click(dialog, "CancelSaveJson");
        await Until(() => !dialog.IsVisible);
        if (File.Exists(cancelledSim)) throw new InvalidOperationException("Cancelled GameSim export wrote a file.");
        var simPath = Path.Combine(output, "sim.json");
        await SaveAsync(window, desktop, "ExportSim", simPath);
        using (var json = JsonDocument.Parse(File.ReadAllText(simPath)))
        {
            var result = json.RootElement.GetProperty("validation");
            if (result.GetProperty("completedSteps").GetInt32() != 16 || result.GetProperty("score").ValueKind != JsonValueKind.Null ||
                result.GetProperty("state").GetProperty("entities").GetArrayLength() != 256 ||
                result.GetProperty("stateSha256").GetString() != "6815765e7f314653b328268725f02c597dc31bb5334670769ab3accfc772c880")
                throw new InvalidOperationException("GameSim UI report invalid.");
        }
        Console.WriteLine("PASS GameSim: no autostart; cancel; 256 entities/16 steps; immutable final preview; oracle hash; cancelled dialog; JSON export");
        await HtmlResultAsync(window, desktop, "ExportSimHtml", simPath, "sim");

        await Until(() => Find<Button>(window, "RefreshSensors").IsEnabled);
        Click(window, "MonitorSensors");
        await Until(() => Find<Button>(window, "StopSensors").IsEnabled);
        await Task.Delay(2200); // Two real read-only samples, not a load test.
        await Until(() => Find<Button>(window, "ExportSensorsHtml").IsEnabled);
        Find<Button>(window, "ExportSensorsHtml").Focus(); Key(window, Avalonia.Input.Key.Space);
        var changingSensors = await Dialog(desktop);
        string beforeSample = Find<TextBlock>(window, "SensorValues").Text!;
        var staleSensorsPath = Path.Combine(output, "sensors-stale.html");
        Find<TextBox>(changingSensors, "DestinationPath").Text = staleSensorsPath;
        await Until(() => Find<TextBlock>(window, "SensorValues").Text != beforeSample);
        Click(changingSensors, "SaveJson");
        await Until(() => !changingSensors.IsVisible && Find<TextBlock>(window, "SensorStatus").Text!.Contains("mudou"));
        if (File.Exists(staleSensorsPath)) throw new InvalidOperationException("Changed sensor snapshot was exported after the dialog.");
        Console.WriteLine("PASS sensors HTML: real polling changes snapshot during dialog; stale selection refused without file");
        Click(window, "StopSensors");
        await Until(() => !Find<Button>(window, "StopSensors").IsEnabled && Find<Button>(window, "RefreshSensors").IsEnabled);
        var sensorsPath = Path.Combine(output, "sensors.json");
        await SaveAsync(window, desktop, "ExportSensors", sensorsPath);
        using (var json = JsonDocument.Parse(File.ReadAllText(sensorsPath)))
        {
            if (json.RootElement.GetProperty("schemaVersion").GetString() != "1.1" || json.RootElement.GetProperty("powerSupplies").ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Aggregated sensor schema incorrect.");
            var readings = json.RootElement.GetProperty("readings");
            if (readings.GetArrayLength() > 0 && !Find<TextBlock>(window, "SensorValues").Text!.Contains(readings[0].GetProperty("device").GetString()!))
                throw new InvalidOperationException("Sensor source not shown in UI.");
        }
        await HtmlResultAsync(window, desktop, "ExportSensorsHtml", sensorsPath, "sensors");
        Find<Button>(window, "RefreshSensors").Focus();
        foreach (var sensorButton in new[] { "MonitorSensors", "ExportSensors", "ExportSensorsHtml" })
        { Key(window, Avalonia.Input.Key.Tab); FocusIs(window, Find<Button>(window, sensorButton)); }
        Key(window, Avalonia.Input.Key.Tab, KeyModifiers.Shift); FocusIs(window, Find<Button>(window, "ExportSensors"));
        Console.WriteLine("PASS sensor keyboard order: JSON/HTML reachable, disabled stop skipped, reverse Tab works");
        var smartInput = Path.Combine(output, "smart-input.json");
        File.WriteAllText(smartInput, "{\"smartctl\":{\"exit_status\":0},\"device\":{\"name\":\"/dev/sda\"},\"smart_status\":{\"passed\":true}}");
        if (Find<Button>(window, "ExportSmartImport").IsEnabled) throw new InvalidOperationException("SMART export enabled before import.");
        Find<TextBox>(window, "SmartImportDevice").Text = "sdb"; Find<TextBox>(window, "SmartImportPath").Text = smartInput;
        Click(window, "ImportSmart");
        await Until(() => Find<TextBlock>(window, "SmartImportStatus").Text!.Contains("recusada"));
        if (Find<Button>(window, "ExportSmartImport").IsEnabled) throw new InvalidOperationException("Refused SMART import enabled export.");
        Find<TextBox>(window, "SmartImportDevice").Text = "sda";
        Click(window, "ImportSmart");
        await Until(() => Find<Button>(window, "ExportSmartImport").IsEnabled);
        if (!Find<TextBlock>(window, "SmartImportValues").Text!.Contains("sda")) throw new InvalidOperationException("Imported SMART device not shown.");
        var smartJson = Path.Combine(output, "smart-import.json");
        await SaveAsync(window, desktop, "ExportSmartImport", smartJson);
        using (var json = JsonDocument.Parse(File.ReadAllText(smartJson)))
            if (json.RootElement.GetProperty("reportKind").GetString() != "smart-import") throw new InvalidOperationException("SMART import JSON invalid.");
        var smartHtml = Path.Combine(output, "smart-import.html");
        Click(window, "ExportSmartImportHtml");
        var smartDialog = await Dialog(desktop);
        Find<TextBox>(smartDialog, "DestinationPath").Text = smartHtml; Click(smartDialog, "SaveJson");
        await Until(() => !smartDialog.IsVisible && File.Exists(smartHtml));
        if (File.ReadAllText(smartHtml).Contains("<script", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("SMART import HTML has script.");
        Console.WriteLine("PASS SMART import UI: no autostart; mismatch refused; explicit file import; JSON/HTML export");
        Find<Button>(window, "ValidateCpu").Focus();
        foreach (var name in new[] { "ExportCpu", "ExportCpuHtml", "ValidateMemory", "ExportMemory", "ExportMemoryHtml", "ValidateSim", "ExportSim", "ExportSimHtml" })
        {
            Key(window, Avalonia.Input.Key.Tab);
            FocusIs(window, Find<Button>(window, name));
        }
        Key(window, Avalonia.Input.Key.Tab, KeyModifiers.Shift); FocusIs(window, Find<Button>(window, "ExportSim"));
        Console.WriteLine("PASS panel keyboard order: CPU/memory/sim JSON/HTML reachable; disabled cancellation controls skipped; reverse Tab works");
        Find<ScrollViewer>(window, "DashboardScroll").ScrollToHome();
        await Task.Delay(80); Capture(window, "dashboard.png");
        Find<ScrollViewer>(window, "DashboardScroll").ScrollToEnd();
        await Task.Delay(80); Capture(window, "validation-panels.png");
        Console.WriteLine("PASS sensor monitor start/stop and UI export");
        await LayoutAsync(window, desktop);
    }
}
