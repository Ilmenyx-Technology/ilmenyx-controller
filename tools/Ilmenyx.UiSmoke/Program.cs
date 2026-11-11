// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
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
using Avalonia.LogicalTree;
using Ilmenyx.Platform.Linux;
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
        // The first-run permissions screen is exercised by OnboardingAsync with a fake runner; the real shell must not open it (it would call pkexec).
        Environment.SetEnvironmentVariable("ILMENYX_NO_ONBOARDING", "1");
        // Only for the audits (screenshots in another language): the smoke checks themselves read the pt-BR texts.
        if (Environment.GetEnvironmentVariable("ILMENYX_LANG") is { Length: > 0 } language) Ilmenyx.Core.L.Use(language);
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
    /// <summary>The GPU validation notice expected on this PC: none on an Intel-only machine.</summary>
    private static readonly string? ExpectedGpuNotice = ValidationNotices.For(DrmGpuInfoProvider.Vendors());
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
            1 => ("LicenseText", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LICENSES", "MIT.txt"))),
            2 => ("DependenciesText", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"))),
            _ => ("OwnTermsText", LicenseDocuments.OwnTerms(file => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file))))
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
        await Until(() => report.Text!.Contains("Ilmenyx Controller doctor") && Find<Button>(dialog, "VerifyRequirements").IsEnabled);
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
            var heading = about.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Ilmenyx Controller · by Ilmenyx Technology");
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
            await Until(() => Find<TextBox>(requirements, "RequirementsReport").Text!.Contains("Ilmenyx Controller doctor"));
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
        if (Find<TextBlock>(shell, "ProductName").Text != "Ilmenyx Controller" || Find<TextBlock>(shell, "MaintainerSignature").Text != "by Ilmenyx Technology")
            throw new InvalidOperationException("Identity missing from shell window.");
        if (Find<TextBlock>(shell, "OverviewGpuNotice").Text != (ExpectedGpuNotice ?? "")) throw new InvalidOperationException("AMD/NVIDIA notice wrong on overview.");
        if (!Find<TextBlock>(shell, "OverviewValue").Text!.Contains('%')) throw new InvalidOperationException("Overview CPU load not shown.");
        if (Find<TextBlock>(shell, "SideVersion").Text!.Length < 4) throw new InvalidOperationException("Sidebar version missing.");
        await Task.Delay(3500); Capture(shell, "shell-overview.png");
        var chips = shell.GetVisualDescendants().OfType<Button>().Where(b => b.Content?.ToString() is "Temperatura" or "15 min").ToList();
        foreach (var chip in chips) chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Find<TextBlock>(shell, "OverviewValue").Text!.Contains("°C") || Find<TextBlock>(shell, "OverviewValue").Text == "—");
        // CPU power (RAPL): enabled only when this user can read the package counter (e.g. after 'Liberar sensores da CPU'); otherwise disabled.
        bool raplReadable;
        try { using (File.OpenRead("/sys/class/powercap/intel-rapl:0/energy_uj")) { } raplReadable = true; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { raplReadable = false; }
        if (raplReadable)
        {
            await Until(() => Find<Button>(shell, "DashMetricPower").IsEnabled);
            Click(shell, "DashMetricPower");
            await Until(() => Find<TextBlock>(shell, "OverviewValue").Text!.EndsWith(" W", StringComparison.Ordinal));
            await Task.Delay(600); Capture(shell, "shell-overview-power.png");
            Console.WriteLine("RAPL overview power: " + Find<TextBlock>(shell, "OverviewValue").Text);
            chips.First(c => c.Content?.ToString() == "Temperatura").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // back to the temperature view
        }
        else if (Find<Button>(shell, "DashMetricPower").IsEnabled) throw new InvalidOperationException("Unavailable power metric must stay disabled.");
        Click(shell, "GoMemory"); await Until(() => shell.CurrentPage == "system");
        var expected = new (string Key, string Control)[] { ("dash", "OverviewChart"), ("system", "SysRefresh"), ("sensors", "RefreshSensors"), ("gpu", "GpuValidationNotice"), ("disks", "BtnDiskRefresh"),
            ("energy", "BtnEnergyRefresh"), ("net", "BtnNetTest"), ("procs", "ProcessRows"), ("launcher", "BtnScanGames"), ("fps", "GameSourceTitle"), ("stress", "BtnStress"), ("optimize", "BtnOptimize"), ("apps", "BtnAppsInstall"), ("bottleneck", "BtnBnSession"), ("validate", "ValidateCpu"), ("makerports", "BtnMakerPortScan"), ("makerdrv", "BtnMakerDrvScan"), ("startup", "BtnStartupScan"), ("driver", "BtnDrvScan"), ("tools", "About") };
        foreach (var (key, control) in expected)
        {
            var nav = Find<Button>(shell, "Nav" + key);
            nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => shell.CurrentPage == key && shell.GetVisualDescendants().Any(c => c is Control { Name: var n, IsVisible: true } && n == control));
            if (Find<TextBlock>(shell, "TitlePage").Text!.Length == 0) throw new InvalidOperationException("Page title missing for " + key);
            if (key == "procs") { await Until(() => Find<StackPanel>(shell, "ProcessRows").Children.Count > 0); Capture(shell, "shell-processes.png"); }
            if (key == "sensors")
            {
                // Friendly view: real readings grouped in cards with the overall banner; the technical names toggle shows the kernel names.
                Click(shell, "RefreshSensors"); await Until(() => Find<Border>(shell, "SensorOverall").IsVisible);
                Capture(shell, "shell-sensors.png");
                var tech = Find<ToggleSwitch>(shell, "SensorTechNames"); tech.IsChecked = true;
                await Until(() => shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text?.Contains(" · ", StringComparison.Ordinal) == true && t.FontFamily == IlmenyxTheme.Mono));
                tech.IsChecked = false;
                Console.WriteLine("PASS sensors page: cards with plain names and overall banner; technical names on request");
            }
            if (key == "gpu" && Find<TextBlock>(shell, "GpuValidationNotice").Text != (ExpectedGpuNotice ?? "")) throw new InvalidOperationException("AMD/NVIDIA notice wrong on GPU page.");
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
        // Synthetic nouveau inventory; the injected launcher must never open a real driver manager.
        string driverFixture = Path.Combine(Program.OutputDirectory, "nouveau-driver-fixture");
        string driverSys = Path.Combine(driverFixture, "sys"), driverEtc = Path.Combine(driverFixture, "etc");
        string driverDevice = Path.Combine(driverSys, "bus/pci/devices/0000:01:00.0");
        Directory.CreateDirectory(driverDevice); Directory.CreateDirectory(driverEtc);
        File.WriteAllText(Path.Combine(driverDevice, "class"), "0x030000");
        File.WriteAllText(Path.Combine(driverDevice, "vendor"), "0x10de");
        File.WriteAllText(Path.Combine(driverDevice, "device"), "0x134d");
        string nouveauModule = Path.Combine(driverSys, "bus/pci/drivers/nouveau");
        Directory.CreateDirectory(nouveauModule);
        Directory.CreateSymbolicLink(Path.Combine(driverDevice, "driver"), nouveauModule);
        File.WriteAllText(Path.Combine(driverEtc, "os-release"), "ID=linuxmint\nID_LIKE=ubuntu\n");
        int driverLaunches = 0;
        var driverFixturePage = new DriverPage(_ => Task.FromResult<string?>(null), CancellationToken.None,
            new LinuxDriverInventory(driverSys, Path.Combine(driverFixture, "proc"), driverEtc),
            manager => { if (manager.Program != "/usr/bin/mintdrivers" || manager.Arguments.Count != 0) throw new InvalidOperationException("Unexpected manager."); driverLaunches++; },
            name => name == "mintdrivers" ? "/usr/bin/mintdrivers" : null);
        await driverFixturePage.ScanAsync();
        if (driverLaunches != 0) throw new InvalidOperationException("Driver manager launched without a click.");
        var managerButton = driverFixturePage.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "BtnDrvManager");
        if (!managerButton.IsEnabled) throw new InvalidOperationException("Driver manager button disabled.");
        managerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (driverLaunches != 1) throw new InvalidOperationException("Driver manager click did not reach fake launcher.");
        Console.WriteLine("PASS nouveau driver recommendation: explicit click invokes fake launcher only");
        Find<Button>(shell, "Navdriver").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetLogicalDescendants().OfType<StackPanel>().Any(p => p.Name == "DrvList" && p.Children.Count > 0));
        if (!Find<TextBlock>(shell, "DrvStatus").Text!.StartsWith("Pronto")) throw new InvalidOperationException("Driver scan did not finish.");
        if (!shell.GetLogicalDescendants().OfType<TextBox>().Any(t => t.Name == "DrvInstallHint" && t.IsReadOnly)) throw new InvalidOperationException("Install hint (never executed) missing.");
        Click(shell, "BtnDrvFwupd");
        await Until(() => { var t = Find<TextBlock>(shell, "DrvStatus").Text!; return t.Contains("fwupd") || t.Contains("dispositivo"); });
        await Task.Delay(200); Capture(shell, "shell-drivers.png");
        Console.WriteLine("PASS driver page: offline inventory, GPU/board cards, never-executed install hint, read-only fwupd listing (network and browser actions not clicked)");
        Find<Button>(shell, "Navgpu").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "GpuSections" && p.Children.Count >= 6));
        if (Find<TextBlock>(shell, "GpuName").Text!.Length == 0 || Find<TextBlock>(shell, "GpuVendor").Text!.Length == 0) throw new InvalidOperationException("GPU highlight card empty.");
        if (Find<TextBlock>(shell, "GpuValidationNotice").Text != (ExpectedGpuNotice ?? "")) throw new InvalidOperationException("AMD/NVIDIA notice wrong on GPU page.");
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
        if (OperatingSystem.IsLinux()) await CompatTriageAsync(desktop, Program.OutputDirectory);
        if (OperatingSystem.IsLinux()) await SensorHelpAsync(desktop);
        if (OperatingSystem.IsLinux()) await OnboardingAsync(desktop);
        if (OperatingSystem.IsLinux()) await AppsAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await NvidiaDriverAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await BenchAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await OcAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await OcGpuAsync(desktop);
        if (OperatingSystem.IsLinux()) await ReportAsync(shell);
        if (OperatingSystem.IsLinux()) await OverlayAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await LightingAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await BlockedAsync(shell);
        if (OperatingSystem.IsLinux()) await OverviewSummaryAsync(shell);
        await TourAndHelpAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await StartupDriversRedesignAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await GamingExtrasAsync(shell, desktop);
        await ThemeAsync(shell);
        if (OperatingSystem.IsLinux()) await FpsScrollAndContrastAsync(shell);
        if (OperatingSystem.IsLinux()) await BlockedAndFullTourAsync(shell);
        await FpsCalibrationGateAsync(shell, desktop);
        if (OperatingSystem.IsLinux()) await GameCheckAndZramAsync(shell, desktop);
        await ToastAsync(shell);
        Click(shell, "SortMemory"); await Until(() => Find<Button>(shell, "SortMemory").Content!.ToString()!.Contains('▼'));
        Click(shell, "ToggleMonitor");
        await Until(() => (Find<Button>(shell, "ToggleMonitor").Tag as string) == "Retomar monitor" && Find<TextBlock>(shell, "SideMonitor").Text == "monitor parado");
        var frozen = Find<TextBlock>(shell, "OverviewValue").Text; await Task.Delay(1500);
        if (Find<TextBlock>(shell, "OverviewValue").Text != frozen) throw new InvalidOperationException("Paused monitor kept sampling.");
        Click(shell, "ToggleMonitor");
        await Until(() => (Find<Button>(shell, "ToggleMonitor").Tag as string) == "Pausar monitor");
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
        if (host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "StartupRemove_sysapp.desktop")) throw new InvalidOperationException("System entries must not offer Remover.");
        // Unknown system entry: the warning dialog comes first; declining keeps it enabled and writes nothing.
        var toggle = Find<CheckBox>(host, "StartupToggle_sysapp.desktop"); toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var keepDialog = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); Click(keepDialog, "ConfirmNo");
        await Until(() => !keepDialog.IsVisible && toggle.IsChecked == true);
        if (File.Exists(Path.Combine(cfg, "autostart", "sysapp.desktop"))) throw new InvalidOperationException("Declined disable wrote an override.");
        toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
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
        Console.WriteLine("PASS startup page: system entry disabled by user override only after the warning (system file untouched), declined removal keeps entry, confirmed removal + restore round trip; real list read-only");
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
            (g, p, v, status, learned, ct) => { launched = g.Key + "|" + p.WinMode; status("stub launched"); return Task.FromResult<Ilmenyx.Core.FpsSession?>(new(g.Name, DateTimeOffset.UtcNow, 61.5, 44, 95, "Fixture GPU", null)); }, () => host, "AMD", true, CancellationToken.None)
        {
            FindSteamId = (name, _) => Task.FromResult<(string, string)?>(name == "Hollow Knight - Silksong" ? ("1030300", "Hollow Knight: Silksong") : null),
            ConfirmRemove = _ => Task.FromResult(true), UmuProton = () => null, AskName = _ => Task.FromResult<string?>("Silksong (teste)"), FetchArt = (_, _) => Task.FromResult<string?>(null),
        };
        host = new Window { Width = 1100, Height = 800, Content = page }; host.Show();
        await Until(() => page.Games.Count == 1);
        Click(host, "BtnAddExe"); Find<TextBox>(host, "LAddPath").Text = "relative/path"; Click(host, "BtnAddExeConfirm");
        await Until(() => Find<TextBlock>(host, "LAddStatus").Text!.Contains("Procurar"));
        if (!Find<Button>(host, "BtnAddExeBrowse").IsVisible) throw new InvalidOperationException("The add box must offer a file chooser (Procurar…).");
        Find<TextBox>(host, "LAddPath").Text = manual; Click(host, "BtnAddExeConfirm");
        await Until(() => page.Games.Count == 2 && page.Profile is not null && host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "LTitle" && t.Text == "manualgame"));
        // A Windows game added by hand: named after its folder, Steam ID found by name (fixture search), executable path shown, removable.
        string winDir = Path.Combine(root, "Hollow Knight - Silksong"); Directory.CreateDirectory(winDir);
        string winExe = Path.Combine(winDir, "Hollow Knight Silksong.exe"); File.WriteAllBytes(winExe, [0x4d, 0x5a, 0x90, 0, 3, 0, 0, 0]);
        Find<TextBox>(host, "LAddPath").Text = winExe; Click(host, "BtnAddExeConfirm");
        await Until(() => page.Games.Count == 3 && Find<TextBlock>(host, "LTitle").Text == "Hollow Knight - Silksong" && Find<TextBox>(host, "LProtonId").Text == "1030300");
        if (Find<TextBox>(host, "LExePath").Text != winExe) throw new InvalidOperationException("The executable path of a hand-added game must be shown, not left empty.");
        if (!Find<TextBlock>(host, "LProtonIdStatus").Text!.Contains("Hollow Knight: Silksong")) throw new InvalidOperationException("The found Steam ID must say which store game it matched.");
        Find<TextBox>(host, "LProtonId").Text = "1234567";
        await Until(() => Find<TextBlock>(host, "LProtonIdStatus").Text!.StartsWith("ID salvo") && page.Profile!.ProtonGameId == "1234567");
        Click(host, "BtnRenameGame");
        await Until(() => Find<TextBlock>(host, "LTitle").Text == "Silksong (teste)" && Ilmenyx.Core.IlmenyxSettings.Load(settingsPath).CustomGames.Any(c => c.Exe == winExe && c.Name == "Silksong (teste)"));
        if (page.Profile!.ProtonGameId != "1234567") throw new InvalidOperationException("Renaming must keep the game's profile.");
        if (!Find<Button>(host, "BtnRemoveGame").IsVisible) throw new InvalidOperationException("A hand-added game must offer Remover.");
        Click(host, "BtnRemoveGame");
        await Until(() => page.Games.Count == 2 && Ilmenyx.Core.IlmenyxSettings.Load(settingsPath).CustomGames.All(c => c.Exe != winExe));
        if (!File.Exists(winExe)) throw new InvalidOperationException("Removing from the library must never delete the game's files.");
        if (Ilmenyx.Core.IlmenyxSettings.Load(settingsPath).GameProfiles.Keys.Any(k => k.Contains("Silksong"))) throw new InvalidOperationException("The removed game's profile must go too.");
        Click(host, "LGame_Steam_100");
        await Until(() => host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "LTitle" && t.Text == "Game One") && Find<StackPanel>(host, "LSteamBox").IsVisible);
        if (Find<Button>(host, "BtnRemoveGame").IsVisible) throw new InvalidOperationException("Steam games are removed in Steam, not here.");
        // Console-style library: Esc leaves the selected game and the library fills the page; clicking a cover opens it again.
        Find<Button>(host, "LGame_Steam_100").Focus();
        Find<Button>(host, "LGame_Steam_100").RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
        await Until(() => !Find<ScrollViewer>(host, "LDetail").IsVisible && page.Profile is null);
        Click(host, "LGame_Steam_100");
        await Until(() => Find<ScrollViewer>(host, "LDetail").IsVisible && Find<TextBlock>(host, "LTitle").Text == "Game One");
        Click(host, "BtnBackToLibrary");
        await Until(() => !Find<ScrollViewer>(host, "LDetail").IsVisible);
        Click(host, "LGame_Steam_100");
        await Until(() => Find<ScrollViewer>(host, "LDetail").IsVisible && Find<StackPanel>(host, "LSteamBox").IsVisible);
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
        await Until(() => launched == "Steam:100|full" && Find<TextBlock>(host, "LStatus").Text == "stub launched" && Find<Button>(host, "BtnPlay").Content!.ToString() == "Jogar");
        if (Ilmenyx.Core.IlmenyxSettings.Load(settingsPath).FpsSessions is not [{ Game: "Game One", AvgFps: 61.5 }]) throw new InvalidOperationException("Measured session returned by the launch was not kept in the settings.");
        host.Close();
        // The real shell page reads the real libraries (Steam, Lutris, Heroic) without launching, writing or closing anything.
        Find<Button>(shell, "Navlauncher").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "LGameCount" && !t.Text!.StartsWith("Procurando")));
        await Task.Delay(200); Capture(shell, "shell-launcher.png");
        Console.WriteLine("PASS launcher page: library scan, manual executable validation, Windows game named by folder with Steam ID found and path shown, removal keeps files, Esc/← Biblioteca return to the full library, profile persistence, Steam launch options with backup, helper, stubbed launch; real library read-only");
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
        await Until(() => engine.Running && page.StatusText == "Rodando" && Find<Button>(host, "BtnStress").Content!.ToString() == "Parar");
        provider.Temp = 95; await usage.SampleOnceAsync(CancellationToken.None); page.Tick();
        await Until(() => !engine.Running && page.StatusText == "Abortado" && Find<Button>(host, "BtnStress").Content!.ToString() == "Iniciar");
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
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnStress" && b.Content?.ToString() == "Iniciar"));
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
        await Until(() => Find<TextBlock>(host, "OcVerdict").Text == "ESTÁVEL" && Find<Button>(host, "BtnOcCheck").Content!.ToString() == "Iniciar verificação");
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
        await Until(() => page.LastResult is not null && Find<StackPanel>(host, "NetCards").Children.Count == 1 && Find<Button>(host, "BtnNetTest").Content!.ToString() == "Testar");
        if (page.LastResult!.VerdictText != "ESTÁVEL") throw new InvalidOperationException("Healthy fake network should be ESTÁVEL.");
        host.Close();
        Find<Button>(shell, "Navnet").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnNetTest" && b.Content?.ToString() == "Testar"));
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
        string mounts = "/dev/sda1 / ext4 rw 0 0\n"; int cpuCalls = 0, memCalls = 0, simCalls = 0, diskCalls = 0, gpuCalls = 0, rtCalls = 0; bool hang = false; double? gpuTemp = 50;
        var workload = new Ilmenyx.Contracts.CpuWorkload("fixture", 4, 2, 1, 4, 1);
        Ilmenyx.Contracts.CpuBenchmarkResult CpuResult(Ilmenyx.Contracts.CpuRunStatus st, int threads, double? score) => new("a", "m", "r", workload, st, DateTimeOffset.Now, 1, threads, 0, 60, 1, 8, score, "x", "h", new("os", "rt", "x64", 8, "Release", 0), []);
        var services = new BenchServices(
            async (o, ct, _) => { cpuCalls++; if (hang) await Task.Delay(Timeout.Infinite, ct); return new Ilmenyx.Core.Benchmarks.CpuRenderOutput(CpuResult(Ilmenyx.Contracts.CpuRunStatus.Completed, o.Threads, o.Threads == 1 ? 900 : 5400), new int[8]); },
            (avail, _, _, _) => { memCalls++; return Task.FromResult(new Ilmenyx.Core.Benchmarks.MemoryBenchResult(Ilmenyx.Core.Benchmarks.BenchRunStatus.Completed, 95.5, 40.1, 37.2, 2400L << 20, 8, null)); },
            (o, _) => { simCalls++; return Task.FromResult(new Ilmenyx.Contracts.GameSimBenchmarkResult("a", "m", "r", "w", Ilmenyx.Contracts.GameSimBenchmarkStatus.Completed, DateTimeOffset.Now, 30000, o.Threads, 1, 10, 400, 2, 2.01, o.Duration.TotalSeconds, o.Timeout.TotalSeconds, 200000, 3, 144.5, 110.2, 5.1, 1.2, 1000, "x", "rt", "x64", [])); },
            (o, _, _) => { diskCalls++; return Task.FromResult(new Ilmenyx.Core.Benchmarks.DiskBenchResult(Ilmenyx.Core.Benchmarks.BenchRunStatus.Completed, o.Directory, o.FileMegabytes, 500, 450, 10000, 9000, 100, 111, 60000, null)); },
            () => 8L << 30, () => mounts,
            (o, ct, _) => { gpuCalls++; if (hang) return Task.Delay(Timeout.Infinite, ct).ContinueWith<Ilmenyx.Gpu.Vulkan.GpuSceneResult>(_ => throw new OperationCanceledException(ct), ct); return Task.FromResult(new Ilmenyx.Gpu.Vulkan.GpuSceneResult(Ilmenyx.Gpu.Vulkan.GpuRunStatus.Completed, "Fake GPU", o.Width, o.Height, 3000, 60, 50, 41.5, 500, null)); },
            () => new GpuBenchInfo(null, "Fake GPU", false, false), () => gpuTemp,
            (o, ct, _) => { rtCalls++; if (hang) return Task.Delay(Timeout.Infinite, ct).ContinueWith<Ilmenyx.Gpu.Vulkan.GpuRtResult>(_ => throw new OperationCanceledException(ct), ct);
                return Task.FromResult(new Ilmenyx.Gpu.Vulkan.GpuRtResult(Ilmenyx.Gpu.Vulkan.GpuRunStatus.Completed, "Fake RT GPU", o.Preset, o.Width, o.Height, [new(20, 18, 200), new(20.2, 18.1, 202), new(20.1, 18, 201)], 1.5, true, null)); },
            () => new GpuRtInfo(null, 0, "Fake RT GPU", false));
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
        await Until(() => Find<TextBlock>(host, "BenchHeadline").Text!.Contains("pts") && Find<Button>(host, "BtnBench").Content!.ToString() == "Iniciar");
        if (cpuCalls != 2 || !Find<TextBlock>(host, "BenchResultText").Text!.Contains("não é comparável") || !Find<TextBlock>(host, "BenchResultText").Text!.Contains("6.0x")) throw new InvalidOperationException("CPU result must show both runs, the ratio and the non-comparability note.");
        if (!Find<Image>(host, "BenchImage").IsVisible) throw new InvalidOperationException("Render image must be shown after the CPU test.");
        // Cancellation publishes no value.
        hang = true; Chip("BenchModePanel", "Single"); Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes");
        await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "Parar");
        Click(host, "BtnBench"); await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "Iniciar");
        if (!Find<TextBlock>(host, "BenchStatus").Text!.Contains("Cancelado") || Find<TextBlock>(host, "BenchHeadline").Text != "—") throw new InvalidOperationException("Stopped test must publish nothing.");
        // Temperature guard cuts a running test.
        Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes"); await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "Parar");
        provider.Temp = 99; await usage.SampleOnceAsync(CancellationToken.None); page.Tick();
        await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "Iniciar"); provider.Temp = 60; await usage.SampleOnceAsync(CancellationToken.None);
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
        Chip("BenchKindPanel", "GPU"); await Until(() => host.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "BenchGpuSeconds"));
        Click(host, "BtnBench"); dialog = await Confirm();
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("Fake GPU") || !Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("1920×1080")) throw new InvalidOperationException("GPU confirmation must name the adapter and workload.");
        Click(dialog, "ConfirmNo"); await Until(() => !dialog.IsVisible); if (gpuCalls != 0) throw new InvalidOperationException("Declined confirmation ran the GPU scene.");
        hang = true; Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes"); await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "Parar");
        gpuTemp = 99; page.Tick(); await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "Iniciar");
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
        // Ray tracing tile (fake engine): enabled after the adapter check, confirmation names adapter/preset/resolution, hot GPU aborts, a run publishes score and reliability.
        await Until(() => Find<Button>(host, "BenchRtTile").IsEnabled);
        Chip("BenchKindPanel", "RAY TRACING"); await Until(() => host.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "BenchRtPreset"));
        Chip("BenchRtPreset", "Normal"); Click(host, "BtnBench"); dialog = await Confirm();
        if (!Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("Fake RT GPU") || !Find<TextBlock>(dialog, "ConfirmText").Text!.Contains("Normal a 2560×1440")) throw new InvalidOperationException("RT confirmation must name the adapter, preset and resolution.");
        Click(dialog, "ConfirmNo"); await Until(() => !dialog.IsVisible); if (rtCalls != 0) throw new InvalidOperationException("Declined confirmation ran the RT test.");
        hang = true; Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes"); await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "Parar");
        gpuTemp = 99; page.Tick(); await Until(() => Find<Button>(host, "BtnBench").Content!.ToString() == "Iniciar");
        if (!Find<TextBlock>(host, "BenchStatus").Text!.Contains("ABORTADO: GPU a 99") || Find<TextBlock>(host, "BenchHeadline").Text != "—") throw new InvalidOperationException("Hot GPU must abort the RT run and publish nothing.");
        gpuTemp = 50; hang = false; Click(host, "BtnBench"); Click(await Confirm(), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "BenchHeadline").Text == "201 pts" && rtCalls == 2);
        if (!Find<TextBlock>(host, "BenchResultText").Text!.Contains("Resultado confiável") || !Find<TextBlock>(host, "BenchResultText").Text!.Contains("não comparável")) throw new InvalidOperationException("RT result must show reliability and the non-comparability note.");
        if (IlmenyxSettings.Load(settingsPath).BenchRuns.Count != 6) throw new InvalidOperationException("The RT run must be kept in the local history.");
        await Task.Delay(300); Capture(host, "bench-rt.png");
        // Without inline ray tracing the tile stays disabled and says why.
        var noRt = new BenchPage(services with { RtInfo = () => new GpuRtInfo("Intel(R) Xe Graphics (TGL GT2) não tem ray tracing por hardware no Vulkan.", -1, "", false) }, usage, () => null, settingsPath, CancellationToken.None);
        var host2 = new Window { Width = 1100, Height = 700, Content = new ScrollViewer { Content = noRt } }; host2.Show();
        await Until(() => host2.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BenchRtTile" && !b.IsEnabled && (ToolTip.GetTip(b) as string ?? "").Contains("TGL GT2")));
        host2.Close();
        host.Close();
        Find<Button>(shell, "Navbench").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnBench" && b.Content?.ToString() == "Iniciar"));
        await Task.Delay(300); Capture(shell, "shell-bench.png");
        Console.WriteLine("PASS bench page: every test needs a visible confirmation, declined/stopped/aborted runs publish nothing, temperature guard cuts the run, ray tracing tile disabled with the reason when unsupported, tmpfs folders refused, results kept in the local history; real load never started");
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
        await Until(() => Find<TextBlock>(host, "OcVerdict").Text == "ESTÁVEL" && Find<Button>(host, "BtnOcCheck").Content!.ToString() == "Iniciar verificação");
        if (engine.Starts != 1 || engine.Stops != 1 || !Find<TextBlock>(host, "OcResult").Text!.Contains("5,400")) throw new InvalidOperationException("Stable run must start and stop the engine once and show the score.");
        errors = new Ilmenyx.Core.KernelHardwareErrors(1, 0, 0, 0, "mce: fixture", null);
        Click(host, "BtnOcCheck"); Click(await ConfirmAsync(desktop), "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "OcVerdict").Text == "INSTÁVEL" && Find<Button>(host, "BtnOcCheck").Content!.ToString() == "Iniciar verificação");
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
        // No button: the selection is saved by itself after a short delay (a burst of clicks writes once).
        if (host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnMangoConfig")) throw new InvalidOperationException("The MangoHud config button was removed: saving is automatic.");
        if (!Find<TextBox>(host, "OvLaunchOption").IsVisible || !Find<TextBox>(host, "OvLaunchOption").Text!.EndsWith("mangohud %command%")) throw new InvalidOperationException("The Steam launch option line must be shown from the start.");
        await Until(() => !page.SavePending && page.SaveCount >= 1 && File.Exists(config) && Find<TextBlock>(host, "OvSaved").Opacity > 0.5);
        if (page.SaveCount != 1) throw new InvalidOperationException($"Quick successive changes must be written once (debounced), got {page.SaveCount} saves.");
        if (IlmenyxSettings.Load(settingsPath).OverlayItems is not { } saved || !saved.SequenceEqual(["fps", "frametime", "low1"])) throw new InvalidOperationException("Selection must be persisted.");
        Click(host, "BtnOverlayToggle"); await Until(() => desktop.Windows.OfType<OverlayWindow>().Any(w => w.IsVisible) && Find<Button>(host, "BtnOverlayToggle").Content!.ToString() == "Ocultar overlay");
        var overlay = desktop.Windows.OfType<OverlayWindow>().Single(w => w.IsVisible);
        await Until(() => overlay.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text!.StartsWith("144 FPS")));
        Click(host, "BtnOverlayToggle"); await Until(() => !overlay.IsVisible && Find<Button>(host, "BtnOverlayToggle").Content!.ToString() == "Mostrar overlay");
        string text = File.ReadAllText(config);
        if (!text.Contains("position=top-left") || !text.Contains("fps_metrics=avg,0.01") || !text.Contains($"output_folder={logs}") || !Find<TextBox>(host, "OvLaunchOption").Text!.EndsWith("mangohud %command%")) throw new InvalidOperationException("Generated MangoHud config and launch option must follow the selection.");
        if (File.GetUnixFileMode(config) != (UnixFileMode.UserRead | UnixFileMode.UserWrite)) throw new InvalidOperationException("Config must be user-only.");
        // Corner change: saved by itself into the same file, without any button.
        Find<StackPanel>(host, "OvCornerPanel").Children.OfType<Button>().Single(b => b.Content?.ToString() == "Inferior dir.").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !page.SavePending && page.SaveCount == 2 && File.ReadAllText(config).Contains("position=bottom-right"));
        if (IlmenyxSettings.Load(settingsPath).OverlayCorner != "bottom-right") throw new InvalidOperationException("Corner must be persisted automatically.");
        await Task.Delay(200); Capture(host, "overlay-autosave.png");
        host.Close();
        Find<Button>(shell, "Navoverlay").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnOverlayToggle"));
        Console.WriteLine("PASS overlay page: item picker and presets persist, Ilmenyx overlay window shows/hides with FPS from the frame source, MangoHud config and launch option saved automatically (debounced, one write per burst, \"Salvo\" shown) into Ilmenyx's own file (user MangoHud.conf untouched), no generate button");
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
            if (!(text.Contains("BLOQUEADO") || text.Contains("TESTE DISPONÍVEL"))) throw new InvalidOperationException("Unexpected state text for " + f.Id + ": " + text);
        }
        var details = Find<StackPanel>(shell, "BlockedDetails_rt-bench"); bool before = details.IsVisible;
        Click(shell, "BlockedWhy_rt-bench"); if (details.IsVisible == before) throw new InvalidOperationException("(!) must toggle the reason.");
        if (!details.Children.OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("VK_KHR_ray_query"))) throw new InvalidOperationException("The reason must name the ray tracing extension.");
        string summary = Find<TextBlock>(shell, "BlockedSummary").Text ?? "";
        if (summary.Length == 0) throw new InvalidOperationException("Summary missing.");
        shell.Select("dash");
        Console.WriteLine("PASS blocked page: one card per blocked feature, state chips, (!) toggles the reason");
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
        fxPanel.Children.OfType<Button>().Single(b => b.Content!.ToString() == "Apagar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(60); await page.TickAsync();
        await Until(() => server.Snapshot().Where(p => p.Type == 1050).Any(p => p.Payload[6..].All(x => x == 0)));
        Click(host, "BtnLightRun"); await Until(() => !page.IsControlling && server.Snapshot().Count(p => p.Type == 1101) == 2);
        if (!Find<TextBlock>(host, "LightRunHint").Text!.Contains("devolvido")) throw new InvalidOperationException("Control must be handed back and say so.");
        host.Close(); await page.StopAsync(false);
        Find<Button>(shell, "Navlighting").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnLightConnect"));
        Console.WriteLine("PASS lighting page: nothing connects or is sent before the click, control needs confirmation, effect frames reach the OpenRGB server, every device is handed back to its original mode (fake loopback server; no real RGB hardware)");
    }
    /// <summary>Welcome dialog, guided tour (forward/back, Enter, Esc, Pular, flag saved) and the Help Center with the context "?" and search.</summary>
    private static async Task TourAndHelpAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var text = HelpContent.For();
        foreach (var key in shell.PageKeys) if (text.Section(key) is null) throw new InvalidOperationException("Page without a help section: " + key);
        if (!ShellWindow.TourSuppressed) throw new InvalidOperationException("The smoke must suppress the automatic welcome tour.");
        if (await shell.OfferTourAsync()) throw new InvalidOperationException("Suppressed welcome must not open by itself.");
        string settingsFile = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")!, "ilmenyx", "settings.json");
        async Task<WelcomeTourDialog> Welcome()
        {
            await Until(() => desktop.Windows.OfType<WelcomeTourDialog>().Any(w => w.IsVisible));
            var w = desktop.Windows.OfType<WelcomeTourDialog>().Single(x => x.IsVisible); await Task.Delay(150); return w;
        }
        // Accept: the tour opens on the first step; the flag is saved before the dialog shows.
        var offer = shell.OfferTourAsync(force: true);
        var welcome = await Welcome();
        if (!File.ReadAllText(settingsFile).Contains("\"TourOffered\": true")) throw new InvalidOperationException("TourOffered must be saved when the welcome is shown.");
        Capture(welcome, "welcome-tour.png");
        Click(welcome, "WelcomeStart");
        if (!await offer || shell.TourStepIndex != 0 || !Find<Border>(shell, "TourCard").IsEffectivelyVisible) throw new InvalidOperationException("Tour did not start on step 1.");
        if (shell.CurrentPage != text.Tour[0].PageKey || Find<TextBlock>(shell, "TourCounter").Text != $"Passo 1 de {text.Tour.Count}") throw new InvalidOperationException("Step 1 page/counter wrong.");
        if (Find<Button>(shell, "TourBack").IsEnabled) throw new InvalidOperationException("Voltar must be disabled on step 1.");
        Click(shell, "TourNext"); await Until(() => shell.TourStepIndex == 1);
        Key(shell, Avalonia.Input.Key.Enter); await Until(() => shell.TourStepIndex == 2 && shell.CurrentPage == text.Tour[2].PageKey);
        Click(shell, "TourBack"); await Until(() => shell.TourStepIndex == 1);
        int games = text.Tour.ToList().FindIndex(t => t.PageKey == "launcher");
        while (shell.TourStepIndex < games) { Click(shell, "TourNext"); await Task.Delay(60); }
        await Until(() => shell.CurrentPage == "launcher" && Find<TextBlock>(shell, "TourTitle").Text == text.Tour[games].Title);
        await Task.Delay(500);
        void CardFits(string where)
        {
            var card = Find<Border>(shell, "TourCard"); var at = card.TranslatePoint(default, shell)!.Value;
            if (at.X < 214 || at.Y < 0 || at.Y + card.Bounds.Height > shell.Bounds.Height + 0.5 || at.X + card.Bounds.Width > shell.Bounds.Width + 0.5)
                throw new InvalidOperationException($"Tour card outside the window ({where}): {at} {card.Bounds.Size} in {shell.Bounds.Size}");
            if (!shell.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "TourRing" && b.IsEffectivelyVisible)) throw new InvalidOperationException("Tour step without a highlight: " + where);
        }
        CardFits("games step"); Capture(shell, "tour-step.png");
        while (shell.TourStepIndex < text.Tour.Count - 1) { Click(shell, "TourNext"); await Task.Delay(60); }
        await Until(() => shell.CurrentPage == "help" && (string?)Find<Button>(shell, "TourNext").Content == "Concluir");
        await Task.Delay(300); CardFits("last step"); Capture(shell, "tour-step-last.png");
        Click(shell, "TourBack"); await Until(() => shell.TourStepIndex == text.Tour.Count - 2);
        Click(shell, "TourSkip"); await Until(() => shell.TourStepIndex == -1 && !Find<Canvas>(shell, "TourLayer").IsVisible);
        // Esc closes; Concluir on the last step closes.
        shell.StartTour(); await Until(() => shell.TourStepIndex == 0);
        Key(shell, Avalonia.Input.Key.Escape); await Until(() => shell.TourStepIndex == -1);
        // Agora não: no tour; without force the welcome is never shown again.
        offer = shell.OfferTourAsync(force: true); welcome = await Welcome(); Click(welcome, "WelcomeLater");
        if (await offer || shell.TourStepIndex != -1) throw new InvalidOperationException("Agora não must not start the tour.");
        if (await shell.OfferTourAsync()) throw new InvalidOperationException("Welcome must be offered only once.");
        Console.WriteLine($"PASS welcome tour: offered once (TourOffered saved), {text.Tour.Count} steps navigate pages with highlights, Próximo/Enter/Voltar, Pular and Esc close, Agora não runs nothing");

        // Help Center: sidebar entry, context "?" scrolls to the current page's section, search opens a question's section, Refazer o tour.
        Find<Button>(shell, "Navhelp").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.CurrentPage == "help" && Find<ScrollViewer>(shell, "HelpScroll").IsEffectivelyVisible && !Find<Button>(shell, "PageHelp").IsVisible);
        if (shell.GetVisualDescendants().OfType<Border>().Count(b => b.Name?.StartsWith("Help_", StringComparison.Ordinal) == true) != shell.PageKeys.Count) throw new InvalidOperationException("Help Center must have one card per page.");
        await Task.Delay(300); Capture(shell, "help-center.png");
        async Task ContextHelp(string key)
        {
            Find<Button>(shell, "Nav" + key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => shell.CurrentPage == key && Find<Button>(shell, "PageHelp").IsVisible);
            Click(shell, "PageHelp");
            var scroll = Find<ScrollViewer>(shell, "HelpScroll");
            await Until(() => shell.CurrentPage == "help" && Find<Border>(shell, "Help_" + key).TranslatePoint(default, scroll) is { } p && p.Y is > -2 and < 40);
        }
        await ContextHelp("launcher");
        await ContextHelp("stress");
        var faq = Find<Expander>(shell, "HelpFaq_stress_safe"); faq.IsExpanded = true; await Task.Delay(400);
        Capture(shell, "help-center-stress.png");
        Find<Button>(shell, "Navsystem").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Find<AutoCompleteBox>(shell, "GlobalSearch").SelectedItem = "Por que a temperatura da CPU não aparece?";
        await Until(() => shell.CurrentPage == "help" && Find<Border>(shell, "Help_dash").TranslatePoint(default, Find<ScrollViewer>(shell, "HelpScroll")) is { } p && p.Y is > -2 and < 40);
        Click(shell, "HelpRetour"); await Until(() => shell.TourStepIndex == 0); shell.EndTour();
        // Minimum window and light theme: the card stays inside the window and readable.
        double w = shell.Width, h = shell.Height;
        shell.Width = 900; shell.Height = 560; await Task.Delay(400);
        shell.StartTour(); while (shell.TourStepIndex < games) { shell.NextTourStep(); await Task.Delay(60); }
        await Task.Delay(500); CardFits("900x560"); Capture(shell, "tour-step-min.png");
        Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight); await Task.Delay(400);
        CardFits("light"); Capture(shell, "tour-step-light.png");
        shell.EndTour(); shell.Width = w; shell.Height = h; await Task.Delay(300);
        shell.OpenHelp("launcher"); await Task.Delay(500); Capture(shell, "help-center-light.png");
        Click(shell, "ToggleTheme"); await Until(() => !Palette.IsLight);
        Console.WriteLine("PASS help center: one section per page, sidebar entry, context ? scrolls to launcher and stress sections, search opens a question's section, Refazer o tour, tour card fits at 900x560 and in the light theme");
    }
    private static async Task ThemeAsync(ShellWindow shell)
    {
        if (Palette.IsLight) throw new InvalidOperationException("Default theme must be dark.");
        Find<Button>(shell, "Navsystem").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight && (Find<Button>(shell, "ToggleTheme").Tag as string) == "Tema escuro");
        if (((Avalonia.Media.SolidColorBrush)Palette.Bg).Color.R < 200 || ((Avalonia.Media.SolidColorBrush)Palette.Fg).Color.R > 80) throw new InvalidOperationException("Light palette must have a light background and dark ink.");
        await Task.Delay(400); Capture(shell, "shell-light.png");
        string saved = File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")!, "ilmenyx", "settings.json"));
        if (!saved.Contains("\"ThemeLight\": true")) throw new InvalidOperationException("Theme choice must be persisted.");
        foreach (var key in new[] { "stress", "oc", "disks" }) { Find<Button>(shell, "Nav" + key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(500); Capture(shell, $"shell-light-{key}.png"); }
        Click(shell, "ToggleTheme"); await Until(() => !Palette.IsLight && (Find<Button>(shell, "ToggleTheme").Tag as string) == "Tema claro");
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
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task FpsAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // The page is fully local (HowManyFPS was removed on 2026-11-08): no HTTP handler at all.
        string root = Path.Combine(Program.OutputDirectory, "fps-fixture"); Directory.CreateDirectory(root);
        string settingsPath = Path.Combine(root, "settings.json"); Window? host = null;
        // A session measured on this PC (as the Launcher records it) for Dota 2; nothing is played.
        var fixture = new Ilmenyx.Core.IlmenyxSettings(); fixture.AddFpsSession(new("Dota 2", new DateTimeOffset(2026, 11, 6, 21, 0, 0, TimeSpan.Zero), 87.4, 52, 1800, "NVIDIA GeForce RTX 4070", "1920x1080")); fixture.Save(settingsPath);
        var ctx = new Ilmenyx.Core.FpsContext("NVIDIA GeForce RTX 4070", "10de", "2786", "nvidia", false, 2UL << 30, null, true, 0, 0);
        var page = new GamesFpsPage(new Ilmenyx.Core.IlmenyxSettings(), settingsPath, () => ("AMD Ryzen 7 7700X 8-Core Processor", "NVIDIA GeForce RTX 4070"), () => ctx);
        host = new Window { Width = 1100, Height = 900, Content = page }; host.Show(); // the page scrolls by itself (sticky catalogue)
        await Until(() => page.Rows.Count > 20 && page.Rows.Any(r => r.Fps > 0));
        if (page.GpuIndex != 100 || page.CpuIndex != 100) throw new InvalidOperationException("Hardware indices not resolved from the catalog tables.");
        string CardFps(string name) => host.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Game_" + LauncherPage.SafeName(name)).GetVisualDescendants().OfType<TextBlock>().First(t => t.FontSize == 28).Text!;
        // The local estimate is shown as a range around the table value (CS2 at 1080p/Alto on RTX 4070 + 7700X = 344 since the GPU index refit of 2026-11-10), never as a single number.
        bool RangeAround(string text, double centre) => text.Split('–') is [var lo, var hi] && double.TryParse(lo, System.Globalization.CultureInfo.InvariantCulture, out double l) && double.TryParse(hi, System.Globalization.CultureInfo.InvariantCulture, out double h) && l < centre && centre < h;
        await Until(() => host.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "Game_" + LauncherPage.SafeName("Counter-Strike 2")));
        if (page.Rows.Single(r => r.Name == "Counter-Strike 2").Fps != 344 || !RangeAround(CardFps("Counter-Strike 2"), 344)) throw new InvalidOperationException("CS2 estimate at 1080p/Alto should be a range around 344, got " + CardFps("Counter-Strike 2") + ".");
        if (page.Assessment.Confidence != Ilmenyx.Core.FpsConfidence.Medium) throw new InvalidOperationException("Desktop GPU + CPU at the reference should give medium confidence.");
        var dota = page.Rows.Single(r => r.Name == "Dota 2");
        if (CardFps("Dota 2") != "87" || dota.Source != "MEDIDO NESTE PC" || !dota.Summary.StartsWith("Medido neste PC: 87 FPS (1% low 52) · última sessão", StringComparison.Ordinal)) throw new InvalidOperationException("Measured session must take priority over the estimate.");
        if (page.Alerts.Select(a => a.Title).ToArray() is not ["2 GB de VRAM", "Notebook na bateria"] || !Find<StackPanel>(host, "FpsAlerts").IsVisible) throw new InvalidOperationException("VRAM and battery alerts expected.");
        await Task.Delay(300); Capture(host, "fps-fixture.png");
        foreach (var chip in host.GetVisualDescendants().OfType<Button>().Where(b => b.Content?.ToString() == "1440p")) chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => CardFps("Counter-Strike 2") is var t && !RangeAround(t, 344) && RangeAround(t, page.Rows.Single(r => r.Name == "Counter-Strike 2").Fps) && CardFps("Dota 2") == "87");
        // No API key box, no query button, no mention of the removed service; the source card says the estimate is local.
        if (host.GetVisualDescendants().OfType<Control>().Any(c => c.Name is "ApiKeyBox" or "BtnApiKey" or "BtnFpsQuery")) throw new InvalidOperationException("The removed HowManyFPS controls are still on the page.");
        if (host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("HowManyFPS", StringComparison.OrdinalIgnoreCase) == true || t.Text?.Contains("CONSULTAR API", StringComparison.Ordinal) == true)) throw new InvalidOperationException("A text still mentions the removed HowManyFPS API.");
        if (Find<TextBlock>(host, "GameSourceTitle").Text != "Fonte: provisória (PC ainda não calibrado)" || !Find<TextBlock>(host, "GameSourceText").Text!.Contains("sem usar a internet", StringComparison.Ordinal)) throw new InvalidOperationException("Source card must say the estimate is local.");
        host.Close();
        Find<Button>(shell, "Navfps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "GameSourceTitle" && t.IsVisible));
        if (Ilmenyx.Core.IlmenyxSettings.Load(settingsPath).FpsSessions.Count != 1) throw new InvalidOperationException("Opening the page changed the measured sessions.");
        Console.WriteLine("PASS fps page: local estimate as range + confidence from catalog tables, measured session has priority, VRAM/battery alerts, option chips recompute, fully local (no HowManyFPS key/query controls or texts), sessions untouched");
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

    private static async Task OnboardingAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Fake runner: pkexec is never started. Two blocked sensors, offered together; one call, one password.
        var scripts = new List<string>(); bool fail = true;
        var fixes = SensorHelp.Build(true, CpuPowerAccess.NoPermission, "Intel(R) Core(TM) i7-7500U", "/sys/class/powercap/intel-rapl:0", false);
        var owner = new Window { Width = 300, Height = 200 }; owner.Show();
        var dialog = new PermissionsOnboardingDialog(fixes, script => { scripts.Add(script); return Task.FromResult(fail ? (false, "Cancelado: a senha não foi informada.") : (true, "Pronto.")); });
        var shown = dialog.ShowDialog(owner);
        await Until(() => dialog.IsVisible);
        if (Find<CheckBox>(dialog, "OnboardFix0").IsChecked != true || Find<CheckBox>(dialog, "OnboardFix1").IsChecked != true) throw new InvalidOperationException("Every fix must start selected.");
        await Task.Delay(300); Capture(dialog, "onboarding-permissions.png");
        Find<CheckBox>(dialog, "OnboardFix0").IsChecked = false; Find<CheckBox>(dialog, "OnboardFix1").IsChecked = false;
        if (Find<Button>(dialog, "OnboardApply").IsEnabled) throw new InvalidOperationException("Nothing selected: apply must be disabled.");
        Find<CheckBox>(dialog, "OnboardFix0").IsChecked = true; Find<CheckBox>(dialog, "OnboardFix1").IsChecked = true;
        // Password dialog dismissed: nothing applied, can retry, sidebar remains the way back.
        Click(dialog, "OnboardApply"); await Until(() => scripts.Count == 1 && Find<Button>(dialog, "OnboardApply").IsEnabled);
        if (!Find<TextBlock>(dialog, "OnboardStatus").Text!.Contains("menu lateral") || dialog.Applied) throw new InvalidOperationException("Refused password must say how to do it later.");
        fail = false; Click(dialog, "OnboardApply"); await Until(() => scripts.Count == 2 && dialog.Applied);
        if (scripts[1] != SensorHelp.Combined(fixes) || !scripts[1].Contains("modprobe coretemp") || !scripts[1].Contains("chmod o+r /sys/class/powercap/intel-rapl:0/energy_uj"))
            throw new InvalidOperationException("Both fixes must go in one privileged call.");
        if ((string?)Find<Button>(dialog, "OnboardSkip").Content != "Continuar") throw new InvalidOperationException("After success the close button must read Continuar.");
        Click(dialog, "OnboardSkip"); await shown;
        // "Agora não" runs nothing.
        scripts.Clear();
        var second = new PermissionsOnboardingDialog(fixes, script => { scripts.Add(script); return Task.FromResult((true, "Pronto.")); });
        var shown2 = second.ShowDialog(owner); await Until(() => second.IsVisible);
        Click(second, "OnboardSkip"); await shown2; owner.Close();
        if (scripts.Count != 0) throw new InvalidOperationException("Agora não must not run anything.");
        Console.WriteLine("PASS first-run permissions: all blocked sensors offered at once, one privileged call (fake polkit runner), refusal keeps the way back, Agora não runs nothing");
    }

    private static async Task SensorHelpAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Fake runner: pkexec is never started by the smoke.
        var scripts = new List<string>();
        var fixes = SensorHelp.Build(false, CpuPowerAccess.NoPermission, "AMD Ryzen 5", "/sys/class/powercap/intel-rapl:0", false);
        var owner = new Window { Width = 300, Height = 200 }; owner.Show();
        var dialog = new SensorHelpDialog(fixes, script => { scripts.Add(script); return Task.FromResult((true, "Pronto.")); });
        var shown = dialog.ShowDialog(owner);
        await Until(() => dialog.IsVisible);
        if (Find<SelectableTextBlock>(dialog, "SensorFixCommand0").IsEffectivelyVisible) throw new InvalidOperationException("Commands must start folded when one click can fix it.");
        Click(dialog, "SensorFixApply0"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var no = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); Click(no, "ConfirmNo"); await Until(() => !no.IsVisible); await Task.Delay(60);
        if (scripts.Count != 0) throw new InvalidOperationException("Declined fix must not run anything.");
        Click(dialog, "SensorFixApply0"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => scripts.Count == 1 && Find<Button>(dialog, "SensorFixUndo0").IsVisible);
        if (!scripts[0].StartsWith("chmod o+r /sys/class/powercap/intel-rapl:0/energy_uj", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fix script.");
        Click(dialog, "SensorFixUndo0"); await Until(() => scripts.Count == 2 && Find<Button>(dialog, "SensorFixApply0").IsEnabled);
        Click(dialog, "SensorFixShowCommands0"); await Until(() => Find<SelectableTextBlock>(dialog, "SensorFixCommand0").IsEffectivelyVisible);
        Click(dialog, "CloseSensorHelp"); await shown; owner.Close();
        Console.WriteLine("PASS sensor help: one-click fix only after confirmation (fake polkit runner), undo, commands folded for technicians");
    }

    private static async Task NvidiaDriverAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Shell tab: real READ-ONLY diagnosis (sysfs, dpkg-query, mokutil...); no action button of the shell page is clicked.
        Find<Button>(shell, "Navnvdriver").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.CurrentPage == "nvdriver" && shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "NvStatus" && t.IsVisible && t.Text!.StartsWith("Verificado", StringComparison.Ordinal)));
        await Task.Delay(300); Capture(shell, "shell-nvdriver.png");
        // Drivers page links to the tab (the NVIDIA card is only there on a machine with an NVIDIA GPU, as this one).
        Find<Button>(shell, "Navdriver").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.CurrentPage == "driver" && shell.GetLogicalDescendants().OfType<StackPanel>().Any(p => p.Name == "DrvList" && p.Children.Count > 0));
        if (shell.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "BtnDrvNvidiaTab") is { } link)
        {
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => shell.CurrentPage == "nvdriver");
        }
        if (shell.GetLogicalDescendants().OfType<TextBox>().Any(t => t.Name == "DrvInstallHint" && (t.Text ?? "").Contains("sudo ubuntu-drivers install"))) throw new InvalidOperationException("Drivers page still suggests ubuntu-drivers install.");

        // Fake services: the incident of 2026-11-07 (Mint 22.3, 940MX, kernel 7.0 with incomplete headers, iF/iU packages). Never pkexec/apt.
        const string running = "6.14.0-37-generic";
        var gpu = new NvidiaGpu("0000:01:00.0", "134d", "GM108M [GeForce 940MX]", NvidiaGeneration.Maxwell, "GM108", "nouveau");
        var mint = DistroInfo.Parse("ID=linuxmint\nID_LIKE=\"ubuntu debian\"\nPRETTY_NAME=\"Linux Mint 22.3\"\n");
        var incident = new NvidiaDiagnosis
        {
            Gpus = [gpu], OtherGpus = ["Intel HD Graphics 620 (i915)"], Distro = mint, RunningKernel = running, SecureBoot = SecureBootState.Disabled, PrimeMode = "on-demand", PrimeSelectAvailable = true,
            Kernels = [new KernelHeaders(running, true, []), new KernelHeaders("7.0.0-42-generic", true, ["scripts/module.lds", "tools/objtool/objtool"])],
            Packages = DpkgPackage.Parse("iU |nvidia-driver-580|580.178.04-0ubuntu0.24.04.1\nii |nvidia-utils-580|580.178.04-0ubuntu0.24.04.1\niF |linux-modules-nvidia-580-7.0.0-42-generic|7.0.0-42.42\nii |linux-modules-nvidia-580-generic-hwe-24.04|6.14.0-37.37\nii |linux-image-7.0.0-42-generic|7.0.0-42.42\nii |linux-modules-7.0.0-42-generic|7.0.0-42.42\nii |linux-image-6.14.0-37-generic|6.14.0-37.37\n"),
        };
        var after = incident with
        {
            Kernels = [new KernelHeaders(running, true, [])],
            Packages = DpkgPackage.Parse("ii |nvidia-driver-580|580.178.04-0ubuntu0.24.04.1\nii |nvidia-dkms-580|580.178.04-0ubuntu0.24.04.1\nii |nvidia-utils-580|580.178.04-0ubuntu0.24.04.1\nii |linux-image-6.14.0-37-generic|6.14.0-37.37\n"),
            Dkms = DkmsEntry.Parse("nvidia/580.178.04, 6.14.0-37-generic, x86_64: installed\n"),
        };
        string repairSim = "NOTE: This is only a simulation!\nPurg linux-modules-nvidia-580-generic-hwe-24.04 [6.14.0-37.37]\nPurg linux-modules-nvidia-580-7.0.0-42-generic [7.0.0-42.42]\nPurg linux-image-7.0.0-42-generic [7.0.0-42.42]\nPurg linux-modules-7.0.0-42-generic [7.0.0-42.42]\nInst nvidia-dkms-580 (580.178.04-0ubuntu0.24.04.1 Ubuntu:24.04/noble-updates [amd64])\n0 upgraded, 1 newly installed, 4 to remove and 0 not upgraded.\n";
        string hweSim = "Inst linux-image-7.0.0-42-generic (7.0.0-42.42 Ubuntu:24.04/noble-updates [amd64])\nInst linux-modules-nvidia-580-7.0.0-42-generic (7.0.0-42.42 Ubuntu:24.04/noble-updates [amd64])\nInst nvidia-driver-580 (580.178.04 Ubuntu:24.04/noble-updates [amd64])\n0 upgraded, 3 newly installed, 0 to remove and 0 not upgraded.\n";
        var scripts = new List<string>(); int diagnoses = 0; string simText = repairSim;
        NvidiaDiagnosis current = incident;
        var services = new NvidiaDriverServices(
            _ => { diagnoses++; return Task.FromResult(current); },
            (_, _) => Task.FromResult(AptSimulation.Parse(simText, 0)),
            (script, line, _) => { scripts.Add(script); line("== Ilmenyx 1/4: simulação de segurança (falsa)"); line("dkms: nvidia/580.178.04 installed (falso)"); current = after; return Task.FromResult((true, "Pronto.")); });
        Window? host = null; var page = new NvidiaDriverPage(services, () => host, CancellationToken.None);
        host = new Window { Width = 1180, Height = 1500, Background = Palette.Bg, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => diagnoses == 1 && Find<TextBlock>(host, "NvState").Text!.Contains("problema"));
        if (Find<TextBlock>(host, "NvGpuName").Text != "GeForce 940MX") throw new InvalidOperationException("Marketing name missing.");
        if (Find<TextBlock>(host, "NvTileKernel").Text != running || Find<TextBlock>(host, "NvTilePrime").Text != "on-demand" || Find<TextBlock>(host, "NvTileSecureBoot").Text != "desligado") throw new InvalidOperationException("Stat tiles wrong.");
        await Task.Delay(200); Capture(host, "nvdriver-incident.png");
        Click(host, "BtnNvRepair"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var confirm = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible);
        string asked = Find<TextBlock>(confirm, "ConfirmText").Text!;
        if (!asked.Contains("linux-image-7.0.0-42-generic") || !asked.Contains("senha")) throw new InvalidOperationException("Confirmation must show exactly what will be removed.");
        Click(confirm, "ConfirmNo"); await Until(() => !confirm.IsVisible); await Task.Delay(60);
        if (scripts.Count != 0) throw new InvalidOperationException("Declined repair must not run anything.");
        Click(host, "BtnNvRepair"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => scripts.Count == 1 && Find<TextBlock>(host, "NvVerify").IsVisible && Find<Border>(host, "NvRebootCard").IsVisible);
        if (!scripts[0].StartsWith("set -eu\n", StringComparison.Ordinal) || !scripts[0].Contains("'linux-image-7.0.0-42-generic-'") || !scripts[0].Contains("'nvidia-dkms-580'") || scripts[0].Contains("ubuntu-drivers"))
            throw new InvalidOperationException("Unexpected repair script.");
        if (!Find<TextBlock>(host, "NvVerify").Text!.Contains("Módulo NVIDIA pronto para o kernel 6.14.0-37-generic")) throw new InvalidOperationException("Verification missing.");
        if (!Find<SelectableTextBlock>(host, "NvLog").Text!.Contains("Simulação aceita")) throw new InvalidOperationException("Log missing.");
        if (!Find<TextBlock>(host, "NvState").Text!.Contains("reinicie")) throw new InvalidOperationException("Reboot state missing.");
        Click(host, "BtnNvReboot"); await Until(() => Find<TextBlock>(host, "NvRebootHelp").IsVisible);
        if (!Find<TextBlock>(host, "NvRebootHelp").Text!.Contains("não reinicia o computador sozinho")) throw new InvalidOperationException("Reboot button must only explain.");
        await Task.Delay(200); Capture(host, "nvdriver-repaired.png");
        host.Close();

        // A simulation that pulls another kernel (what ubuntu-drivers did) is refused before any confirmation.
        simText = hweSim; current = incident with { Packages = [], Kernels = [new KernelHeaders(running, false, KernelHeaders.RequiredFiles)] }; diagnoses = 0;
        Window? host2 = null; var fresh = new NvidiaDriverPage(services, () => host2, CancellationToken.None);
        host2 = new Window { Width = 900, Height = 1300, Background = Palette.Bg, Content = new ScrollViewer { Content = fresh } }; host2.Show();
        await Until(() => diagnoses == 1 && host2.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnNvInstall"));
        Click(host2, "BtnNvInstall");
        await Until(() => Find<TextBlock>(host2, "NvVerify").IsVisible);
        if (!Find<TextBlock>(host2, "NvVerify").Text!.Contains("recusado") || scripts.Count != 1 || desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible)) throw new InvalidOperationException("Kernel-pulling simulation must be refused.");
        Click(host2, "BtnNvScriptInstall"); await Until(() => Find<SelectableTextBlock>(host2, "NvScriptInstall").IsVisible);
        await Task.Delay(250); Capture(host2, "nvdriver-refused.png"); host2.Close();

        // Working (today's state of this machine) and the empty state.
        current = after with { Gpus = [gpu with { KernelDriver = "nvidia" }], LoadedVersion = "580.178.04", SmiVersion = "580.178.04", SmiInstalled = true }; diagnoses = 0;
        Window? host3 = null; var working = new NvidiaDriverPage(services, () => host3, CancellationToken.None);
        host3 = new Window { Width = 1000, Height = 1100, Background = Palette.Bg, Content = new ScrollViewer { Content = working } }; host3.Show();
        await Until(() => diagnoses == 1 && Find<TextBlock>(host3, "NvState").Text!.StartsWith("Funcionando · driver 580.178.04", StringComparison.Ordinal));
        if (!host3.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnNvRevert") || Find<Border>(host3, "NvRebootCard").IsVisible) throw new InvalidOperationException("Working state wrong.");
        await working.RefreshAsync();
        current = new NvidiaDiagnosis { OtherGpus = ["Intel UHD"], Distro = mint, RunningKernel = running };
        await working.RefreshAsync();
        await Until(() => Find<TextBlock>(host3, "NvEmpty").IsEffectivelyVisible && !Find<Border>(host3, "NvHero").IsVisible);
        host3.Close();
        if (scripts.Count != 1) throw new InvalidOperationException("Only the confirmed repair may reach the (fake) privileged runner.");
        Console.WriteLine("PASS nvidia driver tab: real incident detected, repair simulated (fake apt-get -s) and confirmed before the fake polkit runner, kernel-pulling plan refused, verification + reboot notice (never reboots), working and empty states, Drivers page links to the tab");
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
        if (Find<CheckBox>(host, "App_com_valvesoftware_Steam").IsEnabled) throw new InvalidOperationException("Flathub apps must be unavailable until Flathub exists.");
        if (!Find<CheckBox>(host, "App_gh_photocraft").IsEnabled) throw new InvalidOperationException("ArtCraft apps (distribution packages) must not need Flathub.");
        Click(host, "BtnAppsFlathub"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => flathub && Find<CheckBox>(host, "App_com_valvesoftware_Steam").IsEnabled);
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
    /// <summary>Opt-in (ILMENYX_CONTRAST_AUDIT=1): measures the WCAG contrast of every visible text of every shell page in both themes, against the pixels really rendered behind it, and writes contrast.tsv.</summary>
    private static async Task ContrastAuditAsync()
    {
        static double Lum(double r, double g, double b) { static double C(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); } return 0.2126 * C(r) + 0.7152 * C(g) + 0.0722 * C(b); }
        var rows = new List<string> { "theme\tpage\tratio\tsize\tbold\tfg\tbg\tname\ttext" }; var seen = new HashSet<string>();
        var shell = new ShellWindow { Width = 1280, Height = 820 }; shell.Show(); await Task.Delay(1500);
        var keys = shell.GetVisualDescendants().OfType<Button>().Where(b => b.Name is { Length: > 3 } n && n.StartsWith("Nav", StringComparison.Ordinal)).Select(b => b.Name![3..]).ToList();
        foreach (string theme in new[] { "escuro", "claro" })
        {
            if ((theme == "claro") != Palette.IsLight) { Click(shell, "ToggleTheme"); await Task.Delay(500); }
            foreach (string key in keys)
            {
                try { shell.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Nav" + key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } catch (InvalidOperationException) { rows.Add($"{theme}\t{key}\t0\t0\t0\t\t\tNAV-ERROR\t"); continue; }
                await Task.Delay(1200);
                var scroller = shell.GetVisualDescendants().OfType<ScrollViewer>().Where(v => v.IsEffectivelyVisible && v.Bounds.Width > 400).OrderByDescending(v => v.Extent.Height - v.Viewport.Height).FirstOrDefault();
                double maxOffset = scroller is null ? 0 : Math.Min(Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height), 12000), step = Math.Max(200, (scroller?.Viewport.Height ?? 800) * 0.8);
                for (double y = 0; ; y += step)
                {
                    if (scroller is not null) { scroller.Offset = new Vector(0, Math.Min(y, maxOffset)); await Task.Delay(180); }
                    Capture(shell, $"audit-{theme}-{key}-{(int)(y / step)}.png");
                    int w = (int)shell.Bounds.Width, h = (int)shell.Bounds.Height;
                    using var image = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96)); image.Render(shell);
                    var px = new byte[w * h * 4]; var pin = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try { image.CopyPixels(new PixelRect(0, 0, w, h), pin.AddrOfPinnedObject(), px.Length, w * 4); } finally { pin.Free(); }
                    foreach (var tb in shell.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text) && t.Foreground is Avalonia.Media.ISolidColorBrush))
                    {
                        if (tb.TranslatePoint(new Point(0, 0), shell) is not { } at) continue;
                        int x0 = (int)at.X, y0 = (int)at.Y, x1 = (int)(at.X + tb.Bounds.Width), y1 = (int)(at.Y + tb.Bounds.Height);
                        if (x0 < 0 || y0 < 0 || x1 > w || y1 > h || x1 - x0 < 4 || y1 - y0 < 4) continue;
                        if (scroller is not null && scroller.GetVisualDescendants().Contains(tb) && scroller.TranslatePoint(new Point(0, 0), shell) is { } so && (y0 < so.Y || y1 > so.Y + scroller.Bounds.Height)) continue;
                        var hist = new Dictionary<int, int>();
                        for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { int i = (yy * w + xx) * 4; int q = (px[i] >> 2) | ((px[i + 1] >> 2) << 6) | ((px[i + 2] >> 2) << 12); hist[q] = hist.GetValueOrDefault(q) + 1; }
                        int mode = hist.MaxBy(kv => kv.Value).Key; double bb = (mode & 63) * 4 + 2, bg = ((mode >> 6) & 63) * 4 + 2, br = ((mode >> 12) & 63) * 4 + 2;
                        var c = ((Avalonia.Media.ISolidColorBrush)tb.Foreground!).Color; double op = c.A / 255.0 * ((Avalonia.Media.ISolidColorBrush)tb.Foreground!).Opacity;
                        for (Visual? v = tb; v is not null; v = v.GetVisualParent()) op *= v.Opacity;
                        double fr = c.R * op + br * (1 - op), fg = c.G * op + bg * (1 - op), fb = c.B * op + bb * (1 - op);
                        double l1 = Lum(fr, fg, fb), l2 = Lum(br, bg, bb), ratio = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
                        string text = tb.Text!.ReplaceLineEndings(" "); if (text.Length > 70) text = text[..70];
                        if (!seen.Add(theme + key + (tb.Name ?? "") + text)) continue;
                        rows.Add(string.Join('\t', theme, key, ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), tb.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture), ((int)tb.FontWeight >= 600) ? "1" : "0", $"#{c.R:X2}{c.G:X2}{c.B:X2}", $"#{(int)br:X2}{(int)bg:X2}{(int)bb:X2}", tb.Name ?? "", text));
                    }
                    if (y >= maxOffset) break;
                }
            }
        }
        File.WriteAllLines(Path.Combine(Program.OutputDirectory, "contrast.tsv"), rows);
        Console.WriteLine("AUDIT " + Path.Combine(Program.OutputDirectory, "contrast.tsv") + " rows " + rows.Count);
        shell.Close();
    }
    /// <summary>Opt-in (ILMENYX_PERF_AUDIT=1): what Ilmenyx itself costs. Real shell and providers; idle on the first page, minimized,
    /// then each page for ILMENYX_PERF_SECONDS (default 10). CPU includes reaped child processes (smartctl, nvidia-smi...). Writes perf.tsv.</summary>
    private static async Task PerfAuditAsync()
    {
        int seconds = int.TryParse(Environment.GetEnvironmentVariable("ILMENYX_PERF_SECONDS"), out int n) && n > 0 ? n : 10;
        long ticks = 100; // USER_HZ on Linux
        static (double Cpu, long Rss, int Threads) Sample()
        {
            var f = File.ReadAllText("/proc/self/stat"); var parts = f[(f.LastIndexOf(')') + 2)..].Split(' ');
            double cpu = long.Parse(parts[11]) + long.Parse(parts[12]) + long.Parse(parts[13]) + long.Parse(parts[14]);
            long rss = long.Parse(parts[21]) * Environment.SystemPageSize;
            return (cpu, rss, int.Parse(parts[17]));
        }
        var rows = new List<string> { "fase	cpu_pct	rss_mb	pico_rss_mb	threads	alocado_mb	gc0	gc1	gc2	filhos_cpu_pct" };
        async Task Measure(string phase, int secs)
        {
            var (c0, _, _) = Sample(); var f0 = File.ReadAllText("/proc/self/stat"); long a0 = GC.GetTotalAllocatedBytes(); int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            double child0 = ChildTicks(f0); long peak = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < secs) { await Task.Delay(250); peak = Math.Max(peak, Sample().Rss); }
            var (c1, rss, threads) = Sample(); double child1 = ChildTicks(File.ReadAllText("/proc/self/stat")); double wall = sw.Elapsed.TotalSeconds;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            rows.Add(string.Join('\t', phase, ((c1 - c0) / ticks / wall * 100).ToString("0.0", inv), (rss / 1048576.0).ToString("0", inv), (peak / 1048576.0).ToString("0", inv), threads,
                ((GC.GetTotalAllocatedBytes() - a0) / 1048576.0).ToString("0.0", inv), GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2,
                ((child1 - child0) / ticks / wall * 100).ToString("0.0", inv)));
            Console.WriteLine("PERF " + rows[^1]);
        }
        static double ChildTicks(string f) { var p = f[(f.LastIndexOf(')') + 2)..].Split(' '); return long.Parse(p[13]) + long.Parse(p[14]); }
        var shell = new ShellWindow { Width = 1280, Height = 820 }; shell.Show();
        await Measure("abertura (primeiros 5 s)", 5);
        await Measure("parado na primeira página", seconds * 2);
        shell.WindowState = WindowState.Minimized; await Task.Delay(1000);
        await Measure("minimizado", seconds * 2);
        shell.WindowState = WindowState.Normal; await Task.Delay(1000);
        var keys = shell.GetVisualDescendants().OfType<Button>().Where(b => b.Name is { Length: > 3 } k && k.StartsWith("Nav", StringComparison.Ordinal)).Select(b => b.Name![3..]).ToList();
        foreach (string key in keys)
        {
            try { shell.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Nav" + key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            catch (InvalidOperationException) { rows.Add(key + "\tNAV-ERROR"); continue; }
            await Measure(key + " (abrir, 2 s)", 2);
            await Measure(key, seconds);
        }
        await Measure("depois de todas as páginas, parado", seconds * 2);
        File.WriteAllLines(Path.Combine(Program.OutputDirectory, "perf.tsv"), rows);
        Console.WriteLine("PERF-AUDIT " + Path.Combine(Program.OutputDirectory, "perf.tsv"));
        shell.Close();
    }
    private static async Task ExerciseAsync(DashboardWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var output = Program.OutputDirectory;
        if (Environment.GetEnvironmentVariable("ILMENYX_CONTRAST_AUDIT") == "1") { await ContrastAuditAsync(); desktop.Shutdown(0); return; }
        if (Environment.GetEnvironmentVariable("ILMENYX_PERF_AUDIT") == "1") { await PerfAuditAsync(); desktop.Shutdown(0); return; }
        await MonitorAsync(desktop);
        await KeyboardAsync(window, desktop);
        await RequirementsAsync(window, desktop);
        if (window.Title != ProductIdentity.Name || Find<TextBlock>(window, "ProductName").Text != "Ilmenyx Controller" ||
            Find<TextBlock>(window, "MaintainerSignature").Text != "by Ilmenyx Technology")
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
        if (!Find<TextBox>(about, "LicenseTextContent").Text!.Contains("Uso como ferramenta em trabalho pago") || !Find<TextBox>(about, "LicenseTextContent").Text!.Contains("PolyForm Noncommercial License 1.0.0"))
            throw new InvalidOperationException("Own usage and commercialization terms missing from About.");
        selector.SelectedIndex = 0; await Task.Delay(60); Capture(about, "about.png");
        Click(about, "CloseAbout"); await Until(() => !about.IsVisible);
        Console.WriteLine("PASS Ilmenyx identity, by Ilmenyx Technology, live About/attribution/MIT/dependency documents and close");
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
        if (validationNotice.IsEffectivelyVisible != (ExpectedGpuNotice is not null) || validationNotice.Text != (ExpectedGpuNotice ?? ""))
            throw new InvalidOperationException("AMD/NVIDIA hardware validation notice wrong for this PC's GPUs.");
        Console.WriteLine("PASS validation notice for this PC's GPUs: " + (ExpectedGpuNotice ?? "none (no AMD/NVIDIA GPU)"));
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
    /// <summary>
    /// Estimativa de FPS chained scrolling (page, then sticky catalogue; up: catalogue first), back-to-top button, keyboard, at 900x560 and
    /// 1280x820 in both themes; plus screenshots of the Overlay and Relatório pages. Wheel and keys are routed events inside this process.
    /// </summary>
    private static async Task FpsScrollAndContrastAsync(ShellWindow shell)
    {
        double w0 = shell.Width, h0 = shell.Height; bool light0 = Palette.IsLight;
        var mouse = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), Avalonia.Input.PointerType.Mouse, true);
        foreach (var (w, h) in new[] { (900.0, 560.0), (1280.0, 820.0) })
        foreach (bool light in new[] { false, true })
        {
            if (Palette.IsLight != light) { Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight == light); }
            shell.Width = w; shell.Height = h;
            Find<Button>(shell, "Navfps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => shell.GetVisualDescendants().OfType<GamesFpsPage>().Any(p => p.IsVisible && p.Rows.Count > 20));
            var page = shell.GetVisualDescendants().OfType<GamesFpsPage>().Single();
            await Until(() => shell.GetVisualDescendants().OfType<Border>().Any(b => b.Name?.StartsWith("Game_", StringComparison.Ordinal) == true && b.IsEffectivelyVisible));
            page.ScrollToTop(); await Until(() => page.PageOffset == 0 && page.CatalogOffset == 0); await Task.Delay(400);
            string tag = $"{w:0}x{h:0}-{(light ? "light" : "dark")}";
            var scroll = Find<ScrollViewer>(shell, "FpsScroll"); var header = Find<Grid>(shell, "FpsCatalogHeader");
            var cardsScroll = Find<ScrollViewer>(shell, "GameCardsScroll");
            if (page.ToTopVisible) throw new InvalidOperationException("Back-to-top must be hidden at the top.");
            if (page.CatalogViewport < scroll.Viewport.Height - header.Bounds.Height - 45) throw new InvalidOperationException($"Catalogue must fill the window below its header ({tag}): {page.CatalogViewport:0} of {scroll.Viewport.Height:0}.");
            Capture(shell, $"fps-scroll-{tag}-top.png");
            var card = shell.GetVisualDescendants().OfType<Border>().First(b => b.Name?.StartsWith("Game_", StringComparison.Ordinal) == true && b.IsEffectivelyVisible);
            void Wheel(double notches) => card.RaiseEvent(new Avalonia.Input.PointerWheelEventArgs(card, mouse, shell, new Point(10, 10), 0, new Avalonia.Input.PointerPointProperties(), KeyModifiers.None, new Vector(0, notches)));
            double catalogHeight = cardsScroll.Bounds.Height, last = 0; int guard = 0;
            // Down over the catalogue: the page scrolls first (catalogue stays at its top) until the header reaches the top.
            while (page.PageOffset < page.PageMax - 0.5)
            {
                Wheel(-1); await Task.Delay(30);
                if (page.CatalogOffset != 0 && page.PageOffset < page.PageMax - 0.5) throw new InvalidOperationException($"Catalogue scrolled before the page reached the sticky point ({tag}).");
                if (page.PageOffset < last) throw new InvalidOperationException($"Page offset went backwards while scrolling down: jitter ({tag}).");
                last = page.PageOffset; if (++guard > 400) throw new InvalidOperationException("Page never reached the sticky point.");
            }
            double stickyY() => header.TranslatePoint(new Point(0, 0), scroll)!.Value.Y;
            double y0 = stickyY();
            if (Math.Abs(y0 - header.Margin.Top) > 2) throw new InvalidOperationException($"Catalogue header must sit at the top when sticky ({tag}): y={y0:0.0}.");
            for (int i = 0; i < 4; i++) { Wheel(-1); await Task.Delay(30); }
            if (page.CatalogOffset < 100 || Math.Abs(page.PageOffset - page.PageMax) > 0.5 || Math.Abs(stickyY() - y0) > 0.5) throw new InvalidOperationException($"Past the sticky point only the catalogue scrolls and the header stays fixed ({tag}).");
            if (Math.Abs(cardsScroll.Bounds.Height - catalogHeight) > 0.5) throw new InvalidOperationException("Catalogue height changed while scrolling (jitter).");
            await Until(() => page.ToTopVisible); await Task.Delay(250);
            Capture(shell, $"fps-scroll-{tag}-sticky.png");
            // Up: the catalogue returns to its own top first, only then the page above comes back.
            double pageMax = page.PageOffset; guard = 0;
            while (page.CatalogOffset > 0)
            {
                Wheel(1); await Task.Delay(30);
                if (page.CatalogOffset > 0 && page.PageOffset < pageMax - 0.5) throw new InvalidOperationException($"Page moved before the catalogue was back at its top ({tag}).");
                if (++guard > 400) throw new InvalidOperationException("Catalogue never returned to its top.");
            }
            Wheel(1); await Task.Delay(30);
            if (page.PageOffset >= pageMax - 1) throw new InvalidOperationException($"After the catalogue top, scrolling up must move the page ({tag}).");
            // Keyboard: PageDown/End/Home from the page (focus on the scroll area, as after a click on it).
            scroll.Focus(); await Task.Delay(30);
            double before = page.PageOffset; Key(shell, Avalonia.Input.Key.PageDown); await Task.Delay(30);
            if (page.PageOffset + page.CatalogOffset <= before) throw new InvalidOperationException("PageDown must scroll the page.");
            Key(shell, Avalonia.Input.Key.End); await Task.Delay(30);
            if (Math.Abs(page.PageOffset - page.PageMax) > 0.5 || Math.Abs(page.CatalogOffset - page.CatalogMax) > 0.5) throw new InvalidOperationException("End must reach the end of the catalogue.");
            Key(shell, Avalonia.Input.Key.Home); await Until(() => page.PageOffset == 0 && page.CatalogOffset == 0);
            // Back-to-top button: one click, animated, then hidden.
            Key(shell, Avalonia.Input.Key.End); await Until(() => page.ToTopVisible);
            Click(shell, "BtnFpsToTop"); await Task.Delay(60);
            if (page.PageOffset == 0 && page.CatalogOffset == 0) throw new InvalidOperationException("Back to top should animate (not jump) when feasible.");
            await Until(() => page.PageOffset == 0 && page.CatalogOffset == 0 && !page.ToTopVisible);
            if (w == 900)
            {
                foreach (var key in new[] { "overlay", "report" }) { Find<Button>(shell, "Nav" + key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(500); Capture(shell, $"page-{key}-{tag}.png"); }
                var reportTexts = shell.GetVisualDescendants().OfType<Border>().Where(b => b.Name is "ReportIncluded" or "ReportExcluded").SelectMany(b => b.GetVisualDescendants().OfType<TextBlock>()).ToList();
                if (reportTexts.Count < 4 || reportTexts.Any(t => t.Foreground != Palette.Fg && t.Foreground != Palette.Muted)) throw new InvalidOperationException("Report titles must use Fg and descriptions Muted (never Faint).");
            }
        }
        if (Palette.IsLight != light0) { Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight == light0); }
        shell.Width = w0; shell.Height = h0; await Task.Delay(200);
        Console.WriteLine("PASS fps sticky catalogue: page scrolls first, header sticks, catalogue fills the window and scrolls alone, up returns catalogue first; wheel + PageDown/End/Home; animated back-to-top hidden at top; 900x560 and 1280x820, dark and light; overlay/report screenshots; report text Fg/Muted");
    }

    private static async Task CompatTriageAsync(IClassicDesktopStyleApplicationLifetime desktop, string output)
    {
        // Conflicting options from similar-hardware reports → one conflict-free recommended line; artwork only after the online consent. Fake HTTP and Steam.
        static string R(int i, string options, string proton) =>
            "{\"app\":{\"steam\":{\"appId\":\"570\"},\"title\":\"Dota 2\"},\"responses\":{\"verdict\":\"yes\",\"customProtonVersion\":\"" + proton + "\",\"launchOptions\":" + JsonSerializer.Serialize(options)
            + "},\"timestamp\":" + (1793000000 - i) + ",\"systemInfo\":{\"gpu\":\"AMD Radeon 610M (radeonsi, raphael_mendocino)\",\"gpuDriver\":\"Mesa 25.0\",\"os\":\"Linux Mint 22.3\",\"kernel\":\"6.14\"}}";
        byte[] export = CompatExport(
            R(0, "DXVK_ASYNC=1 gamemoderun %command% -dx11", "GE-Proton10-29"), R(1, "DXVK_ASYNC=1 gamemoderun mangohud %command% -dx11", "GE-Proton10-29"),
            R(2, "PROTON_USE_WINED3D=1 RADV_PERFTEST=gpl %command% -dx12", "Proton 9.0-4"), R(3, "PROTON_ENABLE_NVAPI=1 RADV_PERFTEST=gpl gamemoderun %command% -dx12", "GE-Proton10-29"));
        byte[] png;
        using (var rtb = new RenderTargetBitmap(new PixelSize(960, 310), new Vector(96, 96)))
        {
            var art = new Border { Width = 960, Height = 310, Background = new Avalonia.Media.LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new Avalonia.Media.GradientStop(Avalonia.Media.Color.Parse("#B33A1E"), 0), new Avalonia.Media.GradientStop(Avalonia.Media.Color.Parse("#2B1638"), 0.6), new Avalonia.Media.GradientStop(Avalonia.Media.Color.Parse("#0E2A4A"), 1) } } };
            art.Measure(new Size(960, 310)); art.Arrange(new Rect(0, 0, 960, 310)); rtb.Render(art);
            using var ms = new MemoryStream(); rtb.Save(ms, PngBitmapEncoderOptions.Default); png = ms.ToArray();
        }
        var http = new CompatFakeHttp(req => req.RequestUri!.Host switch
        {
            "shared.akamai.steamstatic.com" => new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(png) { Headers = { ContentType = new("image/png") } } },
            "www.protondb.com" => new(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"tier\":\"platinum\",\"confidence\":\"strong\",\"score\":0.9,\"total\":5000}") },
            "store.steampowered.com" => new(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"success\":1,\"results\":{\"resolved_category\":3,\"resolved_items\":[]}}") },
            _ => new(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") },
        });
        string folder = Path.Combine(output, "compat-triage-fixture");
        var db = new CompatDatabase(Path.Combine(folder, "index"));
        await using (var s = new MemoryStream(export)) await CompatDatabase.BuildAsync(s, db.Folder, new CompatDumpInfo("fixture", export.Length, "", "", null));
        var steam = new Dictionary<string, string>(StringComparer.Ordinal) { ["570"] = "-novid" };
        var services = new CompatServices(db, new CompatOnline(folder, http), () => new CompatHardware("AMD Mendocino (iGPU · Radeon 610M)", "Linux Mint 22.3", false),
            _ => Task.FromResult<IReadOnlyList<LibGame>>([]), id => steam.GetValueOrDefault(id), (id, o) => { if (o.Length == 0) steam.Remove(id); else steam[id] = o; return null; }, (_, _) => Task.CompletedTask)
            { ToolInstalled = t => t == "gamemoderun" };
        Window? host = null; var page = new CompatPage(services, () => host, CancellationToken.None);
        host = new Window { Width = 900, Height = 560, Background = Palette.Bg, Content = new ScrollViewer { Content = page } }; host.Show();
        await page.ShowAsync("570", "Dota 2");
        await Until(() => host.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Name == "CompatRecommendedLine"));
        const string expected = "DXVK_ASYNC=1 RADV_PERFTEST=gpl gamemoderun %command% -dx11";
        if (Find<SelectableTextBlock>(host, "CompatRecommendedLine").Text != expected) throw new InvalidOperationException("Recommended line: " + Find<SelectableTextBlock>(host, "CompatRecommendedLine").Text);
        var alternatives = Find<StackPanel>(host, "CompatAlternatives").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        foreach (var must in new[] { "PROTON_USE_WINED3D=1", "PROTON_ENABLE_NVAPI=1", "mangohud", "-dx12", "Proton 9.0-4" })
            if (!alternatives.Any(a => a.StartsWith(must, StringComparison.Ordinal))) throw new InvalidOperationException("Missing alternative " + must + ": " + string.Join(" | ", alternatives));
        if (!Find<TextBlock>(host, "CompatRecommendedProton").Inlines!.OfType<Avalonia.Controls.Documents.Run>().Any(r => r.Text!.StartsWith("GE-Proton10-29", StringComparison.Ordinal))) throw new InvalidOperationException("Recommended Proton missing.");
        if (http.Hosts.Count != 0 || !host.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "CompatHeroPlaceholder")) throw new InvalidOperationException("Without consent: placeholder and no network.");
        ((ScrollViewer)host.Content!).Offset = new Vector(0, 0);
        await Task.Delay(250); Capture(host, "compat-triage-placeholder-dark.png");

        Click(host, "CompatApplyRecommended"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var ask = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible);
        if (!Find<TextBlock>(ask, "ConfirmText").Text!.Contains(expected) || !Find<TextBlock>(ask, "ConfirmText").Text!.Contains("-novid")) throw new InvalidOperationException("Confirmation must show the new and current options.");
        Click(ask, "ConfirmYes"); await Until(() => steam.GetValueOrDefault("570") == expected && Find<Button>(host, "CompatUndoRecommended").IsVisible);
        Click(host, "CompatUndoRecommended"); await Until(() => steam.GetValueOrDefault("570") == "-novid");

        Click(host, "CompatOnlineQuery"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var consent = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible);
        if (!Find<TextBlock>(consent, "ConfirmText").Text!.Contains("shared.akamai.steamstatic.com")) throw new InvalidOperationException("Consent must name the image host.");
        Click(consent, "ConfirmYes");
        await Until(() => Find<TextBlock>(host, "CompatTier").Text == "Platina" && !host.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "CompatHeroPlaceholder") && host.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "CompatHeroTagProtonDB"));
        if (http.Hosts.Any(h => !CompatOnline.Hosts.Contains(h)) || !http.Hosts.Contains("shared.akamai.steamstatic.com")) throw new InvalidOperationException("Hosts: " + string.Join(",", http.Hosts));
        if (services.Online.CachedArt("570") is null) throw new InvalidOperationException("Artwork must be cached on disk.");
        await Task.Delay(250); Capture(host, "compat-triage-dark.png");
        ((ScrollViewer)host.Content!).Offset = new Vector(0, Find<Border>(host, "CompatWorked").TranslatePoint(new Point(0, 0), page)!.Value.Y - 8); await Task.Delay(250); Capture(host, "compat-triage-recommended-dark.png");
        ((ScrollViewer)host.Content!).Offset = new Vector(0, Find<StackPanel>(host, "CompatAlternatives").TranslatePoint(new Point(0, 0), page)!.Value.Y - 200); await Task.Delay(250); Capture(host, "compat-triage-alternatives-dark.png");

        Palette.Apply(true);
        int calls = http.Hosts.Count;
        await page.ShowAsync("570", "Dota 2"); await Until(() => host.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Name == "CompatRecommendedLine"));
        if (host.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "CompatHeroPlaceholder")) throw new InvalidOperationException("Cached artwork must be reused.");
        if (http.Hosts.Count != calls) throw new InvalidOperationException("Cached artwork must not be downloaded again.");
        ((ScrollViewer)host.Content!).Offset = new Vector(0, 0); await Task.Delay(300); Capture(host, "compat-triage-light.png");
        ((ScrollViewer)host.Content!).Offset = new Vector(0, Find<Border>(host, "CompatWorked").TranslatePoint(new Point(0, 0), page)!.Value.Y - 8); await Task.Delay(250); Capture(host, "compat-triage-recommended-light.png");
        Palette.Apply(false);
        host.Close();
        Console.WriteLine("PASS compat triage: conflict-free recommended line (wined3d/NVAPI/second API/missing tool/second Proton moved to alternatives with reasons), apply/undo only that line, Steam artwork only after consent, cached and reused (fake HTTP; no network)");
    }

    /// <summary>Bloqueados without "não se aplica"/WinRAR, the precise AMD item, PeaZip in the apps catalogue, and a tour that reaches every sidebar page.</summary>
    private static async Task BlockedAndFullTourAsync(ShellWindow shell)
    {
        if (Ilmenyx.Core.BlockedCatalog.All.Any(f => f.Id is "windows-tweaks" or "usb-cleaner" or "winrar")) throw new InvalidOperationException("Items that do not exist on Linux must not be listed.");
        if (!Ilmenyx.Core.AppCatalog.Default().Any(a => a.FlatpakId == "io.github.peazip.PeaZip")) throw new InvalidOperationException("PeaZip must replace WinRAR in the apps catalogue.");
        double w = shell.Width, h = shell.Height;
        shell.Width = 900; shell.Height = 560; await Task.Delay(400);
        shell.Select("blocked"); await Task.Delay(300);
        var texts = Find<StackPanel>(shell, "BlockedList").GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        if (texts.Any(t => t.Contains("NÃO SE APLICA") || t.Contains("WinRAR"))) throw new InvalidOperationException("Blocked page still shows a not-applicable or WinRAR item.");
        var amd = Find<StackPanel>(shell, "BlockedDetails_gpu-amd");
        if (!amd.IsVisible) Click(shell, "BlockedWhy_gpu-amd");
        if (!amd.Children.OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("integrada foi validada") && (t.Text ?? "").Contains("dedicada não foi testada"))) throw new InvalidOperationException("AMD item must separate integrated (validated) from dedicated (untested).");
        await Task.Delay(300); Capture(shell, "blocked-dark-900x560.png");
        Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight); await Task.Delay(400); Capture(shell, "blocked-light-900x560.png");
        Click(shell, "ToggleTheme"); await Until(() => !Palette.IsLight);
        Click(shell, "BlockedWhy_gpu-amd");
        var tour = HelpContent.For().Tour; var covered = new HashSet<string>();
        shell.StartTour(); await Until(() => shell.TourStepIndex == 0);
        for (int i = 0; i < tour.Count; i++)
        {
            await Until(() => shell.TourStepIndex == i && shell.CurrentPage == tour[i].PageKey && Find<TextBlock>(shell, "TourTitle").Text == tour[i].Title);
            await Task.Delay(350);
            var card = Find<Border>(shell, "TourCard"); var at = card.TranslatePoint(default, shell)!.Value;
            if (at.Y < 0 || at.Y + card.Bounds.Height > shell.Bounds.Height + 0.5 || at.X + card.Bounds.Width > shell.Bounds.Width + 0.5) throw new InvalidOperationException($"Tour card outside the 900x560 window at step {tour[i].Id}.");
            if (!shell.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "TourRing" && b.IsEffectivelyVisible)) throw new InvalidOperationException("Tour step without a highlight: " + tour[i].Id);
            foreach (var key in tour[i].Highlights.Append(tour[i].PageKey)) covered.Add(key);
            if (tour[i].Id is "maker" or "tools" or "install" or "report")
            {
                Capture(shell, $"tour-{tour[i].Id}-dark.png");
                Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight); await Task.Delay(400); Capture(shell, $"tour-{tour[i].Id}-light.png");
                Click(shell, "ToggleTheme"); await Until(() => !Palette.IsLight); await Task.Delay(200);
            }
            if (i < tour.Count - 1) Click(shell, "TourNext");
        }
        var missing = shell.PageKeys.Where(k => !covered.Contains(k)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException("Tour does not cover: " + string.Join(", ", missing));
        shell.EndTour(); shell.Width = w; shell.Height = h; shell.Select("dash"); await Task.Delay(300);
        Console.WriteLine($"PASS blocked page and full tour: no not-applicable/WinRAR items, AMD integrated vs dedicated explained, PeaZip in the catalogue; {tour.Count} tour steps cover all {shell.PageKeys.Count} sidebar pages, card and highlight inside 900x560 in both themes");
    }

    /// <summary>Inicialização grouped by origin with plain advice, and the Drivers page by category (2026-11-07). Fixtures only for any click; real pages are only listed and captured.</summary>
    private static async Task StartupDriversRedesignAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        string root = Path.Combine(Program.OutputDirectory, "startup-groups-fixture"), cfg = Path.Combine(root, "cfg"), sys = Path.Combine(root, "sys");
        Directory.CreateDirectory(Path.Combine(cfg, "autostart")); Directory.CreateDirectory(sys);
        File.WriteAllText(Path.Combine(sys, "cinnamon-settings-daemon-power.desktop"), "[Desktop Entry]\nType=Application\nName=Cinnamon Settings Daemon - power\nExec=/bin/true\nOnlyShowIn=X-Cinnamon;\nX-GNOME-Autostart-Phase=Initialization\n");
        File.WriteAllText(Path.Combine(sys, "blueman.desktop"), "[Desktop Entry]\nType=Application\nName=Gerenciador Bluetooth\nExec=/bin/true\n");
        File.WriteAllText(Path.Combine(sys, "vendor-tray.desktop"), "[Desktop Entry]\nType=Application\nName=Vendor Tray\nComment=Ícone do fabricante\nExec=/bin/true\n");
        File.WriteAllText(Path.Combine(cfg, "autostart", "steam.desktop"), "[Desktop Entry]\nType=Application\nName=Steam\nExec=/bin/true -silent\n");
        var manager = new Ilmenyx.Platform.Linux.LinuxStartupManager(cfg, Path.Combine(root, "data"), [sys], null, null, "X-Cinnamon");
        Window? host = null;
        var page = new StartupPage(manager, () => host, CancellationToken.None);
        host = new Window { Width = 900, Height = 560, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => page.Entries.Count == 4);
        var sysGroup = Find<StackPanel>(host, "StartupGroupSystem"); var userGroup = Find<StackPanel>(host, "StartupGroupUser");
        bool In(StackPanel g, string id) => g.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "StartupToggle_" + id);
        if (!In(sysGroup, "cinnamon-settings-daemon-power.desktop") || !In(sysGroup, "blueman.desktop") || !In(userGroup, "steam.desktop") || !In(userGroup, "vendor-tray.desktop"))
            throw new InvalidOperationException("Startup entries are not grouped by system/user.");
        if (Find<TextBlock>(host, "StartupAdvice_cinnamon-settings-daemon-power.desktop").Text != "Necessário — não desative.") throw new InvalidOperationException("Required advice missing.");
        if (!Find<TextBlock>(host, "StartupAdvice_blueman.desktop").Text!.StartsWith("Pode desativar se não usa Bluetooth")) throw new InvalidOperationException("Optional advice missing.");
        if (Find<TextBlock>(host, "StartupAdvice_vendor-tray.desktop").Text != "Desconhecido: pesquise antes de desativar.") throw new InvalidOperationException("Unknown advice missing.");
        if (!Find<TextBlock>(host, "StartupAdvice_steam.desktop").Text!.StartsWith("Do usuário")) throw new InvalidOperationException("User advice missing.");
        if (host.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "StartupRemove_vendor-tray.desktop" || b.Name == "StartupRemove_cinnamon-settings-daemon-power.desktop")) throw new InvalidOperationException("System entries must not offer Remover.");
        var power = Find<CheckBox>(host, "StartupToggle_cinnamon-settings-daemon-power.desktop"); power.IsChecked = false; power.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var warn = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible);
        if (!Find<TextBlock>(warn, "ConfirmText").Text!.Contains("ATENÇÃO")) throw new InvalidOperationException("Required entry warning missing.");
        await Task.Delay(150); Capture(warn, "startup-warning.png");
        Click(warn, "ConfirmNo"); await Until(() => !warn.IsVisible && power.IsChecked == true);
        if (File.Exists(Path.Combine(cfg, "autostart", "cinnamon-settings-daemon-power.desktop"))) throw new InvalidOperationException("Declined warning still disabled the entry.");
        // Steam (the user's): no warning, disabled at once.
        var steam = Find<CheckBox>(host, "StartupToggle_steam.desktop"); steam.IsChecked = false; steam.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Find<TextBlock>(host, "StartupState_steam.desktop").Text == "DESATIVADO");
        if (desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible)) throw new InvalidOperationException("User entry must not ask.");
        host.Close();
        Console.WriteLine("PASS startup groups: system/desktop vs user programs, plain advice per entry, warning before disabling a required system entry (declined = untouched), user entry disabled without warning (fixtures)");

        // Drivers page: summary + six category cards; the folded technical list still has every row.
        Find<Button>(shell, "Navdriver").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.CurrentPage == "driver" && shell.GetLogicalDescendants().OfType<StackPanel>().Any(p => p.Name == "DrvList" && p.Children.Count > 0));
        var driverPage = shell.GetLogicalDescendants().OfType<DriverPage>().Single();
        if (driverPage.Cards.Count != 6 || Find<StackPanel>(shell, "DrvColumnA").Children.Count + Find<StackPanel>(shell, "DrvColumnB").Children.Count != 6) throw new InvalidOperationException("Driver categories missing.");
        if (driverPage.Cards.Sum(c => c.Devices.Count) != driverPage.Rows.Count) throw new InvalidOperationException("A device was lost between the inventory and the cards.");
        if (Find<TextBlock>(shell, "DrvHeadline").Text is not { Length: > 0 } head || head == "Ainda não verificado.") throw new InvalidOperationException("Driver summary empty.");
        foreach (var d in driverPage.Cards.SelectMany(c => c.Devices).Where(d => d.Action == DriverAction.OpenNvidiaTab))
            if (!shell.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "BtnDrvNvidiaTab")) throw new InvalidOperationException("NVIDIA device without its recommended action.");
        var sizes = new[] { (1280.0, 820.0), (900.0, 560.0) };
        foreach (var light in new[] { false, true })
        {
            // The real toggle recolours the whole shell (the choice goes to the fixture settings file; it is switched back below).
            if (light != Palette.IsLight) { Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight == light); }
            foreach (var (w, h) in sizes)
            {
                if (w == 900) await MinimumSize(shell); else { shell.Width = w; shell.Height = h; }
                foreach (var key in new[] { "startup", "driver" })
                {
                    Find<Button>(shell, "Nav" + key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => shell.CurrentPage == key); await Task.Delay(500);
                    string theme = light ? "light" : "dark";
                    Capture(shell, $"redesign-{key}-{theme}-{w:0}x{h:0}.png");
                    if (key == "driver")
                    {
                        foreach (var card in shell.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("DrvCat_", StringComparison.Ordinal) == true && b.IsEffectivelyVisible))
                        {
                            var at = card.TranslatePoint(new Point(0, 0), shell) ?? default;
                            if (at.X + card.Bounds.Width > shell.Bounds.Width + 1 || card.Bounds.Width < 240) throw new InvalidOperationException($"Driver card {card.Name} clipped or too narrow: {at}, {card.Bounds.Size}");
                        }
                        Find<Grid>(shell, "DrvCategories").BringIntoView(); await Task.Delay(400);
                        Capture(shell, $"redesign-driver-cards-{theme}-{w:0}x{h:0}.png");
                    }
                    else if (shell.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(p => p.Name == "StartupGroupUser") is { } users)
                    {
                        users.BringIntoView(); await Task.Delay(400);
                        Capture(shell, $"redesign-startup-user-{theme}-{w:0}x{h:0}.png");
                    }
                }
            }
        }
        if (Palette.IsLight) { Click(shell, "ToggleTheme"); await Until(() => !Palette.IsLight); }
        shell.Width = 1280; shell.Height = 820; await Task.Delay(300);
        Console.WriteLine("PASS drivers redesign: plain summary, six category cards with every inventory row, NVIDIA action present, no clipped card at 900x560/1280x820, dark and light captures");
    }

    /// <summary>Fake calibration benchmark: the smoke never runs real GPU/CPU load.</summary>
    private static Ilmenyx.Desktop.FpsCalibrator FakeCalibrator(Func<double?> temp, int delayMs = 400, int? hangMs = null) => new(async (progress, ct) =>
    {
        progress.Report(("Cena de GPU (fixture)", 0.3)); await Task.Delay(delayMs, ct);
        if (hangMs is int h) await Task.Delay(h, ct);
        progress.Report(("Simulação de CPU (fixture)", 0.8)); await Task.Delay(delayMs, ct);
        return new Ilmenyx.Core.FpsCalibrationMeasurement(172, 17.2, "GeForce 940MX (fixture)", 22.06, null);
    }, temp, () => null);

    private static async Task FpsCalibrationGateAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Dell 7460-like fixture (GeForce 940MX + i7-7500U, driver 580); three GPU-limited sessions at Alto feed the personal factor.
        string root = Path.Combine(Program.OutputDirectory, "fps-calibration-fixture"); Directory.CreateDirectory(root);
        string settingsPath = Path.Combine(root, "settings.json");
        var hw = ("Intel(R) Core(TM) i7-7500U CPU @ 2.70GHz", "NVIDIA Corporation GM108M [GeForce 940MX]");
        var ctx = new Ilmenyx.Core.FpsContext("NVIDIA Corporation GM108M [GeForce 940MX]", "10de", "134d", "nvidia", false, 2UL << 30, "Intel HD Graphics 620", false, 1, 1, "580.95.05", 1189);
        var fixture = new Ilmenyx.Core.IlmenyxSettings();
        foreach (var (game, fps) in new[] { ("Cyberpunk 2077", 3.0), ("Hogwarts Legacy", 3.0), ("Starfield", 2.0) })
            fixture.AddFpsSession(new(game, DateTimeOffset.UtcNow.AddDays(-1), fps, fps * 0.7, 900, "GeForce 940MX", "1920x1080", "Alto", "Native"));
        fixture.Save(settingsPath);
        Window? host = null;
        async Task<GamesFpsPage> Open(Ilmenyx.Desktop.FpsCalibrator cal, Func<Ilmenyx.Core.FpsContext> c)
        {
            host?.Close();
            var page = new GamesFpsPage(new Ilmenyx.Core.IlmenyxSettings(), settingsPath, () => hw, c, cal);
            host = new Window { Width = 1100, Height = 900, Content = page }; host.Show();
            await Until(() => page.Rows.Count > 20 && page.Rows.Any(r => r.Fps > 0) && host.GetVisualDescendants().OfType<Border>().Any(b => b.Name?.StartsWith("Game_", StringComparison.Ordinal) == true));
            return page;
        }
        TextBlock Basis(string name) => host!.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "Basis_" + LauncherPage.SafeName(name));
        // 1. Not calibrated: warning at the top, primary button, every estimate greyed and labelled provisional.
        var page = await Open(FakeCalibrator(() => 50), () => ctx);
        if (page.Gate != Ilmenyx.Core.FpsGate.Uncalibrated || !Find<TextBlock>(host!, "FpsGateTitle").Text!.StartsWith("As estimativas ainda não são confiáveis", StringComparison.Ordinal)) throw new InvalidOperationException("Uncalibrated gate must warn at the top.");
        if (Find<Button>(host!, "BtnFpsCalibrate").Content?.ToString() != "Calibrar agora (≈1 min)" || !Find<Button>(host!, "BtnFpsCalibrate").IsEffectivelyVisible) throw new InvalidOperationException("Calibrar agora button missing.");
        if (page.Rows.Where(r => !r.IsMeasured).Any(r => !r.IsProvisional) || Basis("League of Legends").Text != "provisório — calibre o PC") throw new InvalidOperationException("Estimates must be provisional before calibration.");
        await Task.Delay(300); Capture(host!, "fps-gate-uncalibrated.png");
        // 2. Too hot already: refused before any load. 3. Hot during the run: aborted at 90 °C, nothing saved.
        page = await Open(FakeCalibrator(() => 95), () => ctx);
        Click(host!, "BtnFpsCalibrate"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => page.CalibrationMessage?.Contains("95 °C", StringComparison.Ordinal) == true && page.Gate == Ilmenyx.Core.FpsGate.Uncalibrated);
        int ticks = 0;
        page = await Open(FakeCalibrator(() => ++ticks > 2 ? 91 : 60, 200, 20_000), () => ctx);
        Click(host!, "BtnFpsCalibrate"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => page.Gate == Ilmenyx.Core.FpsGate.Running); await Task.Delay(200); Capture(host!, "fps-gate-running.png");
        await Until(() => page.CalibrationMessage?.StartsWith("Calibração interrompida", StringComparison.Ordinal) == true && page.Gate == Ilmenyx.Core.FpsGate.Uncalibrated);
        if (Ilmenyx.Core.IlmenyxSettings.Load(settingsPath).FpsCalibration is not null) throw new InvalidOperationException("Aborted calibration must not be saved.");
        // 4. Calibrated with the fake runner: saved with the fingerprint, cards name their basis, personal factor from the 3 sessions.
        page = await Open(FakeCalibrator(() => 55), () => ctx);
        Click(host!, "BtnFpsCalibrate"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => page.Gate == Ilmenyx.Core.FpsGate.Calibrated);
        var saved = Ilmenyx.Core.IlmenyxSettings.Load(settingsPath);
        if (saved.FpsCalibration is not { SceneScore: 172, CpuSimFps: 22.06 } cal || !cal.Fingerprint.Contains("580.95.05", StringComparison.Ordinal) || saved.FpsSessions.Count != 3) throw new InvalidOperationException("Calibration must be saved with the driver fingerprint, keeping the sessions.");
        if (page.Basis != Ilmenyx.Core.FpsBasis.Calibrated || Math.Abs(page.GpuIndex - 3.2) > 0.01 || Math.Abs(page.CpuIndex - 36) > 0.1) throw new InvalidOperationException($"Calibrated indices expected (3,2 / 36), got {page.GpuIndex} / {page.CpuIndex}.");
        if (page.PersonalGpu is not { Sessions: 3 } || page.Rows.Where(r => !r.IsMeasured).Any(r => r.IsProvisional)) throw new InvalidOperationException("Personal factor from the 3 Alto sessions expected; no provisional card after calibration.");
        if (!Find<TextBlock>(host!, "FpsGateText").Text!.Contains("nota 172", StringComparison.Ordinal) || Find<TextBlock>(host!, "GameSourceTitle").Text != "Fonte: estimativa calibrada neste PC") throw new InvalidOperationException("Calibrated gate/source texts missing.");
        if (!page.Rows.Any(r => r.BasisText == "base: benchmark deste PC · suas 3 sessões medidas")) throw new InvalidOperationException("Card basis must name the PC benchmark and the sessions.");
        await Task.Delay(300); Capture(host!, "fps-gate-calibrated.png");
        // League of Legends on Low (maintainer's observation: > 100 FPS on this hardware; the old table said 16–44).
        foreach (var chip in host!.GetVisualDescendants().OfType<Button>().Where(b => b.Content?.ToString() == "Baixo")) chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => page.Rows.Single(r => r.Name == "League of Legends").Fps >= 100);
        if (Basis("League of Legends").Text != "base: benchmark deste PC") throw new InvalidOperationException("No Baixo session: the personal factor must not apply.");
        Console.WriteLine("LoL Baixo after calibration: " + page.Rows.Single(r => r.Name == "League of Legends").FpsText);
        // 5. Driver changed: stale gate asks for a new calibration and the estimates are provisional again. Light theme capture.
        bool light0 = Palette.IsLight; if (!light0) { Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight); }
        page = await Open(FakeCalibrator(() => 55), () => ctx with { GpuDriverVersion = "580.105.08" });
        if (page.Gate != Ilmenyx.Core.FpsGate.Stale || !Find<TextBlock>(host!, "FpsGateTitle").Text!.Contains("mudou", StringComparison.Ordinal) || page.Rows.Where(r => !r.IsMeasured).Any(r => !r.IsProvisional)) throw new InvalidOperationException("A driver change must make the calibration stale.");
        await Task.Delay(300); Capture(host!, "fps-gate-stale-light.png");
        // 6. Unknown GPU (not in the table) with a GpuSpecDb entry: spec fallback instead of SEM ESTIMATIVA.
        var rtxCtx = ctx with { GpuName = "NVIDIA AD104 [GeForce RTX 4070 Prototype]", GpuDeviceId = "2786", GpuMaxCoreMHz = null };
        hw = ("Intel(R) Core(TM) i7-7500U CPU @ 2.70GHz", "NVIDIA AD104 [Unknown board]");
        page = await Open(FakeCalibrator(() => 55), () => rtxCtx);
        if (page.Basis != Ilmenyx.Core.FpsBasis.Specs || page.Rows.Any(r => r.Source == "SEM ESTIMATIVA") || Math.Abs(page.GpuIndex - 100) > 10) throw new InvalidOperationException($"Spec fallback expected for an unknown name with a known PCI ID (index {page.GpuIndex}).");
        await Task.Delay(300); Capture(host!, "fps-gate-specs-light.png");
        host!.Close();
        if (Palette.IsLight != light0) { Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight == light0); }
        Console.WriteLine("PASS fps calibration gate: provisional greyed cards + Calibrar agora before calibration, hot PC refused, 90 °C abort saves nothing, fake calibration saved with driver fingerprint, basis per card, personal factor from 3 sessions at the same preset, LoL Baixo ≥ 100, stale after driver change, spec fallback for unknown GPU");
    }

    /// <summary>"Jogo não abre?" checklist and the ZRAM card of Otimiza (2026-11-08). Real pages are only read and captured; every click that would
    /// change something goes to fake services (no flatpak, pkexec or package manager is run).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task GameCheckAndZramAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        // 1. Real page, read-only.
        Find<Button>(shell, "Navgamecheck").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.CurrentPage == "gamecheck" && shell.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "GcStatus" && t.IsVisible && t.Text!.StartsWith("Verificado", StringComparison.Ordinal)));
        var real = shell.GetLogicalDescendants().OfType<GameCheckPage>().Single();
        if (real.Checks.Count < 4 || !real.Checks.Any(c => c.Id == "vulkan") || !real.Checks.Any(c => c.Id == "flathub")) throw new InvalidOperationException("Real checklist incomplete: " + string.Join(",", real.Checks.Select(c => c.Id)));
        await Task.Delay(300); Capture(shell, "gamecheck-real-dark.png");
        double w0 = shell.Width, h0 = shell.Height;
        await MinimumSize(shell); await Task.Delay(400); Capture(shell, "gamecheck-real-dark-900x560.png");
        Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight); await Task.Delay(400); Capture(shell, "gamecheck-real-light-900x560.png");
        Click(shell, "ToggleTheme"); await Until(() => !Palette.IsLight);
        shell.Width = w0; shell.Height = h0; await Task.Delay(200);

        // 2. Fixture checklist: Ubuntu 24.04 with AppArmor restriction, no Flathub, Steam Flatpak blind to /mnt/jogos, low max_map_count.
        var facts = new GameCheckFacts
        {
            Distro = DistroInfo.Parse("PRETTY_NAME=\"Ubuntu 24.04.1 LTS\"\nID=ubuntu\nID_LIKE=debian\n"), AppArmorRestrictUserns = 1, MaxUserNamespaces = 62769,
            FlatpakInstalled = true, SystemRemotes = [], UserRemotes = [], SteamFlatpak = true, SteamFilesystems = ["xdg-music:ro"], Home = "/home/ana",
            Drives = [new GameDrive("/mnt/jogos", true)], SteamNative = true, GpuDrivers = ["amdgpu"], IcdLibraries = ["libvulkan_radeon.so"], Lib32Present = ["libGL.so.1"],
            MaxMapCount = 65530, SplitLockMitigate = 1,
        };
        var fixes = new List<string>(); var opened = new List<string>(); int collects = 0;
        var services = new GameCheckServices(_ => { collects++; return Task.FromResult(facts); }, (args, _) => { fixes.Add(string.Join(' ', args)); return Task.FromResult((true, "Pronto.")); }, k => opened.Add(k));
        Window? host = null; var page = new GameCheckPage(services, () => host, CancellationToken.None);
        host = new Window { Width = 1100, Height = 1400, Background = Palette.Bg, Content = new ScrollViewer { Content = page } }; host.Show();
        await Until(() => collects == 1 && page.Checks.Count > 0 && host.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "GcCheck_apparmor"));
        if (!Find<Border>(host, "GcWarning_apparmor").IsVisible) throw new InvalidOperationException("AppArmor warning missing.");
        if (host.GetVisualDescendants().OfType<Button>().Any(b => b.Name?.StartsWith("GcFix_apparmor", StringComparison.Ordinal) == true || b.Name?.StartsWith("GcFix_lib32", StringComparison.Ordinal) == true || b.Name?.StartsWith("GcFix_splitlock", StringComparison.Ordinal) == true))
            throw new InvalidOperationException("Root/security fixes must never be one-click.");
        if (!Find<TextBox>(host, "GcCmd_apparmor_1").Text!.Contains("apparmor_parser -r")) throw new InvalidOperationException("AppArmor command missing.");
        Click(host, "GcFix_flathub_0"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var no = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible);
        if (!Find<TextBlock>(no, "ConfirmText").Text!.Contains("flatpak remote-add --user --if-not-exists flathub")) throw new InvalidOperationException("Flathub confirmation must show the command.");
        Click(no, "ConfirmNo"); await Until(() => !no.IsVisible); await Task.Delay(60);
        if (fixes.Count != 0) throw new InvalidOperationException("Declined fix still ran.");
        Click(host, "GcFix_flathub_0"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => fixes.Count == 1);
        if (fixes[0] != "remote-add --user --if-not-exists flathub " + GameLaunchChecks.FlathubRepoFile) throw new InvalidOperationException("Unexpected flathub fix: " + fixes[0]);
        Click(host, "GcFix_steamdrives_0"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => fixes.Count == 2 && Find<Button>(host, "GcUndo_steamdrives_0").IsVisible);
        Click(host, "GcUndo_steamdrives_0"); await Until(() => fixes.Count == 3);
        if (fixes[1] != "override --user --filesystem=/mnt/jogos " + GameLaunchChecks.SteamAppId || fixes[2] != "override --user --nofilesystem=/mnt/jogos " + GameLaunchChecks.SteamAppId) throw new InvalidOperationException("Unexpected Steam override fix/undo.");
        Click(host, "GcLink_maxmap"); await Until(() => opened.SequenceEqual(["optimize"]));
        await Task.Delay(250); Capture(host, "gamecheck-fixture-dark.png");
        Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight); await Task.Delay(400); Capture(host, "gamecheck-fixture-light.png");
        Click(shell, "ToggleTheme"); await Until(() => !Palette.IsLight);
        host.Close();

        // 3. ZRAM card with a fake probe and a fake privileged runner (Mint, 8 GiB, swapfile, no ZRAM).
        string swapfile = "Filename Type Size Used Priority\n/swapfile file 2097148 0 -2\n", zram = swapfile + "/dev/zram0 partition 4194300 0 100\n";
        var mint = DistroInfo.Parse("ID=linuxmint\nID_LIKE=\"ubuntu debian\"\nPRETTY_NAME=\"Linux Mint 22.3\"\n");
        var zf = new ZramFacts { Distro = mint, MemTotalBytes = 8L << 30, Swaps = ZramPlanner.ParseSwaps(swapfile) };
        var scripts = new List<string>(); int zcollects = 0;
        var zservices = new ZramServices(_ => { zcollects++; return Task.FromResult(zf); }, (script, line, _) =>
        {
            scripts.Add(script); line("== Ilmenyx (falso): " + script.Split('\n')[3]);
            zf = script.Contains("apt-get -y install zram-tools")
                ? zf with { Swaps = ZramPlanner.ParseSwaps(zram), ZramToolsInstalled = true, ZramswapDefaults = ZramPlanner.Marker + ": x\nALGO=zstd\nSIZE=4096\n", State = "package=zram-tools\n" }
                : zf with { Swaps = ZramPlanner.ParseSwaps(swapfile), ZramToolsInstalled = false, ZramswapDefaults = null, State = null };
            return Task.FromResult((true, "Pronto."));
        });
        Window? zhost = null; var panel = new ZramPanel(zservices, () => zhost, CancellationToken.None);
        zhost = new Window { Width = 900, Height = 900, Background = Palette.Bg, Content = new ScrollViewer { Content = UiKit.Card(panel) } }; zhost.Show();
        await Until(() => zcollects == 1 && panel.Plan?.State == ZramState.Recommended && zhost.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnZramApply"));
        if (!Find<TextBlock>(zhost, "ZramSummary").Text!.Contains("8 GiB")) throw new InvalidOperationException("ZRAM summary must name the RAM.");
        await Task.Delay(200); Capture(zhost, "zram-recommended-dark.png");
        Click(zhost, "BtnZramApply"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        var zc = desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible);
        if (!Find<TextBlock>(zc, "ConfirmText").Text!.Contains("zram-tools") || !Find<TextBlock>(zc, "ConfirmText").Text!.Contains("senha")) throw new InvalidOperationException("ZRAM confirmation must list the steps.");
        Click(zc, "ConfirmNo"); await Until(() => !zc.IsVisible); await Task.Delay(60);
        if (scripts.Count != 0) throw new InvalidOperationException("Declined ZRAM still ran.");
        Click(zhost, "BtnZramApply"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => scripts.Count == 1 && panel.Plan?.State == ZramState.ActiveOurs && Find<TextBlock>(zhost, "ZramResult").Text!.Contains("conferido"));
        if (!scripts[0].StartsWith("set -eu\n", StringComparison.Ordinal) || !scripts[0].Contains("SIZE=4096")) throw new InvalidOperationException("Unexpected ZRAM script.");
        if (!zhost.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "BtnZramUndoAndRemovePackage")) throw new InvalidOperationException("Undo with package removal missing.");
        await Task.Delay(200); Capture(zhost, "zram-active-ours-dark.png");
        Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight); await Task.Delay(400); Capture(zhost, "zram-active-ours-light.png");
        Click(shell, "ToggleTheme"); await Until(() => !Palette.IsLight);
        Click(zhost, "BtnZramUndoAndRemovePackage"); await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible));
        Click(desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible), "ConfirmYes");
        await Until(() => scripts.Count == 2 && panel.Plan?.State == ZramState.Recommended && Find<TextBlock>(zhost, "ZramResult").Text!.Contains("Desfeito"));
        if (!scripts[1].Contains("apt-get -y purge zram-tools")) throw new InvalidOperationException("Undo script must remove the package we installed.");
        zhost.Close();

        // 4. Real Otimiza page: the ZRAM card reads this machine (never applied here).
        Find<Button>(shell, "Navoptimize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => shell.CurrentPage == "optimize" && shell.GetLogicalDescendants().OfType<ZramPanel>().Any(z => z.Plan is not null));
        var realZram = shell.GetLogicalDescendants().OfType<ZramPanel>().Single();
        realZram.BringIntoView(); await Task.Delay(400); Capture(shell, "zram-real-dark.png");
        Console.WriteLine($"PASS game check and ZRAM: real checklist read-only ({real.Checks.Count} items, captured dark/light 900x560); fixture page shows AppArmor warning without one-click, user-level Flathub and Steam override only after confirmation (declined = nothing), undo, link to Otimiza; ZRAM recommended -> confirmed fake polkit run -> active with undo/remove package -> undone; real ZRAM card: {realZram.Plan!.State}");
    }

    /// <summary>Gaming extras (2026-11-08): bigger shader cache in Otimiza, gaming apps in Instalar apps, experimental Proton options in Jogos. Fixtures only:
    /// the real ~/.config, flatpak, Steam and games are never touched.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task GamingExtrasAsync(ShellWindow shell, IClassicDesktopStyleApplicationLifetime desktop)
    {
        string root = Path.Combine(Program.OutputDirectory, "gaming-extras-fixture"), proc = Path.Combine(root, "proc"), home = Path.Combine(root, "home");
        Directory.CreateDirectory(Path.Combine(proc, "self")); File.WriteAllText(Path.Combine(proc, "self", "status"), "Uid:\t1000\t1\t1\t1\n");
        Directory.CreateDirectory(Path.Combine(home, ".cache/mesa_shader_cache/ab")); File.WriteAllBytes(Path.Combine(home, ".cache/mesa_shader_cache/ab/1"), new byte[3 << 20]);
        async Task<ConfirmDialog> Confirm() { await Until(() => desktop.Windows.OfType<ConfirmDialog>().Any(w => w.IsVisible)); return desktop.Windows.OfType<ConfirmDialog>().Single(w => w.IsVisible); }
        async Task Answer(string button) { var d = await Confirm(); Click(d, button); await Until(() => !d.IsVisible); }
        async Task Themes(Window host, string stem, Control focus)
        {
            host.Width = 900; host.Height = 560; await Task.Delay(300); focus.BringIntoView(new Rect(0, 0, Math.Max(1, focus.Bounds.Width), 480)); await Task.Delay(300);
            Capture(host, stem + "-dark-900x560.png");
            Click(shell, "ToggleTheme"); await Until(() => Palette.IsLight); await Task.Delay(400); focus.BringIntoView(new Rect(0, 0, Math.Max(1, focus.Bounds.Width), 480)); await Task.Delay(200);
            Capture(host, stem + "-light-900x560.png");
            Click(shell, "ToggleTheme"); await Until(() => !Palette.IsLight); await Task.Delay(200);
        }

        // 1) Otimiza: detect → explain → confirm → write ~/.config/environment.d file → undo (toggle off) and RESTAURAR ORIGINAIS.
        var cache = new LinuxShaderCache(home, Path.Combine(home, ".config"), Path.Combine(home, ".cache"), _ => null);
        var optimizer = new LinuxOptimizer(proc, Path.Combine(root, "sys"), home, (_, _) => Task.FromResult((1, "")), (_, _) => Task.CompletedTask);
        var services = new OptimizeServices(_ => Task.FromResult<string?>(null), (_, _) => Task.FromResult(false)) { ShaderCache = cache, SteamRoot = () => null };
        using var usage = new UsageViewModel(new FakeUsage()); await usage.SampleOnceAsync(CancellationToken.None);
        Window? host = null; var opt = new OptimizePage(optimizer, services, usage, Path.Combine(root, "cfg/settings.json"), () => host, CancellationToken.None);
        host = new Window { Width = 1200, Height = 900, Content = new ScrollViewer { Content = opt } }; host.Show();
        await Until(() => host.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "Tweak_shadercache"));
        var toggle = Find<CheckBox>(host, "Tweak_shadercache");
        if (toggle.IsChecked == true || !toggle.IsEnabled || !Find<TextBlock>(host, "TweakStatus_shadercache").Text!.Contains("Mesa 3,0 MB")) throw new InvalidOperationException("Shader cache item must start off, adjustable, with the measured Mesa cache size.");
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var ask = await Confirm();
        if (!Find<TextBlock>(ask, "ConfirmText").Text!.Contains("próximo login") || Find<TextBlock>(ask, "ConfirmText").Text!.Contains("AMD_VULKAN_ICD")) throw new InvalidOperationException("Confirmation must explain the next login and never mention a driver switch.");
        Click(ask, "ConfirmNo"); await Until(() => !ask.IsVisible && toggle.IsChecked == false);
        if (File.Exists(cache.ConfigPath)) throw new InvalidOperationException("Declined confirmation still wrote the environment.d file.");
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Answer("ConfirmYes");
        await Until(() => File.Exists(cache.ConfigPath) && Find<TextBlock>(host, "TweakStatus_shadercache").Text!.Contains("próximo login"));
        if (File.ReadAllText(cache.ConfigPath) != ShaderCacheTweak.FileContent()) throw new InvalidOperationException("Unexpected environment.d content.");
        await Themes(host, "extras-optimize-shadercache", toggle);
        toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !File.Exists(cache.ConfigPath));
        await Until(() => toggle.IsEnabled); toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Answer("ConfirmYes");
        await Until(() => File.Exists(cache.ConfigPath));
        Click(host, "BtnTweaksRestore"); await Until(() => !File.Exists(cache.ConfigPath) && Find<TextBlock>(host, "OptResult").Text!.Contains("cache de shaders"));
        host.Close();
        Console.WriteLine("PASS shader cache: off by default with measured cache size, explained confirmation (next login, disk, no driver switch), declined = no file, user environment.d file written, toggle off and Restaurar originais delete it (fixture home)");

        // 2) Instalar apps: gaming apps with license/caveats; the Gamescope layer follows the Steam Flatpak runtime branch; link-only items open a page.
        var calls = new List<string>(); var opened = new List<string>();
        var installer = new LinuxAppInstaller((program, args, timeout, ct) =>
        {
            calls.Add(string.Join(" ", args));
            if (args[0] == "remotes") return Task.FromResult((0, "flathub\n"));
            if (args[0] == "info") return Task.FromResult((0, "org.freedesktop.Platform/x86_64/25.08\n"));
            return Task.FromResult((0, ""));
        }, _ => false);
        Window? appsHost = null; var apps = new AppsPage(installer, true, () => appsHost, CancellationToken.None, (url, _) => { opened.Add(url); return Task.CompletedTask; });
        appsHost = new Window { Width = 1000, Height = 900, Content = new ScrollViewer { Content = apps } }; appsHost.Show();
        await Until(() => apps.Apps.Single(a => a.RuntimeFor.Length > 0).Branch == "25.08" && appsHost.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "App_io_github_ilya_zlobintsev_LACT"));
        if (apps.Apps.Any(a => a.Category != "Essenciais" && a.Selected)) throw new InvalidOperationException("Gaming apps must not be pre-selected.");
        if (!Find<TextBlock>(appsHost, "AppCaveat_dev_lizardbyte_app_Sunshine").Text!.Contains("additional-install.sh")) throw new InvalidOperationException("Sunshine caveat missing.");
        if (appsHost.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "App_link_geforcenow").IsVisible) throw new InvalidOperationException("Link-only item must not offer a checkbox.");
        Click(appsHost, "AppLink_link_geforcenow"); await Until(() => opened.Count == 1);
        if (!opened[0].StartsWith("https://www.nvidia.com/", StringComparison.Ordinal)) throw new InvalidOperationException("GeForce NOW must open NVIDIA's own page.");
        Click(appsHost, "BtnAppsNone");
        Find<CheckBox>(appsHost, "App_io_github_ilya_zlobintsev_LACT").IsChecked = true; Find<CheckBox>(appsHost, "App_org_freedesktop_Platform_VulkanLayer_gamescope").IsChecked = true;
        Click(appsHost, "BtnAppsInstall"); var appsAsk = await Confirm();
        if (!Find<TextBlock>(appsAsk, "ConfirmText").Text!.Contains("lactd") || !Find<TextBlock>(appsAsk, "ConfirmText").Text!.Contains("//25.08")) throw new InvalidOperationException("Install confirmation must list caveats and the layer branch.");
        Click(appsAsk, "ConfirmYes");
        await Until(() => calls.Contains("install --user -y --noninteractive flathub org.freedesktop.Platform.VulkanLayer.gamescope//25.08") && calls.Contains("install --user -y --noninteractive flathub io.github.ilya_zlobintsev.LACT"));
        if (calls.Count(c => c.StartsWith("install", StringComparison.Ordinal)) != 2) throw new InvalidOperationException("Only the two selected items may be installed.");
        await Themes(appsHost, "extras-apps-gaming", appsHost.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "AppsGroup_Jogos"));
        appsHost.Close();
        Console.WriteLine("PASS gaming apps: LACT/ProtonPlus/Gamescope layer/Sunshine/Piper/Solaar with license and caveats, none pre-selected, layer installs the Steam Flatpak runtime branch (//25.08), GeForce NOW and input-remapper link-only (fake flatpak)");

        // 3) Jogos: experimental Proton options, off by default, confirmed, shown only when they apply, written to the Steam launch options.
        string steam = Path.Combine(home, ".local/share/Steam"), steamapps = Path.Combine(steam, "steamapps");
        Directory.CreateDirectory(Path.Combine(steamapps, "common/Game Two")); Directory.CreateDirectory(Path.Combine(steam, "userdata/1/config")); Directory.CreateDirectory(Path.Combine(steam, "config"));
        File.WriteAllText(Path.Combine(steamapps, "libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam + "\"\n\t}\n}\n");
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_200.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"200\"\n\t\"name\"\t\t\"Game Two\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"installdir\"\t\t\"Game Two\"\n}\n");
        File.WriteAllText(Path.Combine(steam, "config", "config.vdf"), "\"InstallConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"CompatToolMapping\"\n\t\t\t\t{\n\t\t\t\t\t\"200\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"name\"\t\t\"GE-Proton10-25\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n");
        string localConfig = Path.Combine(steam, "userdata/1/config/localconfig.vdf"); File.WriteAllText(localConfig, "\"UserLocalConfigStore\"\n{\n}\n");
        string settingsPath = Path.Combine(root, "cfg2/settings.json");
        Window? lhost = null;
        var launcher = new LauncherPage(new LinuxGameLibrary(home, (_, _) => Task.FromResult((0, "[]"))), new SteamInfo(home, Path.Combine(root, "noproc")), settingsPath,
            (g, p, v, status, learned, ct) => Task.FromResult<FpsSession?>(null), () => lhost, "NVIDIA", false, CancellationToken.None) { WaylandSession = () => true, UmuProton = () => "UMU-Proton-9.0-3" };
        lhost = new Window { Width = 1100, Height = 800, Content = launcher }; lhost.Show();
        await Until(() => launcher.Games.Count == 1);
        Click(lhost, "LGame_Steam_200");
        bool ExtrasShown() => lhost.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == "LExtrasBox" && p.IsEffectivelyVisible);
        await Until(ExtrasShown);
        var wl = Find<CheckBox>(lhost, "LChkWayland"); var ll = Find<CheckBox>(lhost, "LChkLowLat");
        if (wl.IsChecked == true || ll.IsChecked == true || Find<TextBlock>(lhost, "LPreview").Text!.Contains("PROTON_")) throw new InvalidOperationException("Experimental options must start off.");
        wl.IsChecked = true; var wlAsk = await Confirm();
        if (!Find<TextBlock>(wlAsk, "ConfirmText").Text!.Contains("experimental")) throw new InvalidOperationException("Wayland confirmation must explain it is experimental.");
        Click(wlAsk, "ConfirmNo"); await Until(() => !wlAsk.IsVisible && wl.IsChecked == false);
        if (IlmenyxSettings.Load(settingsPath).GameProfiles.GetValueOrDefault("Steam:200")?.ProtonWayland == true) throw new InvalidOperationException("Declined option was saved.");
        wl.IsChecked = true; await Answer("ConfirmYes");
        ll.IsChecked = true; await Answer("ConfirmYes");
        await Until(() => Find<TextBlock>(lhost, "LPreview").Text == "PROTON_ENABLE_WAYLAND=1 PROTON_ENABLE_NVAPI=1 %command%");
        if (IlmenyxSettings.Load(settingsPath).GameProfiles["Steam:200"] is not { ProtonWayland: true, LowLatency: true }) throw new InvalidOperationException("Experimental options not persisted.");
        Click(lhost, "BtnSteamApply");
        await Until(() => Find<TextBlock>(lhost, "LStatus").Text!.Contains("Opções gravadas na Steam"));
        if (!File.ReadAllText(localConfig).Contains("PROTON_ENABLE_WAYLAND=1 PROTON_ENABLE_NVAPI=1 %command%")) throw new InvalidOperationException("Steam launch options without the experimental options.");
        await Themes(lhost, "extras-launcher-steam", Find<StackPanel>(lhost, "LExtrasBox"));
        // A Windows game run through umu with an older UMU-Proton: the Wayland option is kept in the profile but explained as out of use.
        string exe = Path.Combine(root, "Jogo Win/game.exe"); Directory.CreateDirectory(Path.GetDirectoryName(exe)!); File.WriteAllText(exe, "MZ");
        lhost.Width = 1100; lhost.Height = 800;
        Click(lhost, "BtnAddExe"); Find<TextBox>(lhost, "LAddPath").Text = exe; Click(lhost, "BtnAddExeConfirm");
        await Until(() => launcher.Games.Count == 2 && Find<TextBlock>(lhost, "LTitle").Text != "Game Two" && ExtrasShown());
        Find<CheckBox>(lhost, "LChkWayland").IsChecked = true; await Answer("ConfirmYes");
        await Until(() => Find<TextBlock>(lhost, "LExtrasNote").Text!.Contains("UMU-Proton-9.0-3"));
        await Themes(lhost, "extras-launcher-umu", Find<StackPanel>(lhost, "LExtrasBox"));
        lhost.Close();
        Console.WriteLine("PASS experimental Proton options: off by default, explained confirmation (declined = not saved), Wayland + low latency in the Steam launch options with GE-Proton 10 + NVIDIA, out-of-use reason shown for UMU-Proton 9 (fixtures)");
    }
}
