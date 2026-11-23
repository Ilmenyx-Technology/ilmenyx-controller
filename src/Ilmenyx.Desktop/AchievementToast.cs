// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>
/// Achievement toast drawn by Ilmenyx itself: a borderless, non-focusable window that stays above a fullscreen game. Only on the X11 backend
/// (also XWayland), where the window type CRITICAL_NOTIFICATION (KDE) / NOTIFICATION is set before the window is mapped so KWin keeps it above fullscreen windows.
/// <see cref="TryShow"/> queues them and returns false elsewhere (native Wayland, errors) and the caller falls back to the system notification. UI thread only.
/// </summary>
public sealed class AchievementToast : Window
{
    private const double CardW = 380, CardH = 100, Slide = 24, VisibleMs = 6000;
    private readonly Border card = new(), shine = new(), iconFrame = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Top-left of the toast in physical pixels: top-right of the working area with a margin in DIP, scaling-aware. Pure, so it is unit tested.</summary>
    public static PixelPoint ToastPosition(PixelRect workingArea, double scaling, double widthDip, double marginDip = 24) =>
        new(workingArea.Right - (int)Math.Round((widthDip + marginDip) * scaling), workingArea.Y + (int)Math.Round(marginDip * scaling));

    private sealed record Pending(string Title, string Description, string Game, string? IconPath, Action? Shown);
    private static readonly ToastQueue<Pending> queue = new();

    /// <summary>
    /// Shows the toast now, or after the ones already on screen (one at a time, <see cref="VisibleMs"/> each). <paramref name="shown"/> runs when this toast
    /// actually appears (the sound goes there: one per toast). False when no toast can be drawn here; the caller then uses the system notification.
    /// </summary>
    public static bool TryShow(string title, string description, string game, string? iconPath, Action? shown = null)
    {
        if (!OperatingSystem.IsLinux()) return false;
        var item = new Pending(title, description, game, iconPath, shown);
        if (!queue.Offer(item)) return true;
        if (Display(item)) return true;
        Next(); // nothing can be drawn: free the queue
        return false;
    }

    private static void Next()
    {
        while (queue.TryNext(out var item)) if (Display(item)) return;
    }

    private static bool Display(Pending p)
    {
        AchievementToast? toast = null;
        try
        {
            toast = new AchievementToast(p.Title, p.Description, p.Game, p.IconPath);
            // The platform window already exists here (Avalonia creates it in the constructor); anything but an X11 window means native Wayland.
            if (toast.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle) { toast.Close(); return false; }
            X11.SetNotificationHints(handle.Handle);
            var screen = toast.Screens.Primary;
            if (screen is null) { toast.Close(); return false; }
            toast.Position = ToastPosition(screen.WorkingArea, screen.Scaling, toast.Width);
            toast.Closed += (_, _) => Next();
            toast.Show();
            toast.Begin();
            p.Shown?.Invoke();
            return true;
        }
        catch (Exception)
        {
            try { toast?.Close(); } catch (Exception) { }
            return false;
        }
    }

    private AchievementToast(string title, string description, string game, string? iconPath)
    {
        WindowDecorations = WindowDecorations.None; Topmost = true; ShowActivated = false; ShowInTaskbar = false; CanResize = false; Focusable = false;
        Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Width = CardW; Height = CardH + Slide; Title = L.T("Conquista desbloqueada");
        Bitmap? art = null;
        try { if (iconPath is { Length: > 0 } && File.Exists(iconPath)) art = new Bitmap(iconPath); } catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException) { }
        art ??= AppIcon.Image;
        iconFrame.Width = iconFrame.Height = 64; iconFrame.CornerRadius = new CornerRadius(12); iconFrame.ClipToBounds = true;
        iconFrame.RenderTransformOrigin = RelativePoint.Center; iconFrame.VerticalAlignment = VerticalAlignment.Center;
        iconFrame.Child = new Image { Source = art, Stretch = Stretch.UniformToFill };
        string desc = description.Length > 140 ? description[..140] + "…" : description;
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Children = {
            new TextBlock { Text = L.T("CONQUISTA DESBLOQUEADA"), Foreground = Palette.AccentText, FontSize = 10.5, FontWeight = FontWeight.Bold, LetterSpacing = 1 },
            new TextBlock { Text = title, Foreground = Palette.Fg, FontSize = 15, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = desc, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = game, Foreground = Palette.Faint, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis } } };
        var row = new DockPanel { Margin = new Thickness(18, 0) };
        DockPanel.SetDock(iconFrame, Dock.Left); row.Children.Add(iconFrame); row.Children.Add(text);
        shine.Width = 90; shine.HorizontalAlignment = HorizontalAlignment.Left; shine.IsHitTestVisible = false; shine.Opacity = 0;
        shine.Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Color.FromArgb(0x30, 255, 255, 255), 0.5), new GradientStop(Colors.Transparent, 1) } };
        var inner = new Grid { Children = { row, shine } };
        card.Height = CardH; card.VerticalAlignment = VerticalAlignment.Top; card.CornerRadius = new CornerRadius(14); card.ClipToBounds = true;
        card.Background = Palette.Panel; card.BorderBrush = Palette.Accent; card.BorderThickness = new Thickness(1); card.Child = inner;
        card.BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(0x55, 0, 0, 0) });
        card.Opacity = 0; card.RenderTransform = new TranslateTransform(0, -Slide);
        Content = new Grid { Children = { card } };
    }

    private void Begin()
    {
        clock.Restart();
        timer.Tick += (_, _) => Frame(clock.Elapsed.TotalMilliseconds);
        timer.Start();
    }

    private void Frame(double ms)
    {
        double In = Math.Clamp(ms / 250, 0, 1), fade = Math.Clamp((ms - (VisibleMs - 500)) / 300, 0, 1);
        double eased = new CubicEaseOut().Ease(In);
        card.Opacity = eased * (1 - fade);
        ((TranslateTransform)card.RenderTransform!).Y = -Slide * (1 - eased) - Slide * 0.3 * fade;
        double s = 0.6 + 0.4 * new BackEaseOut().Ease(Math.Clamp((ms - 120) / 350, 0, 1));
        iconFrame.RenderTransform = new ScaleTransform(s, s);
        double sweep = Math.Clamp((ms - 400) / 700, 0, 1);
        shine.Opacity = sweep is > 0 and < 1 ? 1 : 0;
        shine.Margin = new Thickness(-90 + (CardW + 90) * sweep, 0, 0, 0);
        if (ms >= VisibleMs) { timer.Stop(); Close(); }
    }

    // shortcut: no click-through (needs an XShape/XFixes empty input region); the toast is non-focusable so a click cannot steal focus, add the input shape if it gets in the way.
    private static class X11
    {
        [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint name);
        [DllImport("libX11.so.6")] private static extern nint XInternAtom(nint display, string name, int onlyIfExists);
        [DllImport("libX11.so.6")] private static extern int XChangeProperty(nint display, nint window, nint property, nint type, int format, int mode, nint[] data, int count);
        [DllImport("libX11.so.6")] private static extern int XChangeWindowAttributes(nint display, nint window, nint valueMask, ref XSetWindowAttributes attributes);
        [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
        [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);

        // Xlib XSetWindowAttributes on LP64: unsigned long/long are nint, int/Bool are int (Sequential gives the right padding).
        [StructLayout(LayoutKind.Sequential)]
        private struct XSetWindowAttributes
        {
            public nint BackgroundPixmap, BackgroundPixel, BorderPixmap, BorderPixel;
            public int BitGravity, WinGravity, BackingStore;
            public nint BackingPlanes, BackingPixel;
            public int SaveUnder;
            public nint EventMask, DoNotPropagateMask;
            public int OverrideRedirect;
            public nint Colormap, Cursor;
        }

        /// <summary>Makes the window override-redirect, sets _NET_WM_WINDOW_TYPE_NOTIFICATION and the ABOVE/SKIP_TASKBAR/SKIP_PAGER states while the window is still unmapped, so the window manager reads them as its initial state.</summary>
        public static void SetNotificationHints(nint window)
        {
            var d = XOpenDisplay(0);
            if (d == 0) return;
            try
            {
                nint Atom(string n) => XInternAtom(d, n, 0);
                const int PropModeReplace = 0; nint atomType = 4; // XA_ATOM
                // KWin keeps an active fullscreen window above plain NOTIFICATION windows; its CRITICAL_NOTIFICATION layer sits above fullscreen. Other window managers skip the unknown KDE atom and use NOTIFICATION.
                XChangeProperty(d, window, Atom("_NET_WM_WINDOW_TYPE"), atomType, 32, PropModeReplace, [Atom("_KDE_NET_WM_WINDOW_TYPE_CRITICAL_NOTIFICATION"), Atom("_NET_WM_WINDOW_TYPE_NOTIFICATION")], 2);
                XChangeProperty(d, window, Atom("_NET_WM_STATE"), atomType, 32, PropModeReplace,
                    [Atom("_NET_WM_STATE_ABOVE"), Atom("_NET_WM_STATE_SKIP_TASKBAR"), Atom("_NET_WM_STATE_SKIP_PAGER")], 3);
                // Override-redirect windows are stacked above managed ones by X11 compositors (like menus/tooltips): the portable way to sit above a fullscreen game
                // (GNOME Mutter, Xfwm, Cinnamon...). The WM then does not position it, so the Position set by the caller is what shows.
                var attrs = new XSetWindowAttributes { OverrideRedirect = 1 };
                XChangeWindowAttributes(d, window, (nint)(1L << 9), ref attrs); // CWOverrideRedirect
                XFlush(d);
            }
            finally { XCloseDisplay(d); }
        }
    }
}

/// <summary>One-at-a-time queue: <see cref="Offer"/> says whether the item can be shown now; <see cref="TryNext"/> gives the next one or frees the slot. Bounded.</summary>
public sealed class ToastQueue<T>(int max = 20)
{
    private readonly Queue<T> waiting = new(); private bool busy;
    public bool Offer(T item)
    {
        if (!busy) { busy = true; return true; }
        if (waiting.Count < max) waiting.Enqueue(item);
        return false;
    }
    public bool TryNext(out T item)
    {
        if (waiting.TryDequeue(out item!)) return true;
        busy = false; return false;
    }
}

/// <summary>Games of this app that are running now (set by the library page from the game's process), read by pages that only make sense with the game open.</summary>
public static class RunningGames
{
    private static readonly HashSet<string> keys = [];
    public static event Action? Changed;
    public static bool Contains(string key) => keys.Contains(key);
    public static void Set(string key, bool running)
    {
        if (running ? keys.Add(key) : keys.Remove(key)) Changed?.Invoke();
    }
}
