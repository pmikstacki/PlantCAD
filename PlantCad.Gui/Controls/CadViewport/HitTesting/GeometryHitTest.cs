using System;
using System.Collections.Generic;
using Avalonia;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Controls.HitTesting;

public static class GeometryHitTest
{
    public static double DistanceSqToSegment(Point p, Point a, Point b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var apx = p.X - a.X;
        var apy = p.Y - a.Y;
        var abLenSq = abx * abx + aby * aby;
        if (abLenSq <= double.Epsilon)
        {
            // a == b
            var dx0 = p.X - a.X;
            var dy0 = p.Y - a.Y;
            return dx0 * dx0 + dy0 * dy0;
        }
        var t = (apx * abx + apy * aby) / abLenSq;
        if (t <= 0)
            return (apx * apx + apy * apy);
        if (t >= 1)
        {
            var dx1 = p.X - b.X;
            var dy1 = p.Y - b.Y;
            return dx1 * dx1 + dy1 * dy1;
        }
        var cx = a.X + t * abx;
        var cy = a.Y + t * aby;
        var dx = p.X - cx;
        var dy = p.Y - cy;
        return dx * dx + dy * dy;
    }

    public static double DistanceSqToPolyline(Point p, IReadOnlyList<Point> screenPts)
    {
        if (screenPts == null || screenPts.Count < 2)
            return double.PositiveInfinity;
        double best = double.PositiveInfinity;
        var prev = screenPts[0];
        for (int i = 1; i < screenPts.Count; i++)
        {
            var cur = screenPts[i];
            var d = DistanceSqToSegment(p, prev, cur);
            if (d < best)
                best = d;
            prev = cur;
        }
        return best;
    }

    public static Rect ComputeScreenBounds(IReadOnlyList<Point> screenPts)
    {
        double minX = double.PositiveInfinity,
            minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity,
            maxY = double.NegativeInfinity;
        foreach (var pt in screenPts)
        {
            if (pt.X < minX)
                minX = pt.X;
            if (pt.Y < minY)
                minY = pt.Y;
            if (pt.X > maxX)
                maxX = pt.X;
            if (pt.Y > maxY)
                maxY = pt.Y;
        }
        if (double.IsInfinity(minX) || double.IsInfinity(minY))
            return new Rect();
        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    public static double DistanceSqToCircle(Point p, Point screenCenter, double screenRadius)
    {
        var dx = p.X - screenCenter.X;
        var dy = p.Y - screenCenter.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        var diff = dist - Math.Abs(screenRadius);
        return diff * diff;
    }

    public static bool AngleInSweep(double ang, double start, double end)
    {
        // normalize to [0, 2pi)
        ang = NormalizeAngle(ang);
        start = NormalizeAngle(start);
        end = NormalizeAngle(end);
        if (end < start)
            end += Math.PI * 2;
        if (ang < start)
            ang += Math.PI * 2;
        return ang >= start && ang <= end;
    }

    private static double NormalizeAngle(double a)
    {
        var twoPi = Math.PI * 2;
        while (a < 0)
            a += twoPi;
        while (a >= twoPi)
            a -= twoPi;
        return a;
    }

    public static double DistanceSqToArc(
        Point p,
        Point screenCenter,
        double screenRadius,
        double startRad,
        double endRad
    )
    {
        // vector angle of pointer relative to center
        var ang = Math.Atan2(p.Y - screenCenter.Y, p.X - screenCenter.X);
        if (AngleInSweep(ang, startRad, endRad))
        {
            return DistanceSqToCircle(p, screenCenter, screenRadius);
        }
        // not within arc; return min distance to endpoints on the arc
        var spt = new Point(
            screenCenter.X + screenRadius * Math.Cos(startRad),
            screenCenter.Y + screenRadius * Math.Sin(startRad)
        );
        var ept = new Point(
            screenCenter.X + screenRadius * Math.Cos(endRad),
            screenCenter.Y + screenRadius * Math.Sin(endRad)
        );
        var dxs = p.X - spt.X;
        var dys = p.Y - spt.Y;
        var ds = dxs * dxs + dys * dys;
        var dxe = p.X - ept.X;
        var dye = p.Y - ept.Y;
        var de = dxe * dxe + dye * dye;
        return Math.Min(ds, de);
    }

    public static IReadOnlyList<Point> ToScreen(ViewportState state, IReadOnlyList<Point> worldPts)
    {
        var result = new Point[worldPts.Count];
        for (int i = 0; i < worldPts.Count; i++)
        {
            result[i] = state.WorldToScreen(worldPts[i]);
        }
        return result;
    }

    public static IReadOnlyList<Point> SampleCircleToScreen(
        ViewportState state,
        Point centerWorld,
        double radiusWorld,
        int segments
    )
    {
        var pts = new List<Point>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            var ang = (Math.PI * 2.0) * i / segments;
            var wx = centerWorld.X + radiusWorld * Math.Cos(ang);
            var wy = centerWorld.Y + radiusWorld * Math.Sin(ang);
            pts.Add(state.WorldToScreen(new Point(wx, wy)));
        }
        return pts;
    }

    public static IReadOnlyList<Point> SampleEllipseToScreen(
        ViewportState state,
        CadEllipse el,
        int segments
    )
    {
        var pts = new List<Point>(segments + 1);
        double start = el.IsArc ? DegreesToRadians(el.StartAngleDeg) : 0.0;
        double end = el.IsArc ? DegreesToRadians(el.EndAngleDeg) : Math.PI * 2.0;
        while (end < start)
            end += Math.PI * 2.0;
        double rot = DegreesToRadians(el.RotationDeg);
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            double ang = start + (end - start) * t;
            double lx = el.RadiusX * Math.Cos(ang);
            double ly = el.RadiusY * Math.Sin(ang);
            double wx = el.Center.X + (lx * Math.Cos(rot) - ly * Math.Sin(rot));
            double wy = el.Center.Y + (lx * Math.Sin(rot) + ly * Math.Cos(rot));
            pts.Add(state.WorldToScreen(new Point(wx, wy)));
        }
        return pts;
    }

    private static double DegreesToRadians(double deg) => deg * Math.PI / 180.0;
}
