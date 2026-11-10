// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Ilmenyx.Contracts;

namespace Ilmenyx.Desktop;

/// <summary>Top-down final state only. No live mutable arrays, animation timer or FPS counter.</summary>
public sealed class GameSimPreview : Control
{
    public static readonly StyledProperty<GameSimState?> StateProperty = AvaloniaProperty.Register<GameSimPreview, GameSimState?>(nameof(State));
    public GameSimState? State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
    static GameSimPreview() => AffectsRender<GameSimPreview>(StateProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context); context.DrawRectangle(Palette.Panel, new Pen(Palette.Line, 1), new Rect(Bounds.Size).Deflate(0.5), IlmenyxTheme.ControlRadius, IlmenyxTheme.ControlRadius);
        if (State is not { } state) return;
        foreach (var entity in state.Entities)
            context.DrawEllipse(entity.Team == 0 ? Palette.Fg : Palette.DataCpu, null,
                new Point(entity.X / 1000 * Bounds.Width, entity.Y / 1000 * Bounds.Height), 1.5, 1.5);
    }
}
