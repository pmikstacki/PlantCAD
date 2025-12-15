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
    public sealed class SolidRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "SolidRenderer"
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
            if (model.Solids == null || model.Solids.Count == 0)
            {
                return;
            }
            int total = 0;
            int drawn = 0;
            var pxPerWorld = RenderHelpers.PixelsPerWorld(state);
            foreach (var so in model.Solids)
            {
                total++;
                if (options != null && !options.IsLayerVisible(so.Layer))
                {
                    continue;
                }
                if (so.Vertices == null || so.Vertices.Count < 3)
                {
                    continue;
                }
                var bb = BoundsFromPoints(so.Vertices);
                if (!bb.Intersects(visibleWorldRect) && !visibleWorldRect.Contains(bb))
                {
                    continue;
                }

                var geo = new StreamGeometry();
                using (var ctxg = geo.Open())
                {
                    var firstW = so.Vertices[0];
                    ctxg.BeginFigure(firstW, isFilled: true);
                    for (int i = 1; i < so.Vertices.Count; i++)
                    {
                        var pW = so.Vertices[i];
                        ctxg.LineTo(pW);
                    }
                    ctxg.EndFigure(isClosed: true);
                }
                var fill = style.GetFillBrush(so.Layer);
                var basePen = style.GetStrokePen(so.Layer);
                var pen = RenderHelpers.WorldPenNormalized(basePen, pxPerWorld);
                ctx.DrawGeometry(fill, pen, geo);
                drawn++;
            }

            Logger?.LogInformation("SolidRenderer: total={Total}, drawn={Drawn}", total, drawn);
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
