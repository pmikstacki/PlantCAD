using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using PlantCad.Gui.Models;
using PlantCad.Gui.ViewModels.Documents;
using PlantCad.Gui.ViewModels.Tools;

namespace PlantCad.Gui.PropertyGrid;

public static class SelectedEntityPresenters
{
    public static object? Create(CadModel model, SelectedEntityRef sel)
    {
        return sel.Kind switch
        {
            EntityKind.Polyline => model.Polylines.FirstOrDefault(x => x.Id == sel.Id) is { } pl
                ? new PolylineProps(pl)
                : null,
            EntityKind.Line => model.Lines.FirstOrDefault(x => x.Id == sel.Id) is { } ln
                ? new LineProps(ln)
                : null,
            EntityKind.Circle => model.Circles.FirstOrDefault(x => x.Id == sel.Id) is { } c
                ? new CircleProps(c)
                : null,
            EntityKind.Arc => model.Arcs.FirstOrDefault(x => x.Id == sel.Id) is { } a
                ? new ArcProps(a)
                : null,
            EntityKind.Insert => model.Inserts.FirstOrDefault(x => x.Id == sel.Id) is { } ins
                ? new InsertProps(ins)
                : null,
            EntityKind.Ellipse => model.Ellipses.FirstOrDefault(x => x.Id == sel.Id) is { } el
                ? new EllipseProps(el)
                : null,
            EntityKind.Text => model.Texts.FirstOrDefault(x => x.Id == sel.Id) is { } tx
                ? new TextProps(tx)
                : null,
            EntityKind.MText => model.MTexts.FirstOrDefault(x => x.Id == sel.Id) is { } mt
                ? new MTextProps(mt)
                : null,
            EntityKind.Spline => model.Splines.FirstOrDefault(x => x.Id == sel.Id) is { } sp
                ? new SplineProps(sp)
                : null,
            EntityKind.Solid => model.Solids.FirstOrDefault(x => x.Id == sel.Id) is { } so
                ? new SolidProps(so)
                : null,
            EntityKind.Hatch => model.Hatches.FirstOrDefault(x => x.Id == sel.Id) is { } h
                ? new HatchProps(h)
                : null,
            _ => null,
        };
    }

    private static string FormatPoint(Point p)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{p.X:0.###}, {p.Y:0.###}");
    }

    private static IReadOnlyList<string> FormatPoints(IEnumerable<Point> pts) =>
        pts.Select(FormatPoint).ToList();

    [DisplayName("Polyline")]
    public sealed class PolylineProps
    {
        private readonly CadPolyline _src;

        public PolylineProps(CadPolyline src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
            Points = FormatPoints(_src.Points);
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Geometry"), DisplayName("Is Closed"), ReadOnly(true)]
        public bool IsClosed => _src.IsClosed;

        [Category("Geometry"), DisplayName("Points Count"), ReadOnly(true)]
        public int PointsCount => _src.Points?.Count ?? 0;

        [Category("Geometry"), DisplayName("Points"), ReadOnly(true)]
        public IReadOnlyList<string> Points { get; }

        [Category("Geometry"), DisplayName("Bulges Count"), ReadOnly(true)]
        public int BulgesCount => _src.Bulges?.Count ?? 0;
    }

    [DisplayName("Line")]
    public sealed class LineProps
    {
        private readonly CadLine _src;

        public LineProps(CadLine src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Geometry"), DisplayName("Start"), ReadOnly(true)]
        public string Start => FormatPoint(_src.Start);

        [Category("Geometry"), DisplayName("End"), ReadOnly(true)]
        public string End => FormatPoint(_src.End);
    }

    [DisplayName("Circle")]
    public sealed class CircleProps
    {
        private readonly CadCircle _src;

        public CircleProps(CadCircle src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Geometry"), DisplayName("Center"), ReadOnly(true)]
        public string Center => FormatPoint(_src.Center);

        [Category("Geometry"), DisplayName("Radius"), ReadOnly(true)]
        public double Radius => _src.Radius;
    }

    [DisplayName("Arc")]
    public sealed class ArcProps
    {
        private readonly CadArc _src;

        public ArcProps(CadArc src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Geometry"), DisplayName("Center"), ReadOnly(true)]
        public string Center => FormatPoint(_src.Center);

        [Category("Geometry"), DisplayName("Radius"), ReadOnly(true)]
        public double Radius => _src.Radius;

        [Category("Geometry"), DisplayName("Start Angle (deg)"), ReadOnly(true)]
        public double StartAngle => _src.StartAngle;

        [Category("Geometry"), DisplayName("End Angle (deg)"), ReadOnly(true)]
        public double EndAngle => _src.EndAngle;
    }

    [DisplayName("Insert")]
    public sealed class InsertProps
    {
        private readonly CadInsert _src;

        public InsertProps(CadInsert src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Block"), DisplayName("Block Name"), ReadOnly(true)]
        public string BlockName => _src.BlockName;

        [Category("Transform"), DisplayName("Position"), ReadOnly(true)]
        public string Position => FormatPoint(_src.Position);

        [Category("Transform"), DisplayName("Scale X"), ReadOnly(true)]
        public double ScaleX => _src.ScaleX;

        [Category("Transform"), DisplayName("Scale Y"), ReadOnly(true)]
        public double ScaleY => _src.ScaleY;

        [Category("Transform"), DisplayName("Rotation (deg)"), ReadOnly(true)]
        public double RotationDeg => _src.RotationDeg;
    }

    [DisplayName("Ellipse")]
    public sealed class EllipseProps
    {
        private readonly CadEllipse _src;

        public EllipseProps(CadEllipse src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Geometry"), DisplayName("Center"), ReadOnly(true)]
        public string Center => FormatPoint(_src.Center);

        [Category("Geometry"), DisplayName("Radius X"), ReadOnly(true)]
        public double RadiusX => _src.RadiusX;

        [Category("Geometry"), DisplayName("Radius Y"), ReadOnly(true)]
        public double RadiusY => _src.RadiusY;

        [Category("Geometry"), DisplayName("Rotation (deg)"), ReadOnly(true)]
        public double RotationDeg => _src.RotationDeg;

        [Category("Geometry"), DisplayName("Is Arc"), ReadOnly(true)]
        public bool IsArc => _src.IsArc;

        [Category("Geometry"), DisplayName("Start Angle (deg)"), ReadOnly(true)]
        public double StartAngleDeg => _src.StartAngleDeg;

        [Category("Geometry"), DisplayName("End Angle (deg)"), ReadOnly(true)]
        public double EndAngleDeg => _src.EndAngleDeg;
    }

    [DisplayName("Text")]
    public sealed class TextProps
    {
        private readonly CadText _src;

        public TextProps(CadText src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Content"), DisplayName("Value"), ReadOnly(true)]
        public string Value => _src.Value;

        [Category("Text"), DisplayName("Height"), ReadOnly(true)]
        public double Height => _src.Height;

        [Category("Text"), DisplayName("Rotation (deg)"), ReadOnly(true)]
        public double RotationDeg => _src.RotationDeg;

        [Category("Text"), DisplayName("Position"), ReadOnly(true)]
        public string Position => FormatPoint(_src.Position);

        [Category("Text"), DisplayName("H Align"), ReadOnly(true)]
        public CadTextHAlign HorizontalAlignment => _src.HorizontalAlignment;

        [Category("Text"), DisplayName("V Align"), ReadOnly(true)]
        public CadTextVAlign VerticalAlignment => _src.VerticalAlignment;
    }

    [DisplayName("MText")]
    public sealed class MTextProps
    {
        private readonly CadMText _src;

        public MTextProps(CadMText src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Content"), DisplayName("Value"), ReadOnly(true)]
        public string Value => _src.Value;

        [Category("Text"), DisplayName("Height"), ReadOnly(true)]
        public double Height => _src.Height;

        [Category("Text"), DisplayName("Rotation (deg)"), ReadOnly(true)]
        public double RotationDeg => _src.RotationDeg;

        [Category("Text"), DisplayName("Position"), ReadOnly(true)]
        public string Position => FormatPoint(_src.Position);

        [Category("Text"), DisplayName("Rect Width"), ReadOnly(true)]
        public double RectangleWidth => _src.RectangleWidth;
    }

    [DisplayName("Spline")]
    public sealed class SplineProps
    {
        private readonly CadSpline _src;

        public SplineProps(CadSpline src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
            Points = FormatPoints(_src.Points);
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Geometry"), DisplayName("Is Closed"), ReadOnly(true)]
        public bool IsClosed => _src.IsClosed;

        [Category("Geometry"), DisplayName("Points Count"), ReadOnly(true)]
        public int PointsCount => _src.Points?.Count ?? 0;

        [Category("Geometry"), DisplayName("Points"), ReadOnly(true)]
        public IReadOnlyList<string> Points { get; }
    }

    [DisplayName("Solid")]
    public sealed class SolidProps
    {
        private readonly CadSolid _src;

        public SolidProps(CadSolid src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
            Vertices = FormatPoints(_src.Vertices);
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Geometry"), DisplayName("Vertices Count"), ReadOnly(true)]
        public int VerticesCount => _src.Vertices?.Count ?? 0;

        [Category("Geometry"), DisplayName("Vertices"), ReadOnly(true)]
        public IReadOnlyList<string> Vertices { get; }
    }

    [DisplayName("Hatch")]
    public sealed class HatchProps
    {
        private readonly CadHatch _src;

        public HatchProps(CadHatch src)
        {
            _src = src ?? throw new ArgumentNullException(nameof(src));
        }

        [Category("General"), DisplayName("Id"), ReadOnly(true)]
        public string Id => _src.Id;

        [Category("General"), DisplayName("Layer"), ReadOnly(true)]
        public string Layer => _src.Layer;

        [Category("Fill"), DisplayName("Fill Kind"), ReadOnly(true)]
        public CadHatchFillKind FillKind => _src.FillKind;

        [Category("Pattern"), DisplayName("Pattern Name"), ReadOnly(true)]
        public string? PatternName => _src.PatternName;

        [Category("Pattern"), DisplayName("Angle (deg)"), ReadOnly(true)]
        public double PatternAngleDeg => _src.PatternAngleDeg;

        [Category("Pattern"), DisplayName("Scale"), ReadOnly(true)]
        public double PatternScale => _src.PatternScale;

        [Category("Pattern"), DisplayName("Double"), ReadOnly(true)]
        public bool PatternDouble => _src.PatternDouble;

        [Category("Pattern"), DisplayName("Origin"), ReadOnly(true)]
        public string? PatternOrigin => _src.PatternOrigin is { } p ? FormatPoint(p) : null;

        [Category("Gradient"), DisplayName("Name"), ReadOnly(true)]
        public string? GradientName => _src.GradientName;

        [Category("Gradient"), DisplayName("Angle (deg)"), ReadOnly(true)]
        public double GradientAngleDeg => _src.GradientAngleDeg;

        [Category("Geometry"), DisplayName("Loops Count"), ReadOnly(true)]
        public int LoopsCount => _src.Loops?.Count ?? 0;
    }
}
