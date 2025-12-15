using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Controls.Rendering.Skia;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;
using SkiaSharp;

namespace PlantCad.Gui.Controls.Renderers
{
    public sealed class UnderlayRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "UnderlayRenderer"
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
            if (model.Underlays is null || model.Underlays.Count == 0)
            {
                return;
            }

            var resolver = ServiceRegistry.UnderlayImageResolver;
            if (resolver == null)
            {
                // No resolver registered; skip drawing underlays
                return;
            }

            int total = 0,
                drawn = 0,
                culled = 0;

            foreach (var u in model.Underlays)
            {
                total++;
                if (options != null && !options.IsLayerVisible(u.Layer))
                {
                    continue;
                }

                // Resolve image
                SKImage? image = null;
                try
                {
                    image = resolver.Resolve(u);
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Underlay image resolve failed for {Id}", u.Id);
                }
                if (image == null)
                {
                    continue;
                }

                // Determine world bounds for culling and Skia op bounds
                Rect worldBounds = ComputeWorldBounds(u, image);

                // Culling against expanded visible rect
                var marginPx = Math.Max(8.0, options?.EdgeCullingMarginPx ?? 64.0);
                var worldMin = Math.Max(0.0, options?.EdgeCullingMinWorld ?? 5.0);
                var percent = Math.Max(0.0, Math.Min(0.5, options?.EdgeCullingPercent ?? 0.10));
                var expanded = RenderHelpers.ExpandVisibleRect(
                    state,
                    visibleWorldRect,
                    marginPx,
                    worldMin,
                    percent
                );
                if (!worldBounds.Intersects(expanded) && !expanded.Contains(worldBounds))
                {
                    culled++;
                    continue;
                }

                double opacity = style.GetUnderlayOpacity(u);
                // Apply fade factor (0..1); higher fade reduces opacity
                opacity = Math.Clamp(opacity * (1.0 - Math.Clamp(u.Fade, 0.0, 1.0)), 0.0, 1.0);
                IBrush? tintBrush = style.GetUnderlayTintBrush(u);
                Color? tintColor = null;
                if (tintBrush is ISolidColorBrush sb)
                {
                    tintColor = sb.Color;
                }

                var clipLoops = u.ClipLoops;
                var drawOp = new SkiaUnderlayDrawOp(
                    worldBounds,
                    u,
                    image,
                    state,
                    opacity,
                    tintColor,
                    clipLoops
                );
                ctx.Custom((ICustomDrawOperation)drawOp);
                drawn++;
            }

            Logger?.LogDebug(
                "UnderlayRenderer: total={Total}, drawn={Drawn}, culled={Culled}",
                total,
                drawn,
                culled
            );
        }

        private static Rect ComputeWorldBounds(CadUnderlay u, SKImage image)
        {
            // Prefer explicit transform; fallback to quad; fallback to size at origin
            if (u.WorldTransform.HasValue)
            {
                var m = u.WorldTransform.Value;
                var corners = new[]
                {
                    new Point(0, 0),
                    new Point(image.Width, 0),
                    new Point(image.Width, image.Height),
                    new Point(0, image.Height),
                };
                var wc = corners.Select(p => TransformPoint(m, p)).ToArray();
                return RectFromPoints(wc);
            }
            if (u.WorldQuad != null && u.WorldQuad.Length >= 3)
            {
                return RectFromPoints(u.WorldQuad);
            }
            // Default: place native size at world origin
            return new Rect(0, 0, image.Width, image.Height);
        }

        private static Rect RectFromPoints(System.Collections.Generic.IReadOnlyList<Point> pts)
        {
            double minX = double.PositiveInfinity,
                minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity,
                maxY = double.NegativeInfinity;
            foreach (var p in pts)
            {
                if (p.X < minX)
                    minX = p.X;
                if (p.Y < minY)
                    minY = p.Y;
                if (p.X > maxX)
                    maxX = p.X;
                if (p.Y > maxY)
                    maxY = p.Y;
            }
            if (
                double.IsInfinity(minX)
                || double.IsInfinity(minY)
                || double.IsInfinity(maxX)
                || double.IsInfinity(maxY)
            )
            {
                return new Rect(0, 0, 0, 0);
            }
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private static Point TransformPoint(Matrix m, Point p)
        {
            return new Point(m.M11 * p.X + m.M12 * p.Y + m.M31, m.M21 * p.X + m.M22 * p.Y + m.M32);
        }
    }
}
