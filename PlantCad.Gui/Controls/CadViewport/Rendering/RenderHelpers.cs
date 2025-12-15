using System;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using PlantCad.Gui.Controls.Viewport;

namespace PlantCad.Gui.Controls.Rendering
{
    internal static class RenderHelpers
    {
        // Ambient local origin to stabilize large world coordinates during the world pass
        private static readonly AsyncLocal<Point?> LocalOrigin = new AsyncLocal<Point?>();

        public static Point? CurrentLocalOrigin => LocalOrigin.Value;

        public static IDisposable PushLocalOrigin(Point origin)
        {
            var previous = LocalOrigin.Value;
            LocalOrigin.Value = origin;
            return new RestoreScope(previous);
        }

        private readonly struct RestoreScope : IDisposable
        {
            private readonly Point? _previous;

            public RestoreScope(Point? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                LocalOrigin.Value = _previous;
            }
        }

        public static Point ToLocal(Point world)
        {
            var o = LocalOrigin.Value;
            if (o.HasValue)
            {
                return new Point(world.X - o.Value.X, world.Y - o.Value.Y);
            }
            return world;
        }

        // Compose a matrix that maps local=world-origin back to the same screen space as the original matrix
        public static Matrix ComposeWithLocalOrigin(Matrix worldToScreen, Point origin)
        {
            // New translation = t + S * origin
            var sx = worldToScreen.M11 * origin.X + worldToScreen.M21 * origin.Y;
            var sy = worldToScreen.M12 * origin.X + worldToScreen.M22 * origin.Y;
            return new Matrix(
                worldToScreen.M11,
                worldToScreen.M12,
                worldToScreen.M21,
                worldToScreen.M22,
                worldToScreen.M31 + sx,
                worldToScreen.M32 + sy
            );
        }

        // Compute how many screen pixels correspond to 1 world unit along X axis
        public static double PixelsPerWorld(ViewportState state)
        {
            var a = state.Transform.Transform(new Point(0, 0));
            var b = state.Transform.Transform(new Point(1, 0));
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static Rect ExpandWorldRect(Rect r, double marginWorld)
        {
            if (marginWorld <= 0)
                return r;
            return new Rect(
                r.X - marginWorld,
                r.Y - marginWorld,
                r.Width + 2 * marginWorld,
                r.Height + 2 * marginWorld
            );
        }

        public static Rect ExpandVisibleRectPx(
            ViewportState state,
            Rect visibleWorldRect,
            double pixelMargin
        )
        {
            if (pixelMargin <= 0)
                return visibleWorldRect;
            var ppw = PixelsPerWorld(state);
            if (ppw <= 0)
                return visibleWorldRect;
            var marginW = pixelMargin / ppw;
            return ExpandWorldRect(visibleWorldRect, marginW);
        }

        public static Rect ExpandVisibleRect(
            ViewportState state,
            Rect visibleWorldRect,
            double pixelMargin,
            double worldMarginMin,
            double percentOfWorld
        )
        {
            var ppw = PixelsPerWorld(state);
            double pixelAsWorld = ppw > 0 ? pixelMargin / ppw : 0.0;
            double percentWorld =
                percentOfWorld > 0
                    ? Math.Max(visibleWorldRect.Width, visibleWorldRect.Height) * percentOfWorld
                    : 0.0;
            // Combine pixel margin additively with the larger of worldMin/percent to avoid premature culling at both extremes
            double baseWorld = Math.Max(worldMarginMin, percentWorld);
            double marginW = pixelAsWorld + baseWorld;
            if (marginW <= 0)
                return visibleWorldRect;
            return ExpandWorldRect(visibleWorldRect, marginW);
        }

        // Create a pen whose on-screen thickness matches basePen.Thickness regardless of zoom
        public static Pen WorldPenNormalized(
            Pen basePen,
            double pxPerWorld,
            double minPixelThickness = 1.0
        )
        {
            if (basePen == null)
                throw new ArgumentNullException(nameof(basePen));
            if (pxPerWorld <= 1e-12)
            {
                // Degenerate transform; return base pen as-is to avoid division by zero
                return basePen;
            }
            var targetPx = Math.Max(basePen.Thickness, minPixelThickness);
            var worldThickness = Math.Max(1e-6, targetPx / pxPerWorld);
            var p = new Pen(basePen.Brush, worldThickness)
            {
                DashStyle = basePen.DashStyle,
                LineCap = basePen.LineCap,
                LineJoin = basePen.LineJoin,
                MiterLimit = basePen.MiterLimit,
            };
            return p;
        }

        // Returns the pixel length of the portion of a world segment inside visibleWorldRect.
        // If fully outside, returns 0.
        public static double VisibleWorldLengthPx(
            Point a,
            Point b,
            Rect visibleWorldRect,
            double pxPerWorld
        )
        {
            var clipped = ClipSegmentToRect(a, b, visibleWorldRect, out var c0, out var c1);
            if (!clipped)
            {
                return 0.0;
            }
            var dx = c1.X - c0.X;
            var dy = c1.Y - c0.Y;
            var worldLen = Math.Sqrt(dx * dx + dy * dy);
            return worldLen * pxPerWorld;
        }

        // Liang–Barsky line clipping. Returns true if segment intersects rect; outputs clipped endpoints.
        public static bool ClipSegmentToRect(Point p0, Point p1, Rect r, out Point c0, out Point c1)
        {
            double x0 = p0.X,
                y0 = p0.Y,
                x1 = p1.X,
                y1 = p1.Y;
            double dx = x1 - x0,
                dy = y1 - y0;
            double u1 = 0.0,
                u2 = 1.0;

            bool Update(double p, double q)
            {
                if (Math.Abs(p) < 1e-12)
                {
                    return q >= 0; // parallel case: inside if q >= 0
                }
                var t = q / p;
                if (p < 0)
                {
                    if (t > u2)
                        return false;
                    if (t > u1)
                        u1 = t;
                }
                else // p > 0
                {
                    if (t < u1)
                        return false;
                    if (t < u2)
                        u2 = t;
                }
                return true;
            }

            var left = r.X;
            var right = r.Right;
            var top = r.Y;
            var bottom = r.Bottom;

            if (!Update(-dx, x0 - left))
            {
                c0 = default;
                c1 = default;
                return false;
            }
            if (!Update(dx, right - x0))
            {
                c0 = default;
                c1 = default;
                return false;
            }
            if (!Update(-dy, y0 - top))
            {
                c0 = default;
                c1 = default;
                return false;
            }
            if (!Update(dy, bottom - y0))
            {
                c0 = default;
                c1 = default;
                return false;
            }
            if (u1 > u2)
            {
                c0 = default;
                c1 = default;
                return false;
            }

            c0 = new Point(x0 + u1 * dx, y0 + u1 * dy);
            c1 = new Point(x0 + u2 * dx, y0 + u2 * dy);
            return true;
        }

        // Normalize a direction vector (dx, dy). Returns (0,0) if length is ~0.
        public static (double nx, double ny) Normalize(double dx, double dy)
        {
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= 1e-12)
                return (0.0, 0.0);
            return (dx / len, dy / len);
        }

        // Clip an infinite line defined by point P and direction D against rectangle r.
        // Returns true when intersection segment exists and outputs the two clipped endpoints.
        public static bool ClipInfiniteLineToRect(
            Point p,
            Point direction,
            Rect r,
            out Point a,
            out Point b
        )
        {
            // Parameterize line: L(t) = p + t * d, t in (-inf, +inf)
            double dx = direction.X;
            double dy = direction.Y;
            var (nx, ny) = Normalize(dx, dy);
            if (Math.Abs(nx) <= 1e-12 && Math.Abs(ny) <= 1e-12)
            {
                a = default;
                b = default;
                return false;
            }

            // Intersect with each rectangle edge and collect valid t values
            var ts = new System.Collections.Generic.List<double>(4);

            void TryEdge(double ex0, double ey0, double ex1, double ey1)
            {
                // Edge segment parametric: E(s) = e0 + s*(e1-e0), s in [0,1]
                double ex = ex1 - ex0;
                double ey = ey1 - ey0;
                // Solve p + t*d = e0 + s*e for t and s using 2x2
                double det = nx * (-ey) - ny * (-ex);
                if (Math.Abs(det) <= 1e-18)
                    return; // parallel
                double rx = ex0 - p.X;
                double ry = ey0 - p.Y;
                double t = (rx * (-ey) - ry * (-ex)) / det;
                double s = (nx * ry - ny * rx) / det;
                if (s >= -1e-9 && s <= 1 + 1e-9)
                    ts.Add(t);
            }

            double left = r.X,
                right = r.Right,
                top = r.Y,
                bottom = r.Bottom;
            TryEdge(left, top, right, top); // top
            TryEdge(right, top, right, bottom); // right
            TryEdge(right, bottom, left, bottom); // bottom
            TryEdge(left, bottom, left, top); // left

            if (ts.Count < 2)
            {
                a = default;
                b = default;
                return false;
            }
            ts.Sort();
            // Pick the two extreme intersections
            double t0 = ts[0];
            double t1 = ts[ts.Count - 1];
            a = new Point(p.X + t0 * nx, p.Y + t0 * ny);
            b = new Point(p.X + t1 * nx, p.Y + t1 * ny);
            return true;
        }

        // Clip a ray (origin+direction, t>=0) to rect. Returns true with [a,b] segment if intersects.
        public static bool ClipRayToRect(
            Point origin,
            Point direction,
            Rect r,
            out Point a,
            out Point b
        )
        {
            if (!ClipInfiniteLineToRect(origin, direction, r, out var i0, out var i1))
            {
                a = default;
                b = default;
                return false;
            }
            // Choose the intersection that lies in front of the ray (t>=0)
            var (nx, ny) = Normalize(direction.X, direction.Y);
            // Project to compute t for i0 and i1: t = dot(i - origin, n)
            double t0 = (i0.X - origin.X) * nx + (i0.Y - origin.Y) * ny;
            double t1 = (i1.X - origin.X) * nx + (i1.Y - origin.Y) * ny;
            bool i0Ok = t0 >= -1e-9;
            bool i1Ok = t1 >= -1e-9;
            if (!i0Ok && !i1Ok)
            {
                a = default;
                b = default;
                return false;
            }
            if (i0Ok && i1Ok)
            {
                // Both in front: sort by t
                if (t0 <= t1)
                {
                    a = i0;
                    b = i1;
                    return true;
                }
                else
                {
                    a = i1;
                    b = i0;
                    return true;
                }
            }
            // One behind: the other is the visible endpoint; origin must be inside rect to form a segment
            if (r.Contains(origin))
            {
                a = origin;
                b = i0Ok ? i0 : i1;
                return true;
            }
            a = default;
            b = default;
            return false;
        }

        public static Rect WorldRectToDeviceAABB(
            ViewportState state,
            Rect worldRect,
            double pixelInflate = 0
        )
        {
            var tl = worldRect.TopLeft;
            var tr = new Point(worldRect.Right, worldRect.Top);
            var br = worldRect.BottomRight;
            var bl = new Point(worldRect.Left, worldRect.Bottom);

            var tlS = state.WorldToScreen(tl);
            var trS = state.WorldToScreen(tr);
            var brS = state.WorldToScreen(br);
            var blS = state.WorldToScreen(bl);

            var minX = Math.Min(Math.Min(tlS.X, trS.X), Math.Min(brS.X, blS.X));
            var maxX = Math.Max(Math.Max(tlS.X, trS.X), Math.Max(brS.X, blS.X));
            var minY = Math.Min(Math.Min(tlS.Y, trS.Y), Math.Min(brS.Y, blS.Y));
            var maxY = Math.Max(Math.Max(tlS.Y, trS.Y), Math.Max(brS.Y, blS.Y));

            var rect = new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
            return pixelInflate > 0 ? rect.Inflate(pixelInflate) : rect;
        }
    }
}
