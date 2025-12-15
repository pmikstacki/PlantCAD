using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace PlantCad.Gui.Controls.CadViewport.Rendering.Skia
{
    internal class DebugHatchRenderBoundsOp : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly string _label;

        public DebugHatchRenderBoundsOp(Rect bounds, string label)
        {
            _bounds = bounds;
            _label = label;
        }

        public void Dispose() { }

        public Rect Bounds => new Rect(0, 0, 10000, 10000); // Always draw

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature =
                context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature))
                as ISkiaSharpApiLeaseFeature;
            if (leaseFeature == null)
                return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            // Removed ResetMatrix to draw in the passed coordinate space (Control Space)

            using var paint = new SKPaint
            {
                Color = SKColors.Magenta,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true,
            };

            var r = new SKRect(
                (float)_bounds.X,
                (float)_bounds.Y,
                (float)_bounds.Right,
                (float)_bounds.Bottom
            );
            canvas.DrawRect(r, paint);

            paint.Style = SKPaintStyle.Fill;
            paint.TextSize = 14;
            canvas.DrawText(_label, r.Left + 5, r.Top + 20, paint);

            canvas.Restore();
        }
    }
}
