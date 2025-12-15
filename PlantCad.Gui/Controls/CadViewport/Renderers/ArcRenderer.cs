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
    public sealed class ArcRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "ArcRenderer"
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
            if (model.Arcs == null || model.Arcs.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            Point? sample = null;
            int? sampleSegs = null;
            foreach (var a in model.Arcs)
            {
                total++;
                if (options != null && !options.IsLayerVisible(a.Layer))
                {
                    continue;
                }
                // culling via bbox
                var bb = new Rect(
                    a.Center.X - a.Radius,
                    a.Center.Y - a.Radius,
                    2 * a.Radius,
                    2 * a.Radius
                );
                if (!bb.Intersects(visibleWorldRect))
                {
                    continue;
                }

                // Approximate arc with segments adaptively based on on-screen radius and sweep
                var startRad = DegreesToRadians(a.StartAngle);
                var endRad = DegreesToRadians(a.EndAngle);
                // Normalize sweep direction (assume CCW); if end < start, wrap around
                while (endRad < startRad)
                    endRad += Math.PI * 2;
                var sweep = endRad - startRad;

                // Estimate screen-space radius to determine needed tessellation
                var centerS = state.WorldToScreen(a.Center);
                var edgeXS = state.WorldToScreen(new Point(a.Center.X + a.Radius, a.Center.Y));
                var edgeYS = state.WorldToScreen(new Point(a.Center.X, a.Center.Y + a.Radius));
                var rxS = Distance(edgeXS, centerS);
                var ryS = Distance(edgeYS, centerS);
                var rS = Math.Max(rxS, ryS);

                // LOD skip for tiny arcs
                var minR = options?.MinCurvePixelRadius ?? 1.5;
                var minLen = options?.MinCurvePixelLength ?? 3.0;
                if (rS < minR || rS * sweep < minLen)
                {
                    continue;
                }

                // Target pixels per segment; larger = fewer segments (faster), smaller = smoother
                const double pxPerSeg = 4.0;
                var arcLenPx = rS * sweep; // length in pixels
                int segments = (int)Math.Ceiling(arcLenPx / pxPerSeg);
                if (segments < 1)
                    segments = 1;
                if (segments > 256)
                    segments = 256;

                var step = sweep / segments;
                var pen = style.GetStrokePen(a.Layer);
                Point? prev = null;
                for (int i = 0; i <= segments; i++)
                {
                    var ang = startRad + i * step;
                    var x = a.Center.X + a.Radius * Math.Cos(ang);
                    var y = a.Center.Y + a.Radius * Math.Sin(ang);
                    var p = state.WorldToScreen(new Point(x, y));
                    if (prev is { } pr)
                    {
                        ctx.DrawLine(pen, pr, p);
                    }
                    prev = p;
                    if (sample is null)
                    {
                        sample = p;
                        sampleSegs = segments;
                    }
                }
                drawn++;
            }
            if (sample is { } sp)
            {
                Logger?.LogInformation(
                    "ArcRenderer: total={Total}, drawn={Drawn}, sampleS=({X:0.###},{Y:0.###}), segs={Segs}",
                    total,
                    drawn,
                    sp.X,
                    sp.Y,
                    sampleSegs ?? -1
                );
            }
            else
            {
                Logger?.LogInformation("ArcRenderer: total={Total}, drawn={Drawn}", total, drawn);
            }
        }

        private static double DegreesToRadians(double deg) => deg * Math.PI / 180.0;

        private static double Distance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
