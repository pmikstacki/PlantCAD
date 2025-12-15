using System;
using System.Collections.Generic;
using System.Linq;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class HatchReader : ICadEntityReader
{
    public HatchReader() { }

    public bool CanRead(Entity entity) => entity is Hatch;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not Hatch h) return;

        try
        {
            var loops = new List<IReadOnlyList<Avalonia.Point>>();
            var loopCW = new List<bool>();
            foreach (var boundaryPath in h.Paths)
            {
                var loop = FlattenHatchPath(boundaryPath);
                if (loop.Count >= 3)
                {
                    loops.Add(loop);
                    loopCW.Add(IsClockwise(loop));
                }
            }

            if (loops.Count > 0)
            {
                // Precompute gradient fields and fill kind
                var grad = h.GradientColor;
                bool hasGrad = grad != null && grad.Colors != null && grad.Colors.Count > 0;
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

                // Pattern lines mapping (optional)
                IReadOnlyList<CadHatchPatternLine>? patternLines = null;
                if (h.Pattern?.Lines != null && h.Pattern.Lines.Count > 0)
                {
                    var list = new List<CadHatchPatternLine>(h.Pattern.Lines.Count);
                    foreach (var pline in h.Pattern.Lines)
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

                // Map DWG seed point (OCS) as pattern origin in world XY. If absent, keep null to default at (0,0) in renderer.
                Avalonia.Point? patternOrigin = null;
                if (h.SeedPoints != null && h.SeedPoints.Count > 0)
                {
                    var seed = h.SeedPoints[0];
                    patternOrigin = new Avalonia.Point(seed.X, seed.Y);
                }

                var patternName = h.Pattern?.Name;
                bool hasPatternLines = patternLines != null && patternLines.Count > 0;
                bool hasPatternDef = !string.IsNullOrWhiteSpace(patternName)
                                     && !string.Equals(patternName, "SOLID", StringComparison.OrdinalIgnoreCase);
                var fillKind = (hasPatternDef && hasPatternLines)
                    ? CadHatchFillKind.Pattern
                    : (hasGrad
                        ? CadHatchFillKind.Gradient
                        : (h.IsSolid ? CadHatchFillKind.Solid : CadHatchFillKind.Pattern));

                context.Hatches.Add(
                    new CadHatch
                    {
                        Id = h.Handle.ToString(),
                        Layer = h.Layer?.Name ?? string.Empty,
                        Loops = loops,
                        LoopClockwise = loopCW,
                        PatternName = h.Pattern?.Name,
                        // ACadSharp stores angles in radians (IsAngle) for Hatch.PatternAngle
                        PatternAngleDeg = h.PatternAngle * (180.0 / Math.PI),
                        PatternScale = h.PatternScale,
                        PatternDouble = h.IsDouble,
                        PatternOrigin = patternOrigin,
                        PatternLines = patternLines,
                        FillKind = fillKind,
                        GradientName = fillKind == CadHatchFillKind.Gradient ? gradName : null,
                        GradientAngleDeg = fillKind == CadHatchFillKind.Gradient ? gradAngleDeg : 0.0,
                        GradientStartColorArgb = fillKind == CadHatchFillKind.Gradient ? gradStart : null,
                        GradientEndColorArgb = fillKind == CadHatchFillKind.Gradient ? gradEnd : null,
                    }
                );
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read HATCH entity.", ex);
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
                        double bulge = cur.Z; // bulge stored in Z
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
                            // Arc angles are radians in ACadSharp; SampleArc expects degrees
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
}
