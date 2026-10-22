// New Ilmenyx additions: small shared builders for the original-style chips and cards. See LICENSING.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ilmenyx.Desktop;

public static class UiKit
{
    public static TextBlock Label(string text) => new() { Text = text, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.Faint, TextWrapping = TextWrapping.Wrap };
    public static Border Card(Control child, Thickness? margin = null) => new() { Background = Palette.Card, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 18), Margin = margin ?? new Thickness(0, 0, 0, 14), Child = child };
    public static Button Primary(string name, string text, double padX = 20, double padY = 9) => new() { Name = name, Content = text, Background = Palette.Accent, Foreground = Brushes.Black, FontWeight = FontWeight.Bold, Padding = new Thickness(padX, padY), CornerRadius = new CornerRadius(12) };

    public static void ChipStyle(Button b, bool on) { b.Background = on ? Palette.Fg : Brushes.Transparent; b.Foreground = on ? Brushes.Black : Palette.Muted; }

    /// <summary>Exclusive chip group. <see cref="Select"/> changes the visual selection without raising <paramref name="chosen"/> (used when loading a profile).</summary>
    public sealed class ChipGroup<T>
    {
        private readonly List<(Button Button, T Value)> items = [];
        public StackPanel Panel { get; }
        public ChipGroup(string name, IEnumerable<(string Label, T Value)> options, T initial, Action<T> chosen)
        {
            Panel = new StackPanel { Name = name, Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var (label, value) in options)
            {
                var b = new Button { Content = label, FontSize = 11, Padding = new Thickness(14, 5), CornerRadius = new CornerRadius(16), BorderBrush = Palette.Line, BorderThickness = new Thickness(1) };
                ChipStyle(b, EqualityComparer<T>.Default.Equals(value, initial)); items.Add((b, value));
                var v = value; b.Click += (_, _) => { Select(v); chosen(v); };
                Panel.Children.Add(b);
            }
        }
        public void Select(T value) { foreach (var (b, v) in items) ChipStyle(b, EqualityComparer<T>.Default.Equals(v, value)); }
    }
}
