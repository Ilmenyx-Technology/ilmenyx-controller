// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
// Welcome tour (coach marks over the shell) and the way into the Help Center. Texts: Ilmenyx.Core.HelpContent.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

public sealed partial class ShellWindow
{
    private readonly HelpLanguage help = HelpContent.For();
    private readonly Canvas tourLayer = new() { Name = "TourLayer", IsVisible = false };
    private readonly Dictionary<string, Action> navExpand = new();
    private Border? tourCard, tourArrow;
    private TextBlock? tourCounter, tourTitle, tourBody;
    private Button? tourBack, tourNext;
    private IInputElement? tourReturnFocus;
    private HelpCenterPage? helpPage;
    private Control? searchHost, sensorFixButton, sideReadout;
    private Button? themeButton, pageHelpButton;
    private ScrollViewer? navScroll;

    /// <summary>Index of the tour step on screen, or -1 when the tour is closed.</summary>
    public int TourStepIndex { get; private set; } = -1;

    /// <summary>Page keys registered in the shell (each one has a Help Center section).</summary>
    public IReadOnlyCollection<string> PageKeys => pages.Keys;

    /// <summary>Automated runs (ILMENYX_NO_ONBOARDING=1, as the UI smoke sets) and ILMENYX_NO_TOUR=1 never get the welcome dialog by themselves.</summary>
    public static bool TourSuppressed => Environment.GetEnvironmentVariable("ILMENYX_NO_ONBOARDING") == "1" || Environment.GetEnvironmentVariable("ILMENYX_NO_TOUR") == "1";

    private Control HelpPage() => helpPage = new HelpCenterPage(help, Select, StartTour);

    /// <summary>Opens the Help Center at the section of <paramref name="key"/> (a page key; the header "?" passes the current page).</summary>
    public void OpenHelp(string key)
    {
        Select("help");
        helpPage?.ShowSection(key);
    }

    /// <summary>First run: the permissions screen first (when something is blocked), then the welcome dialog that offers the tour.</summary>
    private async Task FirstRunAsync()
    {
        var permissions = OfferPermissionsOnce();
        if (appSettings.TourOffered || TourSuppressed) return;
        try
        {
            // The permissions screen appears after the first reading (about a second); give up on the welcome for this session if it never settles.
            if (await Task.WhenAny(permissions, Task.Delay(TimeSpan.FromSeconds(15), stop.Token)) != permissions) return;
            await Task.Delay(400, stop.Token);
        }
        catch (OperationCanceledException) { return; }
        if (IsVisible && !stop.IsCancellationRequested) await OfferTourAsync();
    }

    /// <summary>Shows the welcome dialog once (settings TourOffered, saved before it opens); <paramref name="force"/> ignores the flag and the variables.
    /// Returns true when the user started the tour.</summary>
    public async Task<bool> OfferTourAsync(bool force = false)
    {
        if (!force && (appSettings.TourOffered || TourSuppressed)) return false;
        try { IlmenyxSettings.Update(settingsPath, s => s.TourOffered = true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        appSettings.TourOffered = true;
        var dialog = new WelcomeTourDialog(help);
        await dialog.ShowDialog(this);
        if (!dialog.StartTour) return false;
        StartTour();
        return true;
    }

    public void StartTour()
    {
        if (help.Tour.Count == 0) return;
        if (tourCard is null) BuildTourCard();
        tourReturnFocus = FocusManager?.GetFocusedElement();
        tourLayer.IsVisible = true;
        ShowStep(0);
    }

    public void NextTourStep() { if (TourStepIndex < 0) return; if (TourStepIndex >= help.Tour.Count - 1) EndTour(); else ShowStep(TourStepIndex + 1); }

    public void PreviousTourStep() { if (TourStepIndex > 0) ShowStep(TourStepIndex - 1); }

    public void EndTour()
    {
        if (TourStepIndex < 0) return;
        TourStepIndex = -1;
        tourLayer.IsVisible = false;
        tourLayer.Children.RemoveAll(tourLayer.Children.Where(c => c.Name == "TourRing").ToList());
        if (tourReturnFocus is Control { IsEffectivelyVisible: true } back) back.Focus();
        else if (nav.TryGetValue(CurrentPage, out var b)) b.Focus();
    }

    private void BuildTourCard()
    {
        tourCounter = new TextBlock { Name = "TourCounter", FontSize = 11, Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = help[HelpKeys.TourName], FontSize = 10.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = Palette.AccentText, VerticalAlignment = VerticalAlignment.Center };
        tourTitle = new TextBlock { Name = "TourTitle", FontSize = 17, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 6) };
        tourBody = new TextBlock { Name = "TourBody", FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Foreground = Palette.Fg };
        AutomationProperties.SetLiveSetting(tourBody, AutomationLiveSetting.Polite);
        var skip = new Button { Name = "TourSkip", Content = help[HelpKeys.TourSkip], Background = Brushes.Transparent, Foreground = Palette.Muted, FontSize = 12, Padding = new Thickness(6, 7), VerticalAlignment = VerticalAlignment.Center };
        tourBack = new Button { Name = "TourBack", Content = help[HelpKeys.TourBack], FontSize = 12, Padding = new Thickness(14, 7) };
        tourNext = UiKit.Primary("TourNext", help[HelpKeys.TourNext], 18, 7);
        skip.Click += (_, _) => EndTour();
        tourBack.Click += (_, _) => PreviousTourStep();
        tourNext.Click += (_, _) => NextTourStep();
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { tourBack, tourNext } };
        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0), Children = { right, skip } };
        DockPanel.SetDock(right, Dock.Right);
        var keys = new TextBlock { Text = help[HelpKeys.TourKeys], FontSize = 10.5, Foreground = Palette.Faint, Margin = new Thickness(0, 8, 0, 0) };
        var top = new Grid { Children = { name, tourCounter } };
        tourCard = new Border
        {
            Name = "TourCard", Width = 372, Background = Palette.Panel, BorderBrush = Palette.Accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(IlmenyxTheme.CardRadius),
            Padding = new Thickness(20, 16, 16, 14), BoxShadow = IlmenyxTheme.SoftShadow, Child = new StackPanel { Children = { top, tourTitle, tourBody, buttons, keys } },
        };
        tourCard.ActualThemeVariantChanged += (_, _) => tourCard.BoxShadow = IlmenyxTheme.SoftShadow;
        KeyboardNavigation.SetTabNavigation(tourCard, KeyboardNavigationMode.Cycle);
        tourArrow = new Border { Width = 14, Height = 14, Background = Palette.Panel, BorderBrush = Palette.Accent, BorderThickness = new Thickness(1, 0, 0, 1), RenderTransform = new RotateTransform(45), IsHitTestVisible = false };
        tourLayer.Children.Add(tourArrow); tourLayer.Children.Add(tourCard);
        AddHandler(KeyDownEvent, OnTourKey, RoutingStrategies.Tunnel);
        tourLayer.SizeChanged += (_, _) => PlaceTour();
        if (navScroll is not null) navScroll.ScrollChanged += (_, _) => PlaceTour();
    }

    private void OnTourKey(object? sender, KeyEventArgs e)
    {
        if (TourStepIndex < 0 || e.KeyModifiers != KeyModifiers.None) return;
        var focused = FocusManager?.GetFocusedElement() as Control;
        bool inCard = focused is not null && tourCard is not null && (ReferenceEquals(focused, tourCard) || tourCard.IsVisualAncestorOf(focused));
        switch (e.Key)
        {
            case Key.Escape: EndTour(); e.Handled = true; break;
            case Key.Enter when focused is not TextBox: NextTourStep(); e.Handled = true; break;
            case Key.Right when inCard: NextTourStep(); e.Handled = true; break;
            case Key.Left when inCard: PreviousTourStep(); e.Handled = true; break;
        }
    }

    private void ShowStep(int i)
    {
        var step = help.Tour[i];
        TourStepIndex = i;
        if (pages.ContainsKey(step.PageKey) && CurrentPage != step.PageKey) Select(step.PageKey);
        tourCounter!.Text = help.Format(HelpKeys.TourCounter, i + 1, help.Tour.Count);
        tourTitle!.Text = step.Title; tourBody!.Text = step.Body;
        tourBack!.IsEnabled = i > 0;
        tourNext!.Content = i == help.Tour.Count - 1 ? help[HelpKeys.TourFinish] : help[HelpKeys.TourNext];
        AutomationProperties.SetName(tourCard!, help.Format(HelpKeys.TourAccessible, i + 1, help.Tour.Count, step.Title));
        foreach (var h in step.Highlights) if (navExpand.TryGetValue(h, out var expand)) expand();
        foreach (var target in TourTargets(step)) target.BringIntoView();
        PlaceTour();
        Dispatcher.UIThread.Post(PlaceTour, DispatcherPriority.Background);
        tourNext.Focus(NavigationMethod.Tab);
    }

    /// <summary>Controls a step points at: sidebar entries, header controls or the sensor button (its readout card while it is hidden).</summary>
    private List<Control> TourTargets(TourStep step)
    {
        var list = new List<Control>();
        foreach (var h in step.Highlights)
        {
            Control? c = h switch
            {
                "search" => searchHost,
                "theme" => themeButton,
                "pagehelp" => pageHelpButton,
                "sensorfix" => sensorFixButton is { IsVisible: true } ? sensorFixButton : sideReadout,
                _ => nav.GetValueOrDefault(h),
            };
            if (c is not null) list.Add(c);
        }
        return list;
    }

    private void PlaceTour()
    {
        if (TourStepIndex < 0 || tourCard is null || tourArrow is null || Content is not Control root) return;
        tourLayer.Children.RemoveAll(tourLayer.Children.Where(c => c.Name == "TourRing").ToList());
        var step = help.Tour[TourStepIndex];
        var rects = new List<(Rect Rect, bool Side)>();
        foreach (var target in TourTargets(step))
        {
            if (!target.IsEffectivelyVisible || target.TranslatePoint(default, root) is not { } p) continue;
            bool side = (target is Button b && nav.ContainsValue(b)) || ReferenceEquals(target, sensorFixButton) || ReferenceEquals(target, sideReadout);
            rects.Add((new Rect(p, target.Bounds.Size), side));
        }
        // Consecutive sidebar entries become one outline (a whole group), header controls keep their own.
        var merged = new List<(Rect Rect, bool Side)>();
        foreach (var r in rects.OrderBy(r => r.Side ? 0 : 1).ThenBy(r => r.Rect.Y))
        {
            if (merged.Count > 0 && merged[^1].Side && r.Side && r.Rect.Y - merged[^1].Rect.Bottom < 8) merged[^1] = (merged[^1].Rect.Union(r.Rect), true);
            else merged.Add(r);
        }
        foreach (var (rect, _) in merged)
        {
            var ring = new Border { Name = "TourRing", Width = rect.Width + 8, Height = rect.Height + 8, BorderBrush = Palette.Accent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(IlmenyxTheme.ControlRadius + 3), IsHitTestVisible = false };
            Canvas.SetLeft(ring, rect.X - 4); Canvas.SetTop(ring, rect.Y - 4);
            tourLayer.Children.Insert(0, ring);
        }
        tourCard.Measure(new Size(tourCard.Width, double.PositiveInfinity));
        double h = tourCard.DesiredSize.Height, height = root.Bounds.Height, left = 214 + 16;
        var anchor = merged.FirstOrDefault(m => m.Side);
        double top;
        if (merged.Count > 0 && anchor.Side)
        {
            double y = Math.Clamp(anchor.Rect.Y + Math.Min(anchor.Rect.Height, 36) / 2, 20, Math.Max(20, height - 20));
            top = Math.Clamp(y - 34, 12, Math.Max(12, height - h - 12));
            tourArrow.IsVisible = true; Canvas.SetLeft(tourArrow, left - 6); Canvas.SetTop(tourArrow, Math.Clamp(y - 7, top + 10, top + h - 24));
        }
        else
        {
            // Header targets: the card sits just below the header, without an arrow.
            top = merged.Count > 0 ? merged.Max(m => m.Rect.Bottom) + 14 : 80;
            tourArrow.IsVisible = false;
        }
        Canvas.SetLeft(tourCard, left); Canvas.SetTop(tourCard, top);
    }
}
