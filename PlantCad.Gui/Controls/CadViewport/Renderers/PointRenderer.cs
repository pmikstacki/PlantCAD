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
    public sealed class PointRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "PointRenderer"
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
            if (model.Points == null || model.Points.Count == 0)
            {
                return;
            }

            var pxPerWorld = RenderHelpers.PixelsPerWorld(state);
            if (pxPerWorld <= 1e-12)
            {
                // Degenerate transform; skip rendering this frame
                return;
            }

            var sizePx = Math.Max(0.0, style.GetPointSizePx());
            // LOD: if too small and not selected, skip all to save work
            if (sizePx < 0.5)
            {
                sizePx = 0.5; // clamp to avoid zero-length math; still may end up too small to see
            }
            var halfWorld = (sizePx * 0.5) / pxPerWorld;

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

            var penCache = new Dictionary<string, Pen>(StringComparer.OrdinalIgnoreCase);
            int total = 0,
                drawn = 0,
                culled = 0;

            foreach (var p in model.Points)
            {
                total++;
                if (options != null && !options.IsLayerVisible(p.Layer))
                {
                    continue;
                }
                if (!expandedRect.Contains(p.Position))
                {
                    culled++;
                    continue;
                }

                // Skip if size would be effectively invisible and point is not selected
                bool isSelected = options?.IsSelected(p.Id) ?? false;
                if (!isSelected && sizePx < 1.0)
                {
                    continue;
                }

                var basePen = style.GetStrokePen(p.Layer);
                var layerKey = p.Layer ?? string.Empty;
                if (!penCache.TryGetValue(layerKey, out var pen))
                {
                    pen = RenderHelpers.WorldPenNormalized(
                        basePen,
                        pxPerWorld,
                        minPixelThickness: 1.0
                    );
                    penCache[layerKey] = pen;
                }

                var c = RenderHelpers.ToLocal(p.Position);
                // Draw a cross marker
                var left = new Point(c.X - halfWorld, c.Y);
                var right = new Point(c.X + halfWorld, c.Y);
                var top = new Point(c.X, c.Y - halfWorld);
                var bottom = new Point(c.X, c.Y + halfWorld);
                ctx.DrawLine(pen, left, right);
                ctx.DrawLine(pen, top, bottom);
                drawn++;
            }

            Logger?.LogDebug(
                "PointRenderer: total={Total}, culled={Culled}, drawn={Drawn}",
                total,
                culled,
                drawn
            );
        }
    }
}
