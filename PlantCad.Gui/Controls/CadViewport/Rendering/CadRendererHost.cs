using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.Renderers;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls.Rendering
{
    public sealed class CadRendererHost
    {
        private readonly IReadOnlyList<ICadEntityRenderer> _entityRenderers;
        private readonly IReadOnlyList<IOverlayRenderer> _preOverlays;
        private readonly IReadOnlyList<IOverlayRenderer> _postOverlays;
        private readonly ILogger? _logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "CadRendererHost"
        );
        private Rect? _lastGoodVisibleWorld;

        public CadRendererHost(
            IEnumerable<ICadEntityRenderer> entityRenderers,
            IEnumerable<IOverlayRenderer> preOverlays,
            IEnumerable<IOverlayRenderer> postOverlays
        )
        {
            _entityRenderers = new List<ICadEntityRenderer>(
                entityRenderers ?? throw new ArgumentNullException(nameof(entityRenderers))
            );
            _preOverlays = new List<IOverlayRenderer>(
                preOverlays ?? Array.Empty<IOverlayRenderer>()
            );
            _postOverlays = new List<IOverlayRenderer>(
                postOverlays ?? Array.Empty<IOverlayRenderer>()
            );
        }

        public void Render(
            DrawingContext ctx,
            ViewportState state,
            CadModel? model,
            Size viewportSize,
            IStyleProvider style,
            OverlayContext overlayContext,
            CadRenderOptions? options
        )
        {
            if (ctx == null)
                throw new ArgumentNullException(nameof(ctx));
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (style == null)
                throw new ArgumentNullException(nameof(style));
            if (overlayContext == null)
                throw new ArgumentNullException(nameof(overlayContext));

            var bounds = new Rect(viewportSize);

            // Background
            ctx.FillRectangle(Brushes.White, bounds);

            // Pre overlays (e.g., grid)
            foreach (var overlay in _preOverlays)
            {
                overlay.Render(ctx, viewportSize, state, overlayContext);
            }

            if (model != null)
            {
                // Entities: derive visible world rectangle from current viewport via inverse transform
                var screenBounds = new Rect(viewportSize);
                var visibleWorld = state.ScreenToWorld(screenBounds);
                if (!IsFiniteRect(visibleWorld))
                {
                    var fallback =
                        _lastGoodVisibleWorld
                        ?? new Rect(-1_000_000, -1_000_000, 2_000_000, 2_000_000);
                    _logger?.LogWarning(
                        "Invalid visibleWorld computed; using fallback last-good rectangle: ({X:0.###},{Y:0.###})-({R:0.###},{B:0.###})",
                        fallback.X,
                        fallback.Y,
                        fallback.Right,
                        fallback.Bottom
                    );
                    visibleWorld = fallback;
                }
                else
                {
                    _lastGoodVisibleWorld = visibleWorld;
                }
                _logger?.LogDebug(
                    "Render frame: Transform=[{M11:0.###} {M12:0.###} {M21:0.###} {M22:0.###} | {M31:0.###} {M32:0.###}], VisibleWorld=({X:0.###},{Y:0.###})-({R:0.###},{B:0.###})",
                    state.Transform.M11,
                    state.Transform.M12,
                    state.Transform.M21,
                    state.Transform.M22,
                    state.Transform.M31,
                    state.Transform.M32,
                    visibleWorld.X,
                    visibleWorld.Y,
                    visibleWorld.Right,
                    visibleWorld.Bottom
                );
                // Split renderers: world-space geometry vs screen-space
                var worldRenderers = new List<ICadEntityRenderer>();
                var screenRenderers = new List<ICadEntityRenderer>();
                var hatches = new List<ICadEntityRenderer>();
                var solids = new List<ICadEntityRenderer>();
                var underlays = new List<ICadEntityRenderer>();
                var wipeouts = new List<ICadEntityRenderer>();
                var polylines = new List<ICadEntityRenderer>();
                var lines = new List<ICadEntityRenderer>();
                var points = new List<ICadEntityRenderer>();
                var annotations = new List<ICadEntityRenderer>();
                foreach (var r in _entityRenderers)
                {
                    if (r is PlantCad.Gui.Controls.Renderers.UnderlayRenderer)
                    {
                        underlays.Add(r);
                    }
                    else if (r is HatchRenderer)
                    {
                        hatches.Add(r);
                    }
                    else if (r is SolidRenderer)
                    {
                        solids.Add(r);
                    }
                    else if (r is PlantCad.Gui.Controls.Renderers.WipeoutRenderer)
                    {
                        wipeouts.Add(r);
                    }
                    else if (r is PolylineRenderer)
                    {
                        polylines.Add(r);
                    }
                    else if (
                        r is LineRenderer
                        || r is PlantCad.Gui.Controls.Renderers.RayRenderer
                        || r is PlantCad.Gui.Controls.Renderers.XLineRenderer
                    )
                    {
                        lines.Add(r);
                    }
                    else if (r is PlantCad.Gui.Controls.Renderers.PointRenderer)
                    {
                        points.Add(r);
                    }
                    else if (
                        r is PlantCad.Gui.Controls.Renderers.LeaderRenderer
                        || r is PlantCad.Gui.Controls.Renderers.DimensionRenderer
                        || r is PlantCad.Gui.Controls.Renderers.TableRenderer
                    )
                    {
                        annotations.Add(r);
                    }
                    else
                    {
                        screenRenderers.Add(r);
                    }
                }
                
                // World pass order: underlays (deep background) -> fills (background) -> polylines -> lines (foreground)
                worldRenderers.AddRange(underlays);
                worldRenderers.AddRange(solids);
                // Wipeouts should mask underlying content; draw after fills and before strokes
                worldRenderers.AddRange(wipeouts);
                worldRenderers.AddRange(polylines);
                worldRenderers.AddRange(hatches);

                worldRenderers.AddRange(lines);
                worldRenderers.AddRange(points);
                worldRenderers.AddRange(annotations);

                var localOrigin = new Point(visibleWorld.Center.X, visibleWorld.Center.Y);
                var composed = RenderHelpers.ComposeWithLocalOrigin(state.Transform, localOrigin);
                using (RenderHelpers.PushLocalOrigin(localOrigin))
                using (ctx.PushTransform(composed))
                {
                    foreach (var renderer in worldRenderers)
                    {
                        renderer.Render(ctx, state, model, style, options, visibleWorld);
                    }
                }

                foreach (var renderer in screenRenderers)
                {
                    renderer.Render(ctx, state, model, style, options, visibleWorld);
                }
            }

            // Post overlays (e.g., selection rectangle)
            foreach (var overlay in _postOverlays)
            {
                overlay.Render(ctx, viewportSize, state, overlayContext);
            }
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        private static bool IsFiniteRect(Rect r)
        {
            return IsFinite(r.X)
                && IsFinite(r.Y)
                && IsFinite(r.Width)
                && IsFinite(r.Height)
                && r.Width >= 0
                && r.Height >= 0;
        }
    }
}
