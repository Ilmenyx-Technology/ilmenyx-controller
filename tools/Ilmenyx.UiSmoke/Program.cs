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
        var expected = new (string Key, string Control)[] { ("dash", "OverviewChart"), ("system", "SysRefresh"), ("sensors", "RefreshSensors"), ("gpu", "GpuValidationNotice"), ("disks", "ImportSmart"),
            ("energy", "EnergyText"), ("net", "NetChart"), ("procs", "ProcessRows"), ("validate", "ValidateCpu"), ("tools", "About") };
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
            if (json.RootElement.GetProperty("schemaVersion").GetString() != "1.2") throw new InvalidOperationException("Hardware schema incorrect.");
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
