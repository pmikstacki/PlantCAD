using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using PlantCad.Core.Data;
using PlantCad.Core.Entities;
using PlantCad.Core.Repositories;

namespace PlantCad.Core.Import;

/// <summary>
/// Imports block definitions from a DWG into the database, computing a content hash and basic extents.
/// Nested inserts are expanded recursively with composed transforms.
/// </summary>
public sealed class BlockImporter
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly IBlockRepository _blocks;

    public BlockImporter(ISqliteConnectionFactory factory, IBlockRepository blocks)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
    }

    public ImportSummary ImportFromDwg(string dwgPath, bool includeAnonymous = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dwgPath))
        {
            throw new ArgumentException("DWG path must not be empty.", nameof(dwgPath));
        }
        var doc = DwgReader.Read(dwgPath);
        return ImportFromDocument(doc, dwgPath, includeAnonymous, cancellationToken);
    }

    public ImportSummary ImportFromDocument(CadDocument doc, string sourcePath, bool includeAnonymous = false, CancellationToken cancellationToken = default)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));

        var summary = new ImportSummary();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        foreach (var br in doc.BlockRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip special/anonymous by default
            if (!includeAnonymous)
            {
                if (string.IsNullOrEmpty(br.Name) || br.Name.StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }
            }
            // Always skip model/paper spaces if exposed
            if (br.Name.Equals("*Model_Space", StringComparison.OrdinalIgnoreCase) ||
                br.Name.Equals("*Paper_Space", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Aggregate block content in local coordinates
            var agg = new GeometryAggregator();
            var visited = new HashSet<ulong>();
            VisitBlock(br, Transform2D.Identity, agg, visited, maxDepth: 32);

            if (!agg.HasAnyGeometry)
            {
                summary.SkippedEmpty++;
                continue;
            }

            var (hash, width, height) = agg.FinalizeHashAndExtents();

            var block = new BlockDef
            {
                SourcePath = sourcePath,
                BlockName = br.Name,
                BlockHandle = br.Handle.ToString(),
                VersionTag = "v1",
                ContentHash = hash,
                Unit = doc.Header?.InsUnits.ToString(),
                WidthWorld = width,
                HeightWorld = height
            };

            var id = _blocks.UpsertBlockDef((IDbConnection)conn, tx, block);
            summary.Upserted++;
        }

        tx.Commit();
        return summary;
    }

    private static void VisitBlock(BlockRecord block, Transform2D t, GeometryAggregator agg, HashSet<ulong> visited, int maxDepth)
    {
        if (block == null) throw new ArgumentNullException(nameof(block));
        if (maxDepth <= 0) throw new InvalidOperationException($"Max recursion depth reached while expanding block '{block.Name}'.");

        // Avoid infinite recursion for self-referential graphs by BlockRecord handle
        if (!visited.Add(block.Handle))
        {
            return;
        }

        try
        {
            foreach (var ent in block.Entities)
            {
                switch (ent)
                {
                    case LwPolyline lp:
                        HandlePolylineVertices(lp.Vertices.Select(v => (v.Location.X, v.Location.Y)), t, lp.Layer?.Name, closed: lp.IsClosed, agg);
                        break;
                    case Polyline2D p2d:
                        HandlePolylineVertices(p2d.Vertices.Select(v => (v.Location.X, v.Location.Y)), t, p2d.Layer?.Name, closed: p2d.IsClosed, agg);
                        break;
                    case Line ln:
                        {
                            var a = t.Apply(ln.StartPoint.X, ln.StartPoint.Y);
                            var b = t.Apply(ln.EndPoint.X, ln.EndPoint.Y);
                            agg.AddSegment(a.x, a.y, b.x, b.y, ln.Layer?.Name);
                        }
                        break;
                    case Arc a:
                        SampleArcAsPolyline(a.Center.X, a.Center.Y, a.Radius, a.StartAngle, a.EndAngle, segments: 64, t, a.Layer?.Name, agg);
                        break;
                    case Circle c:
                        SampleCircleAsPolyline(c.Center.X, c.Center.Y, c.Radius, segments: 64, t, c.Layer?.Name, agg);
                        break;
                    case Ellipse el:
                        SampleEllipseAsPolyline(el, segments: 72, t, el.Layer?.Name, agg);
                        break;
                    case Spline sp:
                        {
                            var pts = (sp.FitPoints != null && sp.FitPoints.Count > 1 ? sp.FitPoints : sp.ControlPoints)
                                .Select(p => (p.X, p.Y));
                            HandlePolylineVertices(pts, t, sp.Layer?.Name, closed: sp.IsClosed, agg);
                        }
                        break;
                    case Solid s:
                        HandlePolylineVertices(new[] { (s.FirstCorner.X, s.FirstCorner.Y), (s.SecondCorner.X, s.SecondCorner.Y), (s.ThirdCorner.X, s.ThirdCorner.Y), (s.FourthCorner.X, s.FourthCorner.Y) }, t, s.Layer?.Name, closed: true, agg);
                        break;
                    case TextEntity txt:
                        {
                            // Approximate text by a minimal rectangle using height; width ~ 0.6*height*len
                            var p = t.Apply(txt.InsertPoint.X, txt.InsertPoint.Y);
                            var h = Math.Max(txt.Height, 0.0);
                            var w = h * 0.6 * (txt.Value?.Length ?? 0);
                            agg.AddAabb(p.x, p.y - 0.8 * h, p.x + w, p.y + h * 0.2, txt.Layer?.Name);
                        }
                        break;
                    case MText mt:
                        {
                            var p = t.Apply(mt.InsertPoint.X, mt.InsertPoint.Y);
                            var h = Math.Max(mt.Height, 0.0);
                            var w = Math.Max(mt.RectangleWidth, h * 0.6 * (mt.Value?.Length ?? 0));
                            agg.AddAabb(p.x, p.y - 0.8 * h, p.x + w, p.y + h * 0.2, mt.Layer?.Name);
                        }
                        break;
                    case Insert ins:
                        {
                            if (ins.Block == null) break;
                            var child = Transform2D.Compose(t, Transform2D.FromInsert(ins));
                            VisitBlock(ins.Block, child, agg, visited, maxDepth - 1);
                        }
                        break;
                    // Hatch omitted for now due to complexity; bounding boxes will be covered by other geometry most of the time.
                }
            }
        }
        finally
        {
            visited.Remove(block.Handle);
        }
    }

    private static void HandlePolylineVertices(IEnumerable<(double x, double y)> verts, Transform2D t, string? layer, bool closed, GeometryAggregator agg)
    {
        var pts = new List<(double x, double y)>();
        foreach (var (x, y) in verts)
        {
            var p = t.Apply(x, y);
            pts.Add(p);
            agg.AddPoint(p.x, p.y, layer);
        }
        if (pts.Count >= 2)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                agg.AddSegment(pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y, layer);
            }
            if (closed)
            {
                agg.AddSegment(pts[^1].x, pts[^1].y, pts[0].x, pts[0].y, layer);
            }
        }
    }

    private static void SampleCircleAsPolyline(double cx, double cy, double r, int segments, Transform2D t, string? layer, GeometryAggregator agg)
    {
        if (segments < 8) segments = 8;
        var pts = new List<(double x, double y)>(segments);
        for (int i = 0; i < segments; i++)
        {
            var ang = (i / (double)segments) * Math.PI * 2.0;
            var x = cx + r * Math.Cos(ang);
            var y = cy + r * Math.Sin(ang);
            pts.Add(t.Apply(x, y));
        }
        HandlePolylineVertices(pts, Transform2D.Identity, layer, closed: true, agg);
    }

    private static void SampleArcAsPolyline(double cx, double cy, double r, double startRad, double endRad, int segments, Transform2D t, string? layer, GeometryAggregator agg)
    {
        if (segments < 8) segments = 8;
        while (endRad < startRad) endRad += Math.PI * 2.0;
        var pts = new List<(double x, double y)>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            var tseg = i / (double)segments;
            var ang = startRad + (endRad - startRad) * tseg;
            var x = cx + r * Math.Cos(ang);
            var y = cy + r * Math.Sin(ang);
            pts.Add(t.Apply(x, y));
        }
        HandlePolylineVertices(pts, Transform2D.Identity, layer, closed: false, agg);
    }

    private static void SampleEllipseAsPolyline(Ellipse el, int segments, Transform2D t, string? layer, GeometryAggregator agg)
    {
        if (segments < 12) segments = 12;
        double start = el.StartParameter;
        double end = el.EndParameter;
        if (el.IsFullEllipse) { start = 0.0; end = Math.PI * 2.0; }
        while (end < start) end += Math.PI * 2.0;
        var pts = new List<(double x, double y)>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            var tseg = i / (double)segments;
            var ang = start + (end - start) * tseg;
            var local = el.PolarCoordinateRelativeToCenter(ang);
            pts.Add(t.Apply(local.X, local.Y));
        }
        HandlePolylineVertices(pts, Transform2D.Identity, layer, closed: el.IsFullEllipse, agg);
    }

    public sealed class ImportSummary
    {
        public int Upserted { get; set; }
        public int SkippedEmpty { get; set; }
    }

    private readonly struct Transform2D
    {
        // 2D affine: [ sx*cos -sy*sin tx ]
        //            [ sx*sin  sy*cos ty ]
        //            [   0        0   1 ]
        private readonly double _m11, _m12, _m21, _m22, _tx, _ty;

        private Transform2D(double m11, double m12, double m21, double m22, double tx, double ty)
        {
            _m11 = m11; _m12 = m12; _m21 = m21; _m22 = m22; _tx = tx; _ty = ty;
        }

        public static Transform2D Identity => new Transform2D(1, 0, 0, 1, 0, 0);

        public (double x, double y) Apply(double x, double y)
        {
            var nx = _m11 * x + _m12 * y + _tx;
            var ny = _m21 * x + _m22 * y + _ty;
            return (nx, ny);
        }

        public static Transform2D Compose(Transform2D a, Transform2D b)
        {
            // result = a * b
            var m11 = a._m11 * b._m11 + a._m12 * b._m21;
            var m12 = a._m11 * b._m12 + a._m12 * b._m22;
            var m21 = a._m21 * b._m11 + a._m22 * b._m21;
            var m22 = a._m21 * b._m12 + a._m22 * b._m22;
            var tx = a._m11 * b._tx + a._m12 * b._ty + a._tx;
            var ty = a._m21 * b._tx + a._m22 * b._ty + a._ty;
            return new Transform2D(m11, m12, m21, m22, tx, ty);
        }

        public static Transform2D FromInsert(Insert ins)
        {
            var sx = ins.XScale;
            var sy = ins.YScale;
            var ang = ins.Rotation; // radians
            var cos = Math.Cos(ang);
            var sin = Math.Sin(ang);
            var m11 = sx * cos;
            var m12 = -sy * sin;
            var m21 = sx * sin;
            var m22 = sy * cos;
            var tx = ins.InsertPoint.X;
            var ty = ins.InsertPoint.Y;
            return new Transform2D(m11, m12, m21, m22, tx, ty);
        }
    }

    private sealed class GeometryAggregator
    {
        private readonly StringBuilder _sb = new StringBuilder(1024);
        private double _minX = double.PositiveInfinity;
        private double _minY = double.PositiveInfinity;
        private double _maxX = double.NegativeInfinity;
        private double _maxY = double.NegativeInfinity;
        private bool _hasGeom = false;

        public bool HasAnyGeometry => _hasGeom;

        public void AddPoint(double x, double y, string? layer)
        {
            _hasGeom = true;
            UpdateExtents(x, y);
            Append("P", layer);
            Append(x);
            Append(y);
            _sb.Append('\n');
        }

        public void AddSegment(double x1, double y1, double x2, double y2, string? layer)
        {
            _hasGeom = true;
            UpdateExtents(x1, y1);
            UpdateExtents(x2, y2);
            Append("L", layer);
            Append(x1); Append(y1); Append(x2); Append(y2);
            _sb.Append('\n');
        }

        public void AddAabb(double minX, double minY, double maxX, double maxY, string? layer)
        {
            _hasGeom = true;
            UpdateExtents(minX, minY);
            UpdateExtents(maxX, maxY);
            Append("B", layer);
            Append(minX); Append(minY); Append(maxX); Append(maxY);
            _sb.Append('\n');
        }

        private void Append(string token, string? layer)
        {
            _sb.Append(token);
            _sb.Append('|');
            _sb.Append(layer ?? string.Empty);
            _sb.Append('|');
        }

        private static string FormatNum(double v)
        {
            // stable formatting with fixed decimals
            return v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void Append(double v)
        {
            _sb.Append(FormatNum(v));
            _sb.Append('|');
        }

        private void UpdateExtents(double x, double y)
        {
            if (x < _minX) _minX = x;
            if (y < _minY) _minY = y;
            if (x > _maxX) _maxX = x;
            if (y > _maxY) _maxY = y;
        }

        public (string hash, double width, double height) FinalizeHashAndExtents()
        {
            var data = Encoding.UTF8.GetBytes(_sb.ToString());
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            var hex = Convert.ToHexString(hash);
            var width = double.IsInfinity(_minX) || double.IsInfinity(_maxX) ? 0.0 : Math.Max(0.0, _maxX - _minX);
            var height = double.IsInfinity(_minY) || double.IsInfinity(_maxY) ? 0.0 : Math.Max(0.0, _maxY - _minY);
            return (hex, width, height);
        }
    }
}
