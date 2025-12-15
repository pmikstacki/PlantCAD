using System;
using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls.Renderers
{
    public sealed class SplineRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "SplineRenderer"
        );

        public void Render(
            DrawingContext ctx,
            ViewportState state,
            CadModel model,
            IStyleProvider style,
            CadRenderOptions? options,
            Rect visibleWorldRect
        )
        {
            if (model.Splines == null || model.Splines.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            foreach (var sp in model.Splines)
            {
                total++;
                if (options != null && !options.IsLayerVisible(sp.Layer))
                {
                    continue;
                }
                if (sp.Points == null || sp.Points.Count < 2)
                {
                    continue;
                }
                var bb = BoundsFromPoints(sp.Points);
                if (!bb.Intersects(visibleWorldRect) && !visibleWorldRect.Contains(bb))
                {
                    continue;
                }

                var pen = style.GetStrokePen(sp.Layer);
                var prev = state.WorldToScreen(sp.Points[0]);
                for (int i = 1; i < sp.Points.Count; i++)
                {
                    var cur = state.WorldToScreen(sp.Points[i]);
                    ctx.DrawLine(pen, prev, cur);
                    prev = cur;
                }
                if (sp.IsClosed && sp.Points.Count > 2)
                {
                    var first = state.WorldToScreen(sp.Points[0]);
                    ctx.DrawLine(pen, prev, first);
                }
                drawn++;
            }
            Logger?.LogInformation("SplineRenderer: total={Total}, drawn={Drawn}", total, drawn);
        }

        private static Rect BoundsFromPoints(System.Collections.Generic.IReadOnlyList<Point> pts)
        {
            double minX = double.PositiveInfinity,
                minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity,
                maxY = double.NegativeInfinity;
            foreach (var p in pts)
            {
                if (p.X < minX)
                    minX = p.X;
                if (p.Y < minY)
                    minY = p.Y;
                if (p.X > maxX)
                    maxX = p.X;
                if (p.Y > maxY)
                    maxY = p.Y;
            }
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }
    }
}
