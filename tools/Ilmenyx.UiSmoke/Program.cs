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
        // Every page that reads or writes Ilmenyx settings/logs uses the fixture directories, never the user's real ones.
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(OutputDirectory, "xdg-config"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(OutputDirectory, "xdg-data"));
        return DesktopBackend.Configure(AppBuilder.Configure<UiSmokeApp>(), DesktopBackend.Parse(DesktopBackend.ReadSelection())).StartWithClassicDesktopLifetime(Array.Empty<string>());
    }
}

public sealed class UiSmokeApp : Application
{
    public override void Initialize() { Styles.Add(IlmenyxTheme.Create()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
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
        {
            var focused = window.FocusManager?.GetFocusedElement() as Control;
            throw new InvalidOperationException("Unexpected keyboard focus; expected " + expected.Name + ", actual " + focused?.Name
                + $" (window {window.GetType().Name} active={window.IsActive}; focused element in {(focused is null ? "none" : TopLevel.GetTopLevel(focused)?.GetType().Name)})");
        }
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
        // Mutter/Muffin on Wayland sometimes never activates a window opened without a real input event (this harness only routes events
        // inside the process). Then keyboard focus stays on the owner and the focus-cycle checks cannot be conclusive: report it, do not fail or pass it.
        var activation = Stopwatch.StartNew();
        while (!dialog.IsActive && activation.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(20);
        bool active = dialog.IsActive;
        var report = Find<TextBox>(dialog, "RequirementsReport");
        if (!report.IsReadOnly || !report.Text!.Contains("Nenhuma verificação")) throw new InvalidOperationException("Requirements must start idle and read-only.");
        if (active) { FocusIs(dialog, Find<Button>(dialog, "VerifyRequirements")); Key(dialog, Avalonia.Input.Key.Space); }
        else Click(dialog, "VerifyRequirements");
        await Until(() => report.Text!.Contains("Ilmenyx doctor") && Find<Button>(dialog, "VerifyRequirements").IsEnabled);
        using var expected = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        RuntimeRequirementsPresentation.Write(new Ilmenyx.Platform.Linux.LinuxRuntimeDependencies().InspectReport(
            Ilmenyx.Platform.Linux.LinuxRuntimeDependencies.IsInvariantModeRequested(),
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")),
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))), expected);
        if (report.Text != expected.ToString()) throw new InvalidOperationException("GUI/doctor report mismatch.");
        if (active)
        {
            Key(dialog, Avalonia.Input.Key.Tab); FocusIs(dialog, Find<Button>(dialog, "CloseRequirements"));
            Key(dialog, Avalonia.Input.Key.Tab); FocusIs(dialog, report);
            Key(dialog, Avalonia.Input.Key.Tab); FocusIs(dialog, Find<Button>(dialog, "VerifyRequirements"));
            Key(dialog, Avalonia.Input.Key.Escape); await Until(() => !dialog.IsVisible); FocusIs(window, opener);
        }
        else
        {
            Console.WriteLine("INCONCLUSIVE requirements keyboard: the compositor did not activate the dialog within 2 s (no real input event); Tab/Escape cycle not asserted");
            Click(dialog, "CloseRequirements"); await Until(() => !dialog.IsVisible);
        }

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
        Console.WriteLine("PASS requirements: idle on open; explicit real probe equals doctor; read-only; " + (active ? "keyboard cycle/Escape/focus" : "keyboard cycle INCONCLUSIVE (dialog not activated by the compositor)") + "; close during pending probe suppresses result");
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
            ("energy", "BtnEnergyRefresh"), ("net", "BtnNetTest"), ("procs", "ProcessRows"), ("launcher", "BtnScanGames"), ("fps", "BtnApiKey"), ("stress", "BtnStress"), ("optimize", "BtnOptimize"), ("apps", "BtnAppsInstall"), ("bottleneck", "BtnBnSession"), ("validate", "ValidateCpu"), ("makerports", "BtnMakerPortScan"), ("makerdrv", "BtnMakerDrvScan"), ("startup", "BtnStartupScan"), ("driver", "BtnDrvScan"), ("tools", "About") };
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
        // At the window minimum (900x560) section values must keep a readable width and live values must not run under their labels.
        double gpuWidth = shell.Width, gpuHeight = shell.Height;
        await MinimumSize(shell); await Task.Delay(300);
        foreach (var row in Find<StackPanel>(shell, "GpuSections").GetVisualDescendants().OfType<Grid>().Where(g => g.ColumnDefinitions.Count == 2 && g.Children.Count == 2))
            if (row.Children[1].Bounds.Width < 120)
                throw new InvalidOperationException($"GPU section value too narrow at the minimum window size: {row.Children[1].Bounds.Width:0} px ({(row.Children[0] as TextBlock)?.Text})");
        foreach (var row in Find<StackPanel>(shell, "GpuLive").GetVisualDescendants().OfType<Grid>().Where(g => g.Children.Count == 2))
            if (row.Children[0].Bounds.Right > row.Children[1].Bounds.Left + 0.5)
                throw new InvalidOperationException("GPU live label overlaps its value at the minimum window size: " + ((row.Children[1] as TextBlock)?.Text));
        Capture(shell, "shell-gpu-min.png");
        shell.Width = gpuWidth; shell.Height = gpuHeight; await Task.Delay(200);
        Console.WriteLine("PASS gpu page: chips, highlight card, 8 GPU-Z style sections, live sensors poll or honest 'no counters' text, notice kept; readable at the 900x560 minimum");
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
        if (OperatingSystem.IsLinux()) await CompatAsync(desktop, Program.OutputDirectory);
        if (OperatingSystem.IsLinux()) await AppsAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await BenchAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await OcAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await OcGpuAsync(desktop);
        if (OperatingSystem.IsLinux()) await ReportAsync(shell);
        if (OperatingSystem.IsLinux()) await OverlayAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await LightingAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await BlockedAsync(shell);
        if (OperatingSystem.IsLinux()) await OverviewSummaryAsync(shell);
        await ThemeAsync(shell);
        await ToastAsync(shell);
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
        await StressGpuAsync(desktop);
        Find<Button>(shell, "Navstress").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnStress" && b.Content?.ToString() == "INICIAR"));
        Console.WriteLine("PASS stress page: invalid limits refused, declined confirmation keeps the load off, CPU temperature abort, timed completion, no-sensor warning; GPU box disabled without an engine, GPU path covered by StressGpuAsync; no real load generated");
    }
    private sealed class FakeGpuStress : Ilmenyx.Core.IGpuStressEngine
    {
        public string? UnavailableReason { get; set; } public string AdapterName => "Fake GPU"; public bool SharesCpuThermals { get; set; } public bool Running { get; private set; } public double Fps => 60; private string? error; public string? Error { get => error; set { error = value; if (value is not null) Running = false; } }
        public int Starts, Stops; public double? Temp = 50;
        public void Start() { Running = true; Starts++; } public void Stop() { Running = false; Stops++; }
    }
    private static async Task OcGpuAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Fake engines, clock and kernel log again; the GPU choice appears only when the GPU engine is usable.
        string root = Path.Combine(Program.OutputDirectory, "oc-gpu-fixture"); Directory.CreateDirectory(root);
        var engine = new FakeStress(); var gpu = new FakeGpuStress(); var fakeNow = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero); double temp = 55;
        var services = new OcServices(engine, () => 8L << 30, _ => Task.FromResult<double?>(5400), (_, _) => Task.FromResult(new Ilmenyx.Core.KernelHardwareErrors(0, 0, 0, 0, null, null)), () => ("", ""),
            _ => Task.FromResult<IReadOnlyList<Ilmenyx.Core.NvidiaTuningState>>([]), () => "AMD Ryzen 7", gpu, _ => Task.FromResult<double?>(480), () => temp);
        using var usage = new UsageViewModel(new FakeUsage()); await usage.SampleOnceAsync(CancellationToken.None);
        Window? host = null; var page = new OcPage(services, usage, () => host, Path.Combine(root, "cfg/settings.json"), CancellationToken.None, () => fakeNow, (t, _) => { fakeNow += t; return Task.CompletedTask; });
        host = new Window { Width = 1100, Height = 1000, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "OcTargetPanel" && p.Children.OfType<Button>().Count() == 3));
        host.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "OcTargetPanel").Children.OfType<Button>().Single(b => b.Content!.ToString() == "GPU").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(host, "BtnOcCheck"); var dialog = await ConfirmAsync(desktop);
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("Fake GPU")) throw new InvalidOperationException("GPU verification must name the adapter.");
        Click(dialog, "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "OcVerdict").Text == "ESTÁVEL" && Find<Button>(host, "BtnOcCheck").Content!.ToString() == "INICIAR VERIFICAÇÃO");
        if (engine.Starts != 0 || gpu.Starts != 1 || gpu.Stops != 1 || !Find<TextBlock>(host, "OcResult").Text!.Contains("Nota GPU (cena): 480")) throw new InvalidOperationException("GPU-only verification must use only the GPU engine and show its score.");
        host.Close();
        Console.WriteLine("PASS oc GPU: GPU target appears only with a usable GPU engine, GPU-only verification never starts the CPU engine and publishes the GPU score (fake engines)");
    }
    private static async Task StressGpuAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // A fake GPU engine: the real Vulkan burn is never started by the smoke test.
        var cpuEngine = new FakeStress(); var gpu = new FakeGpuStress { UnavailableReason = "Nenhum adaptador Vulkan encontrado." }; var provider = new FakeUsage(); using var usage = new UsageViewModel(provider); await usage.SampleOnceAsync(CancellationToken.None);
        Window? host = null; var page = new StressPage(cpuEngine, usage, () => host, () => 8L << 30, CancellationToken.None, gpu, () => gpu.Temp);
        host = new Window { Width = 1100, Height = 800, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnStress"));
        if (Find<CheckBox>(host, "ChkGpu").IsEnabled) throw new InvalidOperationException("Unavailable GPU engine must keep the box disabled.");
        host.Close();
        gpu.UnavailableReason = null; page = new StressPage(cpuEngine, usage, () => host, () => 8L << 30, CancellationToken.None, gpu, () => gpu.Temp);
        host = new Window { Width = 1100, Height = 800, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnStress") && Find<CheckBox>(host, "ChkGpu").IsEnabled);
        Find<CheckBox>(host, "ChkCpu").IsChecked = false; Find<CheckBox>(host, "ChkGpu").IsChecked = true; Find<TextBox>(host, "TxtDuration").Text = "10";
        Click(host, "BtnStress"); var dialog = await ConfirmAsync(desktop);
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("Fake GPU") || !Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("e da GPU: 85")) throw new InvalidOperationException("GPU confirmation must name the adapter and the limits.");
        Click(dialog, "ConfirmNo"); await Until(() => !dialog.IsVisible); if (gpu.Starts != 0) throw new InvalidOperationException("Declined confirmation started the GPU load.");
        Click(host, "BtnStress"); Click(await ConfirmAsync(desktop), "ConfirmYes");
        await Until(() => gpu.Running && page.IsRunning && cpuEngine.Starts == 0);
        gpu.Temp = 90; page.Tick();
        await Until(() => !gpu.Running && page.StatusText == "Abortado" && gpu.Stops == 1);
        if (!host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("ABORTADO: GPU a 90") == true)) throw new InvalidOperationException("GPU temperature abort must be logged.");
        gpu.Temp = 50; Click(host, "BtnStress"); Click(await ConfirmAsync(desktop), "ConfirmYes"); await Until(() => gpu.Running);
        gpu.Error = "DEVICE_LOST"; page.Tick(); await Until(() => page.StatusText == "Abortado" && host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("falha na GPU (DEVICE_LOST)") == true));
        host.Close();
        Console.WriteLine("PASS stress GPU: box enabled only with a usable engine, confirmation names the adapter and limits, GPU temperature and engine-failure aborts stop the load (fake engine; no Vulkan work)");
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
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task BenchAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Fake services drive the real page: no CPU/RAM/disk load is ever generated by the smoke test.
        string root = Path.Combine(Program.OutputDirectory, "bench-fixture"); Directory.CreateDirectory(root);
        string mounts = "/dev/sda1 / ext4 rw 0 0\n"; int cpuCalls = 0, memCalls = 0, simCalls = 0, diskCalls = 0, gpuCalls = 0; bool hang = false; double? gpuTemp = 50;
        var workload = new Ilmenyx.Contracts.CpuWorkload("fixture", 4, 2, 1, 4, 1);
        Ilmenyx.Contracts.CpuBenchmarkResult CpuResult(Ilmenyx.Contracts.CpuRunStatus st, int threads, double? score) => new("a", "m", "r", workload, st, DateTimeOffset.Now, 1, threads, 0, 60, 1, 8, score, "x", "h", new("os", "rt", "x64", 8, "Release", 0), []);
        var services = new BenchServices(
            async (o, ct, _) => { cpuCalls++; if (hang) await Task.Delay(Timeout.Infinite, ct); return new Ilmenyx.Core.Benchmarks.CpuRenderOutput(CpuResult(Ilmenyx.Contracts.CpuRunStatus.Completed, o.Threads, o.Threads == 1 ? 900 : 5400), new int[8]); },
            (avail, _, _, _) => { memCalls++; return Task.FromResult(new Ilmenyx.Core.Benchmarks.MemoryBenchResult(Ilmenyx.Core.Benchmarks.BenchRunStatus.Completed, 95.5, 40.1, 37.2, 2400L << 20, 8, null)); },
            (o, _) => { simCalls++; return Task.FromResult(new Ilmenyx.Contracts.GameSimBenchmarkResult("a", "m", "r", "w", Ilmenyx.Contracts.GameSimBenchmarkStatus.Completed, DateTimeOffset.Now, 30000, o.Threads, 1, 10, 400, 2, 2.01, o.Duration.TotalSeconds, o.Timeout.TotalSeconds, 200000, 3, 144.5, 110.2, 5.1, 1.2, 1000, "x", "rt", "x64", [])); },
            (o, _, _) => { diskCalls++; return Task.FromResult(new Ilmenyx.Core.Benchmarks.DiskBenchResult(Ilmenyx.Core.Benchmarks.BenchRunStatus.Completed, o.Directory, o.FileMegabytes, 500, 450, 10000, 9000, 100, 111, 60000, null)); },
            () => 8L << 30, () => mounts,
            (o, ct, _) => { gpuCalls++; if (hang) return Task.Delay(Timeout.Infinite, ct).ContinueWith<Ilmenyx.Gpu.Vulkan.GpuSceneResult>(_ => throw new OperationCanceledException(ct), ct); return Task.FromResult(new Ilmenyx.Gpu.Vulkan.GpuSceneResult(Ilmenyx.Gpu.Vulkan.GpuRunStatus.Completed, "Fake GPU", o.Width, o.Height, 3000, 60, 50, 41.5, 500, null)); },
            () => new GpuBenchInfo(null, "Fake GPU", false, false), () => gpuTemp);
        var provider = new FakeUsage(); using var usage = new UsageViewModel(provider); await usage.SampleOnceAsync(CancellationToken.None);
        string settingsPath = Path.Combine(root, "cfg/settings.json"); Window? host = null;
        var page = new BenchPage(services, usage, () => host, settingsPath, CancellationToken.None);
        host = new Window { Width = 1100, Height = 900, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnBench"));
        void Chip(string panel, string text) { var b = host!.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == panel).Children.OfType<Button>().Single(x => x.Content!.ToString() == text); b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        async Task<ConfirmDialog> Confirm() { await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible)); return desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); }
        // CPU: declining runs nothing; accepting shows scores, ratio, image and history.
        Click(host, "BtnBench"); var dialog = await Confirm();
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("100% da CPU") || !Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("95")) throw new InvalidOperationException("CPU confirmation must state the full load and the temperature limit.");
        Click(dialog, "ConfirmNo"); await Until(() => !dialog.IsVisible); if (cpuCalls != 0) throw new InvalidOperationException("Declined confirmation still ran the CPU test.");
        Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "BenchHeadline").Text!.Contains("pts") && Find<Button>(host, "BtnBench").Content!.ToString() == "INICIAR");
        if (cpuCalls != 2 || !Find<TextBlock>(host, "BenchResultText").Text!.Contains("não é comparável") || !Find<TextBlock>(host, "BenchResultText").Text!.Contains("6.0x")) throw new InvalidOperationException("CPU result must show both runs, the ratio and the non-comparability note.");
        if (!Find<Image>(host, "BenchImage").IsVisible) throw new InvalidOperationException("Render image must be shown after the CPU test.");
        // Cancellation publishes no value.
        hang = true; Chip("BenchModePanel", "SINGLE"); Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes");
        await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "PARAR");
        Click(host, "BtnBench"); await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "INICIAR");
        if (!Find<TextBlock>(host, "BenchStatus").Text!.Contains("Cancelado") || Find<TextBlock>(host, "BenchHeadline").Text != "—") throw new InvalidOperationException("Stopped test must publish nothing.");
        // Temperature guard cuts a running test.
        Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes"); await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "PARAR");
        provider.Temp = 99; await usage.SampleOnceAsync(CancellationToken.None); page.Tick();
        await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "INICIAR"); provider.Temp = 60; await usage.SampleOnceAsync(CancellationToken.None);
        if (!Find<TextBlock>(host, "BenchStatus").Text!.Contains("ABORTADO")) throw new InvalidOperationException("Temperature guard must abort and say why.");
        hang = false;
        // Memory.
        Chip("BenchKindPanel", "MEMÓRIA"); Click(host, "BtnBench"); dialog = await Confirm();
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("2,3 GiB")) throw new InvalidOperationException("Memory confirmation must state the allocation.");
        Click(dialog, "ConfirmYes"); await Until(() => Find<TextBlock>(host, "BenchHeadline").Text == "95.5 ns" && memCalls == 1);
        // GameSim.
        Chip("BenchKindPanel", "GAMESIM"); Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "BenchHeadline").Text == "145 FPS" && simCalls == 1);
        // GPU scene: confirmation names the adapter, a hot discrete GPU aborts, a normal run publishes FPS and score.
        Chip("BenchKindPanel", "GPU"); await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BenchGpuRt" && !b.IsEnabled));
        Click(host, "BtnBench"); dialog = await Confirm();
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("Fake GPU") || !Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("1920×1080")) throw new InvalidOperationException("GPU confirmation must name the adapter and workload.");
        Click(dialog, "ConfirmNo"); await Until(() => !dialog.IsVisible); if (gpuCalls != 0) throw new InvalidOperationException("Declined confirmation ran the GPU scene.");
        hang = true; Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes"); await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "PARAR");
        gpuTemp = 99; page.Tick(); await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "INICIAR");
        if (!Find<TextBlock>(host, "BenchStatus").Text!.Contains("ABORTADO: GPU a 99")) throw new InvalidOperationException("Discrete GPU temperature must abort the run.");
        gpuTemp = 50; hang = false; Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "BenchHeadline").Text == "50 FPS" && gpuCalls == 2);
        if (!Find<TextBlock>(host, "BenchResultText").Text!.Contains("não comparável")) throw new InvalidOperationException("GPU result must carry the non-comparability note.");
        // Disk: memory-backed folder is refused without a confirmation; a real folder runs after confirming.
        Chip("BenchKindPanel", "DISCO"); await Until(() => host.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "BenchDiskDir"));
        Find<TextBox>(host, "BenchDiskDir").Text = root; mounts = "tmpfs / tmpfs rw 0 0\n"; Click(host, "BtnBench");
        await Until(() => Find<TextBlock>(host, "BenchHint").Text!.Contains("memória")); if (desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible) || diskCalls != 0) throw new InvalidOperationException("tmpfs folder must be refused before any confirmation.");
        mounts = "/dev/sda1 / ext4 rw 0 0\n"; Click(host, "BtnBench"); dialog = await Confirm();
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains(root)) throw new InvalidOperationException("Disk confirmation must name the folder.");
        Click(dialog, "ConfirmYes"); await Until(() => Find<TextBlock>(host, "BenchHeadline").Text == "500 MB/s" && diskCalls == 1);
        if (Directory.GetFileSystemEntries(root).Any(e => Path.GetFileName(e).StartsWith("ilmenyx-bench-"))) throw new InvalidOperationException("Fake disk run must not leave test files.");
        if (IlmenyxSettings.Load(settingsPath).BenchRuns.Count != 5) throw new InvalidOperationException("Completed runs must be kept in the local history.");
        host.Close();
        Find<Button>(shell, "Navbench").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnBench" && b.Content?.ToString() == "INICIAR"));
        await Task.Delay(300); Capture(shell, "shell-bench.png");
        Console.WriteLine("PASS bench page: every test needs a visible confirmation, declined/stopped/aborted runs publish nothing, temperature guard cuts the run, tmpfs folders refused, results kept in the local history; real load never started");
    }
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task OcAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Fake engine, clock and kernel log: no stress, no journal read, no nvidia-smi and no privileged command is ever run.
        string root = Path.Combine(Program.OutputDirectory, "oc-fixture"); Directory.CreateDirectory(root);
        var engine = new FakeStress(); var errors = new Ilmenyx.Core.KernelHardwareErrors(0, 0, 0, 0, null, null); var fakeNow = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        var gpu = new Ilmenyx.Core.NvidiaTuningState(0, "Fixture GPU", 200, 200, 100, 220, 3000, 210);
        var services = new OcServices(engine, () => 8L << 30, _ => Task.FromResult<double?>(5400), (_, _) => Task.FromResult(errors), () => ("Gigabyte Technology Co., Ltd.", "B650 FIXTURE"),
            _ => Task.FromResult<IReadOnlyList<Ilmenyx.Core.NvidiaTuningState>>([gpu]), () => "AMD Ryzen 7 7800X3D 8-Core Processor");
        using var usage = new UsageViewModel(new FakeUsage()); await usage.SampleOnceAsync(CancellationToken.None);
        Window? host = null;
        var page = new OcPage(services, usage, () => host, Path.Combine(root, "cfg/settings.json"), CancellationToken.None, () => fakeNow, (t, _) => { fakeNow += t; return Task.CompletedTask; });
        host = new Window { Width = 1100, Height = 1000, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnOcCommands" && b.IsEnabled) && host.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "OcGuide" && p.Children.Count == 6));
        if (!Find<TextBlock>(host, "OcGuideTitle").Text!.Contains("GIGABYTE B650 FIXTURE")) throw new InvalidOperationException("Guide must use the board from DMI.");
        Find<TextBox>(host, "OcPower").Text = "90"; Click(host, "BtnOcCommands");
        await Until(() => Find<TextBlock>(host, "OcGpuResult").Text!.Contains("100")); if (Find<TextBox>(host, "OcCommands").IsVisible) throw new InvalidOperationException("Out-of-range power must not produce commands.");
        Find<TextBox>(host, "OcPower").Text = "180"; Click(host, "BtnOcCommands");
        await Until(() => Find<TextBox>(host, "OcCommands").IsVisible && Find<TextBox>(host, "OcCommands").Text!.Contains("sudo nvidia-smi -i 0 -pl 180"));
        if (!Find<TextBlock>(host, "OcGpuResult").Text!.Contains("nunca usa sudo")) throw new InvalidOperationException("Commands must be shown, never run, with the sudo note.");
        Click(host, "BtnOcCheck"); var dialog = await ConfirmAsync(desktop);
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("15 min")) throw new InvalidOperationException("Confirmation must state the duration.");
        Click(dialog, "ConfirmNo"); await Until(() => !dialog.IsVisible); if (engine.Starts != 0) throw new InvalidOperationException("Declined confirmation still started stress.");
        Click(host, "BtnOcCheck"); Click(await ConfirmAsync(desktop), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "OcVerdict").Text == "ESTÁVEL" && Find<Button>(host, "BtnOcCheck").Content!.ToString() == "INICIAR VERIFICAÇÃO");
        if (engine.Starts != 1 || engine.Stops != 1 || !Find<TextBlock>(host, "OcResult").Text!.Contains("5,400")) throw new InvalidOperationException("Stable run must start and stop the engine once and show the score.");
        errors = new Ilmenyx.Core.KernelHardwareErrors(1, 0, 0, 0, "mce: fixture", null);
        Click(host, "BtnOcCheck"); Click(await ConfirmAsync(desktop), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "OcVerdict").Text == "INSTÁVEL" && Find<Button>(host, "BtnOcCheck").Content!.ToString() == "INICIAR VERIFICAÇÃO");
        if (!Find<TextBlock>(host, "OcVerdictWhy").Text!.Contains("MCE")) throw new InvalidOperationException("Kernel errors must explain the unstable verdict.");
        if (IlmenyxSettings.Load(Path.Combine(root, "cfg/settings.json")).BenchRuns.Count(r => r.Kind == "OC") != 2) throw new InvalidOperationException("Both verdicts must be kept in the local history.");
        host.Close();
        Find<Button>(shell, "Navoc").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnOcCheck"));
        await Task.Delay(300); Capture(shell, "shell-oc.png");
        Console.WriteLine("PASS oc page: GPU limits validated and shown as commands (never run), verifier needs confirmation, stable/unstable verdicts from temperature, memory and kernel-log errors, BIOS guide from DMI board; no real stress or journal read");
    }
    private static async Task<ConfirmDialog> ConfirmAsync(IClassicDesktopStyleApplicationLifetime desktop)
    { await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible)); return desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); }
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task ReportAsync(ShellWindow shell)
    {
        // The real read-only gatherer runs here (no SMART, benchmark or network test); the destination is a fixed path instead of the dialog.
        string dir = Path.Combine(Program.OutputDirectory, "report-fixture"); Directory.CreateDirectory(dir);
        string html = Path.Combine(dir, "r.html"), json = Path.Combine(dir, "r.json"); File.Delete(html); File.Delete(json);
        string? next = null; Window? host = null;
        var page = new ReportPage(ct => Ilmenyx.Platform.Linux.LinuxReportGatherer.GatherAsync("0.29.0", null, null, [], new Dictionary<string, Ilmenyx.Contracts.SmartDiagnostic>(), null, [], ct), _ => Task.FromResult(next), CancellationToken.None);
        host = new Window { Width = 900, Height = 700, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnReportHtml"));
        next = null; Click(host, "BtnReportHtml"); await Until(() => Find<TextBlock>(host, "ReportStatus").Text == "Exportação cancelada."); if (File.Exists(html)) throw new InvalidOperationException("Cancelled destination must write nothing.");
        next = html; Click(host, "BtnReportHtml"); await Until(() => File.Exists(html) && Find<TextBlock>(host, "ReportStatus").Text!.StartsWith("Relatório salvo"));
        next = json; Click(host, "BtnReportJson"); await Until(() => File.Exists(json) && Find<TextBlock>(host, "ReportStatus").Text!.StartsWith("Relatório salvo"));
        string text = File.ReadAllText(html);
        if (text.Contains("<script", StringComparison.OrdinalIgnoreCase) || text.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && Environment.MachineName.Length > 3 || text.Length < 2000) throw new InvalidOperationException("Report must be a real, script-free file without the host name.");
        using (System.Text.Json.JsonDocument.Parse(File.ReadAllText(json))) { }
        next = html; Click(host, "BtnReportHtml"); await Until(() => Find<TextBlock>(host, "ReportStatus").Text!.StartsWith("Não foi possível exportar"));
        host.Close();
        Find<Button>(shell, "Navreport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnReportHtml"));
        Console.WriteLine($"PASS report page: real read-only collection exported to new HTML ({text.Length} chars) and valid JSON, cancelled destination writes nothing, existing file is never overwritten, host name and scripts absent");
    }
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task OverlayAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Fixture paths and fake telemetry: the user's MangoHud.conf and the real Ilmenyx log folder are never touched.
        string root = Path.Combine(Program.OutputDirectory, "overlay-fixture"); Directory.CreateDirectory(root);
        string settingsPath = Path.Combine(root, "cfg/settings.json"), config = Path.Combine(root, "cfg/mangohud.conf"), logs = Path.Combine(root, "logs");
        var gpu = new Ilmenyx.Core.GpuTelemetry(55, 66, null, null, 1500, 1800, 2UL << 30, 8UL << 30, 35.5, null, null, 1200, null, null, null, null, null, null, null);
        var frames = new Ilmenyx.Core.FrameStats(144, 6.9, 98, [6.9, 7.1, 6.8, 7.0], DateTimeOffset.UtcNow);
        var services = new OverlayServices(_ => Task.FromResult<Ilmenyx.Core.GpuTelemetry?>(gpu), _ => frames, config, logs, () => true);
        using var usage = new UsageViewModel(new FakeUsage()); await usage.SampleOnceAsync(CancellationToken.None);
        var page = new OverlayPage(services, usage, settingsPath, CancellationToken.None);
        var host = new Window { Width = 1000, Height = 1000, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnOverlayToggle") && page.Lines.Count > 0);
        if (!page.Lines.Any(l => l.Label == "FPS" && l.Value.StartsWith("144 FPS"))) throw new InvalidOperationException("Default selection must show FPS from the frame source.");
        var fpsBox = Find<CheckBox>(host, "Ov_fps"); fpsBox.IsChecked = false; await page.TickAsync();
        if (page.Lines.FirstOrDefault(l => l.Label == "FPS")?.Value.Contains("FPS") == true) throw new InvalidOperationException("Unchecked item must disappear.");
        Click(host, "OvPresetMin"); await page.TickAsync();
        if (page.Lines.Count != 1 || page.Lines[0].Label != "FPS" || !Find<TextBlock>(host, "OverlayPreview").Text!.Contains("98")) throw new InvalidOperationException("Minimal preset must show only the FPS line.");
        if (IlmenyxSettings.Load(settingsPath).OverlayItems is not { } saved || !saved.SequenceEqual(["fps", "frametime", "low1"])) throw new InvalidOperationException("Selection must be persisted.");
        Click(host, "BtnOverlayToggle"); await Until(() => desktop.Windows.OfType<OverlayWindow>().Any(w => w.IsVisible) && Find<Button>(host, "BtnOverlayToggle").Content!.ToString() == "OCULTAR OVERLAY");
        var overlay = desktop.Windows.OfType<OverlayWindow>().Single(w => w.IsVisible);
        await Until(() => overlay.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text!.StartsWith("144 FPS")));
        Click(host, "BtnOverlayToggle"); await Until(() => !overlay.IsVisible && Find<Button>(host, "BtnOverlayToggle").Content!.ToString() == "MOSTRAR OVERLAY");
        Click(host, "BtnMangoConfig"); await Until(() => File.Exists(config) && Find<TextBox>(host, "OvLaunchOption").IsVisible);
        string text = File.ReadAllText(config);
        if (!text.Contains("position=top-left") || !text.Contains("fps_metrics=avg,0.01") || !text.Contains($"output_folder={logs}") || !Find<TextBox>(host, "OvLaunchOption").Text!.EndsWith("mangohud %command%")) throw new InvalidOperationException("Generated MangoHud config and launch option must follow the selection.");
        if (File.GetUnixFileMode(config) != (UnixFileMode.UserRead | UnixFileMode.UserWrite)) throw new InvalidOperationException("Config must be user-only.");
        host.Close();
        Find<Button>(shell, "Navoverlay").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnOverlayToggle"));
        Console.WriteLine("PASS overlay page: item picker and presets persist, Ilmenyx overlay window shows/hides with FPS from the frame source, MangoHud config and launch option generated into Ilmenyx's own file (user MangoHud.conf untouched)");
    }
    private static async Task BlockedAsync(ShellWindow shell)
    {
        shell.Select("blocked"); await Task.Delay(100);
        var list = Find<StackPanel>(shell, "BlockedList");
        if (list.Children.Count != Ilmenyx.Core.BlockedCatalog.All.Count) throw new InvalidOperationException("Every blocked feature must have a card.");
        foreach (var f in Ilmenyx.Core.BlockedCatalog.All)
        {
            var chip = (TextBlock)Find<Border>(shell, "BlockedChip_" + f.Id).Child!;
            string text = chip.Text ?? "";
            if (!(text.Contains("BLOQUEADO") || text.Contains("TESTE DISPONÍVEL") || text.Contains("NÃO SE APLICA"))) throw new InvalidOperationException("Unexpected state text for " + f.Id + ": " + text);
            if (f.Kind == Ilmenyx.Core.BlockReason.WindowsOnly && !text.Contains("NÃO SE APLICA")) throw new InvalidOperationException(f.Id + " must be not applicable.");
        }
        var details = Find<StackPanel>(shell, "BlockedDetails_rt-bench"); bool before = details.IsVisible;
        Click(shell, "BlockedWhy_rt-bench"); if (details.IsVisible == before) throw new InvalidOperationException("(!) must toggle the reason.");
        if (!details.Children.OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("VK_KHR_ray_tracing_pipeline"))) throw new InvalidOperationException("The reason must name the missing extension.");
        string summary = Find<TextBlock>(shell, "BlockedSummary").Text ?? "";
        if (summary.Length == 0) throw new InvalidOperationException("Summary missing.");
        shell.Select("dash");
        Console.WriteLine("PASS blocked page: one card per blocked feature, state chips, (!) toggles the reason, Windows-only items are not applicable");
    }

    private static async Task LightingAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // A fake OpenRGB SDK server on loopback: no real light, device or driver is touched.
        await using var server = new Ilmenyx.TestSupport.FakeOpenRgbServer();
        server.Devices.Add(Ilmenyx.TestSupport.FakeOpenRgbServer.DevicePayload("Teclado", "Corsair", 4)); server.Devices.Add(Ilmenyx.TestSupport.FakeOpenRgbServer.DevicePayload("Fita", "MSI", 3));
        var services = new LightingServices(Ilmenyx.Core.OpenRgbClient.ConnectAsync, () => 50, (_, _) => Task.CompletedTask, () => false);
        using var usage = new UsageViewModel(new FakeUsage()); await usage.SampleOnceAsync(CancellationToken.None);
        Window? host = null; var page = new LightingPage(services, usage, () => host, CancellationToken.None);
        host = new Window { Width = 1000, Height = 1100, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnLightConnect"));
        Click(host, "BtnLightRun"); await Until(() => Find<TextBlock>(host, "LightRunHint").Text!.Contains("Conecte")); if (server.Snapshot().Length != 0) throw new InvalidOperationException("Nothing may reach the server before the user connects.");
        Find<TextBox>(host, "LightPort").Text = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture); Click(host, "BtnLightConnect");
        await Until(() => page.Devices.Count == 2 && Find<TextBlock>(host, "LightStatus").Text!.StartsWith("Conectado"));
        Click(host, "BtnLightRun"); var dialog = await ConfirmAsync(desktop);
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("2 dispositivo(s)")) throw new InvalidOperationException("Confirmation must state how many devices are taken.");
        Click(dialog, "ConfirmNo"); await Until(() => !dialog.IsVisible); await Task.Delay(150); if (server.Snapshot().Any(p => p.Type >= 1000)) throw new InvalidOperationException("Declined confirmation still took control.");
        Click(host, "BtnLightRun"); Click(await ConfirmAsync(desktop), "ConfirmYes");
        await Until(() => page.IsControlling && server.Snapshot().Count(p => p.Type == 1100) == 2);
        await page.TickAsync(); await Task.Delay(60); await page.TickAsync();
        await Until(() => server.Snapshot().Any(p => p.Type == 1050));
        var fxPanel = host.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "LightFxPanel");
        fxPanel.Children.OfType<Button>().Single(b => b.Content!.ToString() == "APAGAR").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(60); await page.TickAsync();
        await Until(() => server.Snapshot().Where(p => p.Type == 1050).Any(p => p.Payload[6..].All(x => x == 0)));
        Click(host, "BtnLightRun"); await Until(() => !page.IsControlling && server.Snapshot().Count(p => p.Type == 1101) == 2);
        if (!Find<TextBlock>(host, "LightRunHint").Text!.Contains("devolvido")) throw new InvalidOperationException("Control must be handed back and say so.");
        host.Close(); await page.StopAsync(false);
        Find<Button>(shell, "Navlighting").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnLightConnect"));
        Console.WriteLine("PASS lighting page: nothing connects or is sent before the click, control needs confirmation, effect frames reach the OpenRGB server, every device is handed back to its original mode (fake loopback server; no real RGB hardware)");
    }
    private static async Task ThemeAsync(ShellWindow shell)
    {
        if (Palette.IsLight) throw new InvalidOperationException("Default theme must be dark.");
        Find<Button>(shell, "Navsystem").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight && Find<Button>(shell, "ToggleTheme").Content!.ToString() == "Tema escuro");
        if (((Avalonia.Media.SolidColorBrush)Palette.Bg).Color.R < 200 || ((Avalonia.Media.SolidColorBrush)Palette.Fg).Color.R > 80) throw new InvalidOperationException("Light palette must have a light background and dark ink.");
        await Task.Delay(400); Capture(shell, "shell-light.png");
        string saved = File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")!, "ilmenyx", "settings.json"));
        if (!saved.Contains("\"ThemeLight\": true")) throw new InvalidOperationException("Theme choice must be persisted.");
        foreach (var key in new[] { "stress", "oc", "disks" }) { Find<Button>(shell, "Nav" + key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(500); Capture(shell, $"shell-light-{key}.png"); }
        Click(shell, "ToggleTheme"); await Until(() => !Palette.IsLight && Find<Button>(shell, "ToggleTheme").Content!.ToString() == "Tema claro");
        Console.WriteLine("PASS theme: light/dark toggle recolours the live window, choice persisted to the settings file (fixture directory), default is dark");
    }
    private static async Task ToastAsync(ShellWindow shell)
    {
        // A fake snapshot source: no device is touched; the first read is only a baseline.
        var snap = new Ilmenyx.Core.DeviceSnapshot([new("1-1", "Teclado")], []);
        using var service = new DeviceToastService(() => snap);
        await service.PollAsync(); await Task.Delay(100);
        if (service.Shown != 0) throw new InvalidOperationException("The baseline read must not toast.");
        snap = new Ilmenyx.Core.DeviceSnapshot([new("1-1", "Teclado"), new("1-4", "Pendrive X")], [new("/run/media/u/X", "X (/run/media/u/X)")]);
        await service.PollAsync(); await Until(() => service.Window is { IsVisible: true });
        await Until(() => service.Shown == 2);
        var toast = service.Window!;
        if (toast.TitleText.Length == 0 || toast.BodyText.Length == 0) throw new InvalidOperationException("Toast must show title and body.");
        service.Enabled = false; snap = new Ilmenyx.Core.DeviceSnapshot([], []); await service.PollAsync(); await Task.Delay(200);
        if (service.Shown != 2) throw new InvalidOperationException("Disabled toasts must not show.");
        toast.Close();
        Find<Button>(shell, "Navtools").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "DeviceToastsToggle" && c.IsChecked == true));
        Console.WriteLine("PASS toasts: baseline read is silent, connect/remove notices appear bottom-right and queue, disabling stops them, toggle persisted (fake device source)");
    }
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task OverviewSummaryAsync(ShellWindow shell)
    {
        // The history lives in the fixture settings file; health reads (journal, sysfs, power profile) are the real read-only ones.
        var settings = IlmenyxSettings.Load(IlmenyxSettings.DefaultPath());
        settings.AddBenchRun(new(DateTimeOffset.Now.AddMinutes(-30), "CPU", "multi 5,400", 5400)); settings.AddBenchRun(new(DateTimeOffset.Now.AddMinutes(-10), "CPU", "multi 6,000", 6000));
        settings.AddBenchRun(new(DateTimeOffset.Now.AddMinutes(-5), "Memória", "95.5 ns · 40.1 GB/s leitura", null)); settings.Save(IlmenyxSettings.DefaultPath());
        Find<Button>(shell, "Navsystem").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<Button>(shell, "Navdash").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Find<TextBlock>(shell, "OverviewScore").Text == "6,000" && Find<TextBlock>(shell, "OverviewScoreDelta").Text!.Contains("600"));
        if (!shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "OverviewScores" && p.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "95.5 ns"))) throw new InvalidOperationException("Latest memory result must be listed.");
        await Until(() => shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "OverviewHealth" && p.Children.Count >= 1) && Find<TextBlock>(shell, "OverviewHealthStatus").Text!.StartsWith("Lido"));
        await Until(() => shell.GetVisualDescendants().OfType<WrapPanel>().Any(p => p.Name == "OverviewRings" && p.Children.Count >= 2));
        if (!shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "OverviewEvents" && p.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text!.Contains("multi 6,000")))) throw new InvalidOperationException("Recent events must list the latest benchmark.");
        await Task.Delay(300); Capture(shell, "shell-overview.png");
        Console.WriteLine("PASS overview: performance card from the local benchmark history with delta, real read-only health checklist, occupancy rings and recent events");
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
    private sealed class CompatFakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { lock (Hosts) Hosts.Add(request.RequestUri!.Host); return Task.FromResult(respond(request)); }
    }

    private static byte[] CompatExport(params string[] reports)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new System.Formats.Tar.TarWriter(gz, System.Formats.Tar.TarEntryFormat.Pax, leaveOpen: true))
            tar.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "reports_piiremoved.json") { DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("[" + string.Join(",", reports) + "]")) });
        return ms.ToArray();
    }

    private static async Task CompatAsync(IClassicDesktopStyleApplicationLifetime desktop, string output)
    {
        // Fake HTTP (no network), a synthetic ProtonDB export and an in-memory Steam: nothing real is contacted or written.
        static string Report(long ts, string gpu, string os, string verdict, string options, string fault) =>
            "{\"app\":{\"steam\":{\"appId\":\"1091500\"},\"title\":\"Cyberpunk 2077\"},\"responses\":{\"verdict\":\"" + verdict + "\",\"customProtonVersion\":\"GE-Proton10-29\",\"" + fault + "\":\"yes\",\"launchOptions\":" + JsonSerializer.Serialize(options)
            + ",\"notes\":{\"" + fault + "\":\"Fixed with the launch options below\"},\"concludingNotes\":\"Saves in /home/someone/Games\"},\"timestamp\":" + ts + ",\"systemInfo\":{\"gpu\":\"" + gpu + "\",\"gpuDriver\":\"Mesa 25.0\",\"os\":\"" + os + "\",\"kernel\":\"6.14\"}}";
        byte[] export = CompatExport(
            Report(1793000000, "AMD Radeon 610M (radeonsi, raphael_mendocino)", "Linux Mint 22.3", "yes", "PROTON_ENABLE_WAYLAND=1 gamemoderun %command%", "stabilityFaults"),
            Report(1792000000, "NVIDIA GeForce RTX 4070", "Fedora 41", "no", "rm -rf ~; %command%", "graphicalFaults"));
        string sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData([.. System.Text.Encoding.ASCII.GetBytes($"blob {export.Length}\0"), .. export]));
        string listing = JsonSerializer.Serialize(new[] { new { name = "reports_nov1_2026.tar.gz", size = (long)export.Length, sha, download_url = "https://raw.githubusercontent.com/bdefore/protondb-data/master/reports/reports_nov1_2026.tar.gz" } });
        var http = new CompatFakeHttp(req => req.RequestUri!.Host switch
        {
            "www.protondb.com" => new(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"tier\":\"gold\",\"trendingTier\":\"platinum\",\"confidence\":\"strong\",\"score\":0.8,\"total\":2792}") },
            "store.steampowered.com" => new(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"success\":1,\"results\":{\"resolved_category\":3,\"resolved_items\":[{\"loc_token\":\"#SteamDeckVerified_TestResult_DefaultConfigurationIsPerformant\"}]}}") },
            "api.github.com" => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(listing) },
            _ when req.RequestUri!.AbsolutePath.EndsWith("games.json", StringComparison.Ordinal) => new(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") },
            _ => new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(export) },
        });
        string folder = Path.Combine(output, "compat-fixture");
        var db = new CompatDatabase(Path.Combine(folder, "index"));
        await using (var s = new MemoryStream(export)) await CompatDatabase.BuildAsync(s, db.Folder, new CompatDumpInfo("fixture", export.Length, "", "", null));
        var steam = new Dictionary<string, string>(StringComparer.Ordinal) { ["1091500"] = "-skipStartScreen" }; int opened = 0;
        var services = new CompatServices(db, new CompatOnline(folder, http), () => new CompatHardware("AMD Mendocino (iGPU · Radeon 610M)", "Linux Mint 22.3", false),
            _ => Task.FromResult<IReadOnlyList<LibGame>>([new LibGame("Steam", "1091500", "Cyberpunk 2077", "/nonexistent")]),
            id => steam.GetValueOrDefault(id), (id, o) => { if (o.Length == 0) steam.Remove(id); else steam[id] = o; return null; }, (_, _) => { opened++; return Task.CompletedTask; });
        Window? host = null; var page = new CompatPage(services, () => host, CancellationToken.None);
        host = new Window { Width = 1180, Height = 900, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CompatLib1091500"));
        if (!Find<TextBlock>(host, "CompatDbStatus").Text!.Contains("1 jogo ·")) throw new InvalidOperationException("Index status missing: " + Find<TextBlock>(host, "CompatDbStatus").Text);
        Find<TextBox>(host, "CompatSearch").Text = "cyberpunk-2077";
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CompatHit1091500"));
        Click(host, "CompatHit1091500");
        await Until(() => host.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "CompatReports" && p.Children.Count == 2));
        if (http.Hosts.Count != 0) throw new InvalidOperationException("Opening a game must not use the network.");
        if (!Find<TextBlock>(host, "CompatCounts").Text!.Contains("1 com a mesma GPU")) throw new InvalidOperationException("Same-GPU report not counted: " + Find<TextBlock>(host, "CompatCounts").Text);
        var first = host.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Name == "CompatOptions0");
        if (first.Text != "PROTON_ENABLE_WAYLAND=1 gamemoderun %command%") throw new InvalidOperationException("The same-GPU report must rank first.");
        if (Find<Button>(host, "CompatApply1").IsEnabled) throw new InvalidOperationException("An unsafe launch option must not be applicable.");
        if (host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("/home/someone") == true)) throw new InvalidOperationException("Home folder leaked from a report.");

        Click(host, "CompatOnlineQuery"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var no = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); Click(no, "ConfirmNo"); await Until(() => !no.IsVisible); await Task.Delay(100);
        if (http.Hosts.Count != 0 || Find<TextBlock>(host, "CompatTier").Text != "—") throw new InvalidOperationException("Declined query used the network.");
        Click(host, "CompatOnlineQuery"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "CompatTier").Text == "Ouro" && Find<TextBlock>(host, "CompatDeck").Text == "Verificado" && Find<TextBlock>(host, "CompatAntiCheat").Text == "Não listado");
        if (http.Hosts.Any(h => !CompatOnline.Hosts.Contains(h))) throw new InvalidOperationException("Unexpected host: " + string.Join(",", http.Hosts));

        Click(host, "CompatApply0"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var apply = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible);
        if (!Find<TextBlock>(apply, "ConfirmText").Text!.Contains("-skipStartScreen")) throw new InvalidOperationException("Confirmation must show the current options.");
        Click(apply, "ConfirmYes"); await Until(() => steam.GetValueOrDefault("1091500") == "PROTON_ENABLE_WAYLAND=1 gamemoderun %command%" && Find<Button>(host, "CompatUndo0").IsVisible);
        Click(host, "CompatUndo0"); await Until(() => steam.GetValueOrDefault("1091500") == "-skipStartScreen");
        await Task.Delay(200); Capture(host, "compat.png");

        Click(host, "CompatDbUpdate"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "CompatDbStatus").Text!.Contains("reports_nov1_2026.tar.gz"));
        if (opened != 0) throw new InvalidOperationException("No link was clicked, nothing may be opened.");
        host.Close();
        // Opt-in look at the real local index (offline: the online query is never clicked here).
        if (Environment.GetEnvironmentVariable("ILMENYX_SMOKE_REAL_COMPAT") is { Length: > 0 } realApp && realApp.All(char.IsAsciiDigit))
        {
            var realDb = new CompatDatabase(CompatPaths.Folder());
            var realServices = services with { Db = realDb, Hardware = () => Ilmenyx.Platform.Linux.LinuxCompatHardware.Detect(), Library = _ => Task.FromResult<IReadOnlyList<LibGame>>([]) };
            Window? realHost = null; var realPage = new CompatPage(realServices, () => realHost, CancellationToken.None);
            realHost = new Window { Width = 1180, Height = 2400, Background = Palette.Bg, Content = new ScrollViewer { Content = realPage } }; realHost.Show();
            await realPage.ShowAsync(realApp, realDb.Title(realApp) ?? realApp); await Task.Delay(400);
            Capture(realHost, "compat-real.png"); realHost.Close();
            if (http.Hosts.Any(h => h != "www.protondb.com" && h != "store.steampowered.com" && h != "raw.githubusercontent.com" && h != "api.github.com")) throw new InvalidOperationException("Unexpected host.");
        }
        Console.WriteLine("PASS compat page: offline index search and similar-hardware ranking, unsafe options locked, home paths hidden, online query only after confirmation (fixed hosts), apply/undo on a fake Steam config, verified export update (fake HTTP; no network)");
    }

    private static async Task AppsAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // A fake runner stands in for flatpak: nothing is installed and Flathub is never contacted.
        var calls = new List<string>(); bool flathub = false;
        var installer = new Ilmenyx.Platform.Linux.LinuxAppInstaller((program, args, timeout, ct) =>
        {
            calls.Add(string.Join(" ", args));
            if (args[0] == "remotes") return Task.FromResult((0, flathub ? "flathub\n" : ""));
            if (args[0] == "remote-add") { flathub = true; return Task.FromResult((0, "")); }
            if (args[0] == "list") return Task.FromResult((0, "org.videolan.VLC\n"));
            return Task.FromResult((0, ""));
        }, _ => false);
        Window? host = null; var page = new AppsPage(installer, true, () => host, CancellationToken.None);
        host = new Window { Width = 1000, Height = 900, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnAppsFlathub" && b.IsVisible) && page.Apps.Any(a => a.Installed));
        if (Find<Button>(host, "BtnAppsInstall").IsEnabled) throw new InvalidOperationException("Install must be disabled until Flathub exists.");
        Click(host, "BtnAppsFlathub"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => flathub && Find<Button>(host, "BtnAppsInstall").IsEnabled);
        if (Find<CheckBox>(host, "App_org_videolan_VLC").IsEnabled) throw new InvalidOperationException("Already installed app must not be selectable.");
        Click(host, "BtnAppsInstall"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var no = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); Click(no, "ConfirmNo"); await Until(() => !no.IsVisible);
        if (calls.Any(c => c.StartsWith("install"))) throw new InvalidOperationException("Declined confirmation still installed.");
        Click(host, "BtnAppsInstall"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "AppsStatus").Text!.Contains("instalados.") && calls.Count(c => c.StartsWith("install --user -y --noninteractive flathub")) == page.Apps.Count(a => a.PreSelect && a.FlatpakId != "org.videolan.VLC"));
        host.Close();
        Find<Button>(shell, "Navapps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnAppsInstall"));
        Console.WriteLine("PASS apps page: Flathub added only after confirmation (user scope), installed apps locked, declined install runs nothing, confirmed install uses user-scope noninteractive flatpak calls; real flatpak never installs");
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
        if (!validationNotice.IsEffectivelyVisible || validationNotice.Text != ValidationNotices.AmdNvidia)
            throw new InvalidOperationException("AMD/NVIDIA hardware validation notice missing.");
        Console.WriteLine("PASS visible notice: " + ValidationNotices.AmdNvidia);
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
