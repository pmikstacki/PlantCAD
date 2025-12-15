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
    public sealed class XLineRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "XLineRenderer"
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
            if (model.XLines == null || model.XLines.Count == 0)
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

            foreach (var xl in model.XLines)
            {
                total++;
                if (options != null && !options.IsLayerVisible(xl.Layer))
                {
                    continue;
                }
                var penBase = style.GetStrokePen(xl.Layer);
                if (
                    !RenderHelpers.ClipInfiniteLineToRect(
                        xl.Origin,
                        xl.Direction,
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
                var isSelected = options?.IsSelected(xl.Id) ?? false;
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
                "XLineRenderer: total={Total}, drawn={Drawn}, culled={Culled}, lodSkipped={Lod}",
                total,
                drawn,
                culled,
                lodSkipped
            );
        }
    }
}
