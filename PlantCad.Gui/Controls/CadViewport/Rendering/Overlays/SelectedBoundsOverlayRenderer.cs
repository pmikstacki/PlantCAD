using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using PlantCad.Gui.Controls.Viewport;

namespace PlantCad.Gui.Controls.Rendering
{
    /// <summary>
    /// Renders world-space bounding boxes of currently selected CAD entities.
    /// </summary>
    public sealed class SelectedBoundsOverlayRenderer : IOverlayRenderer
    {
        private static readonly Pen BoundsPen;
        private static readonly Pen CenterPen;

        static SelectedBoundsOverlayRenderer()
        {
            var stroke = new SolidColorBrush(Color.FromArgb(220, 255, 165, 0)); // orange
            BoundsPen = new Pen(stroke, 2.0)
            {
                DashStyle = new DashStyle(new double[] { 4, 3 }, 0),
            };
            CenterPen = new Pen(Brushes.OrangeRed, 1.5);
        }

        public void Render(
            DrawingContext ctx,
            Size viewportSize,
            ViewportState state,
            OverlayContext context
        )
        {
            var list = context.SelectedWorldBounds;
            if (list == null || list.Count == 0)
            {
                return;
            }

            foreach (var bb in list)
            {
                DrawWorldRect(ctx, state, bb);
                DrawWorldCenterCross(ctx, state, bb);
            }
        }

        private static void DrawWorldRect(DrawingContext ctx, ViewportState state, Rect world)
        {
            if (world.Width <= 0 || world.Height <= 0)
            {
                return;
            }
            var p1 = state.WorldToScreen(world.TopLeft);
            var p2 = state.WorldToScreen(new Point(world.Right, world.Top));
            var p3 = state.WorldToScreen(world.BottomRight);
            var p4 = state.WorldToScreen(new Point(world.Left, world.Bottom));

            ctx.DrawLine(BoundsPen, p1, p2);
            ctx.DrawLine(BoundsPen, p2, p3);
            ctx.DrawLine(BoundsPen, p3, p4);
            ctx.DrawLine(BoundsPen, p4, p1);
        }

        private static void DrawWorldCenterCross(
            DrawingContext ctx,
            ViewportState state,
            Rect world
        )
        {
            var cx = world.X + world.Width / 2.0;
            var cy = world.Y + world.Height / 2.0;
            var c = state.WorldToScreen(new Point(cx, cy));
            const double d = 8.0; // pixels
            ctx.DrawLine(CenterPen, new Point(c.X - d, c.Y), new Point(c.X + d, c.Y));
            ctx.DrawLine(CenterPen, new Point(c.X, c.Y - d), new Point(c.X, c.Y + d));
        }
    }
}
