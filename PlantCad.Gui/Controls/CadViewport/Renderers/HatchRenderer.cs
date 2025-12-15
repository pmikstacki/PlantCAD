using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.CadViewport.Rendering.Skia;
using PlantCad.Gui.Controls.Debug;
using PlantCad.Gui.Controls.Hatching;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Controls.Rendering.Skia;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls.Renderers
{
    public sealed class HatchRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "HatchRenderer"
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
            if (model.Hatches == null || model.Hatches.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            foreach (var h in model.Hatches)
            {
                total++;
                if (options != null && !options.IsLayerVisible(h.Layer))
                {
                    continue;
                }
                if (h.Loops == null || h.Loops.Count == 0)
                {
                    continue;
                }
                var bb = BoundsFromLoops(h.Loops);
                if (!bb.Intersects(visibleWorldRect) && !visibleWorldRect.Contains(bb))
                {
                    continue;
                }

                var drawBounds = visibleWorldRect;

                var geo = new StreamGeometry();
                using (var gctx = geo.Open())
                {
                    bool firstLoop = true;
                    for (int li = 0; li < h.Loops.Count; li++)
                    {
                        var loop = h.Loops[li];
                        if (loop == null || loop.Count < 3)
                        {
                            continue;
                        }
                        // Adaptive decimation based on zoom (projected screen distance)
                        var decimated = DecimateLoop(loop, state, 0.75);
                        if (decimated.Count < 3)
                        {
                            continue;
                        }
                        // Enforce orientation if provided (outer vs holes)
                        if (h.LoopClockwise != null && li < h.LoopClockwise.Count)
                        {
                            bool cw = h.LoopClockwise[li];
                            // Prefer outer as CCW; if CW, reverse
                            if (cw)
                            {
                                decimated.Reverse();
                            }
                        }
                        var first = RenderHelpers.ToLocal(decimated[0]);
                        if (firstLoop)
                        {
                            gctx.BeginFigure(first, isFilled: true);
                            firstLoop = false;
                        }
                        else
                        {
                            // Begin a new figure for holes
                            gctx.BeginFigure(first, isFilled: true);
                        }
                        for (int i = 1; i < decimated.Count; i++)
                        {
                            var p = RenderHelpers.ToLocal(decimated[i]);
                            gctx.LineTo(p);
                        }
                        gctx.EndFigure(isClosed: true);
                    }
                }
                // Fill selection: solid, gradient, or pattern
                IBrush? fill = null;
                if (h.FillKind == CadHatchFillKind.Gradient)
                {
                    fill = CreateGradientBrush(h, style);
                    // Gradient hatches often coexist with explicit boundary geometry in blocks.
                    // Drawing an additional stroke here can double the apparent outline thickness.
                    ctx.DrawGeometry(fill, null, geo);
                    drawn++;
                    continue;
                }
                else if (
                    h.FillKind == CadHatchFillKind.Pattern
                    && !string.Equals(h.PatternName, "SOLID", StringComparison.OrdinalIgnoreCase)
                )
                {
                    // Pattern fills should not draw a solid underlay. Draw only pattern lines clipped to the hatch geometry.
                    using (ctx.PushGeometryClip(geo))
                    {
                        // Use CPU path for reliability while GPU shader path is being validated
                        var basePen = style.GetStrokePen(h.Layer);
                        var color = Colors.Black;
                        if (basePen?.Brush is ISolidColorBrush sbrush)
                        {
                            color = sbrush.Color;
                        }
                        else
                        {
                            // Fallback: try layer fill brush color if stroke not solid
                            var fb = style.GetFillBrush(h.Layer);
                            if (fb is ISolidColorBrush fsolid)
                            {
                                color = fsolid.Color;
                            }
                        }
                        // Some style providers deliver A=0 for layer colors to indicate "unspecified".
                        // For hatch lines we need a visible stroke; force opaque alpha if zero.
                        if (color.A == 0)
                        {
                            color = Color.FromArgb(255, color.R, color.G, color.B);
                        }
                        // If the resolved color is effectively white, switch to a neutral dark gray
                        // so hatch lines remain visible over light backgrounds.
                        if (color.R > 240 && color.G > 240 && color.B > 240)
                        {
                            color = Color.FromArgb(color.A, 40, 40, 40);
                        }
                        // Final fallback: resolve from model layer color if available
                        if (color == Colors.Black && !string.IsNullOrWhiteSpace(h.Layer))
                        {
                            var layer = model.Layers.FirstOrDefault(l =>
                                string.Equals(l.Name, h.Layer, StringComparison.OrdinalIgnoreCase)
                            );
                            if (layer != null)
                            {
                                var a = (byte)((layer.ColorArgb >> 24) & 0xFF);
                                var r = (byte)((layer.ColorArgb >> 16) & 0xFF);
                                var g = (byte)((layer.ColorArgb >> 8) & 0xFF);
                                var b = (byte)(layer.ColorArgb & 0xFF);
                                if (a == 0)
                                    a = 255;
                                color = Color.FromArgb(a, r, g, b);
                            }
                        }

                        // Use SkiaHatchDrawOp (GPU path) with targeted logging support

                        // FIX: We must pass BOUNDS in the coordinate space of the DrawingContext.
                        // CadRendererHost pushes a transform: World -> Screen (with LocalOrigin shift).
                        // So the context expects "Local World Coordinates" (P - LocalOrigin).
                        // Therefore, we must convert drawBounds (World) to LocalBounds.
                        var localOrigin = RenderHelpers.CurrentLocalOrigin ?? new Point(0, 0);
                        var localBoundsRect = new Rect(
                            drawBounds.X - localOrigin.X,
                            drawBounds.Y - localOrigin.Y,
                            drawBounds.Width,
                            drawBounds.Height
                        );

                        // Inflate bounds slightly to account for stroke width/antialiasing
                        // Use BoundsInflation from DebugSettings for real-time adjustment
                        int inflation = 1 + DebugSettings.Hatches.BoundsInflation;
                        // Inflation is in PIXELS, but our rect is in WORLD units.
                        // We need to convert inflation to world units.
                        double pxPerWorld = RenderHelpers.PixelsPerWorld(state);
                        double inflationWorld = (pxPerWorld > 0) ? (inflation / pxPerWorld) : 0;
                        localBoundsRect = localBoundsRect.Inflate(inflationWorld);

                        if (
                            DebugSettings.Hatches.ShowHatchDiagnostics
                            || (DebugSettings.Hatches.DebugHatchId == h.Id)
                        )
                        {
                            // Calculate device bounds just for logging
                            var deviceBounds = RenderHelpers.WorldRectToDeviceAABB(
                                state,
                                drawBounds,
                                pixelInflate: inflation
                            );

                            var msg =
                                $"[HatchDebug] Bounds Calc for {h.Id}:\n"
                                + $"  WorldRect: {drawBounds}\n"
                                + $"  LocalOrigin: {localOrigin}\n"
                                + $"  LocalBounds: {localBoundsRect}\n"
                                + $"  Est DeviceBounds: {deviceBounds}\n";
                            DebugSettings.Hatches.Log(msg);

                            // Visualize the bounds being used for rendering (in Local Space)
                            ctx.Custom(
                                new DebugHatchRenderBoundsOp(localBoundsRect, $"Hatch {h.Id}")
                            );
                        }

                        // Use stable render origin (PatternOrigin) for the shader phase logic
                        var renderOriginWorld = h.PatternOrigin ?? new Point(0, 0);

                        double strokeWorld = (pxPerWorld > 0) ? (1.0 / pxPerWorld) : 1.0;

                        // Pass localBoundsRect as the operation bounds
                        var skiaOp = new SkiaHatchDrawOp(
                            localBoundsRect,
                            renderOriginWorld,
                            h,
                            state,
                            color,
                            strokeWorld,
                            localOrigin,
                            Logger
                        );
                        ctx.Custom((ICustomDrawOperation)skiaOp);
                    } // End using
                    drawn++;
                    continue;
                } // End if
                else
                {
                    // Solid hatch: draw fill at 30% opacity, then draw stroke at full opacity.
                    var baseFill = style.GetFillBrush(h.Layer);
                    if (baseFill is ISolidColorBrush sbFill)
                    {
                        var c = sbFill.Color;
                        // If the intended fill color is effectively black, switch to a neutral light gray
                        if (c.R < 8 && c.G < 8 && c.B < 8)
                        {
                            c = Color.FromArgb(c.A, 200, 200, 200);
                        }
                        fill = new SolidColorBrush(c);
                    }
                    else
                    {
                        fill = baseFill; // Use as provided (e.g., gradient or other brush kinds)
                    }

                    var strokePen = style.GetStrokePen(h.Layer);
                    using (ctx.PushOpacity(0.8))
                    {
                        ctx.DrawGeometry(fill, null, geo);
                    }
                    // Draw stroke at full opacity on top
                    ctx.DrawGeometry(null, strokePen, geo);
                    drawn++;
                    continue;
                }
            }
            Logger?.LogDebug("HatchRenderer: total={Total}, drawn={Drawn}", total, drawn);
        }

        private static Rect BoundsFromLoops(IReadOnlyList<IReadOnlyList<Point>> loops)
        {
            double minX = double.PositiveInfinity,
                minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity,
                maxY = double.NegativeInfinity;
            foreach (var loop in loops)
            {
                if (loop == null)
                    continue;
                foreach (var p in loop)
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
            }
            if (double.IsInfinity(minX))
            {
                return new Rect(0, 0, 0, 0);
            }
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private static List<Point> DecimateLoop(
            IReadOnlyList<Point> loop,
            ViewportState state,
            double pixelTolerance
        )
        {
            var result = new List<Point>(loop.Count);
            if (loop.Count == 0)
                return result;
            var lastScreen = state.WorldToScreen(loop[0]);
            result.Add(loop[0]);
            for (int i = 1; i < loop.Count; i++)
            {
                var scr = state.WorldToScreen(loop[i]);
                var dx = scr.X - lastScreen.X;
                var dy = scr.Y - lastScreen.Y;
                if ((dx * dx + dy * dy) >= pixelTolerance * pixelTolerance)
                {
                    result.Add(loop[i]);
                    lastScreen = scr;
                }
            }
            return result;
        }

        private static IBrush CreateGradientBrush(CadHatch h, IStyleProvider style)
        {
            var start = Colors.White;
            var end = Colors.Gray;
            // Prefer explicit gradient colors if available
            if (h.GradientStartColorArgb.HasValue && h.GradientEndColorArgb.HasValue)
            {
                uint s = h.GradientStartColorArgb.Value;
                uint e = h.GradientEndColorArgb.Value;
                start = Color.FromArgb(
                    (byte)((s >> 24) & 0xFF),
                    (byte)((s >> 16) & 0xFF),
                    (byte)((s >> 8) & 0xFF),
                    (byte)(s & 0xFF)
                );
                end = Color.FromArgb(
                    (byte)((e >> 24) & 0xFF),
                    (byte)((e >> 16) & 0xFF),
                    (byte)((e >> 8) & 0xFF),
                    (byte)(e & 0xFF)
                );
                // DWG ARGB may have alpha=0 (unspecified). Default to fully opaque to make gradient visible.
                if (start.A == 0)
                    start = Color.FromArgb(255, start.R, start.G, start.B);
                if (end.A == 0)
                    end = Color.FromArgb(255, end.R, end.G, end.B);
            }
            else
            {
                var baseBrush = style.GetFillBrush(h.Layer);
                if (baseBrush is SolidColorBrush sb)
                {
                    var c = sb.Color;
                    if (c.A == 0)
                        c = Color.FromArgb(255, c.R, c.G, c.B);
                    start = c;
                    end = Color.FromArgb(
                        c.A,
                        (byte)Math.Min(255, c.R + 40),
                        (byte)Math.Min(255, c.G + 40),
                        (byte)Math.Min(255, c.B + 40)
                    );
                }
            }
            var stops = new GradientStops { new GradientStop(start, 0), new GradientStop(end, 1) };
            var brush = new LinearGradientBrush
            {
                GradientStops = stops,
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                Transform = new RotateTransform(
                    h.GradientAngleDeg != 0 ? h.GradientAngleDeg : h.PatternAngleDeg
                ),
            };
            brush.Opacity = 0.8;
            return brush;
        }
    }
}
