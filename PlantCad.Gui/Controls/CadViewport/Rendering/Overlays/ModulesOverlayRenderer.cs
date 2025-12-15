using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using PlantCad.Gui.Controls.Viewport;

namespace PlantCad.Gui.Controls.Rendering
{
    /// <summary>
    /// Renders module polygons as a screen-space overlay, using world-space points
    /// provided via a delegate. This is decoupled from services; wiring can set the
    /// ShapesProvider at runtime.
    /// </summary>
    public sealed class ModulesOverlayRenderer : IOverlayRenderer
    {
        public static Func<IEnumerable<IReadOnlyList<Point>>>? ShapesProvider { get; set; }
        // Optional: handles (world-space) to support editing visuals (e.g., vertices)
        public static Func<IEnumerable<Point>>? HandlesProvider { get; set; }
        // Optional: hovered edge provider (world-space segment) for highlighting during editing
        public static Func<(Point a, Point b)?>? HoveredEdgeProvider { get; set; }
        // Optional: labels provider (world-space position, text)
        public static Func<IEnumerable<(Point pos, string text)>>? LabelsProvider { get; set; }
        // Optional: cards provider (world-space position, text, module id) for world-anchored cards with wrench action
        public static Func<IEnumerable<(Point pos, string text, string id)>>? CardsProvider { get; set; }
        // Optional: selected handle provider (world-space point)
        public static Func<Point?>? SelectedHandleProvider { get; set; }
        // Optional: whether an editor is currently active (so we still render editing visuals even if overlay hidden)
        public static Func<bool>? IsEditingProvider { get; set; }
        // Optional: active polygon provider (world-space points) to emphasize during editing
        public static Func<IReadOnlyList<Point>?>? ActivePolygonProvider { get; set; }

        public static bool Visible { get; set; } = true;

        private readonly SolidColorBrush _fillBrush;
        private readonly Pen _strokePen;
        private readonly Pen _hoverPen;
        private static readonly List<(string id, Rect rect)> _cardIconHits = new();

        public static string? HitTestCardIcon(Point screen)
        {
            foreach (var (id, rect) in _cardIconHits)
            {
                if (rect.Contains(screen))
                    return id;
            }
            return null;
        }

        public ModulesOverlayRenderer()
        {
            _fillBrush = new SolidColorBrush(Color.FromArgb(64, 30, 144, 255)); // semi-transparent
            _strokePen = new Pen(new SolidColorBrush(Color.FromArgb(200, 30, 144, 255)), 1.5);
            _hoverPen = new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 196, 0)), 3.0);
        }

        public void Render(
            DrawingContext ctx,
            Size viewportSize,
            ViewportState state,
            OverlayContext context
        )
        {
            _cardIconHits.Clear();
            var isEditing = IsEditingProvider?.Invoke() == true;
            if (!Visible && !isEditing)
            {
                return;
            }

            var provider = ShapesProvider;

            if (Visible && provider != null)
            {
                var visibleWorld = state.ScreenToWorld(new Rect(viewportSize));
                foreach (var worldPoly in provider())
                {
                    if (worldPoly == null || worldPoly.Count < 3)
                    {
                        continue;
                    }
                    // Quick reject by world bounding box
                    double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                    double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                    foreach (var p in worldPoly)
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                    }
                    var bb = new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
                    if (!bb.Intersects(visibleWorld) && !visibleWorld.Contains(bb))
                    {
                        continue;
                    }

                    // Convert to screen and build a filled geometry
                    var screenPts = worldPoly.Select(state.WorldToScreen).ToList();
                    if (screenPts.Count == 0)
                        continue;

                    var geometry = new StreamGeometry();
                    using (var gctx = geometry.Open())
                    {
                        gctx.BeginFigure(screenPts[0], isFilled: true);
                        for (int i = 1; i < screenPts.Count; i++)
                        {
                            gctx.LineTo(screenPts[i]);
                        }
                        // Close polygon
                        gctx.EndFigure(isClosed: true);
                    }
                    ctx.DrawGeometry(_fillBrush, _strokePen, geometry);
                }
            }

            // Emphasize active polygon outline when available (drawn before handles so handles stay on top)
            var active = ActivePolygonProvider?.Invoke();
            if (active != null && active.Count >= 2)
            {
                var outline = new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 196, 0)), 2.5);
                var spts = active.Select(state.WorldToScreen).ToList();
                for (int i = 0; i < spts.Count; i++)
                {
                    var a = spts[i];
                    var b = spts[(i + 1) % spts.Count];
                    ctx.DrawLine(outline, a, b);
                }
            }

            // Draw handles if provided
            var handles = HandlesProvider?.Invoke();
            if (handles != null)
            {
                var handleBrush = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170));
                var handlePen = new Pen(Brushes.Black, 1.5);
                const double r = 6.0;
                foreach (var h in handles)
                {
                    var s = state.WorldToScreen(h);
                    var rect = new Rect(s.X - r, s.Y - r, 2 * r, 2 * r);
                    ctx.DrawEllipse(handleBrush, handlePen, rect.Center, r, r);
                }
            }

            // Draw selected handle on top, if available
            var selected = SelectedHandleProvider?.Invoke();
            if (selected.HasValue)
            {
                var sh = state.WorldToScreen(selected.Value);
                const double rSel = 8.0;
                var selBrush = new SolidColorBrush(Color.FromArgb(255, 255, 196, 0));
                var selPen = new Pen(Brushes.Black, 2.5);
                var rect = new Rect(sh.X - rSel, sh.Y - rSel, 2 * rSel, 2 * rSel);
                ctx.DrawEllipse(selBrush, selPen, rect.Center, rSel, rSel);
            }

            // Draw hovered edge highlight if available
            var hovered = HoveredEdgeProvider?.Invoke();
            if (hovered.HasValue)
            {
                var (a, b) = hovered.Value;
                var sa = state.WorldToScreen(a);
                var sb = state.WorldToScreen(b);
                ctx.DrawLine(_hoverPen, sa, sb);
            }

            // Draw world-anchored cards (preferred), otherwise fallback to plain labels
            if (Visible)
            {
                var cards = CardsProvider?.Invoke();
                if (cards != null)
                {
                    var typeface = new Typeface("Segoe UI");
                    foreach (var (pos, text, id) in cards)
                    {
                        var s = state.WorldToScreen(pos);
                        var ft = new FormattedText(
                            text ?? string.Empty,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            12,
                            Brushes.White
                        );
                        const double pad = 6.0;
                        const double iconSize = 16.0;
                        var bgRect = new Rect(
                            s.X + 6,
                            s.Y - ft.Height - pad - 6,
                            Math.Max(24, ft.Width + iconSize + pad * 2),
                            ft.Height + pad * 2
                        );
                        var bg = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0));
                        var border = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1.0);
                        ctx.DrawRectangle(bg, border, bgRect, 6, 6);
                        // Text
                        var textPos = new Point(bgRect.X + pad, bgRect.Y + pad);
                        ctx.DrawText(ft, textPos);
                        // Wrench icon (simple glyph drawing)
                        var iconRect = new Rect(bgRect.Right - pad - iconSize, bgRect.Y + pad, iconSize, iconSize);
                        DrawWrenchIcon(ctx, iconRect);
                        _cardIconHits.Add((id ?? string.Empty, iconRect));
                    }
                }
                else
                {
                    var labels = LabelsProvider?.Invoke();
                    if (labels != null)
                    {
                        var typeface = new Typeface("Segoe UI");
                        foreach (var (pos, text) in labels)
                        {
                            var s = state.WorldToScreen(pos);
                            var ft = new FormattedText(
                                text ?? string.Empty,
                                CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight,
                                typeface,
                                12,
                                Brushes.White
                            );
                            ctx.DrawText(ft, new Point(s.X + 6, s.Y - 6));
                        }
                    }
                }
            }
        }

        private static void DrawWrenchIcon(DrawingContext ctx, Rect r)
        {
            // Simple stylized wrench: a small circle and diagonal handle
            var pen = new Pen(Brushes.White, 1.5);
            // Handle
            var a = new Point(r.X + r.Width * 0.2, r.Y + r.Height * 0.8);
            var b = new Point(r.X + r.Width * 0.8, r.Y + r.Height * 0.2);
            ctx.DrawLine(pen, a, b);
            // Head circle
            var c = new Point(b.X, b.Y);
            ctx.DrawEllipse(null, pen, c, r.Width * 0.2, r.Height * 0.2);
        }
    }
}
