using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PlantCad.Core.Data;
using PlantCad.Core.Entities;
using PlantCad.Core.Repositories;
using PlantCad.Gui.Controls.Renderers;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using APoint = Avalonia.Point;

namespace PlantCad.Gui.Services;

/// <summary>
/// Renders block thumbnails offscreen using Avalonia and the existing CadRendererHost pipeline.
/// </summary>
public sealed class BlockThumbnailRenderer
{
    private sealed class ThumbnailControl : Control
    {
        public CadModel? Model { get; set; }
        public IStyleProvider Style { get; set; } = new DefaultStyleProvider();
        public CadRenderOptions Options { get; set; } =
            new CadRenderOptions(Array.Empty<CadLayer>());
        public CadRendererHost Renderer { get; }
        public ViewportState State { get; } = new ViewportState();

        public ThumbnailControl()
        {
            var entityRenderers = new ICadEntityRenderer[]
            {
                new HatchRenderer(),
                new SolidRenderer(),
                new PolylineRenderer(),
                new SplineRenderer(),
                new LineRenderer(),
                new CircleRenderer(),
                new ArcRenderer(),
                new EllipseRenderer(),
                new TextRenderer(),
                new MTextRenderer(),
                new InsertRenderer(),
            };
            Renderer = new CadRendererHost(
                entityRenderers,
                Array.Empty<IOverlayRenderer>(),
                Array.Empty<IOverlayRenderer>()
            );
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var size = Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0 || Model is null)
            {
                return;
            }
            // Fit to model extents
            var rect = new Rect(size);
            State.FitToExtents(rect, Model.Extents, margin: 4);
            var overlay = new OverlayContext(
                false,
                null,
                new List<Rect>(),
                new List<IReadOnlyList<APoint>>()
            );
            Renderer.Render(context, State, Model, size, Style, overlay, Options);
        }
    }

    public byte[] RenderBlockToPng(
        CadDocument doc,
        BlockRecord block,
        int sizePx = 256,
        string background = "transparent"
    )
    {
        if (doc == null)
            throw new ArgumentNullException(nameof(doc));
        if (block == null)
            throw new ArgumentNullException(nameof(block));
        if (sizePx <= 0)
            throw new ArgumentOutOfRangeException(nameof(sizePx));

        var model = BuildModelFromBlock(doc, block);
        var layers = BuildLayers(doc);
        var style = new DefaultStyleProvider(layers);
        var options = new CadRenderOptions(layers);

        var control = new ThumbnailControl
        {
            Width = sizePx,
            Height = sizePx,
            Model = model,
            Style = style,
            Options = options,
        };
        control.Measure(new Size(sizePx, sizePx));
        control.Arrange(new Rect(0, 0, sizePx, sizePx));

        var rtb = new RenderTargetBitmap(new PixelSize(sizePx, sizePx), new Vector(96, 96));
        rtb.Render(control);

        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }

    public void GenerateAndStoreThumbnailsForDwg(
        IBlockRepository repo,
        ISqliteConnectionFactory factory,
        string dwgPath,
        int sizePx = 256,
        string background = "transparent",
        bool includeAnonymous = false
    )
    {
        if (repo == null)
            throw new ArgumentNullException(nameof(repo));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        if (string.IsNullOrWhiteSpace(dwgPath))
            throw new ArgumentException("DWG path must not be empty.", nameof(dwgPath));

        var doc = DwgReader.Read(dwgPath);
        using var conn = factory.Create();
        using var tx = conn.BeginTransaction();

        foreach (var br in doc.BlockRecords)
        {
            if (!includeAnonymous)
            {
                if (
                    string.IsNullOrEmpty(br.Name)
                    || br.Name.StartsWith("*", StringComparison.Ordinal)
                )
                {
                    continue;
                }
            }
            if (
                br.Name.Equals("*Model_Space", StringComparison.OrdinalIgnoreCase)
                || br.Name.Equals("*Paper_Space", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            var existing = repo.GetBySourceAndName(conn, tx, dwgPath, br.Name);
            if (existing == null)
            {
                continue; // not imported
            }
            var png = RenderBlockToPng(doc, br, sizePx, background);
            repo.ReplaceThumbnail(conn, tx, existing.Id, sizePx, png, background);
        }

        tx.Commit();
    }

    private static IReadOnlyList<CadLayer> BuildLayers(CadDocument doc)
    {
        var layers = new List<CadLayer>();
        foreach (var l in doc.Layers)
        {
            var color = l.Color;
            uint argb = (0xFFu << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            layers.Add(
                new CadLayer
                {
                    Name = l.Name,
                    ColorArgb = argb,
                    IsOn = l.IsOn,
                    IsFrozen = false,
                    IsLocked = false,
                }
            );
        }
        if (layers.Count == 0)
        {
            layers.Add(
                new CadLayer
                {
                    Name = "0",
                    ColorArgb = 0xFF000000,
                    IsOn = true,
                    IsFrozen = false,
                    IsLocked = false,
                }
            );
        }
        return layers;
    }

    private static CadModel BuildModelFromBlock(CadDocument doc, BlockRecord br)
    {
        var polylines = new List<CadPolyline>();
        var lines = new List<CadLine>();
        var circles = new List<CadCircle>();
        var arcs = new List<CadArc>();
        var inserts = new List<CadInsert>();
        var ellipses = new List<CadEllipse>();
        var texts = new List<CadText>();
        var mtexts = new List<CadMText>();
        var splines = new List<CadSpline>();
        var solids = new List<CadSolid>();
        var hatches = new List<CadHatch>();

        void AddEntity(
            Entity e,
            double sx,
            double sy,
            double rot,
            double tx,
            double ty,
            string? parentLayer
        )
        {
            switch (e)
            {
                case LwPolyline blp:
                {
                    var pts = blp
                        .Vertices.Select(v =>
                            TransformPoint(v.Location.X, v.Location.Y, sx, sy, rot, tx, ty)
                        )
                        .ToList();
                    var bulges = blp.Vertices.Select(v => (double)v.Bulge).ToList();
                    polylines.Add(
                        new CadPolyline
                        {
                            Id = blp.Handle.ToString(),
                            Layer = blp.Layer?.Name ?? parentLayer ?? string.Empty,
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
                    polylines.Add(
                        new CadPolyline
                        {
                            Id = bp2d.Handle.ToString(),
                            Layer = bp2d.Layer?.Name ?? parentLayer ?? string.Empty,
                            Points = pts,
                            Bulges = bulges,
                            IsClosed = bp2d.IsClosed,
                        }
                    );
                    break;
                }
                case Line bln:
                {
                    var a = TransformPoint(bln.StartPoint.X, bln.StartPoint.Y, sx, sy, rot, tx, ty);
                    var b = TransformPoint(bln.EndPoint.X, bln.EndPoint.Y, sx, sy, rot, tx, ty);
                    lines.Add(
                        new CadLine
                        {
                            Id = bln.Handle.ToString(),
                            Layer = bln.Layer?.Name ?? parentLayer ?? string.Empty,
                            Start = a,
                            End = b,
                        }
                    );
                    break;
                }
                case Arc barc:
                {
                    if (Math.Abs(sx - sy) < 1e-9)
                    {
                        var c = TransformPoint(barc.Center.X, barc.Center.Y, sx, sy, rot, tx, ty);
                        arcs.Add(
                            new CadArc
                            {
                                Id = barc.Handle.ToString(),
                                Layer = barc.Layer?.Name ?? parentLayer ?? string.Empty,
                                Center = c,
                                Radius = barc.Radius * sx,
                                StartAngle =
                                    barc.StartAngle * 180.0 / Math.PI + rot * 180.0 / Math.PI,
                                EndAngle = barc.EndAngle * 180.0 / Math.PI + rot * 180.0 / Math.PI,
                            }
                        );
                    }
                    else
                    {
                        int segs = 48;
                        var pts = new List<APoint>(segs + 1);
                        double a0 = barc.StartAngle,
                            a1 = barc.EndAngle;
                        while (a1 < a0)
                            a1 += Math.PI * 2.0;
                        for (int i = 0; i <= segs; i++)
                        {
                            double t = (double)i / segs;
                            double ang = a0 + (a1 - a0) * t;
                            double x = barc.Center.X + barc.Radius * Math.Cos(ang);
                            double y = barc.Center.Y + barc.Radius * Math.Sin(ang);
                            pts.Add(TransformPoint(x, y, sx, sy, rot, tx, ty));
                        }
                        polylines.Add(
                            new CadPolyline
                            {
                                Id = barc.Handle.ToString(),
                                Layer = barc.Layer?.Name ?? parentLayer ?? string.Empty,
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
                        var c = TransformPoint(bc.Center.X, bc.Center.Y, sx, sy, rot, tx, ty);
                        circles.Add(
                            new CadCircle
                            {
                                Id = bc.Handle.ToString(),
                                Layer = bc.Layer?.Name ?? parentLayer ?? string.Empty,
                                Center = c,
                                Radius = bc.Radius * sx,
                            }
                        );
                    }
                    else
                    {
                        var c = TransformPoint(bc.Center.X, bc.Center.Y, sx, sy, rot, tx, ty);
                        ellipses.Add(
                            new CadEllipse
                            {
                                Id = bc.Handle.ToString(),
                                Layer = bc.Layer?.Name ?? parentLayer ?? string.Empty,
                                Center = c,
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
                    int segs = 64;
                    var pts = new List<APoint>(segs + 1);
                    double start = bel.StartParameter,
                        end = bel.EndParameter;
                    while (end < start)
                        end += Math.PI * 2.0;
                    for (int i = 0; i <= segs; i++)
                    {
                        double t = (double)i / segs;
                        double ang = start + (end - start) * t;
                        var local = bel.PolarCoordinateRelativeToCenter(ang);
                        pts.Add(TransformPoint(local.X, local.Y, sx, sy, rot, tx, ty));
                    }
                    polylines.Add(
                        new CadPolyline
                        {
                            Id = bel.Handle.ToString(),
                            Layer = bel.Layer?.Name ?? parentLayer ?? string.Empty,
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
                        splines.Add(
                            new CadSpline
                            {
                                Id = bsp.Handle.ToString(),
                                Layer = bsp.Layer?.Name ?? parentLayer ?? string.Empty,
                                Points = basePts,
                                IsClosed = bsp.IsClosed,
                            }
                        );
                    }
                    break;
                }
                case Solid bsol:
                {
                    var v = new List<APoint>
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
                    solids.Add(
                        new CadSolid
                        {
                            Id = bsol.Handle.ToString(),
                            Layer = bsol.Layer?.Name ?? parentLayer ?? string.Empty,
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
                    texts.Add(
                        new CadText
                        {
                            Id = btxt.Handle.ToString(),
                            Layer = btxt.Layer?.Name ?? parentLayer ?? string.Empty,
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
                    mtexts.Add(
                        new CadMText
                        {
                            Id = bmt.Handle.ToString(),
                            Layer = bmt.Layer?.Name ?? parentLayer ?? string.Empty,
                            Position = p,
                            RotationDeg = (bmt.Rotation + rot) * 180.0 / Math.PI,
                            Height = bmt.Height * Math.Max(Math.Abs(sx), Math.Abs(sy)),
                            RectangleWidth = bmt.RectangleWidth * Math.Abs(sx),
                            Value = bmt.Value ?? string.Empty,
                        }
                    );
                    break;
                }
                case Insert ins:
                {
                    if (ins.Block == null)
                        break;
                    var csx = sx * ins.XScale;
                    var csy = sy * ins.YScale;
                    var crot = rot + ins.Rotation;
                    var ctx2 =
                        tx
                        + (Math.Cos(rot) * sx) * ins.InsertPoint.X
                        + (-Math.Sin(rot) * sy) * ins.InsertPoint.Y;
                    var cty2 =
                        ty
                        + (Math.Sin(rot) * sx) * ins.InsertPoint.X
                        + (Math.Cos(rot) * sy) * ins.InsertPoint.Y;
                    foreach (var be in ins.Block.Entities)
                    {
                        AddEntity(be, csx, csy, crot, ctx2, cty2, ins.Layer?.Name ?? parentLayer);
                    }
                    break;
                }
            }
        }

        foreach (var e in br.Entities)
        {
            AddEntity(e, 1.0, 1.0, 0.0, 0.0, 0.0, e.Layer?.Name);
        }

        var ext = ComputeExtents(
            polylines,
            lines,
            circles,
            arcs,
            inserts,
            ellipses,
            texts,
            splines,
            solids,
            mtexts,
            hatches
        );
        return new CadModel
        {
            Polylines = polylines,
            Lines = lines,
            Circles = circles,
            Arcs = arcs,
            Inserts = inserts,
            Ellipses = ellipses,
            Texts = texts,
            MTexts = mtexts,
            Splines = splines,
            Solids = solids,
            Hatches = hatches,
            Layers = BuildLayers(doc),
            Extents = ext,
        };
    }

    private static APoint TransformPoint(
        double x,
        double y,
        double sx,
        double sy,
        double rot,
        double tx,
        double ty
    )
    {
        var cos = Math.Cos(rot);
        var sin = Math.Sin(rot);
        var nx = cos * (x * sx) - sin * (y * sy) + tx;
        var ny = sin * (x * sx) + cos * (y * sy) + ty;
        return new APoint(nx, ny);
    }

    private static CadExtents ComputeExtents(
        List<CadPolyline> polylines,
        List<CadLine> lines,
        List<CadCircle> circles,
        List<CadArc> arcs,
        List<CadInsert> inserts,
        List<CadEllipse> ellipses,
        List<CadText> texts,
        List<CadSpline> splines,
        List<CadSolid> solids,
        List<CadMText> mtexts,
        List<CadHatch> hatches
    )
    {
        double minX = double.PositiveInfinity,
            minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity,
            maxY = double.NegativeInfinity;
        void Consider(APoint p)
        {
            if (p.X < minX)
                minX = p.X;
            if (p.X > maxX)
                maxX = p.X;
            if (p.Y < minY)
                minY = p.Y;
            if (p.Y > maxY)
                maxY = p.Y;
        }
        foreach (var pl in polylines)
        foreach (var p in pl.Points)
            Consider(p);
        foreach (var ln in lines)
        {
            Consider(ln.Start);
            Consider(ln.End);
        }
        foreach (var c in circles)
        {
            Consider(new APoint(c.Center.X - c.Radius, c.Center.Y - c.Radius));
            Consider(new APoint(c.Center.X + c.Radius, c.Center.Y + c.Radius));
        }
        foreach (var a in arcs)
        {
            // Approximate arc bbox conservatively by circle bbox
            Consider(new APoint(a.Center.X - a.Radius, a.Center.Y - a.Radius));
            Consider(new APoint(a.Center.X + a.Radius, a.Center.Y + a.Radius));
        }
        foreach (var el in ellipses)
        {
            // Conservative bbox using radii
            Consider(new APoint(el.Center.X - el.RadiusX, el.Center.Y - el.RadiusY));
            Consider(new APoint(el.Center.X + el.RadiusX, el.Center.Y + el.RadiusY));
        }
        foreach (var s in solids)
        foreach (var p in s.Vertices)
            Consider(p);
        foreach (var t in texts)
        {
            var w = t.Height * 0.6 * Math.Max(t.Value?.Length ?? 0, 1);
            Consider(new APoint(t.Position.X, t.Position.Y - 0.8 * t.Height));
            Consider(new APoint(t.Position.X + w, t.Position.Y + 0.2 * t.Height));
        }
        foreach (var mt in mtexts)
        {
            var w = Math.Max(
                mt.RectangleWidth,
                mt.Height * 0.6 * Math.Max(mt.Value?.Length ?? 0, 1)
            );
            Consider(new APoint(mt.Position.X, mt.Position.Y - 0.8 * mt.Height));
            Consider(new APoint(mt.Position.X + w, mt.Position.Y + 0.2 * mt.Height));
        }

        if (
            double.IsInfinity(minX)
            || double.IsInfinity(minY)
            || double.IsInfinity(maxX)
            || double.IsInfinity(maxY)
        )
        {
            minX = -1;
            minY = -1;
            maxX = 1;
            maxY = 1;
        }
        return new CadExtents
        {
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
        };
    }
}
