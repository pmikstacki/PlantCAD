using System;
using System.Collections.Generic;
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
    public sealed class MTextRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "MTextRenderer"
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
            if (model.MTexts == null || model.MTexts.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            int culled = 0;
            int hidden = 0;
            int tooSmall = 0;
            foreach (var t in model.MTexts)
            {
                total++;
                if (options != null && !options.IsLayerVisible(t.Layer))
                {
                    hidden++;
                    continue;
                }

                // Prepare lines and estimate bounds in world space
                var textValue = t.Value ?? string.Empty;
                double lineHeightW = Math.Max(t.Height, 0.0) * 1.2; // simple line spacing
                List<string> lines;
                double widthW;
                if (t.RectangleWidth > 0)
                {
                    lines = WrapToWidth(textValue, t.RectangleWidth, t.Height);
                    widthW = t.RectangleWidth;
                }
                else
                {
                    // No wrapping requested: split into paragraphs and estimate width by 0.6 * height per character
                    lines = SplitParagraphs(textValue);
                    widthW = 0.0;
                    foreach (var ln in lines)
                    {
                        widthW = Math.Max(widthW, ln.Length * (0.6 * Math.Max(t.Height, 0.0)));
                    }
                }
                double totalHeightW = Math.Max(lineHeightW * Math.Max(1, lines.Count), 0.0);
                var topY = t.Position.Y - 0.8 * Math.Max(t.Height, 0.0);
                var approxBb = new Rect(t.Position.X, topY, Math.Max(0, widthW), totalHeightW);
                // Expand a bit to be conservative and account for glyph metrics
                approxBb = approxBb.Inflate(lineHeightW * 0.2);
                if (!approxBb.Intersects(visibleWorldRect) && !visibleWorldRect.Contains(approxBb))
                {
                    culled++;
                    continue;
                }

                var posS = state.WorldToScreen(t.Position);
                var pxPerWorld = PixelsPerWorld(state);
                var fontPxRaw = t.Height * pxPerWorld;
                var minTextPx = options?.MinTextPixelHeight ?? 2.0;
                if (fontPxRaw < minTextPx)
                {
                    tooSmall++;
                    continue; // too small to render
                }
                var fontPx = Math.Min(fontPxRaw, 96.0);
                var typeface = style.GetMTextTypeface(t);
                var brush = style.GetMTextBrush(t);

                double x = posS.X;
                double y = posS.Y;

                // Screen-space culling with rotated AABB based on measured lines
                var visibleScreen = state.WorldToScreen(visibleWorldRect);
                double lineHeightPx = fontPx * 1.2;
                double minX = double.PositiveInfinity,
                    minY = double.PositiveInfinity,
                    maxX = double.NegativeInfinity,
                    maxY = double.NegativeInfinity;
                bool rotated = Math.Abs(t.RotationDeg) > 1e-6;
                double angle = rotated ? t.RotationDeg * Math.PI / 180.0 : 0.0;
                double cos = rotated ? Math.Cos(angle) : 1.0;
                double sin = rotated ? Math.Sin(angle) : 0.0;
                var mCull = rotated
                    ? new Matrix(cos, sin, -sin, cos, x - x * cos + y * sin, y - x * sin - y * cos)
                    : Matrix.Identity;
                for (int i = 0; i < lines.Count; i++)
                {
                    var ftLine = new FormattedText(
                        lines[i],
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontPx,
                        brush
                    );
                    var lx = x;
                    var ly = y + i * lineHeightPx;
                    if (rotated)
                    {
                        var tl = mCull.Transform(new Point(lx, ly));
                        var tr = mCull.Transform(new Point(lx + ftLine.Width, ly));
                        var br = mCull.Transform(new Point(lx + ftLine.Width, ly + ftLine.Height));
                        var bl = mCull.Transform(new Point(lx, ly + ftLine.Height));
                        minX = Math.Min(minX, Math.Min(Math.Min(tl.X, tr.X), Math.Min(br.X, bl.X)));
                        minY = Math.Min(minY, Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(br.Y, bl.Y)));
                        maxX = Math.Max(maxX, Math.Max(Math.Max(tl.X, tr.X), Math.Max(br.X, bl.X)));
                        maxY = Math.Max(maxY, Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(br.Y, bl.Y)));
                    }
                    else
                    {
                        minX = Math.Min(minX, lx);
                        minY = Math.Min(minY, ly);
                        maxX = Math.Max(maxX, lx + ftLine.Width);
                        maxY = Math.Max(maxY, ly + ftLine.Height);
                    }
                }
                if (
                    !(
                        new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY))
                    ).Intersects(visibleScreen)
                )
                {
                    culled++;
                    continue;
                }

                if (rotated)
                {
                    var m = mCull;
                    using (ctx.PushTransform(m))
                    {
                        DrawLines(ctx, lines, typeface, brush, fontPx, x, y);
                    }
                }
                else
                {
                    DrawLines(ctx, lines, typeface, brush, fontPx, x, y);
                }

                drawn++;
            }

            Logger?.LogInformation(
                "MTextRenderer: total={Total}, drawn={Drawn}, culled={Culled}, hidden={Hidden}, tooSmall={TooSmall}",
                total,
                drawn,
                culled,
                hidden,
                tooSmall
            );
        }

        private static void DrawLines(
            DrawingContext ctx,
            List<string> lines,
            Typeface typeface,
            IBrush brush,
            double fontPx,
            double x,
            double y
        )
        {
            double lineHeightPx = fontPx * 1.2;
            for (int i = 0; i < lines.Count; i++)
            {
                var text = lines[i];
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }
                var ft = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontPx,
                    brush
                );
                ctx.DrawText(ft, new Point(x, y + i * lineHeightPx));
            }
        }

        // Very simple greedy word-wrapping using average glyph width ~ 0.6 * height in world units
        private static List<string> WrapToWidth(string value, double rectWidthW, double heightW)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(value) || rectWidthW <= 0 || heightW <= 0)
            {
                lines.Add(string.Empty);
                return lines;
            }

            var paragraphs = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int maxCharsPerLine = (int)Math.Max(Math.Floor(rectWidthW / (0.6 * heightW)), 1);

            foreach (var para in paragraphs)
            {
                if (string.IsNullOrEmpty(para))
                {
                    lines.Add(string.Empty);
                    continue;
                }
                var words = para.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var current = string.Empty;
                foreach (var w in words)
                {
                    if (current.Length == 0)
                    {
                        current = w;
                    }
                    else if (current.Length + 1 + w.Length <= maxCharsPerLine)
                    {
                        current += " " + w;
                    }
                    else
                    {
                        lines.Add(current);
                        current = w;
                    }
                }
                if (current.Length > 0)
                {
                    lines.Add(current);
                }
            }

            if (lines.Count == 0)
            {
                lines.Add(string.Empty);
            }
            return lines;
        }

        private static List<string> SplitParagraphs(string value)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(value))
            {
                list.Add(string.Empty);
                return list;
            }
            var paragraphs = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var p in paragraphs)
            {
                list.Add(p ?? string.Empty);
            }
            if (list.Count == 0)
            {
                list.Add(string.Empty);
            }
            return list;
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
