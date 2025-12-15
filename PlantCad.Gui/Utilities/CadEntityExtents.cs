using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using PlantCad.Gui.Models;
using PlantCad.Gui.ViewModels.Documents;

namespace PlantCad.Gui.Utilities;

/// <summary>
/// Centralized helpers to compute world-space bounds (extents) for CAD entities.
/// </summary>
public static class CadEntityExtents
{
    public static bool TryGetBounds(
        CadModel model,
        SelectedEntityRef sel,
        CadRenderOptions? options,
        out Rect bounds
    )
    {
        bounds = default;
        bool IsVisible(string? layer)
        {
            if (string.IsNullOrWhiteSpace(layer))
                return true;
            return options?.IsLayerVisible(layer) ?? true;
        }
        switch (sel.Kind)
        {
            case ViewModels.Tools.EntityKind.Polyline:
            {
                var pl = model.Polylines.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (pl == null || pl.Points == null || pl.Points.Count == 0)
                    return false;
                bounds = BoundsFromPoints(pl.Points);
                return true;
            }
            case ViewModels.Tools.EntityKind.Leader:
            {
                var ld = model.Leaders.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (ld == null || ld.Points == null || ld.Points.Count == 0)
                    return false;
                bounds = BoundsFromPoints(ld.Points);
                return true;
            }
            case ViewModels.Tools.EntityKind.Line:
            {
                var ln = model.Lines.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (ln == null)
                    return false;
                bounds = RectFromMinMax(
                    Math.Min(ln.Start.X, ln.End.X),
                    Math.Min(ln.Start.Y, ln.End.Y),
                    Math.Max(ln.Start.X, ln.End.X),
                    Math.Max(ln.Start.Y, ln.End.Y)
                );
                return true;
            }
            case ViewModels.Tools.EntityKind.Circle:
            {
                var c = model.Circles.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (c == null)
                    return false;
                bounds = new Rect(
                    c.Center.X - c.Radius,
                    c.Center.Y - c.Radius,
                    2 * c.Radius,
                    2 * c.Radius
                );
                return true;
            }
            case ViewModels.Tools.EntityKind.Arc:
            {
                var a = model.Arcs.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (a == null)
                    return false;
                bounds = BoundsFromArc(a);
                return true;
            }
            case ViewModels.Tools.EntityKind.Insert:
            {
                var i = model.Inserts.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (i == null)
                    return false;

                // Prefer accurate bounds from flattened block content, fallback to a small dot
                if (TryGetBoundsForInsert(model, i, options, out var bb))
                {
                    bounds = bb;
                    return true;
                }

                var r = 1.0; // fallback small visual radius
                bounds = new Rect(i.Position.X - r, i.Position.Y - r, 2 * r, 2 * r);
                return true;
            }
            case ViewModels.Tools.EntityKind.Ellipse:
            {
                var el = model.Ellipses.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (el == null)
                    return false;
                bounds = BoundsFromEllipse(el);
                return true;
            }
            case ViewModels.Tools.EntityKind.Text:
            {
                var t = model.Texts.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (t == null)
                    return false;
                var h = Math.Max(t.Height, 0.0);
                var w = h * 0.6 * (t.Value?.Length ?? 0);
                bounds = new Rect(
                    t.Position.X,
                    t.Position.Y - 0.8 * h,
                    Math.Max(0, w),
                    Math.Max(0, h)
                );
                return true;
            }
            case ViewModels.Tools.EntityKind.MText:
            {
                var mt = model.MTexts.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (mt == null)
                    return false;
                var h = Math.Max(mt.Height, 0.0);
                var w = h * 0.6 * (mt.Value?.Length ?? 0);
                bounds = new Rect(
                    mt.Position.X,
                    mt.Position.Y - 0.8 * h,
                    Math.Max(0, w),
                    Math.Max(0, h)
                );
                return true;
            }
            case ViewModels.Tools.EntityKind.Spline:
            {
                var sp = model.Splines.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (sp == null || sp.Points == null || sp.Points.Count == 0)
                    return false;
                bounds = BoundsFromPoints(sp.Points);
                return true;
            }
            case ViewModels.Tools.EntityKind.Solid:
            {
                var so = model.Solids.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (so == null || so.Vertices == null || so.Vertices.Count == 0)
                    return false;
                bounds = BoundsFromPoints(so.Vertices);
                return true;
            }
            case ViewModels.Tools.EntityKind.Hatch:
            {
                var ha = model.Hatches.FirstOrDefault(x => x.Id == sel.Id && IsVisible(x.Layer));
                if (ha == null || ha.Loops == null || ha.Loops.Count == 0)
                    return false;
                Rect? acc = null;
                foreach (var loop in ha.Loops)
                {
                    if (loop == null || loop.Count == 0)
                        continue;
                    var bb = BoundsFromPoints(loop);
                    acc =
                        acc == null
                            ? bb
                            : new Rect(
                                Math.Min(acc.Value.X, bb.X),
                                Math.Min(acc.Value.Y, bb.Y),
                                Math.Max(acc.Value.Right, bb.Right) - Math.Min(acc.Value.X, bb.X),
                                Math.Max(acc.Value.Bottom, bb.Bottom) - Math.Min(acc.Value.Y, bb.Y)
                            );
                }
                if (acc == null)
                    return false;
                bounds = acc.Value;
                return true;
            }
            case ViewModels.Tools.EntityKind.DimAligned:
            {
                var d = model.DimensionsAligned.FirstOrDefault(x =>
                    x.Id == sel.Id && IsVisible(x.Layer)
                );
                if (d == null)
                    return false;
                var v = new Point(d.P2.X - d.P1.X, d.P2.Y - d.P1.Y);
                var len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                if (len < 1e-6)
                {
                    bounds = RectFromMinMax(
                        Math.Min(d.P1.X, d.P2.X),
                        Math.Min(d.P1.Y, d.P2.Y),
                        Math.Max(d.P1.X, d.P2.X),
                        Math.Max(d.P1.Y, d.P2.Y)
                    );
                    return true;
                }
                var ux = v.X / len;
                var uy = v.Y / len;
                var nx = -uy;
                var ny = ux;
                var a = new Point(d.P1.X + nx * d.Offset, d.P1.Y + ny * d.Offset);
                var b = new Point(d.P2.X + nx * d.Offset, d.P2.Y + ny * d.Offset);
                bounds = BoundsFromPoints(new[] { d.P1, d.P2, a, b });
                return true;
            }
            case ViewModels.Tools.EntityKind.DimLinear:
            {
                var d = model.DimensionsLinear.FirstOrDefault(x =>
                    x.Id == sel.Id && IsVisible(x.Layer)
                );
                if (d == null)
                    return false;
                Point a,
                    b;
                if (d.Orientation == DimLinearOrientation.Horizontal)
                {
                    a = new Point(d.P1.X, d.P1.Y + d.Offset);
                    b = new Point(d.P2.X, d.P2.Y + d.Offset);
                }
                else
                {
                    a = new Point(d.P1.X + d.Offset, d.P1.Y);
                    b = new Point(d.P2.X + d.Offset, d.P2.Y);
                }
                bounds = BoundsFromPoints(new[] { d.P1, d.P2, a, b });
                return true;
            }
            default:
                return false;
        }
    }

    public static bool TryGetExtents(
        CadModel model,
        SelectedEntityRef sel,
        CadRenderOptions? options,
        out CadExtents extents
    )
    {
        extents = default!;
        if (!TryGetBounds(model, sel, options, out var r))
            return false;
        extents = ToExtents(r);
        return true;
    }

    public static CadExtents ToExtents(Rect r, double pad = 5.0)
    {
        return new CadExtents
        {
            MinX = r.X - pad,
            MinY = r.Y - pad,
            MaxX = r.Right + pad,
            MaxY = r.Bottom + pad,
        };
    }

    public static CadExtents? TryGetExtentsForEntity(
        CadModel model,
        ViewModels.Tools.EntityKind kind,
        string id,
        CadRenderOptions? options
    )
    {
        var sel = new SelectedEntityRef(id, kind);
        return TryGetExtents(model, sel, options, out var ext) ? ext : null;
    }

    public static CadExtents? TryGetExtentsForKindInLayer(
        CadModel model,
        ViewModels.Tools.EntityKind kind,
        string layer,
        CadRenderOptions? options
    )
    {
        var rects = new List<Rect>();
        bool Matches(string? l) =>
            string.Equals(l ?? string.Empty, layer, StringComparison.OrdinalIgnoreCase);
        switch (kind)
        {
            case ViewModels.Tools.EntityKind.Polyline:
                foreach (var pl in model.Polylines)
                    if (Matches(pl.Layer) && pl.Points != null && pl.Points.Count > 0)
                        rects.Add(BoundsFromPoints(pl.Points));
                break;
            case ViewModels.Tools.EntityKind.Line:
                foreach (var ln in model.Lines)
                    if (Matches(ln.Layer))
                        rects.Add(
                            RectFromMinMax(
                                Math.Min(ln.Start.X, ln.End.X),
                                Math.Min(ln.Start.Y, ln.End.Y),
                                Math.Max(ln.Start.X, ln.End.X),
                                Math.Max(ln.Start.Y, ln.End.Y)
                            )
                        );
                break;
            case ViewModels.Tools.EntityKind.Circle:
                foreach (var c in model.Circles)
                    if (Matches(c.Layer))
                        rects.Add(
                            new Rect(
                                c.Center.X - c.Radius,
                                c.Center.Y - c.Radius,
                                2 * c.Radius,
                                2 * c.Radius
                            )
                        );
                break;
            case ViewModels.Tools.EntityKind.Arc:
                foreach (var a in model.Arcs)
                    if (Matches(a.Layer))
                        rects.Add(BoundsFromArc(a));
                break;
            case ViewModels.Tools.EntityKind.Insert:
            {
                foreach (var i in model.Inserts)
                {
                    if (!Matches(i.Layer))
                        continue;
                    if (TryGetBoundsForInsert(model, i, options, out var bb))
                    {
                        rects.Add(bb);
                    }
                    else
                    {
                        var r = 1.0;
                        rects.Add(new Rect(i.Position.X - r, i.Position.Y - r, 2 * r, 2 * r));
                    }
                }
                break;
            }
            case ViewModels.Tools.EntityKind.Ellipse:
                foreach (var el in model.Ellipses)
                    if (Matches(el.Layer))
                        rects.Add(BoundsFromEllipse(el));
                break;
            case ViewModels.Tools.EntityKind.Text:
                foreach (var t in model.Texts)
                    if (Matches(t.Layer))
                    {
                        var h = Math.Max(t.Height, 0.0);
                        var w = h * 0.6 * (t.Value?.Length ?? 0);
                        rects.Add(
                            new Rect(
                                t.Position.X,
                                t.Position.Y - 0.8 * h,
                                Math.Max(0, w),
                                Math.Max(0, h)
                            )
                        );
                    }
                break;
            case ViewModels.Tools.EntityKind.MText:
                foreach (var mt in model.MTexts)
                    if (Matches(mt.Layer))
                    {
                        var h = Math.Max(mt.Height, 0.0);
                        var w = h * 0.6 * (mt.Value?.Length ?? 0);
                        rects.Add(
                            new Rect(
                                mt.Position.X,
                                mt.Position.Y - 0.8 * h,
                                Math.Max(0, w),
                                Math.Max(0, h)
                            )
                        );
                    }
                break;
            case ViewModels.Tools.EntityKind.Spline:
                foreach (var sp in model.Splines)
                    if (Matches(sp.Layer) && sp.Points != null && sp.Points.Count > 0)
                        rects.Add(BoundsFromPoints(sp.Points));
                break;
            case ViewModels.Tools.EntityKind.Solid:
                foreach (var so in model.Solids)
                    if (Matches(so.Layer) && so.Vertices != null && so.Vertices.Count > 0)
                        rects.Add(BoundsFromPoints(so.Vertices));
                break;
            case ViewModels.Tools.EntityKind.Hatch:
                foreach (var ha in model.Hatches)
                {
                    if (!Matches(ha.Layer) || ha.Loops == null || ha.Loops.Count == 0)
                        continue;
                    Rect? acc = null;
                    foreach (var loop in ha.Loops)
                    {
                        if (loop == null || loop.Count == 0)
                            continue;
                        var bb = BoundsFromPoints(loop);
                        acc =
                            acc == null
                                ? bb
                                : new Rect(
                                    Math.Min(acc.Value.X, bb.X),
                                    Math.Min(acc.Value.Y, bb.Y),
                                    Math.Max(acc.Value.Right, bb.Right)
                                        - Math.Min(acc.Value.X, bb.X),
                                    Math.Max(acc.Value.Bottom, bb.Bottom)
                                        - Math.Min(acc.Value.Y, bb.Y)
                                );
                    }
                    if (acc != null)
                        rects.Add(acc.Value);
                }
                break;
        }
        if (rects.Count == 0)
            return null;
        double minX = double.PositiveInfinity,
            minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity,
            maxY = double.NegativeInfinity;
        foreach (var rr in rects)
        {
            if (rr.X < minX)
                minX = rr.X;
            if (rr.Y < minY)
                minY = rr.Y;
            if (rr.Right > maxX)
                maxX = rr.Right;
            if (rr.Bottom > maxY)
                maxY = rr.Bottom;
        }
        var union = new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        return ToExtents(union);
    }

    public static CadExtents? TryGetExtentsForLayer(
        CadModel model,
        string layer,
        CadRenderOptions? options
    )
    {
        var kinds = Enum.GetValues(typeof(ViewModels.Tools.EntityKind))
            .Cast<ViewModels.Tools.EntityKind>();
        var parts = new List<CadExtents>();
        foreach (var k in kinds)
        {
            var e = TryGetExtentsForKindInLayer(model, k, layer, options);
            if (e != null)
                parts.Add(e);
        }
        if (parts.Count == 0)
            return null;
        double minX = double.PositiveInfinity,
            minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity,
            maxY = double.NegativeInfinity;
        foreach (var e in parts)
        {
            if (e.MinX < minX)
                minX = e.MinX;
            if (e.MinY < minY)
                minY = e.MinY;
            if (e.MaxX > maxX)
                maxX = e.MaxX;
            if (e.MaxY > maxY)
                maxY = e.MaxY;
        }
        return new CadExtents
        {
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
        };
    }

    public static Rect BoundsFromPoints(IReadOnlyList<Point> points)
    {
        double minX = double.PositiveInfinity,
            minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity,
            maxY = double.NegativeInfinity;
        foreach (var p in points)
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
        return RectFromMinMax(minX, minY, maxX, maxY);
    }

    public static Rect RectFromMinMax(double minX, double minY, double maxX, double maxY)
    {
        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    private static double DegreesToRadians(double deg) => deg * Math.PI / 180.0;

    public static Rect BoundsFromArc(CadArc a)
    {
        // Sample the arc to approximate bounds
        int segments = 36;
        var startRad = DegreesToRadians(a.StartAngle);
        var endRad = DegreesToRadians(a.EndAngle);
        while (endRad < startRad)
            endRad += Math.PI * 2;
        var sweep = endRad - startRad;
        var step = sweep / segments;
        double minX = double.PositiveInfinity,
            minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity,
            maxY = double.NegativeInfinity;
        for (int i = 0; i <= segments; i++)
        {
            var ang = startRad + i * step;
            var x = a.Center.X + a.Radius * Math.Cos(ang);
            var y = a.Center.Y + a.Radius * Math.Sin(ang);
            if (x < minX)
                minX = x;
            if (y < minY)
                minY = y;
            if (x > maxX)
                maxX = x;
            if (y > maxY)
                maxY = y;
        }
        return RectFromMinMax(minX, minY, maxX, maxY);
    }

    public static Rect BoundsFromEllipse(CadEllipse el)
    {
        int segments = 72;
        double minX = double.PositiveInfinity,
            minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity,
            maxY = double.NegativeInfinity;
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
            if (wx < minX)
                minX = wx;
            if (wy < minY)
                minY = wy;
            if (wx > maxX)
                maxX = wx;
            if (wy > maxY)
                maxY = wy;
        }
        return RectFromMinMax(minX, minY, maxX, maxY);
    }

    private static bool TryGetBoundsForInsert(
        CadModel model,
        CadInsert insert,
        CadRenderOptions? options,
        out Rect bounds
    )
    {
        bounds = default;
        if (model == null || insert == null)
        {
            return false;
        }

        bool IsVisible(string? layer)
        {
            if (string.IsNullOrWhiteSpace(layer))
                return true;
            return options?.IsLayerVisible(layer) ?? true;
        }

        var prefix = insert.Id + ":";
        double minX = double.PositiveInfinity,
            minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity,
            maxY = double.NegativeInfinity;
        void Acc(Rect r)
        {
            if (r.X < minX)
                minX = r.X;
            if (r.Y < minY)
                minY = r.Y;
            if (r.Right > maxX)
                maxX = r.Right;
            if (r.Bottom > maxY)
                maxY = r.Bottom;
        }

        // Polylines
        foreach (var pl in model.Polylines)
        {
            if (
                pl.Id.StartsWith(prefix, StringComparison.Ordinal)
                && IsVisible(pl.Layer)
                && pl.Points != null
                && pl.Points.Count > 0
            )
            {
                Acc(BoundsFromPoints(pl.Points));
            }
        }

        // Lines
        foreach (var ln in model.Lines)
        {
            if (ln.Id.StartsWith(prefix, StringComparison.Ordinal) && IsVisible(ln.Layer))
            {
                Acc(
                    RectFromMinMax(
                        Math.Min(ln.Start.X, ln.End.X),
                        Math.Min(ln.Start.Y, ln.End.Y),
                        Math.Max(ln.Start.X, ln.End.X),
                        Math.Max(ln.Start.Y, ln.End.Y)
                    )
                );
            }
        }

        // Circles
        foreach (var c in model.Circles)
        {
            if (c.Id.StartsWith(prefix, StringComparison.Ordinal) && IsVisible(c.Layer))
            {
                Acc(
                    new Rect(
                        c.Center.X - c.Radius,
                        c.Center.Y - c.Radius,
                        2 * c.Radius,
                        2 * c.Radius
                    )
                );
            }
        }

        // Arcs
        foreach (var a in model.Arcs)
        {
            if (a.Id.StartsWith(prefix, StringComparison.Ordinal) && IsVisible(a.Layer))
            {
                Acc(BoundsFromArc(a));
            }
        }

        // Ellipses
        foreach (var el in model.Ellipses)
        {
            if (el.Id.StartsWith(prefix, StringComparison.Ordinal) && IsVisible(el.Layer))
            {
                Acc(BoundsFromEllipse(el));
            }
        }

        // Texts
        foreach (var t in model.Texts)
        {
            if (t.Id.StartsWith(prefix, StringComparison.Ordinal) && IsVisible(t.Layer))
            {
                var h = Math.Max(t.Height, 0.0);
                var w = h * 0.6 * (t.Value?.Length ?? 0);
                Acc(new Rect(t.Position.X, t.Position.Y - 0.8 * h, Math.Max(0, w), Math.Max(0, h)));
            }
        }

        // MTexts
        foreach (var mt in model.MTexts)
        {
            if (mt.Id.StartsWith(prefix, StringComparison.Ordinal) && IsVisible(mt.Layer))
            {
                var h = Math.Max(mt.Height, 0.0);
                var w = Math.Max(mt.RectangleWidth, h * 0.6 * (mt.Value?.Length ?? 0));
                Acc(
                    new Rect(mt.Position.X, mt.Position.Y - 0.8 * h, Math.Max(0, w), Math.Max(0, h))
                );
            }
        }

        // Splines
        foreach (var sp in model.Splines)
        {
            if (
                sp.Id.StartsWith(prefix, StringComparison.Ordinal)
                && IsVisible(sp.Layer)
                && sp.Points != null
                && sp.Points.Count > 0
            )
            {
                Acc(BoundsFromPoints(sp.Points));
            }
        }

        // Solids
        foreach (var so in model.Solids)
        {
            if (
                so.Id.StartsWith(prefix, StringComparison.Ordinal)
                && IsVisible(so.Layer)
                && so.Vertices != null
                && so.Vertices.Count > 0
            )
            {
                Acc(BoundsFromPoints(so.Vertices));
            }
        }

        if (
            double.IsInfinity(minX)
            || double.IsInfinity(minY)
            || double.IsInfinity(maxX)
            || double.IsInfinity(maxY)
        )
        {
            return false;
        }

        bounds = RectFromMinMax(minX, minY, maxX, maxY);
        return true;
    }
}
