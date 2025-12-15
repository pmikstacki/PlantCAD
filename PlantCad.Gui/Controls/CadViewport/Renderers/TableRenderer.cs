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
    public sealed class TableRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "TableRenderer"
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
            if (model.Tables is null || model.Tables.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            foreach (var t in model.Tables)
            {
                total++;
                if (options != null && !options.IsLayerVisible(t.Layer))
                {
                    continue;
                }
                if (t.Rows <= 0 || t.Columns <= 0)
                {
                    continue;
                }
                if (!t.Bounds.Intersects(visibleWorldRect) && !visibleWorldRect.Contains(t.Bounds))
                {
                    continue;
                }

                // Pixels per world unit
                var ppw = RenderHelpers.PixelsPerWorld(state);
                if (ppw <= 0)
                {
                    continue;
                }

                // Compute cumulative offsets
                var rowOffsets = Cumulate(t.RowHeights, t.Bounds.Y);
                var colOffsets = Cumulate(t.ColumnWidths, t.Bounds.X);

                // Determine visible row/col index ranges
                (int r0, int r1) = IntersectIndices(
                    rowOffsets,
                    t.RowHeights,
                    visibleWorldRect.Y,
                    visibleWorldRect.Bottom
                );
                (int c0, int c1) = IntersectIndices(
                    colOffsets,
                    t.ColumnWidths,
                    visibleWorldRect.X,
                    visibleWorldRect.Right
                );
                if (r0 > r1 || c0 > c1)
                {
                    continue;
                }

                // LOD thresholds
                double minRowHpx = double.PositiveInfinity;
                for (int r = r0; r <= r1; r++)
                {
                    var h = r >= 0 && r < t.RowHeights.Count ? t.RowHeights[r] : 0.0;
                    if (h > 0)
                        minRowHpx = Math.Min(minRowHpx, h * ppw);
                }
                double minColWpx = double.PositiveInfinity;
                for (int c = c0; c <= c1; c++)
                {
                    var w = c >= 0 && c < t.ColumnWidths.Count ? t.ColumnWidths[c] : 0.0;
                    if (w > 0)
                        minColWpx = Math.Min(minColWpx, w * ppw);
                }
                double minTextPx = options?.MinTextPixelHeight ?? 2.0;
                bool far =
                    (double.IsInfinity(minRowHpx) || minRowHpx < 2.0)
                    && (double.IsInfinity(minColWpx) || minColWpx < 2.0);
                bool near = Math.Min(minRowHpx, minColWpx) >= minTextPx;

                // Draw header fill (if any) and outer border in far LOD
                if (t.HeaderRowCount > 0)
                {
                    double headerTop = t.Bounds.Y;
                    double headerBottom = headerTop;
                    for (int i = 0; i < Math.Min(t.HeaderRowCount, t.Rows); i++)
                    {
                        headerBottom += (i < t.RowHeights.Count ? t.RowHeights[i] : 0.0);
                    }
                    var headerRectLocal = new Rect(
                        RenderHelpers.ToLocal(new Point(t.Bounds.X, headerTop)),
                        RenderHelpers.ToLocal(new Point(t.Bounds.X + t.Bounds.Width, headerBottom))
                    ).Normalize();
                    var headerFill = style.GetTableHeaderFill(t);
                    ctx.DrawRectangle(headerFill, null, headerRectLocal);
                }

                var gridPenBase = style.GetTableGridPen(t);
                var normPen = RenderHelpers.WorldPenNormalized(gridPenBase, ppw);
                // Outer border
                var outerLocal = new Rect(
                    RenderHelpers.ToLocal(t.Bounds.TopLeft),
                    RenderHelpers.ToLocal(t.Bounds.BottomRight)
                ).Normalize();
                ctx.DrawRectangle(null, normPen, outerLocal);

                // Selection highlight (draw on top of border for visibility)
                if (options != null && options.IsSelected(t.Id))
                {
                    var selPen = new Pen(normPen.Brush, Math.Max(2.0, normPen.Thickness * 2));
                    ctx.DrawRectangle(null, selPen, outerLocal);
                }

                // Clip drawing to table bounds to avoid overdraw
                using (ctx.PushClip(outerLocal))
                {
                    if (!far)
                    {
                        // Grid lines
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            // Vertical separators
                            double x = t.Bounds.X;
                            for (int c = 0; c < t.Columns - 1; c++)
                            {
                                double w = c < t.ColumnWidths.Count ? t.ColumnWidths[c] : 0.0;
                                x += w;
                                if (c < c0 - 1 || c > c1)
                                    continue;
                                var p0 = RenderHelpers.ToLocal(new Point(x, t.Bounds.Y));
                                var p1 = RenderHelpers.ToLocal(
                                    new Point(x, t.Bounds.Y + t.Bounds.Height)
                                );
                                g.BeginFigure(p0, isFilled: false);
                                g.LineTo(p1);
                                g.EndFigure(isClosed: false);
                            }
                            // Horizontal separators
                            double y = t.Bounds.Y;
                            for (int r = 0; r < t.Rows - 1; r++)
                            {
                                double h = r < t.RowHeights.Count ? t.RowHeights[r] : 0.0;
                                y += h;
                                if (r < r0 - 1 || r > r1)
                                    continue;
                                var p0 = RenderHelpers.ToLocal(new Point(t.Bounds.X, y));
                                var p1 = RenderHelpers.ToLocal(
                                    new Point(t.Bounds.X + t.Bounds.Width, y)
                                );
                                g.BeginFigure(p0, isFilled: false);
                                g.LineTo(p1);
                                g.EndFigure(isClosed: false);
                            }
                        }
                        ctx.DrawGeometry(null, normPen, geo);
                    }

                    // Optional alternating row shading for visible rows (subtle)
                    // Draw after grid to avoid overpainting borders; very low opacity
                    if (!far && r0 <= r1)
                    {
                        var baseFill = style.GetFillBrush(t.Layer);
                        if (baseFill is ISolidColorBrush sfb)
                        {
                            var col = sfb.Color;
                            for (int r = r0; r <= r1 && r < t.Rows; r++)
                            {
                                if ((r - (t.HeaderRowCount > 0 ? t.HeaderRowCount : 0)) % 2 != 0)
                                {
                                    double cy = rowOffsets[r];
                                    double ch = r < t.RowHeights.Count ? t.RowHeights[r] : 0.0;
                                    var rowRect = new Rect(
                                        RenderHelpers.ToLocal(new Point(t.Bounds.X, cy)),
                                        RenderHelpers.ToLocal(
                                            new Point(t.Bounds.X + t.Bounds.Width, cy + ch)
                                        )
                                    ).Normalize();
                                    // very subtle tint
                                    var tint = new SolidColorBrush(col) { Opacity = 0.06 };
                                    ctx.DrawRectangle(tint, null, rowRect);
                                }
                            }
                        }
                    }

                    if (near)
                    {
                        // Draw per-cell backgrounds and texts for visible anchor cells
                        var typeface = style.GetTableTextTypeface(t);
                        var textBrush = style.GetTableTextBrush(t);
                        for (int r = r0; r <= r1 && r < t.Rows; r++)
                        {
                            // Base world Y and row height
                            var rowH = r < t.RowHeights.Count ? t.RowHeights[r] : 0.0;
                            double cy = rowOffsets[r];

                            for (int c = c0; c <= c1 && c < t.Columns; c++)
                            {
                                // Determine if this cell is an anchor for a merged area
                                GetSpan(t, r, c, out int rs, out int cs);
                                bool isAnchor = rs >= 1 || cs >= 1;
                                if (!isAnchor)
                                {
                                    // Covered or no data for this cell
                                    continue;
                                }

                                double cx = colOffsets[c];
                                double cw = c < t.ColumnWidths.Count ? t.ColumnWidths[c] : 0.0;
                                double ch = rowH;
                                if (rs > 1)
                                {
                                    for (int rr = 1; rr < rs && (r + rr) < t.Rows; rr++)
                                    {
                                        ch +=
                                            (r + rr) < t.RowHeights.Count
                                                ? t.RowHeights[r + rr]
                                                : 0.0;
                                    }
                                }
                                if (cs > 1)
                                {
                                    for (int cc = 1; cc < cs && (c + cc) < t.Columns; cc++)
                                    {
                                        cw +=
                                            (c + cc) < t.ColumnWidths.Count
                                                ? t.ColumnWidths[c + cc]
                                                : 0.0;
                                    }
                                }

                                var boxLocal = new Rect(
                                    RenderHelpers.ToLocal(new Point(cx, cy)),
                                    RenderHelpers.ToLocal(new Point(cx + cw, cy + ch))
                                ).Normalize();

                                // Per-cell background
                                if (TryGetCellBackground(t, r, c, out var bgColor))
                                {
                                    // Paint opaque background to cover internal grid lines for merged spans
                                    var bgBrush = new SolidColorBrush(bgColor);
                                    ctx.DrawRectangle(bgBrush, null, boxLocal);
                                }

                                // Resolve text and decide wrapping
                                string? text = SafeGetCell(t, r, c);
                                if (!string.IsNullOrEmpty(text))
                                {
                                    // Slightly larger font for header rows
                                    double baseWorldH = ch; // use merged height
                                    double fontPx = Math.Max(
                                        1.0,
                                        Math.Min(baseWorldH * ppw * 0.8, 96.0)
                                    );
                                    if (r < Math.Max(0, t.HeaderRowCount))
                                    {
                                        fontPx = Math.Min(fontPx * 1.05, 96.0);
                                    }

                                    double pad = Math.Min(
                                        4.0,
                                        0.1 * Math.Min(boxLocal.Width, boxLocal.Height)
                                    );
                                    double availW = Math.Max(0, boxLocal.Width - 2 * pad);
                                    double availH = Math.Max(0, boxLocal.Height - 2 * pad);

                                    bool wrap = TryGetCellWrap(t, r, c, out var w) && w;
                                    // Measure single-line height
                                    double lineHeight = new FormattedText(
                                        "X",
                                        CultureInfo.InvariantCulture,
                                        FlowDirection.LeftToRight,
                                        typeface,
                                        fontPx,
                                        textBrush
                                    ).Height;

                                    List<string> lines;
                                    if (wrap)
                                    {
                                        lines = WrapToWidth(
                                            text,
                                            availW,
                                            typeface,
                                            fontPx,
                                            textBrush
                                        );
                                        int maxLines = Math.Max(
                                            1,
                                            (int)Math.Floor(availH / Math.Max(1.0, lineHeight))
                                        );
                                        if (lines.Count > maxLines)
                                        {
                                            // Ellipsize last line if we truncate
                                            lines = lines.GetRange(0, maxLines);
                                            lines[^1] = Ellipsize(
                                                lines[^1],
                                                availW,
                                                typeface,
                                                fontPx,
                                                textBrush
                                            );
                                        }
                                    }
                                    else
                                    {
                                        lines = new List<string>
                                        {
                                            Ellipsize(text, availW, typeface, fontPx, textBrush),
                                        };
                                    }

                                    // Alignment
                                    (CadTextHAlign ha, CadTextVAlign va) = GetCellAlign(t, r, c);
                                    double totalH = Math.Max(lineHeight, lines.Count * lineHeight);
                                    double baseY = va switch
                                    {
                                        CadTextVAlign.Top => boxLocal.Y + pad,
                                        CadTextVAlign.Middle => boxLocal.Y
                                            + pad
                                            + Math.Max(0, (availH - totalH) / 2.0),
                                        CadTextVAlign.Bottom => boxLocal.Bottom - pad - totalH,
                                        CadTextVAlign.Baseline => boxLocal.Bottom - pad - totalH,
                                        _ => boxLocal.Y + pad,
                                    };

                                    for (int i = 0; i < lines.Count; i++)
                                    {
                                        var s = lines[i];
                                        var ft = new FormattedText(
                                            s,
                                            CultureInfo.InvariantCulture,
                                            FlowDirection.LeftToRight,
                                            typeface,
                                            fontPx,
                                            textBrush
                                        );
                                        double tx = ha switch
                                        {
                                            CadTextHAlign.Left => boxLocal.X + pad,
                                            CadTextHAlign.Center => boxLocal.X
                                                + pad
                                                + Math.Max(0, (availW - ft.Width) / 2.0),
                                            CadTextHAlign.Right => boxLocal.Right - pad - ft.Width,
                                            _ => boxLocal.X + pad,
                                        };
                                        double ty = baseY + i * lineHeight;
                                        ctx.DrawText(ft, new Point(tx, ty));
                                    }
                                }

                                // Per-cell border overlay (drawn last)
                                if (TryGetCellBorder(t, r, c, out var borderColor))
                                {
                                    var bpen = RenderHelpers.WorldPenNormalized(
                                        new Pen(new SolidColorBrush(borderColor), 1.0),
                                        ppw
                                    );
                                    ctx.DrawRectangle(null, bpen, boxLocal);
                                }
                            }
                        }
                    }
                }

                drawn++;
            }

            Logger?.LogDebug("TableRenderer: total={Total}, drawn={Drawn}", total, drawn);
        }

        private static List<double> Cumulate(IReadOnlyList<double> sizes, double start)
        {
            var list = new List<double>(Math.Max(1, sizes?.Count ?? 0));
            double acc = start;
            if (sizes != null)
            {
                for (int i = 0; i < sizes.Count; i++)
                {
                    list.Add(acc);
                    acc += sizes[i];
                }
            }
            return list;
        }

        private static (int, int) IntersectIndices(
            List<double> offsets,
            IReadOnlyList<double> sizes,
            double min,
            double max
        )
        {
            if (offsets.Count == 0 || sizes.Count == 0)
            {
                return (1, 0); // empty
            }
            int first = -1,
                last = -1;
            for (int i = 0; i < sizes.Count; i++)
            {
                double a = offsets[i];
                double b = a + sizes[i];
                if (b >= min && a <= max)
                {
                    if (first == -1)
                        first = i;
                    last = i;
                }
            }
            if (first == -1)
                return (1, 0);
            return (first, last);
        }

        private static string? SafeGetCell(CadTable t, int r, int c)
        {
            if (t == null)
                throw new ArgumentNullException(nameof(t));
            if (t.Cells == null)
                return null;
            int rows = t.Cells.GetLength(0);
            int cols = t.Cells.GetLength(1);
            if (r < 0 || c < 0 || r >= rows || c >= cols)
            {
                return null;
            }
            return t.Cells[r, c];
        }

        private static void GetSpan(CadTable t, int r, int c, out int rowSpan, out int colSpan)
        {
            rowSpan = 1;
            colSpan = 1;
            if (t == null)
                throw new ArgumentNullException(nameof(t));
            if (t.CellRowSpan == null && t.CellColSpan == null)
            {
                return;
            }
            int rows = t.Cells?.GetLength(0) ?? t.Rows;
            int cols = t.Cells?.GetLength(1) ?? t.Columns;
            if (r < 0 || c < 0 || r >= rows || c >= cols)
            {
                rowSpan = 0;
                colSpan = 0;
                return;
            }
            if (t.CellRowSpan != null)
            {
                if (r < t.CellRowSpan.GetLength(0) && c < t.CellRowSpan.GetLength(1))
                {
                    rowSpan = Math.Max(0, t.CellRowSpan[r, c]);
                }
            }
            if (t.CellColSpan != null)
            {
                if (r < t.CellColSpan.GetLength(0) && c < t.CellColSpan.GetLength(1))
                {
                    colSpan = Math.Max(0, t.CellColSpan[r, c]);
                }
            }
            if (rowSpan == 0 && colSpan == 0)
            {
                // Covered cell
                return;
            }
            if (rowSpan < 1)
                rowSpan = 1;
            if (colSpan < 1)
                colSpan = 1;
        }

        private static bool TryGetCellBackground(CadTable t, int r, int c, out Color color)
        {
            color = default;
            if (t == null)
                throw new ArgumentNullException(nameof(t));
            var bg = t.CellBackgroundArgb;
            if (bg == null)
                return false;
            if (r < 0 || c < 0 || r >= bg.GetLength(0) || c >= bg.GetLength(1))
                return false;
            var v = bg[r, c];
            if (!v.HasValue)
                return false;
            color = ColorFromArgb(v.Value);
            return true;
        }

        private static bool TryGetCellBorder(CadTable t, int r, int c, out Color color)
        {
            color = default;
            if (t == null)
                throw new ArgumentNullException(nameof(t));
            var br = t.CellBorderArgb;
            if (br == null)
                return false;
            if (r < 0 || c < 0 || r >= br.GetLength(0) || c >= br.GetLength(1))
                return false;
            var v = br[r, c];
            if (!v.HasValue)
                return false;
            color = ColorFromArgb(v.Value);
            return true;
        }

        private static bool TryGetCellWrap(CadTable t, int r, int c, out bool wrap)
        {
            wrap = false;
            if (t == null)
                throw new ArgumentNullException(nameof(t));
            var w = t.CellWrap;
            if (w == null)
                return false;
            if (r < 0 || c < 0 || r >= w.GetLength(0) || c >= w.GetLength(1))
                return false;
            wrap = w[r, c];
            return true;
        }

        private static (CadTextHAlign, CadTextVAlign) GetCellAlign(CadTable t, int r, int c)
        {
            if (t == null)
                throw new ArgumentNullException(nameof(t));
            CadTextHAlign ha = CadTextHAlign.Left;
            CadTextVAlign va = CadTextVAlign.Middle;

            bool isHeader = r >= 0 && r < Math.Max(0, t.HeaderRowCount);
            if (isHeader)
            {
                ha = CadTextHAlign.Center;
                va = CadTextVAlign.Middle;
            }
            var haArr = t.CellHAlign;
            var vaArr = t.CellVAlign;
            if (haArr != null)
            {
                if (r >= 0 && c >= 0 && r < haArr.GetLength(0) && c < haArr.GetLength(1))
                {
                    ha = haArr[r, c];
                }
            }
            if (vaArr != null)
            {
                if (r >= 0 && c >= 0 && r < vaArr.GetLength(0) && c < vaArr.GetLength(1))
                {
                    va = vaArr[r, c];
                }
            }
            return (ha, va);
        }

        private static List<string> WrapToWidth(
            string text,
            double maxWidth,
            Typeface typeface,
            double fontPx,
            IBrush brush
        )
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
                return lines;
            var words = text.Split(' ', StringSplitOptions.None);
            string current = string.Empty;
            foreach (var w in words)
            {
                string candidate = string.IsNullOrEmpty(current) ? w : current + " " + w;
                var ft = new FormattedText(
                    candidate,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontPx,
                    brush
                );
                if (ft.Width <= maxWidth || string.IsNullOrEmpty(current))
                {
                    current = candidate;
                }
                else
                {
                    // commit current
                    lines.Add(current);
                    // if single word too long, hard-break
                    var wordFt = new FormattedText(
                        w,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontPx,
                        brush
                    );
                    if (wordFt.Width <= maxWidth)
                    {
                        current = w;
                    }
                    else
                    {
                        // hard-break long word
                        var parts = HardBreakWord(w, maxWidth, typeface, fontPx, brush);
                        if (parts.Count > 0)
                        {
                            lines.AddRange(parts.GetRange(0, parts.Count - 1));
                            current = parts[^1];
                        }
                        else
                        {
                            current = w;
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(current))
            {
                lines.Add(current);
            }
            return lines;
        }

        private static List<string> HardBreakWord(
            string word,
            double maxWidth,
            Typeface typeface,
            double fontPx,
            IBrush brush
        )
        {
            var parts = new List<string>();
            string current = string.Empty;
            foreach (char ch in word)
            {
                string candidate = current + ch;
                var ft = new FormattedText(
                    candidate,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontPx,
                    brush
                );
                if (ft.Width <= maxWidth || string.IsNullOrEmpty(current))
                {
                    current = candidate;
                }
                else
                {
                    parts.Add(current);
                    current = ch.ToString();
                }
            }
            if (!string.IsNullOrEmpty(current))
            {
                parts.Add(current);
            }
            return parts;
        }

        private static string Ellipsize(
            string text,
            double maxWidth,
            Typeface typeface,
            double fontPx,
            IBrush brush
        )
        {
            if (string.IsNullOrEmpty(text))
                return text;
            var ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontPx,
                brush
            );
            if (ft.Width <= maxWidth)
                return text;

            var ellipsis = "…";
            var eFt = new FormattedText(
                ellipsis,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontPx,
                brush
            );
            double target = Math.Max(0, maxWidth - eFt.Width);
            if (target <= 0)
                return ellipsis;
            int end = text.Length;
            while (end > 0)
            {
                string s = text.Substring(0, end);
                var sFt = new FormattedText(
                    s,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontPx,
                    brush
                );
                if (sFt.Width <= target)
                {
                    return s + ellipsis;
                }
                end--;
            }
            return ellipsis;
        }

        private static Color ColorFromArgb(uint argb)
        {
            var a = (byte)((argb >> 24) & 0xFF);
            var r = (byte)((argb >> 16) & 0xFF);
            var g = (byte)((argb >> 8) & 0xFF);
            var b = (byte)(argb & 0xFF);
            if (a == 0)
                a = 255;
            return Color.FromArgb(a, r, g, b);
        }
    }
}
