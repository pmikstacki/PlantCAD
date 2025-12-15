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
    public sealed class WipeoutRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "WipeoutRenderer"
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
            if (model.Wipeouts == null || model.Wipeouts.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            int culled = 0;

            foreach (var w in model.Wipeouts)
            {
                total++;
                if (options != null && !options.IsLayerVisible(w.Layer))
                {
                    continue;
                }
                if (w.Vertices == null || w.Vertices.Count < 3)
                {
                    continue;
                }
                // Coarse culling by polygon bbox
                double minX = double.PositiveInfinity,
                    minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity,
                    maxY = double.NegativeInfinity;
                foreach (var p in w.Vertices)
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
                var bb = new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
                if (!bb.Intersects(visibleWorldRect) && !visibleWorldRect.Contains(bb))
                {
                    culled++;
                    continue;
                }

                var geo = new StreamGeometry();
                using (var gctx = geo.Open())
                {
                    var first = RenderHelpers.ToLocal(w.Vertices[0]);
                    gctx.BeginFigure(first, isFilled: true);
                    for (int i = 1; i < w.Vertices.Count; i++)
                    {
                        var p = RenderHelpers.ToLocal(w.Vertices[i]);
                        gctx.LineTo(p);
                    }
                    gctx.EndFigure(isClosed: true);
                }

                // Wipeout should mask underlying content: use the style-provided background brush
                IBrush fill = style.GetBackgroundBrush();
                ctx.DrawGeometry(fill, w.ShowFrame ? style.GetStrokePen(w.Layer) : null, geo);
                drawn++;
            }

            Logger?.LogInformation(
                "WipeoutRenderer: total={Total}, drawn={Drawn}, culled={Culled}",
                total,
                drawn,
                culled
            );
        }
    }
}
