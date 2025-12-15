using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using PlantCad.Gui.Controls.Renderers;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Controls.Rendering
{
    /// <summary>
    /// Minimal renderer host for thumbnails/previews. No overlays, no background fill, no logging.
    /// Renders world-space geometry with the current ViewportState transform and performs basic culling
    /// using the visible world rectangle derived from the current viewport size.
    /// </summary>
    public sealed class SimpleCadRendererHost
    {
        private readonly IReadOnlyList<ICadEntityRenderer> _entityRenderers;

        public SimpleCadRendererHost(IEnumerable<ICadEntityRenderer> entityRenderers)
        {
            if (entityRenderers == null)
                throw new ArgumentNullException(nameof(entityRenderers));
            _entityRenderers = new List<ICadEntityRenderer>(entityRenderers);
        }

        public void Render(
            DrawingContext ctx,
            ViewportState state,
            CadModel model,
            Size viewportSize,
            IStyleProvider style,
            CadRenderOptions? options
        )
        {
            if (ctx == null)
                throw new ArgumentNullException(nameof(ctx));
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (style == null)
                throw new ArgumentNullException(nameof(style));

            var screenBounds = new Rect(viewportSize);
            var visibleWorld = state.ScreenToWorld(screenBounds);

            // Order: background fills first, then strokes/curves, then the rest
            var hatches = new List<ICadEntityRenderer>();
            var solids = new List<ICadEntityRenderer>();
            var wipeouts = new List<ICadEntityRenderer>();
            var polylines = new List<ICadEntityRenderer>();
            var lines = new List<ICadEntityRenderer>();
            var rest = new List<ICadEntityRenderer>();

            foreach (var r in _entityRenderers)
            {
                if (r is HatchRenderer)
                    hatches.Add(r);
                else if (r is SolidRenderer)
                    solids.Add(r);
                else if (r is PlantCad.Gui.Controls.Renderers.WipeoutRenderer)
                    wipeouts.Add(r);
                else if (r is PolylineRenderer)
                    polylines.Add(r);
                else if (
                    r is LineRenderer
                    || r is PlantCad.Gui.Controls.Renderers.RayRenderer
                    || r is PlantCad.Gui.Controls.Renderers.XLineRenderer
                )
                    lines.Add(r);
                else
                    rest.Add(r);
            }

            using (ctx.PushTransform(state.Transform))
            {
                foreach (var r in hatches)
                    r.Render(ctx, state, model, style, options, visibleWorld);
                foreach (var r in solids)
                    r.Render(ctx, state, model, style, options, visibleWorld);
                foreach (var r in wipeouts)
                    r.Render(ctx, state, model, style, options, visibleWorld);
                foreach (var r in polylines)
                    r.Render(ctx, state, model, style, options, visibleWorld);
                foreach (var r in lines)
                    r.Render(ctx, state, model, style, options, visibleWorld);
                foreach (var r in rest)
                    r.Render(ctx, state, model, style, options, visibleWorld);
            }
        }
    }
}
