using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using PlantCad.Gui.Controls.Viewport;

namespace PlantCad.Gui.Controls.Rendering
{
    /// <summary>
    /// Renders a glow/outline for the hovered entity using sampled world-space paths.
    /// </summary>
    public sealed class HoverHighlightOverlayRenderer : IOverlayRenderer
    {
        private readonly SolidColorBrush _glowBrushOuter;
        private readonly SolidColorBrush _glowBrushInner;
        private readonly SolidColorBrush _coreBrush;

        public HoverHighlightOverlayRenderer()
        {
            // Cyan-like glow with different opacities; core is white
            _glowBrushOuter = new SolidColorBrush(Color.FromArgb(64, 0, 255, 255)); // alpha 64/255
            _glowBrushInner = new SolidColorBrush(Color.FromArgb(128, 0, 255, 255)); // alpha 128/255
            _coreBrush = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)); // near-white core
        }

        public void Render(
            DrawingContext ctx,
            Size viewportSize,
            ViewportState state,
            OverlayContext context
        )
        {
            var paths = context.HoveredWorldPaths;
            if (paths == null || paths.Count == 0)
            {
                return;
            }

            foreach (var worldPath in paths)
            {
                if (worldPath == null || worldPath.Count < 2)
                {
                    continue;
                }

                // Convert world to screen once
                var screenPts = new Point[worldPath.Count];
                for (int i = 0; i < worldPath.Count; i++)
                {
                    screenPts[i] = state.WorldToScreen(worldPath[i]);
                }

                // Draw layered glow: outer -> inner -> core
                DrawPolyline(ctx, screenPts, new Pen(_glowBrushOuter, 6.0));
                DrawPolyline(ctx, screenPts, new Pen(_glowBrushInner, 3.0));
                DrawPolyline(ctx, screenPts, new Pen(_coreBrush, 1.75));
            }
        }

        private static void DrawPolyline(DrawingContext ctx, IReadOnlyList<Point> pts, Pen pen)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                ctx.DrawLine(pen, pts[i - 1], pts[i]);
            }
        }
    }
}
