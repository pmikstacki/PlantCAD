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
    public sealed class PolylineRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "PolylineRenderer"
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
            if (model.Polylines == null || model.Polylines.Count == 0)
            {
                return;
            }

            int polyCount = 0;
            int segCount = 0;
            int lodSkipped = 0;
            int bboxCulled = 0;
            int dashFallback = 0;
            var pxPerWorld = RenderHelpers.PixelsPerWorld(state);
            var marginPx = options?.EdgeCullingMarginPx ?? 24.0;
            var worldMin = options?.EdgeCullingMinWorld ?? 0.5;
            var percent = options?.EdgeCullingPercent ?? 0.02;
            var expandedRect = RenderHelpers.ExpandVisibleRect(
                state,
                visibleWorldRect,
                marginPx,
                worldMin,
                percent
            );
            var worldPenCache = new Dictionary<string, Pen>(StringComparer.OrdinalIgnoreCase);
            foreach (var pl in model.Polylines)
            {
                if (options != null && !options.IsLayerVisible(pl.Layer))
                {
                    continue;
                }
                var layerKey = pl.Layer ?? string.Empty;
                var basePen = style.GetPolylinePen(pl);
                if (!worldPenCache.TryGetValue(layerKey, out var pen))
                {
                    pen = RenderHelpers.WorldPenNormalized(
                        basePen,
                        pxPerWorld,
                        minPixelThickness: 1.5
                    );
                    worldPenCache[layerKey] = pen;
                }
                if (pl.Points.Count < 2)
                {
                    continue;
                }
                var isSelected = options?.IsSelected(pl.Id) ?? false;
                int selectedDrawnSegs = 0;
                int selectedLodSegs = 0;
                var selectedExpandedRect = isSelected
                    ? RenderHelpers.ExpandVisibleRect(
                        state,
                        visibleWorldRect,
                        marginPx * 2.0,
                        worldMin,
                        percent
                    )
                    : expandedRect;
                // world-space bounding box culling against current visible rectangle
                var bb = ComputePolylineBounds(pl);
                if (!bb.Intersects(selectedExpandedRect) && !selectedExpandedRect.Contains(bb))
                {
                    if (isSelected)
                    {
                        Logger?.LogWarning(
                            "PolylineRenderer: selected polyline culled by bbox. id={Id}, layer={Layer}, bb=({X:0.###},{Y:0.###},{W:0.###},{H:0.###}), expanded=({RX:0.###},{RY:0.###},{RW:0.###},{RH:0.###})",
                            pl.Id,
                            pl.Layer,
                            bb.X,
                            bb.Y,
                            bb.Width,
                            bb.Height,
                            selectedExpandedRect.X,
                            selectedExpandedRect.Y,
                            selectedExpandedRect.Width,
                            selectedExpandedRect.Height
                        );
                    }
                    bboxCulled++;
                    continue;
                }

                // Render segments with bulge support
                int n = pl.Points.Count;
                for (int i = 1; i < n; i++)
                {
                    var p0 = pl.Points[i - 1];
                    var p1 = pl.Points[i];
                    var bulge =
                        (pl.Bulges != null && pl.Bulges.Count > i - 1) ? pl.Bulges[i - 1] : 0.0;
                    if (Math.Abs(bulge) > 1e-9)
                    {
                        // LOD for bulged arc: estimate screen radius and arc length from world using pxPerWorld
                        var dx = p1.X - p0.X;
                        var dy = p1.Y - p0.Y;
                        var chord = Math.Sqrt(dx * dx + dy * dy);
                        if (chord > 1e-9)
                        {
                            var theta = 4.0 * Math.Atan(bulge);
                            var r = (chord / 2.0) / Math.Abs(Math.Sin(theta / 2.0));
                            var rS = r * pxPerWorld;
                            var arcLenPx = Math.Abs(theta) * rS;
                            var minR = isSelected ? 0.1 : (options?.MinCurvePixelRadius ?? 1.5);
                            var minLen = isSelected ? 0.2 : (options?.MinCurvePixelLength ?? 3.0);
                            if (rS < minR || arcLenPx < minLen)
                            {
                                lodSkipped++;
                                if (isSelected)
                                    selectedLodSegs++;
                            }
                            else
                            {
                                DrawBulgedSegment(ctx, state, pen, p0, p1, bulge);
                                segCount++;
                                if (isSelected)
                                    selectedDrawnSegs++;
                            }
                        }
                    }
                    else
                    {
                        var thicknessPx = Math.Max(0.1, basePen.Thickness);
                        var defaultMinLen = Math.Min(1.0, 0.5 * thicknessPx);
                        var minLenBase = options?.MinCurvePixelLength ?? defaultMinLen;
                        var minLenEff = isSelected ? 0.0 : Math.Min(minLenBase, 0.1);
                        var rect = isSelected ? selectedExpandedRect : expandedRect;
                        var visiblePx = RenderHelpers.VisibleWorldLengthPx(
                            p0,
                            p1,
                            rect,
                            pxPerWorld
                        );
                        if (visiblePx < minLenEff)
                        {
                            lodSkipped++;
                            if (isSelected)
                                selectedLodSegs++;
                        }
                        else
                        {
                            // Dash fallback for very short segments
                            var segPen = pen;
                            double dashPeriodPx = double.PositiveInfinity;
                            var ds = basePen.DashStyle?.Dashes;
                            if (ds != null && ds.Count > 0)
                            {
                                double dashSum = 0.0;
                                for (int k = 0; k < ds.Count; k++)
                                    dashSum += Math.Abs(ds[k]);
                                if (dashSum > 0)
                                    dashPeriodPx = dashSum;
                            }
                            if (
                                dashPeriodPx < double.PositiveInfinity
                                && visiblePx <= dashPeriodPx + 1e-6
                            )
                            {
                                var solidBase = new Pen(basePen.Brush, basePen.Thickness)
                                {
                                    DashStyle = null,
                                    LineCap = basePen.LineCap,
                                    LineJoin = basePen.LineJoin,
                                    MiterLimit = basePen.MiterLimit,
                                };
                                segPen = RenderHelpers.WorldPenNormalized(
                                    solidBase,
                                    pxPerWorld,
                                    minPixelThickness: 1.5
                                );
                                dashFallback++;
                            }
                            // Draw in local coordinates under composed transform
                            var aL = RenderHelpers.ToLocal(p0);
                            var bL = RenderHelpers.ToLocal(p1);
                            ctx.DrawLine(segPen, aL, bL);
                            segCount++;
                            if (isSelected)
                                selectedDrawnSegs++;
                        }
                    }
                }
                if (pl.IsClosed && n > 2)
                {
                    var p0 = pl.Points[n - 1];
                    var p1 = pl.Points[0];
                    var bulge =
                        (pl.Bulges != null && pl.Bulges.Count >= n) ? pl.Bulges[n - 1] : 0.0;
                    if (Math.Abs(bulge) > 1e-9)
                    {
                        var dx = p1.X - p0.X;
                        var dy = p1.Y - p0.Y;
                        var chord = Math.Sqrt(dx * dx + dy * dy);
                        if (chord > 1e-9)
                        {
                            var theta = 4.0 * Math.Atan(bulge);
                            var r = (chord / 2.0) / Math.Abs(Math.Sin(theta / 2.0));
                            var rS = r * pxPerWorld;
                            var arcLenPx = Math.Abs(theta) * rS;
                            var minR = options?.MinCurvePixelRadius ?? 1.0;
                            var minLen = options?.MinCurvePixelLength ?? 0.5;
                            if (rS < minR || arcLenPx < minLen)
                            {
                                lodSkipped++;
                                if (isSelected)
                                    selectedLodSegs++;
                            }
                            else
                            {
                                DrawBulgedSegment(ctx, state, pen, p0, p1, bulge);
                                segCount++;
                                if (isSelected)
                                    selectedDrawnSegs++;
                            }
                        }
                    }
                    else
                    {
                        var thicknessPx = Math.Max(0.1, basePen.Thickness);
                        var defaultMinLen = Math.Min(1.0, 0.5 * thicknessPx);
                        var minLenBase = options?.MinCurvePixelLength ?? defaultMinLen;
                        var minLenEff = isSelected ? 0.0 : Math.Min(minLenBase, 0.1);
                        var rect = isSelected ? selectedExpandedRect : expandedRect;
                        var visiblePx = RenderHelpers.VisibleWorldLengthPx(
                            p0,
                            p1,
                            rect,
                            pxPerWorld
                        );
                        if (visiblePx < minLenEff)
                        {
                            lodSkipped++;
                            if (isSelected)
                                selectedLodSegs++;
                        }
                        else
                        {
                            // Dash fallback for very short segments
                            var segPen = pen;
                            double dashPeriodPx = double.PositiveInfinity;
                            var ds = basePen.DashStyle?.Dashes;
                            if (ds != null && ds.Count > 0)
                            {
                                double dashSum = 0.0;
                                for (int k = 0; k < ds.Count; k++)
                                    dashSum += Math.Abs(ds[k]);
                                if (dashSum > 0)
                                    dashPeriodPx = dashSum;
                            }
                            if (
                                dashPeriodPx < double.PositiveInfinity
                                && visiblePx <= dashPeriodPx + 1e-6
                            )
                            {
                                var solidBase = new Pen(basePen.Brush, basePen.Thickness)
                                {
                                    DashStyle = null,
                                    LineCap = basePen.LineCap,
                                    LineJoin = basePen.LineJoin,
                                    MiterLimit = basePen.MiterLimit,
                                };
                                segPen = RenderHelpers.WorldPenNormalized(
                                    solidBase,
                                    pxPerWorld,
                                    minPixelThickness: 1.5
                                );
                                dashFallback++;
                            }
                            // Draw in local coordinates under composed transform
                            var aL = RenderHelpers.ToLocal(p0);
                            var bL = RenderHelpers.ToLocal(p1);
                            ctx.DrawLine(segPen, aL, bL);
                            segCount++;
                            if (isSelected)
                                selectedDrawnSegs++;
                        }
                    }
                }
                if (isSelected)
                {
                    // Always emit a summary for selected entity for diagnostics
                    var culled = !(
                        bb.Intersects(selectedExpandedRect) || selectedExpandedRect.Contains(bb)
                    );
                    Logger?.LogWarning(
                        "PolylineRenderer: selected polyline summary id={Id}, layer={Layer}, drawnSegs={Drawn}, lodSkippedSegs={Lod}, culled={Culled}, bbox=({X:0.###},{Y:0.###},{W:0.###},{H:0.###}), expanded=({RX:0.###},{RY:0.###},{RW:0.###},{RH:0.###})",
                        pl.Id,
                        pl.Layer,
                        selectedDrawnSegs,
                        selectedLodSegs,
                        culled,
                        bb.X,
                        bb.Y,
                        bb.Width,
                        bb.Height,
                        selectedExpandedRect.X,
                        selectedExpandedRect.Y,
                        selectedExpandedRect.Width,
                        selectedExpandedRect.Height
                    );
                }
                polyCount++;
            }
            Logger?.LogInformation(
                "PolylineRenderer: polylines={Poly}, segments={Seg}, lodSkipped={Lod}, bboxCulled={Box}, dashFallback={Dash}",
                polyCount,
                segCount,
                lodSkipped,
                bboxCulled,
                dashFallback
            );
        }

        private static Rect ComputePolylineBounds(CadPolyline pl)
        {
            if (pl.Points == null || pl.Points.Count == 0)
            {
                return new Rect(0, 0, 0, 0);
            }
            double minX = double.PositiveInfinity,
                minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity,
                maxY = double.NegativeInfinity;
            void Include(Point p)
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
            // Always include vertices
            for (int i = 0; i < pl.Points.Count; i++)
                Include(pl.Points[i]);

            // Include bulged arc extrema when present
            int n = pl.Points.Count;
            for (int i = 1; i < n; i++)
            {
                double bulge =
                    (pl.Bulges != null && pl.Bulges.Count > i - 1) ? pl.Bulges[i - 1] : 0.0;
                if (Math.Abs(bulge) <= 1e-9)
                    continue;
                var p0 = pl.Points[i - 1];
                var p1 = pl.Points[i];
                IncludeArcExtents(p0, p1, bulge, ref minX, ref minY, ref maxX, ref maxY);
            }
            if (pl.IsClosed && n > 2)
            {
                double bulge = (pl.Bulges != null && pl.Bulges.Count >= n) ? pl.Bulges[n - 1] : 0.0;
                if (Math.Abs(bulge) > 1e-9)
                {
                    var p0 = pl.Points[n - 1];
                    var p1 = pl.Points[0];
                    IncludeArcExtents(p0, p1, bulge, ref minX, ref minY, ref maxX, ref maxY);
                }
            }
            if (double.IsInfinity(minX))
                return new Rect(0, 0, 0, 0);
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private static void IncludeArcExtents(
            Point p0,
            Point p1,
            double bulge,
            ref double minX,
            ref double minY,
            ref double maxX,
            ref double maxY
        )
        {
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            var chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord <= 1e-9)
                return;
            var theta = 4.0 * Math.Atan(bulge);
            var sinHalf = Math.Sin(theta / 2.0);
            if (Math.Abs(sinHalf) <= 1e-12)
            {
                IncludePoint(p0, ref minX, ref minY, ref maxX, ref maxY);
                IncludePoint(p1, ref minX, ref minY, ref maxX, ref maxY);
                return;
            }
            var r = (chord / 2.0) / Math.Abs(sinHalf);
            var mx = (p0.X + p1.X) / 2.0;
            var my = (p0.Y + p1.Y) / 2.0;
            var nx = -dy / chord;
            var ny = dx / chord;
            var d = Math.Sqrt(Math.Max(r * r - (chord * chord) / 4.0, 0.0));
            var sign = bulge >= 0 ? 1.0 : -1.0;
            var cx = mx + sign * nx * d;
            var cy = my + sign * ny * d;
            var a0 = Math.Atan2(p0.Y - cy, p0.X - cx);
            var a1 = a0 + theta;
            // Normalize to [0, 2pi)
            a0 = NormalizeAngle(a0);
            a1 = NormalizeAngle(a1);

            // Always include endpoints
            IncludePoint(p0, ref minX, ref minY, ref maxX, ref maxY);
            IncludePoint(p1, ref minX, ref minY, ref maxX, ref maxY);

            // Check quadrantal angles
            var quads = new double[] { 0, Math.PI / 2, Math.PI, 3 * Math.PI / 2 };
            foreach (var q in quads)
            {
                if (AngleOnSweep(a0, a1, theta, q))
                {
                    var ex = cx + r * Math.Cos(q);
                    var ey = cy + r * Math.Sin(q);
                    IncludePoint(new Point(ex, ey), ref minX, ref minY, ref maxX, ref maxY);
                }
            }
        }

        private static bool AngleOnSweep(double a0, double a1, double theta, double test)
        {
            // Sweep direction based on theta sign
            if (theta >= 0)
            {
                // Counter-clockwise sweep
                if (a1 < a0)
                    a1 += 2 * Math.PI;
                if (test < a0)
                    test += 2 * Math.PI;
                return test >= a0 && test <= a1;
            }
            else
            {
                // Clockwise sweep
                if (a0 < a1)
                    a0 += 2 * Math.PI;
                if (test > a0)
                    test -= 2 * Math.PI;
                return test <= a0 && test >= a1;
            }
        }

        private static double NormalizeAngle(double a)
        {
            var twoPi = 2 * Math.PI;
            a %= twoPi;
            if (a < 0)
                a += twoPi;
            return a;
        }

        private static void IncludePoint(
            Point p,
            ref double minX,
            ref double minY,
            ref double maxX,
            ref double maxY
        )
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

        private static void DrawBulgedSegment(
            DrawingContext ctx,
            ViewportState state,
            Pen pen,
            Point p0,
            Point p1,
            double bulge
        )
        {
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            var chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord <= 1e-9)
            {
                // degenerate arc -> nothing to draw
                return;
            }
            var theta = 4.0 * Math.Atan(bulge); // included angle, signed
            var sinHalf = Math.Sin(theta / 2.0);
            if (Math.Abs(sinHalf) <= 1e-12)
            {
                // degenerate arc -> line as local coordinates under composed transform
                var aL = RenderHelpers.ToLocal(p0);
                var bL = RenderHelpers.ToLocal(p1);
                ctx.DrawLine(pen, aL, bL);
                return;
            }
            var r = (chord / 2.0) / Math.Abs(sinHalf);
            // midpoint
            var mx = (p0.X + p1.X) / 2.0;
            var my = (p0.Y + p1.Y) / 2.0;
            // unit normal (left of chord)
            var nx = -dy / chord;
            var ny = dx / chord;
            var d = Math.Sqrt(Math.Max(r * r - (chord * chord) / 4.0, 0.0));
            var sign = bulge >= 0 ? 1.0 : -1.0;
            var cx = mx + sign * nx * d;
            var cy = my + sign * ny * d;

            // start and end angles
            var a0 = Math.Atan2(p0.Y - cy, p0.X - cx);
            var a1 = a0 + theta;

            // choose segments based on angle magnitude (approx every 10 degrees)
            int segs = Math.Clamp((int)Math.Ceiling(Math.Abs(theta) / (Math.PI / 18.0)), 4, 128);
            var prev = p0;
            for (int i = 1; i <= segs; i++)
            {
                double t = (double)i / segs;
                var ang = a0 + (a1 - a0) * t;
                var wx = cx + r * Math.Cos(ang) * 1.0 * (bulge >= 0 ? 1.0 : 1.0);
                var wy = cy + r * Math.Sin(ang) * 1.0 * (bulge >= 0 ? 1.0 : 1.0);
                var curW = new Point(wx, wy);
                var aL = RenderHelpers.ToLocal(prev);
                var bL = RenderHelpers.ToLocal(curW);
                ctx.DrawLine(pen, aL, bL);
                prev = curW;
            }
        }
    }
}
