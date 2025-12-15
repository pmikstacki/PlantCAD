using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls.Renderers
{
    public sealed class DimensionRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "DimensionRenderer"
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
            int total = 0,
                drawn = 0;

            // Common helper values
            double pxPerWorld = PixelsPerWorld(state);
            double arrowPx = Math.Max(1.0, style.GetLeaderArrowSizePx());

            // Aligned dimensions
            var dimsA = model.DimensionsAligned;
            if (dimsA != null)
            {
                foreach (var d in dimsA)
                {
                    total++;
                    if (options != null && !options.IsLayerVisible(d.Layer))
                        continue;

                    var pen = EnsurePen(style.GetStrokePen(d.Layer));

                    var v = new Point(d.P2.X - d.P1.X, d.P2.Y - d.P1.Y);
                    var len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                    if (len < 1e-6)
                        continue;
                    var ux = v.X / len;
                    var uy = v.Y / len; // along measured segment
                    var nx = -uy;
                    var ny = ux; // outward normal

                    // Dimension points offset by normal
                    var a = new Point(d.P1.X + nx * d.Offset, d.P1.Y + ny * d.Offset);
                    var b = new Point(d.P2.X + nx * d.Offset, d.P2.Y + ny * d.Offset);

                    // Extension lines (from P1->a and P2->b)
                    var P1s = state.WorldToScreen(d.P1);
                    var P2s = state.WorldToScreen(d.P2);
                    var As = state.WorldToScreen(a);
                    var Bs = state.WorldToScreen(b);

                    ctx.DrawLine(pen, P1s, As);
                    ctx.DrawLine(pen, P2s, Bs);

                    // Dimension line
                    ctx.DrawLine(pen, As, Bs);

                    // Arrows at ends pointing inward along +/-u
                    DrawArrow(
                        ctx,
                        pen,
                        Bs,
                        new Point(Bs.X - ux * arrowPx, Bs.Y - uy * arrowPx),
                        arrowPx
                    );
                    DrawArrow(
                        ctx,
                        pen,
                        As,
                        new Point(As.X + ux * arrowPx, As.Y + uy * arrowPx),
                        arrowPx
                    );

                    // Text at the midpoint
                    var midWorld = new Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
                    var mid = state.WorldToScreen(midWorld);
                    var text = !string.IsNullOrWhiteSpace(d.TextOverride)
                        ? d.TextOverride!
                        : len.ToString("0.###", CultureInfo.InvariantCulture);
                    DrawCenterText(
                        ctx,
                        style,
                        d.Layer,
                        text,
                        d.TextHeight,
                        pxPerWorld,
                        options,
                        mid
                    );

                    drawn++;
                }
            }

            // Linear dimensions
            var dimsL = model.DimensionsLinear;
            if (dimsL != null)
            {
                foreach (var d in dimsL)
                {
                    total++;
                    if (options != null && !options.IsLayerVisible(d.Layer))
                        continue;
                    var pen = EnsurePen(style.GetStrokePen(d.Layer));

                    if (d.Orientation == DimLinearOrientation.Horizontal)
                    {
                        // Offset upwards by +Offset in world Y
                        var a = new Point(d.P1.X, d.P1.Y + d.Offset);
                        var b = new Point(d.P2.X, d.P2.Y + d.Offset);
                        var P1s = state.WorldToScreen(d.P1);
                        var P2s = state.WorldToScreen(d.P2);
                        var As = state.WorldToScreen(a);
                        var Bs = state.WorldToScreen(b);
                        ctx.DrawLine(pen, P1s, As);
                        ctx.DrawLine(pen, P2s, Bs);
                        ctx.DrawLine(pen, As, Bs);
                        // arrows pointing inward along -/+X
                        DrawArrow(ctx, pen, Bs, new Point(Bs.X - arrowPx, Bs.Y), arrowPx);
                        DrawArrow(ctx, pen, As, new Point(As.X + arrowPx, As.Y), arrowPx);

                        var midWorld = new Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
                        var mid = state.WorldToScreen(midWorld);
                        var dist = Math.Abs(d.P2.X - d.P1.X);
                        var text = !string.IsNullOrWhiteSpace(d.TextOverride)
                            ? d.TextOverride!
                            : dist.ToString("0.###", CultureInfo.InvariantCulture);
                        DrawCenterText(
                            ctx,
                            style,
                            d.Layer,
                            text,
                            d.TextHeight,
                            pxPerWorld,
                            options,
                            mid
                        );
                    }
                    else // Vertical
                    {
                        // Offset to +X by Offset
                        var a = new Point(d.P1.X + d.Offset, d.P1.Y);
                        var b = new Point(d.P2.X + d.Offset, d.P2.Y);
                        var P1s = state.WorldToScreen(d.P1);
                        var P2s = state.WorldToScreen(d.P2);
                        var As = state.WorldToScreen(a);
                        var Bs = state.WorldToScreen(b);
                        ctx.DrawLine(pen, P1s, As);
                        ctx.DrawLine(pen, P2s, Bs);
                        ctx.DrawLine(pen, As, Bs);
                        // arrows pointing inward along -/+Y
                        DrawArrow(ctx, pen, Bs, new Point(Bs.X, Bs.Y - arrowPx), arrowPx);
                        DrawArrow(ctx, pen, As, new Point(As.X, As.Y + arrowPx), arrowPx);

                        var midWorld = new Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
                        var mid = state.WorldToScreen(midWorld);
                        var dist = Math.Abs(d.P2.Y - d.P1.Y);
                        var text = !string.IsNullOrWhiteSpace(d.TextOverride)
                            ? d.TextOverride!
                            : dist.ToString("0.###", CultureInfo.InvariantCulture);
                        DrawCenterText(
                            ctx,
                            style,
                            d.Layer,
                            text,
                            d.TextHeight,
                            pxPerWorld,
                            options,
                            mid
                        );
                    }

                    drawn++;
                }
            }

            Logger?.LogDebug("DimensionRenderer: total={Total}, drawn={Drawn}", total, drawn);
        }

        private static Pen EnsurePen(Pen p)
        {
            return new Pen(p.Brush, Math.Max(1.0, p.Thickness));
        }

        private static void DrawArrow(
            DrawingContext ctx,
            Pen pen,
            Point tip,
            Point alongPoint,
            double size
        )
        {
            var dx = tip.X - alongPoint.X;
            var dy = tip.Y - alongPoint.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6)
                return;
            var ux = dx / len;
            var uy = dy / len;
            var basePt = new Point(tip.X - ux * size, tip.Y - uy * size);
            var pxv = -uy;
            var pyv = ux; // perpendicular
            var halfWidth = size * 0.5;
            var left = new Point(basePt.X + pxv * halfWidth, basePt.Y + pyv * halfWidth);
            var right = new Point(basePt.X - pxv * halfWidth, basePt.Y - pyv * halfWidth);

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

        private static void DrawCenterText(
            DrawingContext ctx,
            IStyleProvider style,
            string layer,
            string text,
            double textHeightWorld,
            double pxPerWorld,
            CadRenderOptions? options,
            Point center
        )
        {
            var fontPxRaw = Math.Max(0.0, textHeightWorld) * pxPerWorld;
            var minTextPx = options?.MinTextPixelHeight ?? 2.0;
            if (fontPxRaw < minTextPx)
                return;
            var fontPx = Math.Min(fontPxRaw, 96.0);

            // Use text style methods via a synthetic CadText carrying the layer and height
            var synthetic = new CadText
            {
                Layer = layer,
                Height = textHeightWorld,
                Position = new Point(0, 0),
                Value = text,
            };
            var typeface = style.GetTextTypeface(synthetic);
            var brush = style.GetTextBrush(synthetic);
            var ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontPx,
                brush
            );

            var x = center.X - ft.Width / 2.0;
            var y = center.Y - ft.Height / 2.0;
            ctx.DrawText(ft, new Point(x, y));
        }

        private static double PixelsPerWorld(ViewportState state)
        {
            var a = state.Transform.Transform(new Point(0, 0));
            var b = state.Transform.Transform(new Point(1, 0));
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
