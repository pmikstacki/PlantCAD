using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using SkiaSharp;

namespace PlantCad.Gui.Controls.Rendering.Skia
{
    internal sealed class SkiaUnderlayDrawOp : ICustomDrawOperation
    {
        private readonly Rect _worldBounds;
        private readonly CadUnderlay _underlay;
        private readonly SKImage _image;
        private readonly ViewportState _state;
        private readonly double _opacity;
        private readonly Color? _tint;
        private readonly IReadOnlyList<IReadOnlyList<Point>>? _clipLoops;

        public SkiaUnderlayDrawOp(
            Rect worldBounds,
            CadUnderlay underlay,
            SKImage image,
            ViewportState state,
            double opacity,
            Color? tint,
            IReadOnlyList<IReadOnlyList<Point>>? clipLoops
        )
        {
            _worldBounds = worldBounds;
            _underlay = underlay;
            _image = image ?? throw new ArgumentNullException(nameof(image));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _opacity = Math.Clamp(opacity, 0.0, 1.0);
            _tint = tint;
            _clipLoops = clipLoops;
        }

        public void Dispose()
        {
            // SKImage is owned by resolver cache; do not dispose here
        }

        public Rect Bounds => _worldBounds;

        public bool HitTest(Point p) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature =
                context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature))
                as ISkiaSharpApiLeaseFeature;
            if (leaseFeature == null)
            {
                return;
            }
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            if (canvas == null)
            {
                return;
            }

            // Build device-space destination quad
            var dst = new SKPoint[4];
            if (_underlay.WorldTransform.HasValue)
            {
                var m = _underlay.WorldTransform.Value;
                var pts = new[]
                {
                    new Point(0, 0),
                    new Point(_image.Width, 0),
                    new Point(_image.Width, _image.Height),
                    new Point(0, _image.Height),
                };
                var w0 = Transform(m, pts[0]);
                var w1 = Transform(m, pts[1]);
                var w2 = Transform(m, pts[2]);
                var w3 = Transform(m, pts[3]);
                dst[0] = ToSK(_state.WorldToScreen(w0));
                dst[1] = ToSK(_state.WorldToScreen(w1));
                dst[2] = ToSK(_state.WorldToScreen(w2));
                dst[3] = ToSK(_state.WorldToScreen(w3));
            }
            else if (_underlay.WorldQuad != null && _underlay.WorldQuad.Length >= 4)
            {
                dst[0] = ToSK(_state.WorldToScreen(_underlay.WorldQuad[0]));
                dst[1] = ToSK(_state.WorldToScreen(_underlay.WorldQuad[1]));
                dst[2] = ToSK(_state.WorldToScreen(_underlay.WorldQuad[2]));
                dst[3] = ToSK(_state.WorldToScreen(_underlay.WorldQuad[3]));
            }
            else
            {
                // Axis-aligned fallback at origin
                var tl = _state.WorldToScreen(new Point(0, 0));
                var tr = _state.WorldToScreen(new Point(_image.Width, 0));
                var br = _state.WorldToScreen(new Point(_image.Width, _image.Height));
                var bl = _state.WorldToScreen(new Point(0, _image.Height));
                dst[0] = ToSK(tl);
                dst[1] = ToSK(tr);
                dst[2] = ToSK(br);
                dst[3] = ToSK(bl);
            }

            // Optional clip path (EvenOdd)
            if (_clipLoops != null && _clipLoops.Count > 0)
            {
                using var clip = new SKPath { FillType = SKPathFillType.EvenOdd };
                foreach (var loop in _clipLoops)
                {
                    if (loop == null || loop.Count < 3)
                        continue;
                    var p0 = _state.WorldToScreen(loop[0]);
                    clip.MoveTo((float)p0.X, (float)p0.Y);
                    for (int i = 1; i < loop.Count; i++)
                    {
                        var pi = _state.WorldToScreen(loop[i]);
                        clip.LineTo((float)pi.X, (float)pi.Y);
                    }
                    clip.Close();
                }
                canvas.Save();
                canvas.ClipPath(clip, SKClipOperation.Intersect, true);
            }

            using var paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High,
            };
            // Opacity via paint alpha
            byte a = (byte)Math.Round(255 * _opacity);
            paint.Color = new SKColor(255, 255, 255, a);
            if (_tint.HasValue)
            {
                var c = _tint.Value;
                var skc = new SKColor(c.R, c.G, c.B, c.A);
                // Modulate image with tint
                paint.ColorFilter = SKColorFilter.CreateBlendMode(skc, SKBlendMode.Modulate);
            }

            // Fallback MVP: draw to device-space bounding rectangle of the destination quad
            float minX = Math.Min(Math.Min(dst[0].X, dst[1].X), Math.Min(dst[2].X, dst[3].X));
            float minY = Math.Min(Math.Min(dst[0].Y, dst[1].Y), Math.Min(dst[2].Y, dst[3].Y));
            float maxX = Math.Max(Math.Max(dst[0].X, dst[1].X), Math.Max(dst[2].X, dst[3].X));
            float maxY = Math.Max(Math.Max(dst[0].Y, dst[1].Y), Math.Max(dst[2].Y, dst[3].Y));
            var destRect = new SKRect(minX, minY, maxX, maxY);
            canvas.DrawImage(_image, destRect, paint);

            if (_clipLoops != null && _clipLoops.Count > 0)
            {
                canvas.Restore();
            }
        }

        public bool Equals(ICustomDrawOperation? other) => false;

        private static SKPoint ToSK(Point p) => new SKPoint((float)p.X, (float)p.Y);

        private static Point Transform(Matrix m, Point p)
        {
            return new Point(m.M11 * p.X + m.M12 * p.Y + m.M31, m.M21 * p.X + m.M22 * p.Y + m.M32);
        }
    }
}
