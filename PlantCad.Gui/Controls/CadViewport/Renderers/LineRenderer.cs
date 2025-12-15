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
    public sealed class LineRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "LineRenderer"
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
            if (model.Lines == null || model.Lines.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            int bboxCulled = 0;
            int lodTooShort = 0;
            int dashFallback = 0;
            Point? sampleAWorld = null;
            Point? sampleBWorld = null;
            Point? sampleAScreen = null;
            Point? sampleBScreen = null;
            // Compute pixels-per-world once and prepare a small layer->pen cache
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
            foreach (var ln in model.Lines)
            {
                total++;
                if (options != null && !options.IsLayerVisible(ln.Layer))
                {
                    continue;
                }
                var isSelected = options?.IsSelected(ln.Id) ?? false;
                var selectedExpandedRect = isSelected
                    ? RenderHelpers.ExpandVisibleRect(
                        state,
                        visibleWorldRect,
                        marginPx * 2.0,
                        worldMin,
                        percent
                    )
                    : expandedRect;
                var rectForCulling = selectedExpandedRect;
                // culling by bbox (inflate to avoid zero-area rejection)
                var minX = Math.Min(ln.Start.X, ln.End.X);
                var minY = Math.Min(ln.Start.Y, ln.End.Y);
                var maxX = Math.Max(ln.Start.X, ln.End.X);
                var maxY = Math.Max(ln.Start.Y, ln.End.Y);
                const double pad = 1e-3;
                var bb = new Rect(
                    minX - pad,
                    minY - pad,
                    (maxX - minX) + 2 * pad,
                    (maxY - minY) + 2 * pad
                );
                if (
                    !bb.Intersects(rectForCulling)
                    && !rectForCulling.Contains(ln.Start)
                    && !rectForCulling.Contains(ln.End)
                )
                {
                    if (isSelected)
                    {
                        Logger?.LogWarning(
                            "LineRenderer: selected line culled by bbox. id={Id}, layer={Layer}, bb=({X:0.###},{Y:0.###},{W:0.###},{H:0.###}), expanded=({RX:0.###},{RY:0.###},{RW:0.###},{RH:0.###})",
                            ln.Id,
                            ln.Layer,
                            bb.X,
                            bb.Y,
                            bb.Width,
                            bb.Height,
                            rectForCulling.X,
                            rectForCulling.Y,
                            rectForCulling.Width,
                            rectForCulling.Height
                        );
                    }
                    bboxCulled++;
                    continue;
                }

                var aS = state.WorldToScreen(ln.Start);
                var bS = state.WorldToScreen(ln.End);
                var basePen = style.GetStrokePen(ln.Layer);
                var thicknessPx = Math.Max(0.1, basePen.Thickness);
                var defaultMinLen = Math.Min(1.0, 0.5 * thicknessPx);
                var minLenBase = options?.MinCurvePixelLength ?? defaultMinLen;
                var minLen = isSelected ? 0.0 : Math.Min(minLenBase, 0.1);
                var visiblePx = RenderHelpers.VisibleWorldLengthPx(
                    ln.Start,
                    ln.End,
                    selectedExpandedRect,
                    pxPerWorld
                );
                bool lodSkippedThis = false;
                bool drawnThis = false;
                if (!isSelected && visiblePx < minLen)
                {
                    lodTooShort++;
                    lodSkippedThis = true;
                    continue;
                }
                var layerKey = ln.Layer ?? string.Empty;
                if (!worldPenCache.TryGetValue(layerKey, out var pen))
                {
                    pen = RenderHelpers.WorldPenNormalized(
                        basePen,
                        pxPerWorld,
                        minPixelThickness: 1.5
                    );
                    worldPenCache[layerKey] = pen;
                }
                // Dashed short segment fallback: if the segment is shorter than one dash cycle, draw solid
                var segPen = pen;
                double dashPeriodPx = double.PositiveInfinity;
                var ds = basePen.DashStyle?.Dashes;
                if (ds != null && ds.Count > 0)
                {
                    double dashSum = 0.0;
                    for (int i = 0; i < ds.Count; i++)
                        dashSum += Math.Abs(ds[i]);
                    if (dashSum > 0)
                        dashPeriodPx = dashSum;
                }
                if (dashPeriodPx < double.PositiveInfinity && visiblePx <= dashPeriodPx + 1e-6)
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
                // With CadRendererHost pushing composed transform, draw using local coordinates (world - origin)
                var aL = RenderHelpers.ToLocal(ln.Start);
                var bL = RenderHelpers.ToLocal(ln.End);
                ctx.DrawLine(segPen, aL, bL);
                drawn++;
                drawnThis = true;
                if (sampleAWorld is null)
                {
                    sampleAWorld = ln.Start;
                    sampleBWorld = ln.End;
                    sampleAScreen = aS;
                    sampleBScreen = bS;
                }
                if (isSelected)
                {
                    Logger?.LogWarning(
                        "LineRenderer: selected line summary id={Id}, layer={Layer}, drawn={Drawn}, lodSkipped={Lod}, bb=({X:0.###},{Y:0.###},{W:0.###},{H:0.###}), expanded=({RX:0.###},{RY:0.###},{RW:0.###},{RH:0.###})",
                        ln.Id,
                        ln.Layer,
                        drawnThis,
                        lodSkippedThis,
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
            }
            Logger?.LogInformation(
                "LineRenderer: total={Total}, bboxCulled={Box}, lodTooShort={Lod}, dashFallback={Dash}, drawn={Drawn}",
                total,
                bboxCulled,
                lodTooShort,
                dashFallback,
                drawn
            );
        }
    }
}
