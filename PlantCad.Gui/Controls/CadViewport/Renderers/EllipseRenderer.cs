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
    public sealed class EllipseRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "EllipseRenderer"
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
            if (model.Ellipses == null || model.Ellipses.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            foreach (var el in model.Ellipses)
            {
                total++;
                if (options != null && !options.IsLayerVisible(el.Layer))
                {
                    continue;
                }
                if (!EllipseIntersectsVisible(el, visibleWorldRect))
                {
                    continue;
                }
                var pen = style.GetStrokePen(el.Layer);

                // LOD: estimate on-screen radii using rotated local axes
                double rot = DegreesToRadians(el.RotationDeg);
                var centerS = state.WorldToScreen(el.Center);
                var majorEndW = new Point(
                    el.Center.X + el.RadiusX * Math.Cos(rot),
                    el.Center.Y + el.RadiusX * Math.Sin(rot)
                );
                var minorEndW = new Point(
                    el.Center.X - el.RadiusY * Math.Sin(rot),
                    el.Center.Y + el.RadiusY * Math.Cos(rot)
                );
                var rxS = Distance(state.WorldToScreen(majorEndW), centerS);
                var ryS = Distance(state.WorldToScreen(minorEndW), centerS);
                var rMax = Math.Max(rxS, ryS);
                var rAvg = 0.5 * (rxS + ryS);

                var minR = options?.MinCurvePixelRadius ?? 1.5;
                var minLen = options?.MinCurvePixelLength ?? 3.0;

                double start = el.IsArc ? DegreesToRadians(el.StartAngleDeg) : 0.0;
                double end = el.IsArc ? DegreesToRadians(el.EndAngleDeg) : Math.PI * 2.0;
                while (end < start)
                    end += Math.PI * 2.0;
                var sweep = end - start;
                var approxLenPx = rAvg * sweep;
                if (rMax < minR || approxLenPx < minLen)
                {
                    continue;
                }

                const double pxPerSeg = 4.0;
                int segments = (int)Math.Ceiling(Math.Max(approxLenPx, 1.0) / pxPerSeg);
                if (segments < 16)
                    segments = 16;
                if (segments > 256)
                    segments = 256;

                DrawEllipseOrArc(ctx, state, pen, el, segments);
                drawn++;
            }
            Logger?.LogInformation("EllipseRenderer: total={Total}, drawn={Drawn}", total, drawn);
        }

        private static bool EllipseIntersectsVisible(CadEllipse el, Rect visibleWorldRect)
        {
            // Approximate by sampling bbox; cheap and sufficient for culling
            var bb = SampledBounds(el, segments: 36);
            return bb.Intersects(visibleWorldRect) || visibleWorldRect.Contains(bb);
        }

        private static Rect SampledBounds(CadEllipse el, int segments)
        {
            double minX = double.PositiveInfinity,
                minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity,
                maxY = double.NegativeInfinity;
            double start = el.IsArc ? DegreesToRadians(el.StartAngleDeg) : 0.0;
            double end = el.IsArc ? DegreesToRadians(el.EndAngleDeg) : Math.PI * 2.0;
            while (end < start)
                end += Math.PI * 2.0;
            double rot = DegreesToRadians(el.RotationDeg);
            for (int i = 0; i <= segments; i++)
            {
                double t = (double)i / segments;
                double ang = start + (end - start) * t;
                double lx = el.RadiusX * Math.Cos(ang);
                double ly = el.RadiusY * Math.Sin(ang);
                double wx = el.Center.X + (lx * Math.Cos(rot) - ly * Math.Sin(rot));
                double wy = el.Center.Y + (lx * Math.Sin(rot) + ly * Math.Cos(rot));
                if (wx < minX)
                    minX = wx;
                if (wy < minY)
                    minY = wy;
                if (wx > maxX)
                    maxX = wx;
                if (wy > maxY)
                    maxY = wy;
            }
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private static void DrawEllipseOrArc(
            DrawingContext ctx,
            ViewportState state,
            Pen pen,
            CadEllipse el,
            int segments
        )
        {
            double start = el.IsArc ? DegreesToRadians(el.StartAngleDeg) : 0.0;
            double end = el.IsArc ? DegreesToRadians(el.EndAngleDeg) : Math.PI * 2.0;
            while (end < start)
                end += Math.PI * 2.0;
            double rot = DegreesToRadians(el.RotationDeg);

            Point? prev = null;
            for (int i = 0; i <= segments; i++)
            {
                double t = (double)i / segments;
                double ang = start + (end - start) * t;
                double lx = el.RadiusX * Math.Cos(ang);
                double ly = el.RadiusY * Math.Sin(ang);
                double wx = el.Center.X + (lx * Math.Cos(rot) - ly * Math.Sin(rot));
                double wy = el.Center.Y + (lx * Math.Sin(rot) + ly * Math.Cos(rot));
                var p = state.WorldToScreen(new Point(wx, wy));
                if (prev is { } pr)
                {
                    ctx.DrawLine(pen, pr, p);
                }
                prev = p;
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
