using System;
using Avalonia;
using Avalonia.Media;
using PlantCad.Gui.Controls.Viewport;

namespace PlantCad.Gui.Controls.Rendering
{
    public sealed class WorldGridOverlayRenderer : IOverlayRenderer
    {
        private readonly double? _fixedStepWorld;
        private readonly int _desiredPixelStep;
        private readonly Pen _pen;

        public WorldGridOverlayRenderer(double? fixedStepWorld, int desiredPixelStep = 50)
        {
            if (fixedStepWorld.HasValue && fixedStepWorld.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedStepWorld),
                    "World grid step must be positive when provided."
                );
            }
            if (desiredPixelStep <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(desiredPixelStep),
                    "Desired pixel step must be positive."
                );
            }
            _fixedStepWorld = fixedStepWorld;
            _desiredPixelStep = desiredPixelStep;
            _pen = new Pen(Brushes.LightGray, 1);
        }

        public void Render(
            DrawingContext ctx,
            Size viewportSize,
            ViewportState state,
            OverlayContext context
        )
        {
            var bounds = new Rect(viewportSize);
            // Compute visible world rectangle
            var worldRect = state.ScreenToWorld(bounds);
            var minX = worldRect.Left;
            var maxX = worldRect.Right;
            var minY = worldRect.Top;
            var maxY = worldRect.Bottom;

            // Determine step (adaptive by zoom if not fixed)
            var stepWorld = _fixedStepWorld ?? ComputeAdaptiveWorldStep(state);
            if (stepWorld <= 0 || double.IsNaN(stepWorld) || double.IsInfinity(stepWorld))
            {
                stepWorld = 100.0;
            }

            // Determine starting lines aligned to step
            var startX = Math.Floor(minX / stepWorld) * stepWorld;
            var startY = Math.Floor(minY / stepWorld) * stepWorld;

            // Clamp number of lines to avoid excessive drawing
            var rangeX = maxX - minX;
            var rangeY = maxY - minY;
            if (rangeX <= 0 || rangeY <= 0)
            {
                return;
            }
            const int maxLines = 500;
            var countX = rangeX / stepWorld;
            var countY = rangeY / stepWorld;
            if (countX > maxLines)
            {
                var factor = Math.Ceiling(countX / maxLines);
                stepWorld *= factor;
                startX = Math.Floor(minX / stepWorld) * stepWorld;
                countX = rangeX / stepWorld;
            }
            if (countY > maxLines)
            {
                var factor = Math.Ceiling(countY / maxLines);
                stepWorld *= factor;
                startY = Math.Floor(minY / stepWorld) * stepWorld;
                countY = rangeY / stepWorld;
            }

            var m = state.Transform;

            // Vertical lines
            if (countX < 1)
            {
                // draw one at center
                var cx = (minX + maxX) * 0.5;
                var a = m.Transform(new Point(cx, minY));
                var b = m.Transform(new Point(cx, maxY));
                ctx.DrawLine(_pen, a, b);
            }
            else
            {
                for (double x = startX; x <= maxX; x += stepWorld)
                {
                    var a = m.Transform(new Point(x, minY));
                    var b = m.Transform(new Point(x, maxY));
                    ctx.DrawLine(_pen, a, b);
                }
            }
            // Horizontal lines
            if (countY < 1)
            {
                var cy = (minY + maxY) * 0.5;
                var a = m.Transform(new Point(minX, cy));
                var b = m.Transform(new Point(maxX, cy));
                ctx.DrawLine(_pen, a, b);
            }
            else
            {
                for (double y = startY; y <= maxY; y += stepWorld)
                {
                    var a = m.Transform(new Point(minX, y));
                    var b = m.Transform(new Point(maxX, y));
                    ctx.DrawLine(_pen, a, b);
                }
            }
        }

        private double ComputeAdaptiveWorldStep(ViewportState state)
        {
            // Approximate pixels per world unit using transform of (0,0)->(1,0)
            var p0 = state.Transform.Transform(new Point(0, 0));
            var p1 = state.Transform.Transform(new Point(1, 0));
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            var pxPerWorld = Math.Sqrt(dx * dx + dy * dy);
            if (pxPerWorld <= 1e-9)
            {
                return 100.0;
            }
            var targetWorld = _desiredPixelStep / pxPerWorld;
            return NiceStep(targetWorld);
        }

        private static double NiceStep(double value)
        {
            if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
            {
                return 100.0;
            }
            var exp = Math.Floor(Math.Log10(value));
            var basePow = Math.Pow(10, exp);
            var candidates = new[] { 1.0, 2.0, 5.0, 10.0 };
            foreach (var c in candidates)
            {
                var step = c * basePow;
                if (step >= value)
                {
                    return step;
                }
            }
            return 10.0 * basePow;
        }
    }
}
