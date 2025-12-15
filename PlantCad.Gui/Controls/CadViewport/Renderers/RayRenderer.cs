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
    public sealed class RayRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "RayRenderer"
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
            if (model.Rays == null || model.Rays.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            int culled = 0;
            int lodSkipped = 0;
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

            foreach (var r in model.Rays)
            {
                total++;
                if (options != null && !options.IsLayerVisible(r.Layer))
                {
                    continue;
                }
                var penBase = style.GetStrokePen(r.Layer);
                // Clip the ray to the expanded rect
                if (
                    !RenderHelpers.ClipRayToRect(
                        r.Origin,
                        r.Direction,
                        expandedRect,
                        out var aW,
                        out var bW
                    )
                )
                {
                    culled++;
                    continue;
                }
                // LOD: skip if visible length smaller than threshold (unless selected)
                var isSelected = options?.IsSelected(r.Id) ?? false;
                var minLenBase = options?.MinCurvePixelLength ?? Math.Max(0.5, penBase.Thickness);
                var minLenEff = isSelected ? 0.0 : Math.Min(minLenBase, 0.75);
                var visiblePx = RenderHelpers.VisibleWorldLengthPx(
                    aW,
                    bW,
                    expandedRect,
                    pxPerWorld
                );
                if (!isSelected && visiblePx < minLenEff)
                {
                    lodSkipped++;
                    continue;
                }
                // Normalize pen to keep on-screen thickness consistent
                var pen = RenderHelpers.WorldPenNormalized(
                    penBase,
                    pxPerWorld,
                    minPixelThickness: 1.5
                );
                var aL = RenderHelpers.ToLocal(aW);
                var bL = RenderHelpers.ToLocal(bW);
                ctx.DrawLine(pen, aL, bL);
                drawn++;
            }

            Logger?.LogInformation(
                "RayRenderer: total={Total}, drawn={Drawn}, culled={Culled}, lodSkipped={Lod}",
                total,
                drawn,
                culled,
                lodSkipped
            );
        }
    }
}
