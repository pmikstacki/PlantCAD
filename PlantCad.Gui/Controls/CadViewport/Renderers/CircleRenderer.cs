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
    public sealed class CircleRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "CircleRenderer"
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
            if (model.Circles == null || model.Circles.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            Point? sampleCenterS = null;
            double sampleRx = 0,
                sampleRy = 0;
            foreach (var c in model.Circles)
            {
                total++;
                if (options != null && !options.IsLayerVisible(c.Layer))
                {
                    continue;
                }
                // World-space coarse culling by bounding box
                var bb = new Rect(
                    c.Center.X - c.Radius,
                    c.Center.Y - c.Radius,
                    2 * c.Radius,
                    2 * c.Radius
                );
                if (!bb.Intersects(visibleWorldRect) && !visibleWorldRect.Contains(bb))
                {
                    continue;
                }

                // Transform radii to screen space (assume uniform scale, but compute from two axes for robustness)
                var centerS = state.WorldToScreen(c.Center);
                var rxS = Distance(
                    state.WorldToScreen(new Point(c.Center.X + c.Radius, c.Center.Y)),
                    centerS
                );
                var ryS = Distance(
                    state.WorldToScreen(new Point(c.Center.X, c.Center.Y + c.Radius)),
                    centerS
                );
                var minR = options?.MinCurvePixelRadius ?? 1.5;
                if (Math.Max(rxS, ryS) < minR)
                {
                    continue;
                }
                var pen = style.GetStrokePen(c.Layer);
                ctx.DrawEllipse(null, pen, centerS, rxS, ryS);
                drawn++;
                if (sampleCenterS is null)
                {
                    sampleCenterS = centerS;
                    sampleRx = rxS;
                    sampleRy = ryS;
                }
            }
            if (sampleCenterS is { } sc)
            {
                Logger?.LogInformation(
                    "CircleRenderer: total={Total}, drawn={Drawn}, sample centerS=({X:0.###},{Y:0.###}) rx={RX:0.###} ry={RY:0.###}",
                    total,
                    drawn,
                    sc.X,
                    sc.Y,
                    sampleRx,
                    sampleRy
                );
            }
            else
            {
                Logger?.LogInformation(
                    "CircleRenderer: total={Total}, drawn={Drawn}",
                    total,
                    drawn
                );
            }
        }

        private static double Distance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
