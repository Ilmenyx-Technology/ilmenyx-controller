// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
// Official Ilmenyx identity assets (Assets/), from the maintainer-approved identity kit.
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Ilmenyx.Desktop;

/// <summary>
/// Product icon (window/taskbar) and symbol (header), loaded once from the embedded Avalonia resources.
/// A missing or unreadable asset leaves the system default instead of failing the window.
/// </summary>
public static class AppIcon
{
    private static readonly Lazy<WindowIcon?> WindowIcon_ = new(() => Load(s => new WindowIcon(s), "ilmenyx-icon-256.png"));
    private static readonly Lazy<Bitmap?> Symbol_ = new(() => Load(s => new Bitmap(s), "ilmenyx-symbol-48.png"));

    private static readonly Lazy<Bitmap?> Image_ = new(() => Load(s => new Bitmap(s), "ilmenyx-icon-256.png"));
    public static WindowIcon? Window => WindowIcon_.Value;
    /// <summary>The product icon as a bitmap, to draw over (the notification-area icon with its attention dot).</summary>
    public static Bitmap? Image => Image_.Value;
    public static Bitmap? Symbol => Symbol_.Value;

    private static T? Load<T>(Func<Stream, T> create, string name) where T : class
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Ilmenyx.Desktop/Assets/" + name));
            return create(stream);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException) { return null; }
    }
}
