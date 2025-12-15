using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls.Renderers
{
    // Screen-space renderer for leaders: world polyline transformed to screen, arrow sized in pixels.
    public sealed class LeaderRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "LeaderRenderer"
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
            var leaders = model.Leaders;
            if (leaders == null || leaders.Count == 0)
                return;

            int total = 0,
                drawn = 0,
                culled = 0;

            foreach (var l in leaders)
            {
                total++;
                if (options != null && !options.IsLayerVisible(l.Layer))
                    continue;
                if (l.Points == null || l.Points.Count < 2)
                {
                    culled++;
                    continue;
                }

                // World-space quick culling using leader polyline bounds
                double minX = double.PositiveInfinity,
                    minY = double.PositiveInfinity,
                    maxX = double.NegativeInfinity,
                    maxY = double.NegativeInfinity;
                foreach (var p in l.Points)
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
                var bbWorld = new Rect(
                    minX,
                    minY,
                    Math.Max(0, maxX - minX),
                    Math.Max(0, maxY - minY)
                );
                if (!bbWorld.Intersects(visibleWorldRect) && !visibleWorldRect.Contains(bbWorld))
                {
                    culled++;
                    continue;
                }

                // Transform to screen and do precise screen culling
                var spts = new List<Point>(l.Points.Count);
                for (int i = 0; i < l.Points.Count; i++)
                    spts.Add(state.WorldToScreen(l.Points[i]));
                // Arrow size in pixels
                var arrowPx = Math.Max(1.0, style.GetLeaderArrowSizePx());

                // Pen: use layer brush with a sensible pixel thickness
                var basePen = style.GetStrokePen(l.Layer);
                var pen = new Pen(basePen.Brush, Math.Max(1.0, basePen.Thickness));

                // Draw leader segments
                for (int i = 1; i < spts.Count; i++)
                {
                    ctx.DrawLine(pen, spts[i - 1], spts[i]);
                }

                // Draw arrow at end if requested
                if (l.ArrowAtEnd && spts.Count >= 2)
                {
                    var tip = spts[^1];
                    var prev = spts[^2];
                    var dx = tip.X - prev.X;
                    var dy = tip.Y - prev.Y;
                    var len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 1e-6)
                    {
                        double ux = dx / len,
                            uy = dy / len;
                        var size = arrowPx;
                        // Base width factor; tweak for aesthetics
                        double halfWidth = size * 0.5;
                        // Base point of arrow along the direction backwards from tip
                        var basePt = new Point(tip.X - ux * size, tip.Y - uy * size);
                        // Perpendicular
                        double pxv = -uy,
                            pyv = ux;
                        var left = new Point(
                            basePt.X + pxv * halfWidth,
                            basePt.Y + pyv * halfWidth
                        );
                        var right = new Point(
                            basePt.X - pxv * halfWidth,
                            basePt.Y - pyv * halfWidth
                        );

                        var geo = new StreamGeometry();
                        using (var gc = geo.Open())
                        {
                            gc.BeginFigure(tip, isFilled: true);
                            gc.LineTo(left);
                            gc.LineTo(right);
                            gc.EndFigure(isClosed: true);
                        }
                        ctx.DrawGeometry(pen.Brush, null, geo);
                    }
                }

                drawn++;
            }

            Logger?.LogDebug(
                "LeaderRenderer: total={Total}, culled={Culled}, drawn={Drawn}",
                total,
                culled,
                drawn
            );
        }

        private static Rect ComputeScreenBounds(IReadOnlyList<Point> pts)
        {
            double minX = double.PositiveInfinity,
                minY = double.PositiveInfinity,
                maxX = double.NegativeInfinity,
                maxY = double.NegativeInfinity;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                if (p.X < minX)
                    minX = p.X;
                if (p.Y < minY)
                    minY = p.Y;
                if (p.X > maxX)
                    maxX = p.X;
                if (p.Y > maxY)
                    maxY = p.Y;
            }
            if (
                !double.IsFinite(minX)
                || !double.IsFinite(minY)
                || !double.IsFinite(maxX)
                || !double.IsFinite(maxY)
            )
            {
                return new Rect();
            }
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }
    }
}
