using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Silex.Audit.Contracts;

namespace Silex.Audit.Desktop;

/// <summary>Top-down final state only. No live mutable arrays, animation timer or FPS counter.</summary>
public sealed class GameSimPreview : Control
{
    public static readonly StyledProperty<GameSimState?> StateProperty = AvaloniaProperty.Register<GameSimPreview, GameSimState?>(nameof(State));
    public GameSimState? State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
    static GameSimPreview() => AffectsRender<GameSimPreview>(StateProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.DrawRectangle(Brushes.Black, null, new Rect(Bounds.Size));
        if (State is not { } state) return;
        foreach (var entity in state.Entities)
            context.DrawEllipse(entity.Team == 0 ? Brushes.White : Brushes.LightSkyBlue, null,
                new Point(entity.X / 1000 * Bounds.Width, entity.Y / 1000 * Bounds.Height), 1.5, 1.5);
    }
}
