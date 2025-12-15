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
    public sealed class TextRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "TextRenderer"
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
            if (model.Texts == null || model.Texts.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            foreach (var t in model.Texts)
            {
                total++;
                if (options != null && !options.IsLayerVisible(t.Layer))
                {
                    continue;
                }
                if (!TextIntersectsVisible(t, visibleWorldRect))
                {
                    continue;
                }

                var posS = state.WorldToScreen(t.Position);
                // Estimate pixels per world unit using unit vector
                var pxPerWorld = PixelsPerWorld(state);
                var fontPxRaw = t.Height * pxPerWorld;
                var minTextPx = options?.MinTextPixelHeight ?? 2.0;
                if (fontPxRaw < minTextPx)
                {
                    continue; // too small to render
                }
                var fontPx = Math.Min(fontPxRaw, 96.0);

                var typeface = style.GetTextTypeface(t);
                var brush = style.GetTextBrush(t);
                var ft = new FormattedText(
                    t.Value ?? string.Empty,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontPx,
                    brush
                );

                // Alignment offsets
                double x = posS.X;
                double y = posS.Y;
                switch (t.HorizontalAlignment)
                {
                    case CadTextHAlign.Center:
                        x -= ft.Width / 2.0;
                        break;
                    case CadTextHAlign.Right:
                        x -= ft.Width;
                        break;
                }
                switch (t.VerticalAlignment)
                {
                    case CadTextVAlign.Bottom:
                    case CadTextVAlign.Baseline:
                        y -= ft.Height;
                        break;
                    case CadTextVAlign.Middle:
                        y -= ft.Height / 2.0;
                        break;
                    case CadTextVAlign.Top:
                        break;
                }

                // Screen-space culling using precise rotated AABB
                var visibleScreen = state.WorldToScreen(visibleWorldRect);
                bool visible;
                if (Math.Abs(t.RotationDeg) > 1e-6)
                {
                    var angle = t.RotationDeg * Math.PI / 180.0;
                    var cos = Math.Cos(angle);
                    var sin = Math.Sin(angle);
                    var mCull = new Matrix(
                        cos,
                        sin,
                        -sin,
                        cos,
                        x - x * cos + y * sin,
                        y - x * sin - y * cos
                    );
                    var tl = mCull.Transform(new Point(x, y));
                    var tr = mCull.Transform(new Point(x + ft.Width, y));
                    var br = mCull.Transform(new Point(x + ft.Width, y + ft.Height));
                    var bl = mCull.Transform(new Point(x, y + ft.Height));
                    var minX = Math.Min(Math.Min(tl.X, tr.X), Math.Min(br.X, bl.X));
                    var minY = Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(br.Y, bl.Y));
                    var maxX = Math.Max(Math.Max(tl.X, tr.X), Math.Max(br.X, bl.X));
                    var maxY = Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(br.Y, bl.Y));
                    var aabb = new Rect(
                        minX,
                        minY,
                        Math.Max(0, maxX - minX),
                        Math.Max(0, maxY - minY)
                    );
                    visible = aabb.Intersects(visibleScreen) || visibleScreen.Contains(aabb);
                }
                else
                {
                    var aabb = new Rect(x, y, Math.Max(0, ft.Width), Math.Max(0, ft.Height));
                    visible = aabb.Intersects(visibleScreen) || visibleScreen.Contains(aabb);
                }
                if (!visible)
                {
                    continue;
                }

                if (Math.Abs(t.RotationDeg) > 1e-6)
                {
                    var angle = t.RotationDeg * Math.PI / 180.0;
                    var cos = Math.Cos(angle);
                    var sin = Math.Sin(angle);
                    var m = new Matrix(
                        cos,
                        sin,
                        -sin,
                        cos,
                        x - x * cos + y * sin,
                        y - x * sin - y * cos
                    );
                    using (ctx.PushTransform(m))
                    {
                        ctx.DrawText(ft, new Point(x, y));
                    }
                }
                else
                {
                    ctx.DrawText(ft, new Point(x, y));
                }
                drawn++;
            }

            Logger?.LogInformation("TextRenderer: total={Total}, drawn={Drawn}", total, drawn);
        }

        private static double PixelsPerWorld(ViewportState state)
        {
            var a = state.Transform.Transform(new Point(0, 0));
            var b = state.Transform.Transform(new Point(1, 0));
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool TextIntersectsVisible(CadText t, Rect visibleWorldRect)
        {
            double h = Math.Max(t.Height, 0.0);
            double w = h * 0.6 * (t.Value?.Length ?? 0);
            var bb = new Rect(t.Position.X, t.Position.Y - 0.8 * h, Math.Max(0, w), Math.Max(0, h));
            return bb.Intersects(visibleWorldRect) || visibleWorldRect.Contains(bb);
        }
    }
}
