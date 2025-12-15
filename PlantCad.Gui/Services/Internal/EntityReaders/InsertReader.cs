using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class InsertReader : ICadEntityReader
{
    public InsertReader() { }

    public bool CanRead(Entity entity) => entity is Insert;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not Insert ins)
            return;

        if (ins.Block == null)
        {
            return;
        }

        context.Inserts.Add(
            new CadInsert
            {
                Id = ins.Handle.ToString(),
                Layer = ins.Layer?.Name ?? string.Empty,
                BlockName = ins.Block.Name,
                Position = new Avalonia.Point(ins.InsertPoint.X, ins.InsertPoint.Y),
                ScaleX = ins.XScale,
                ScaleY = ins.YScale,
                RotationDeg = ins.Rotation * (180.0 / Math.PI),
            }
        );

        // Expand block content into model space for immediate rendering
        var sx = ins.XScale;
        var sy = ins.YScale;
        var rot = ins.Rotation; // radians
        var tx = ins.InsertPoint.X;
        var ty = ins.InsertPoint.Y;

        foreach (var be in ins.Block.Entities)
        {
            switch (be)
            {
                case Hatch bh:
                {
                    try
                    {
                        var loops = new List<IReadOnlyList<Avalonia.Point>>();
                        var loopCW = new List<bool>();
                        foreach (var boundaryPath in bh.Paths)
                        {
                            var loopLocal = FlattenHatchPath(boundaryPath);
                            if (loopLocal.Count < 3)
                            {
                                continue;
                            }

                            var loopWorld = loopLocal
                                .Select(p => TransformPoint(p.X, p.Y, sx, sy, rot, tx, ty))
                                .ToList();
                            if (loopWorld.Count < 3)
                            {
                                continue;
                            }
                            loops.Add(loopWorld);
                            loopCW.Add(IsClockwise(loopWorld));
                        }

                        if (loops.Count == 0)
                        {
                            break;
                        }

                        var grad = bh.GradientColor;
                        bool hasGrad =
                            grad != null
                            && grad.Colors != null
                            && grad.Colors.Count > 0;
                        string? gradName = hasGrad ? grad!.Name : null;
                        double gradAngleDeg = hasGrad ? grad!.Angle * (180.0 / Math.PI) : 0.0;
                        uint? gradStart = null;
                        uint? gradEnd = null;
                        if (hasGrad)
                        {
                            var g = grad!;
                            var colors = g.Colors!;
                            var startCol = colors[0].Color;
                            var endCol = colors[colors.Count - 1].Color;
                            gradStart =
                                (0xFFu << 24)
                                | ((uint)startCol.R << 16)
                                | ((uint)startCol.G << 8)
                                | startCol.B;
                            gradEnd =
                                (0xFFu << 24)
                                | ((uint)endCol.R << 16)
                                | ((uint)endCol.G << 8)
                                | endCol.B;
                        }

                        IReadOnlyList<CadHatchPatternLine>? patternLines = null;
                        if (bh.Pattern?.Lines != null && bh.Pattern.Lines.Count > 0)
                        {
                            var list = new List<CadHatchPatternLine>(bh.Pattern.Lines.Count);
                            foreach (var pline in bh.Pattern.Lines)
                            {
                                list.Add(
                                    new CadHatchPatternLine
                                    {
                                        AngleDeg = pline.Angle * (180.0 / Math.PI),
                                        BaseX = pline.BasePoint.X,
                                        BaseY = pline.BasePoint.Y,
                                        OffsetX = pline.Offset.X,
                                        OffsetY = pline.Offset.Y,
                                        DashLengths = pline.DashLengths.ToArray(),
                                    }
                                );
                            }
                            patternLines = list;
                        }

                        Avalonia.Point? patternOrigin = null;
                        if (bh.SeedPoints != null && bh.SeedPoints.Count > 0)
                        {
                            var seed = bh.SeedPoints[0];
                            patternOrigin = TransformPoint(seed.X, seed.Y, sx, sy, rot, tx, ty);
                        }

                        var patternName = bh.Pattern?.Name;
                        bool hasPatternLines = patternLines != null && patternLines.Count > 0;
                        bool hasPatternDef = !string.IsNullOrWhiteSpace(patternName)
                                             && !string.Equals(patternName, "SOLID", StringComparison.OrdinalIgnoreCase);
                        var fillKind = (hasPatternDef && hasPatternLines)
                            ? CadHatchFillKind.Pattern
                            : (hasGrad
                                ? CadHatchFillKind.Gradient
                                : (bh.IsSolid ? CadHatchFillKind.Solid : CadHatchFillKind.Pattern));

                        var geomScale = Math.Max(Math.Abs(sx), Math.Abs(sy));
                        context.Hatches.Add(
                            new CadHatch
                            {
                                Id = $"{ins.Handle}:{bh.Handle}",
                                Layer = bh.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                                Loops = loops,
                                LoopClockwise = loopCW,
                                PatternName = bh.Pattern?.Name,
                                PatternAngleDeg =
                                    bh.PatternAngle * (180.0 / Math.PI) + rot * (180.0 / Math.PI),
                                PatternScale = bh.PatternScale * (geomScale <= 0 ? 1.0 : geomScale),
                                PatternDouble = bh.IsDouble,
                                PatternOrigin = patternOrigin,
                                PatternLines = patternLines,
                                FillKind = fillKind,
                                GradientName = gradName,
                                GradientAngleDeg = gradAngleDeg + rot * (180.0 / Math.PI),
                                GradientStartColorArgb = gradStart,
                                GradientEndColorArgb = gradEnd,
                            }
                        );
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Failed to read HATCH entity in INSERT '{ins.Handle}'.",
                            ex
                        );
                    }
                    break;
                }
                case LwPolyline blp:
                {
                    var pts = blp
                        .Vertices.Select(v =>
                            TransformPoint(v.Location.X, v.Location.Y, sx, sy, rot, tx, ty)
                        )
                        .ToList();
                    var bulges = blp.Vertices.Select(v => (double)v.Bulge).ToList();
                    context.Polylines.Add(
                        new CadPolyline
                        {
                            Id = $"{ins.Handle}:{blp.Handle}",
                            Layer = blp.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                            Points = pts,
                            Bulges = bulges,
                            IsClosed = blp.IsClosed,
                        }
                    );
                    break;
                }
                case Polyline2D bp2d:
                {
                    var pts = bp2d
                        .Vertices.Select(v =>
                            TransformPoint(v.Location.X, v.Location.Y, sx, sy, rot, tx, ty)
                        )
                        .ToList();
                    var bulges = bp2d.Vertices.Select(v => (double)v.Bulge).ToList();
                    context.Polylines.Add(
                        new CadPolyline
                        {
                            Id = $"{ins.Handle}:{bp2d.Handle}",
                            Layer = bp2d.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                            Points = pts,
                            Bulges = bulges,
                            IsClosed = bp2d.IsClosed,
                        }
                    );
                    break;
                }
                case Line bln:
                {
                    var startPt = TransformPoint(
                        bln.StartPoint.X,
                        bln.StartPoint.Y,
                        sx,
                        sy,
                        rot,
                        tx,
                        ty
                    );
                    var endPt = TransformPoint(bln.EndPoint.X, bln.EndPoint.Y, sx, sy, rot, tx, ty);
                    context.Lines.Add(
                        new CadLine
                        {
                            Id = $"{ins.Handle}:{bln.Handle}",
                            Layer = bln.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                            Start = startPt,
                            End = endPt,
                        }
                    );
                    break;
                }
                case Arc barc:
                {
                    if (Math.Abs(sx - sy) < 1e-9)
                    {
                        var centerArc = TransformPoint(
                            barc.Center.X,
                            barc.Center.Y,
                            sx,
                            sy,
                            rot,
                            tx,
                            ty
                        );
                        context.Arcs.Add(
                            new CadArc
                            {
                                Id = $"{ins.Handle}:{barc.Handle}",
                                Layer = barc.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                                Center = centerArc,
                                Radius = barc.Radius * sx,
                                StartAngle =
                                    barc.StartAngle * 180.0 / Math.PI + rot * 180.0 / Math.PI,
                                EndAngle = barc.EndAngle * 180.0 / Math.PI + rot * 180.0 / Math.PI,
                            }
                        );
                    }
                    else
                    {
                        // Approximate to polyline under non-uniform scaling
                        int segs = 48;
                        var pts = new List<Avalonia.Point>(segs + 1);
                        double a0Rad = barc.StartAngle;
                        double a1Rad = barc.EndAngle;
                        while (a1Rad < a0Rad)
                            a1Rad += Math.PI * 2.0;
                        for (int i = 0; i <= segs; i++)
                        {
                            double tseg = (double)i / segs;
                            double ang = a0Rad + (a1Rad - a0Rad) * tseg;
                            double x = barc.Center.X + barc.Radius * Math.Cos(ang);
                            double y = barc.Center.Y + barc.Radius * Math.Sin(ang);
                            pts.Add(TransformPoint(x, y, sx, sy, rot, tx, ty));
                        }
                        context.Polylines.Add(
                            new CadPolyline
                            {
                                Id = $"{ins.Handle}:{barc.Handle}",
                                Layer = barc.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                                Points = pts,
                                Bulges = new List<double>(),
                                IsClosed = false,
                            }
                        );
                    }
                    break;
                }
                case Circle bc:
                {
                    if (Math.Abs(sx - sy) < 1e-9)
                    {
                        var centerCircle = TransformPoint(
                            bc.Center.X,
                            bc.Center.Y,
                            sx,
                            sy,
                            rot,
                            tx,
                            ty
                        );
                        context.Circles.Add(
                            new CadCircle
                            {
                                Id = $"{ins.Handle}:{bc.Handle}",
                                Layer = bc.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                                Center = centerCircle,
                                Radius = bc.Radius * sx,
                            }
                        );
                    }
                    else
                    {
                        var centerCircle = TransformPoint(
                            bc.Center.X,
                            bc.Center.Y,
                            sx,
                            sy,
                            rot,
                            tx,
                            ty
                        );
                        context.Ellipses.Add(
                            new CadEllipse
                            {
                                Id = $"{ins.Handle}:{bc.Handle}",
                                Layer = bc.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                                Center = centerCircle,
                                RadiusX = bc.Radius * sx,
                                RadiusY = bc.Radius * sy,
                                RotationDeg = rot * 180.0 / Math.PI,
                                IsArc = false,
                                StartAngleDeg = 0,
                                EndAngleDeg = 360,
                            }
                        );
                    }
                    break;
                }
                case Ellipse bel:
                {
                    // Approximate to polyline for simplicity under possible non-uniform scaling
                    int segs = 64;
                    var pts = new List<Avalonia.Point>(segs + 1);
                    double start = bel.StartParameter;
                    double end = bel.EndParameter;
                    while (end < start)
                        end += Math.PI * 2.0;
                    for (int i = 0; i <= segs; i++)
                    {
                        double t = (double)i / segs;
                        double ang = start + (end - start) * t;
                        var local = bel.PolarCoordinateRelativeToCenter(ang);
                        pts.Add(TransformPoint(local.X, local.Y, sx, sy, rot, tx, ty));
                    }
                    context.Polylines.Add(
                        new CadPolyline
                        {
                            Id = $"{ins.Handle}:{bel.Handle}",
                            Layer = bel.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                            Points = pts,
                            Bulges = new List<double>(),
                            IsClosed = !bel.IsFullEllipse,
                        }
                    );
                    break;
                }
                case Spline bsp:
                {
                    var basePts = (
                        bsp.FitPoints != null && bsp.FitPoints.Count > 1
                            ? bsp.FitPoints
                            : bsp.ControlPoints
                    )
                        .Select(p => TransformPoint(p.X, p.Y, sx, sy, rot, tx, ty))
                        .ToList();
                    if (basePts.Count >= 2)
                    {
                        context.Splines.Add(
                            new CadSpline
                            {
                                Id = $"{ins.Handle}:{bsp.Handle}",
                                Layer = bsp.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                                Points = basePts,
                                IsClosed = bsp.IsClosed,
                            }
                        );
                    }
                    break;
                }
                case Solid bsol:
                {
                    var v = new List<Avalonia.Point>
                    {
                        TransformPoint(bsol.FirstCorner.X, bsol.FirstCorner.Y, sx, sy, rot, tx, ty),
                        TransformPoint(
                            bsol.SecondCorner.X,
                            bsol.SecondCorner.Y,
                            sx,
                            sy,
                            rot,
                            tx,
                            ty
                        ),
                        TransformPoint(bsol.ThirdCorner.X, bsol.ThirdCorner.Y, sx, sy, rot, tx, ty),
                        TransformPoint(
                            bsol.FourthCorner.X,
                            bsol.FourthCorner.Y,
                            sx,
                            sy,
                            rot,
                            tx,
                            ty
                        ),
                    };
                    if (Math.Abs(v[2].X - v[3].X) < 1e-9 && Math.Abs(v[2].Y - v[3].Y) < 1e-9)
                        v.RemoveAt(3);
                    context.Solids.Add(
                        new CadSolid
                        {
                            Id = $"{ins.Handle}:{bsol.Handle}",
                            Layer = bsol.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                            Vertices = v,
                        }
                    );
                    break;
                }
                case TextEntity btxt:
                {
                    var p = TransformPoint(
                        btxt.InsertPoint.X,
                        btxt.InsertPoint.Y,
                        sx,
                        sy,
                        rot,
                        tx,
                        ty
                    );
                    context.Texts.Add(
                        new CadText
                        {
                            Id = $"{ins.Handle}:{btxt.Handle}",
                            Layer = btxt.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                            Position = p,
                            RotationDeg = (btxt.Rotation + rot) * 180.0 / Math.PI,
                            Height = btxt.Height * Math.Max(Math.Abs(sx), Math.Abs(sy)),
                            Value = btxt.Value ?? string.Empty,
                        }
                    );
                    break;
                }
                case MText bmt:
                {
                    var p = TransformPoint(
                        bmt.InsertPoint.X,
                        bmt.InsertPoint.Y,
                        sx,
                        sy,
                        rot,
                        tx,
                        ty
                    );
                    context.MTexts.Add(
                        new CadMText
                        {
                            Id = $"{ins.Handle}:{bmt.Handle}",
                            Layer = bmt.Layer?.Name ?? ins.Layer?.Name ?? string.Empty,
                            Position = p,
                            RotationDeg = (bmt.Rotation + rot) * 180.0 / Math.PI,
                            Height = bmt.Height * Math.Max(Math.Abs(sx), Math.Abs(sy)),
                            RectangleWidth = bmt.RectangleWidth * Math.Abs(sx),
                            Value = bmt.Value ?? string.Empty,
                        }
                    );
                    break;
                }
            }
        }
    }

    private static List<Avalonia.Point> FlattenHatchPath(Hatch.BoundaryPath path)
    {
        var pts = new List<Avalonia.Point>();
        foreach (var edge in path.Edges)
        {
            switch (edge)
            {
                case Hatch.BoundaryPath.Polyline pl:
                {
                    var v = pl.Vertices;
                    if (v == null || v.Count < 2)
                        break;
                    int last = pl.IsClosed ? v.Count : v.Count - 1;
                    for (int i = 0; i < last; i++)
                    {
                        var cur = v[i];
                        var next = v[(i + 1) % v.Count];
                        double bulge = cur.Z;
                        var p0 = new Avalonia.Point(cur.X, cur.Y);
                        var p1 = new Avalonia.Point(next.X, next.Y);
                        if (Math.Abs(bulge) < 1e-9)
                        {
                            if (pts.Count == 0 || pts[^1] != p0)
                                pts.Add(p0);
                            if (pts.Count == 0 || pts[^1] != p1)
                                pts.Add(p1);
                        }
                        else
                        {
                            foreach (var p in SampleBulgeArc(p0, p1, bulge, 16))
                            {
                                if (pts.Count == 0 || pts[^1] != p)
                                    pts.Add(p);
                            }
                        }
                    }
                    break;
                }
                case Hatch.BoundaryPath.Line ln:
                {
                    var p0 = new Avalonia.Point(ln.Start.X, ln.Start.Y);
                    var p1 = new Avalonia.Point(ln.End.X, ln.End.Y);
                    if (pts.Count == 0 || pts[^1] != p0)
                        pts.Add(p0);
                    if (pts.Count == 0 || pts[^1] != p1)
                        pts.Add(p1);
                    break;
                }
                default:
                {
                    var e = edge.ToEntity();
                    switch (e)
                    {
                        case Polyline2D p2:
                        {
                            var verts = p2.Vertices.ToList();
                            int last = p2.IsClosed ? verts.Count : verts.Count - 1;
                            for (int i = 0; i < last; i++)
                            {
                                var a = verts[i];
                                var b = verts[(i + 1) % verts.Count];
                                if (Math.Abs(b.Bulge) < 1e-9)
                                {
                                    var p0 = new Avalonia.Point(a.Location.X, a.Location.Y);
                                    var p1 = new Avalonia.Point(b.Location.X, b.Location.Y);
                                    if (pts.Count == 0 || pts[^1] != p0)
                                        pts.Add(p0);
                                    pts.Add(p1);
                                }
                                else
                                {
                                    foreach (
                                        var p in SampleBulgeArc(
                                            new Avalonia.Point(a.Location.X, a.Location.Y),
                                            new Avalonia.Point(b.Location.X, b.Location.Y),
                                            b.Bulge,
                                            16
                                        )
                                    )
                                    {
                                        if (pts.Count == 0 || pts[^1] != p)
                                            pts.Add(p);
                                    }
                                }
                            }
                            break;
                        }
                        case ACadSharp.Entities.Line le:
                        {
                            var p0 = new Avalonia.Point(le.StartPoint.X, le.StartPoint.Y);
                            var p1 = new Avalonia.Point(le.EndPoint.X, le.EndPoint.Y);
                            if (pts.Count == 0 || pts[^1] != p0)
                                pts.Add(p0);
                            pts.Add(p1);
                            break;
                        }
                        case Arc ar:
                        {
                            foreach (
                                var p in SampleArc(
                                    new Avalonia.Point(ar.Center.X, ar.Center.Y),
                                    ar.Radius,
                                    ar.StartAngle * (180.0 / Math.PI),
                                    ar.EndAngle * (180.0 / Math.PI),
                                    32
                                )
                            )
                            {
                                if (pts.Count == 0 || pts[^1] != p)
                                    pts.Add(p);
                            }
                            break;
                        }
                        case Ellipse el:
                        {
                            foreach (
                                var p in SampleEllipse(
                                    new Avalonia.Point(el.Center.X, el.Center.Y),
                                    el.MajorAxis / 2.0,
                                    el.MajorAxis / 2.0 * el.RadiusRatio,
                                    el.StartParameter * 180.0 / Math.PI,
                                    el.EndParameter * 180.0 / Math.PI,
                                    el.Rotation * 180.0 / Math.PI,
                                    64
                                )
                            )
                            {
                                if (pts.Count == 0 || pts[^1] != p)
                                    pts.Add(p);
                            }
                            break;
                        }
                    }
                    break;
                }
            }
        }
        return pts;
    }

    private static IEnumerable<Avalonia.Point> SampleBulgeArc(
        Avalonia.Point p0,
        Avalonia.Point p1,
        double bulge,
        int segments
    )
    {
        double dx = p1.X - p0.X;
        double dy = p1.Y - p0.Y;
        double chord = Math.Sqrt(dx * dx + dy * dy);
        if (chord < 1e-12)
        {
            yield break;
        }
        double theta = 4.0 * Math.Atan(bulge);
        double r = chord / (2.0 * Math.Sin(Math.Abs(theta) / 2.0));
        double sagitta = bulge * chord / 2.0;
        double mx = (p0.X + p1.X) / 2.0;
        double my = (p0.Y + p1.Y) / 2.0;
        double ux = -dy / chord;
        double uy = dx / chord;
        double cx = mx + ux * sagitta;
        double cy = my + uy * sagitta;
        double a0 = Math.Atan2(p0.Y - cy, p0.X - cx);
        double a1 = a0 + theta;
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            double a = a0 + (a1 - a0) * t;
            yield return new Avalonia.Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        }
    }

    private static IEnumerable<Avalonia.Point> SampleArc(
        Avalonia.Point center,
        double radius,
        double startAngleDeg,
        double endAngleDeg,
        int segments
    )
    {
        double a0 = startAngleDeg * Math.PI / 180.0;
        double a1 = endAngleDeg * Math.PI / 180.0;
        while (a1 < a0)
            a1 += Math.PI * 2.0;
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            double a = a0 + (a1 - a0) * t;
            yield return new Avalonia.Point(
                center.X + radius * Math.Cos(a),
                center.Y + radius * Math.Sin(a)
            );
        }
    }

    private static IEnumerable<Avalonia.Point> SampleEllipse(
        Avalonia.Point center,
        double rx,
        double ry,
        double startDeg,
        double endDeg,
        double rotationDeg,
        int segments
    )
    {
        double a0 = startDeg * Math.PI / 180.0;
        double a1 = endDeg * Math.PI / 180.0;
        while (a1 < a0)
            a1 += Math.PI * 2.0;
        double r = rotationDeg * Math.PI / 180.0;
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            double a = a0 + (a1 - a0) * t;
            double lx = rx * Math.Cos(a);
            double ly = ry * Math.Sin(a);
            double x = center.X + (lx * Math.Cos(r) - ly * Math.Sin(r));
            double y = center.Y + (lx * Math.Sin(r) + ly * Math.Cos(r));
            yield return new Avalonia.Point(x, y);
        }
    }

    private static bool IsClockwise(IReadOnlyList<Avalonia.Point> loop)
    {
        if (loop == null || loop.Count < 3)
        {
            return false;
        }
        double sum = 0.0;
        for (int i = 0; i < loop.Count; i++)
        {
            var p0 = loop[i];
            var p1 = loop[(i + 1) % loop.Count];
            sum += (p1.X - p0.X) * (p1.Y + p0.Y);
        }
        return sum > 0;
    }

    private static Avalonia.Point TransformPoint(
        double x,
        double y,
        double sx,
        double sy,
        double rot,
        double tx,
        double ty
    )
    {
        double xr = x * sx;
        double yr = y * sy;
        double c = Math.Cos(rot);
        double s = Math.Sin(rot);
        double X = xr * c - yr * s + tx;
        double Y = xr * s + yr * c + ty;
        return new Avalonia.Point(X, Y);
    }
}
