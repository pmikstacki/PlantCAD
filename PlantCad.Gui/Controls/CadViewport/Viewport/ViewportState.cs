using System;
using Avalonia;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls.Viewport
{
    /// <summary>
    /// Holds the world-to-screen transform and provides fit, pan, zoom and conversion helpers.
    /// </summary>
    public sealed class ViewportState
    {
        public Matrix Transform { get; private set; } = Matrix.Identity;
        public Matrix InverseTransform { get; private set; } = Matrix.Identity;
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "ViewportState"
        );

        // Dynamic scale limits and reference scale (set by FitToExtents and/or CadViewportControl)
        public double MinScale { get; private set; } = 1e-6;
        public double MaxScale { get; private set; } = 1e6;
        public double ReferenceScale { get; private set; } = 1.0;

        /// <summary>
        /// Indicates whether the current transform was modified by the user via pan/zoom.
        /// When false, the control can re-compute a fit-to-extents transform on render.
        /// </summary>
        public bool IsCustom { get; private set; }

        public void ResetToIdentity()
        {
            Transform = Matrix.Identity;
            InverseTransform = Matrix.Identity;
            IsCustom = false;
        }

        public void SetCustom(Matrix transform)
        {
            Transform = transform;
            InverseTransform = TryInvertOrThrow(transform);
            IsCustom = true;
        }

        public void FitToExtents(Rect viewport, Models.CadExtents extents, double margin)
        {
            // Defensive checks
            var vx = viewport.Width - 2 * margin;
            var vy = viewport.Height - 2 * margin;
            if (vx <= 0 || vy <= 0)
            {
                Transform = Matrix.Identity;
                InverseTransform = Matrix.Identity;
                IsCustom = false;
                return;
            }

            // Sanitize extents (guard against NaN/Infinity/degenerate)
            double minX = extents.MinX,
                minY = extents.MinY,
                maxX = extents.MaxX,
                maxY = extents.MaxY;
            bool invalid =
                double.IsNaN(minX)
                || double.IsInfinity(minX)
                || double.IsNaN(minY)
                || double.IsInfinity(minY)
                || double.IsNaN(maxX)
                || double.IsInfinity(maxX)
                || double.IsNaN(maxY)
                || double.IsInfinity(maxY)
                || (maxX <= minX)
                || (maxY <= minY);
            if (invalid)
            {
                // Default to a reasonable viewport-scale box around origin
                Logger?.LogWarning(
                    "FitToExtents: invalid model extents. Using default extents around origin."
                );
                minX = -50;
                minY = -50;
                maxX = 50;
                maxY = 50;
            }

            var worldWidth = Math.Max(1e-6, maxX - minX);
            var worldHeight = Math.Max(1e-6, maxY - minY);
            var sx = vx / worldWidth;
            var sy = vy / worldHeight;
            var scale = Math.Min(sx, sy);
            // Clamp only for numeric stability (allow very small values to properly fit large models)
            const double minScale = 1e-12; // extremely small but > 0 to keep matrix invertible
            const double maxScale = 1_000_000; // safety upper bound to avoid exploding zoom
            if (scale < minScale)
                scale = minScale;
            if (scale > maxScale)
                scale = maxScale;

            var worldCenterX = (minX + maxX) / 2.0;
            var worldCenterY = (minY + maxY) / 2.0;
            var viewCenterX = viewport.X + viewport.Width / 2.0;
            var viewCenterY = viewport.Y + viewport.Height / 2.0;

            // Build explicit matrix that maps world to screen with Y-up (world) to Y-down (screen) flip.
            // x' = scale * x + tx
            // y' = -scale * y + ty
            // Solve tx, ty to place world center at view center.
            var tx = viewCenterX - scale * worldCenterX;
            var ty = viewCenterY + scale * worldCenterY;
            var m = new Matrix(scale, 0, 0, -scale, tx, ty);

            Transform = m;
            InverseTransform = TryInvertOrThrow(m);
            ReferenceScale = Math.Max(Math.Abs(m.M11), 1e-12);
            Logger?.LogInformation(
                "FitToExtents: viewport=({VW:0}x{VH:0}) extents=({MinX:0.###},{MinY:0.###})-({MaxX:0.###},{MaxY:0.###}) scale={Scale:0.###} tx={TX:0.###} ty={TY:0.###}",
                viewport.Width,
                viewport.Height,
                extents.MinX,
                extents.MinY,
                extents.MaxX,
                extents.MaxY,
                scale,
                tx,
                ty
            );
            // Fit is not considered custom; next render can recompute if needed
            IsCustom = false;
        }

        public void Pan(double dxScreen, double dyScreen)
        {
            var m = Matrix.CreateTranslation(dxScreen, dyScreen) * Transform;
            Transform = m;
            InverseTransform = TryInvertOrThrow(m);
            IsCustom = true;
            Logger?.LogInformation(
                "Pan: dx={DX:0.###} dy={DY:0.###} Transform=[{M11:0.###} {M12:0.###} {M21:0.###} {M22:0.###} | {M31:0.###} {M32:0.###}]",
                dxScreen,
                dyScreen,
                Transform.M11,
                Transform.M12,
                Transform.M21,
                Transform.M22,
                Transform.M31,
                Transform.M32
            );
        }

        public void Zoom(Point pivotScreen, double zoomFactor)
        {
            if (zoomFactor <= 0 || double.IsNaN(zoomFactor) || double.IsInfinity(zoomFactor))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(zoomFactor),
                    "Zoom factor must be positive and finite."
                );
            }

            // Clamp overall scale to keep transform invertible and content visible
            var currentScale = Math.Abs(Transform.M11);
            if (currentScale <= 0)
            {
                currentScale = 1e-12;
            }
            var minScale = MinScale;
            var maxScale = MaxScale;
            var desiredScale = currentScale * zoomFactor;
            if (desiredScale < minScale)
            {
                zoomFactor = minScale / currentScale;
            }
            else if (desiredScale > maxScale)
            {
                zoomFactor = maxScale / currentScale;
            }

            // If factor is effectively 1, skip to avoid accumulating rounding drift
            if (Math.Abs(Math.Log(zoomFactor)) < 1e-6)
            {
                return;
            }
            // Direct world-pivot solve to keep the pivot pixel fixed exactly
            var pivotWorld = InverseTransform.Transform(pivotScreen);
            var newScale = Math.Abs(Transform.M11) * zoomFactor;
            newScale = Math.Clamp(newScale, minScale, maxScale);
            var m11 = Math.Sign(Transform.M11) * newScale;
            var m22 = Math.Sign(Transform.M22) * newScale; // preserve Y-flip/sign
            var tx = pivotScreen.X - m11 * pivotWorld.X;
            var ty = pivotScreen.Y - m22 * pivotWorld.Y;
            var m = new Matrix(m11, 0, 0, m22, tx, ty);
            Transform = m;
            InverseTransform = TryInvertOrThrow(m);
            IsCustom = true;
            var logScale = Math.Abs(Transform.M11);
            Logger?.LogInformation(
                "Zoom: pivot=({PX:0.###},{PY:0.###}) factor={Z:0.######} scale={S:0.######} Transform=[{M11:0.###} {M12:0.###} {M21:0.###} {M22:0.###} | {M31:0.###} {M32:0.###}]",
                pivotScreen.X,
                pivotScreen.Y,
                zoomFactor,
                logScale,
                Transform.M11,
                Transform.M12,
                Transform.M21,
                Transform.M22,
                Transform.M31,
                Transform.M32
            );
        }

        public void SetScaleLimits(double minScale, double maxScale)
        {
            if (minScale <= 0 || double.IsNaN(minScale) || double.IsInfinity(minScale))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minScale),
                    "MinScale must be positive and finite."
                );
            }
            if (maxScale <= minScale || double.IsNaN(maxScale) || double.IsInfinity(maxScale))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxScale),
                    "MaxScale must be greater than MinScale and finite."
                );
            }
            MinScale = minScale;
            MaxScale = maxScale;
            Logger?.LogInformation(
                "Scale limits set: min={Min:0.###}, max={Max:0.###}",
                MinScale,
                MaxScale
            );
        }

        public Point WorldToScreen(Point world) => Transform.Transform(world);

        public Point ScreenToWorld(Point screen) => InverseTransform.Transform(screen);

        public Rect WorldToScreen(Rect worldRect)
        {
            var p1 = WorldToScreen(worldRect.TopLeft);
            var p2 = WorldToScreen(worldRect.BottomRight);
            return NormalizeRect(p1, p2);
        }

        public Rect ScreenToWorld(Rect screenRect)
        {
            var p1 = ScreenToWorld(screenRect.TopLeft);
            var p2 = ScreenToWorld(screenRect.BottomRight);
            return NormalizeRect(p1, p2);
        }

        private static Matrix TryInvertOrThrow(Matrix m)
        {
            if (!m.TryInvert(out var inv))
            {
                throw new InvalidOperationException("ViewportState: transform is not invertible.");
            }
            return inv;
        }

        private static Rect NormalizeRect(Point a, Point b)
        {
            var minX = Math.Min(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxX = Math.Max(a.X, b.X);
            var maxY = Math.Max(a.Y, b.Y);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
