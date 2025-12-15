using System;
using System.Collections.Generic;
using Avalonia;

namespace PlantCad.Gui.Models;

public sealed class CadModel
{
    public CadProjectInfo? ProjectInfo { get; init; }
    public CadExtents Extents { get; init; } = new CadExtents();
    public IReadOnlyList<CadPolyline> Polylines { get; init; } = new List<CadPolyline>();
    public IReadOnlyList<CadInsert> Inserts { get; init; } = new List<CadInsert>();
    public IReadOnlyList<CadLine> Lines { get; init; } = new List<CadLine>();
    public IReadOnlyList<CadCircle> Circles { get; init; } = new List<CadCircle>();
    public IReadOnlyList<CadArc> Arcs { get; init; } = new List<CadArc>();
    public IReadOnlyList<CadEllipse> Ellipses { get; init; } = new List<CadEllipse>();
    public IReadOnlyList<CadText> Texts { get; init; } = new List<CadText>();
    public IReadOnlyList<CadMText> MTexts { get; init; } = new List<CadMText>();
    public IReadOnlyList<CadSpline> Splines { get; init; } = new List<CadSpline>();
    public IReadOnlyList<CadSolid> Solids { get; init; } = new List<CadSolid>();
    public IReadOnlyList<CadHatch> Hatches { get; init; } = new List<CadHatch>();
    public IReadOnlyList<CadPoint> Points { get; init; } = new List<CadPoint>();
    public IReadOnlyList<CadLeader> Leaders { get; init; } = new List<CadLeader>();
    public IReadOnlyList<CadDimAligned> DimensionsAligned { get; init; } =
        new List<CadDimAligned>();
    public IReadOnlyList<CadDimLinear> DimensionsLinear { get; init; } = new List<CadDimLinear>();
    public IReadOnlyList<CadRay> Rays { get; init; } = new List<CadRay>();
    public IReadOnlyList<CadXLine> XLines { get; init; } = new List<CadXLine>();
    public IReadOnlyList<CadWipeout> Wipeouts { get; init; } = new List<CadWipeout>();
    public IReadOnlyList<CadLayer> Layers { get; init; } = new List<CadLayer>();
    public IReadOnlyList<CadTable> Tables { get; init; } = new List<CadTable>();
    public IReadOnlyList<CadUnderlay> Underlays { get; init; } = new List<CadUnderlay>();
    public IReadOnlyList<CadShape> Shapes { get; init; } = new List<CadShape>();
    public IReadOnlyList<CadTolerance> Tolerances { get; init; } = new List<CadTolerance>();
}

public sealed class CadPolyline
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public IReadOnlyList<Point> Points { get; init; } = new List<Point>();

    // Bulge per vertex (applies to segment from this vertex to the next). Length should be >= Points.Count - 1.
    public IReadOnlyList<double> Bulges { get; init; } = new List<double>();
    public bool IsClosed { get; init; }
}

public sealed class CadInsert
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public string BlockName { get; init; } = string.Empty;
    public Point Position { get; init; }
    public double ScaleX { get; init; } = 1.0;
    public double ScaleY { get; init; } = 1.0;
    public double RotationDeg { get; init; } = 0.0;
}

public sealed class CadExtents
{
    public double MinX { get; init; }
    public double MinY { get; init; }
    public double MaxX { get; init; }
    public double MaxY { get; init; }
}

public sealed class CadLine
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Start { get; init; }
    public Point End { get; init; }
}

public sealed class CadPoint
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Position { get; init; }
}

public sealed class CadLeader
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public IReadOnlyList<Point> Points { get; init; } = new List<Point>();
    public bool ArrowAtEnd { get; init; } = true;
}

public sealed class CadDimAligned
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point P1 { get; init; }
    public Point P2 { get; init; }

    // Offset distance from the measured segment to the dimension line (world units)
    public double Offset { get; init; } = 5.0;

    // Optional override text; if null or empty, measured distance is shown
    public string? TextOverride { get; init; }

    // Text height in world units (converted to px at render time)
    public double TextHeight { get; init; } = 2.5;
    public bool ShowArrows { get; init; } = true;
}

public enum DimLinearOrientation
{
    Horizontal,
    Vertical,
}

public sealed class CadDimLinear
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point P1 { get; init; }
    public Point P2 { get; init; }
    public double Offset { get; init; } = 5.0;
    public string? TextOverride { get; init; }
    public double TextHeight { get; init; } = 2.5;
    public bool ShowArrows { get; init; } = true;
    public DimLinearOrientation Orientation { get; init; } = DimLinearOrientation.Horizontal;
}

public sealed class CadCircle
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Center { get; init; }
    public double Radius { get; init; }
}

public sealed class CadArc
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Center { get; init; }
    public double Radius { get; init; }

    // Angles in degrees, CAD convention (counter-clockwise, world units)
    public double StartAngle { get; init; }
    public double EndAngle { get; init; }
}

public sealed class CadEllipse
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Center { get; init; }
    public double RadiusX { get; init; }
    public double RadiusY { get; init; }

    // Rotation of the ellipse major axis in degrees
    public double RotationDeg { get; init; }

    // If true, this ellipse is an arc segment defined by start/end angles in degrees (relative to rotated local axes)
    public bool IsArc { get; init; }
    public double StartAngleDeg { get; init; }
    public double EndAngleDeg { get; init; }
}

public enum CadTextHAlign
{
    Left,
    Center,
    Right,
}

public enum CadTextVAlign
{
    Baseline,
    Bottom,
    Middle,
    Top,
}

public sealed class CadText
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Position { get; init; }
    public double RotationDeg { get; init; }
    public double Height { get; init; } = 2.5; // world units
    public string Value { get; init; } = string.Empty;
    public CadTextHAlign HorizontalAlignment { get; init; } = CadTextHAlign.Left;
    public CadTextVAlign VerticalAlignment { get; init; } = CadTextVAlign.Baseline;
}

public sealed class CadMText
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Position { get; init; }
    public double RotationDeg { get; init; }
    public double Height { get; init; } = 2.5; // world units
    public double RectangleWidth { get; init; }
    public string Value { get; init; } = string.Empty;
}

public sealed class CadSpline
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public IReadOnlyList<Point> Points { get; init; } = new List<Point>();
    public bool IsClosed { get; init; }
}

public sealed class CadSolid
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public IReadOnlyList<Point> Vertices { get; init; } = new List<Point>();
}

public sealed class CadRay
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Origin { get; init; }

    // Direction in world units (does not need to be normalized)
    public Point Direction { get; init; }
}

public sealed class CadXLine
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Origin { get; init; }

    // Direction in world units (does not need to be normalized)
    public Point Direction { get; init; }
}

public sealed class CadWipeout
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;

    // Closed polygon vertices in world coordinates; first and last may be different (renderer closes it)
    public IReadOnlyList<Point> Vertices { get; init; } = new List<Point>();

    // If true, draw the outline/frame using the layer stroke pen
    public bool ShowFrame { get; init; }
}

public sealed class CadHatch
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;

    // Each loop is a closed ring of points (first and last may be equal or will be closed by renderer)
    public IReadOnlyList<IReadOnlyList<Point>> Loops { get; init; } =
        new List<IReadOnlyList<Point>>();

    // Optional per-loop orientation: true = clockwise, false = counter-clockwise
    public IReadOnlyList<bool>? LoopClockwise { get; init; }

    // Fill kind and metadata
    public CadHatchFillKind FillKind { get; init; } = CadHatchFillKind.Solid;

    // Pattern
    public string? PatternName { get; init; }
    public double PatternAngleDeg { get; init; }
    public double PatternScale { get; init; } = 1.0;
    public bool PatternDouble { get; init; }

    // Optional origin/seed point for pattern phasing (DWG OCS seed mapped to world XY)
    public Point? PatternOrigin { get; init; }

    // Gradient (basic support)
    public string? GradientName { get; init; }
    public double GradientAngleDeg { get; init; }
    public uint? GradientStartColorArgb { get; init; }
    public uint? GradientEndColorArgb { get; init; }

    // Pattern lines (optional, when available from source)
    public IReadOnlyList<CadHatchPatternLine>? PatternLines { get; init; }
}

public enum CadHatchFillKind
{
    Solid,
    Pattern,
    Gradient,
}

public enum HatchRenderMode
{
    Auto,
    Vector,
    Raster,
}

public sealed class CadHatchPatternLine
{
    // Angle in degrees (world-space)
    public double AngleDeg { get; init; }

    // Base point of the line (world-space)
    public double BaseX { get; init; }
    public double BaseY { get; init; }

    // Offset vector to the next parallel line (world-space)
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }

    // Dash/gap sequence (positive = dash length, negative = gap length)
    public IReadOnlyList<double> DashLengths { get; init; } = Array.Empty<double>();
}

public sealed class CadLayer
{
    public string Name { get; init; } = string.Empty;

    // Store ARGB as 0xAARRGGBB for simplicity
    public uint ColorArgb { get; init; }
    public bool IsOn { get; init; } = true;
    public bool IsFrozen { get; init; }
    public bool IsLocked { get; init; }
}

public sealed class CadTable
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;

    // Overall outer bounds of the table in world space
    public Rect Bounds { get; init; }

    // Structure
    public int Rows { get; init; }
    public int Columns { get; init; }
    public IReadOnlyList<double> RowHeights { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> ColumnWidths { get; init; } = Array.Empty<double>();
    public int HeaderRowCount { get; init; } = 0;

    // Simple cell contents as plain strings (Rows x Columns)
    public string?[,] Cells { get; init; } = new string?[0, 0];

    // Optional per-cell horizontal and vertical alignment (same dimensions as Cells)
    public CadTextHAlign[,]? CellHAlign { get; init; }
    public CadTextVAlign[,]? CellVAlign { get; init; }

    // Optional per-cell background color (ARGB 0xAARRGGBB). When null, no override.
    public uint?[,]? CellBackgroundArgb { get; init; }

    // Optional per-cell border color (ARGB). When null, grid pen is used only.
    public uint?[,]? CellBorderArgb { get; init; }

    // Optional per-cell row/column span. Anchor cells should have values >= 1.
    // Covered cells may be left null or set to 0.
    public int[,]? CellRowSpan { get; init; }
    public int[,]? CellColSpan { get; init; }

    // Optional per-cell wrapping hint. When true, try word-wrapping; otherwise ellipsis.
    public bool[,]? CellWrap { get; init; }
}

public sealed class CadUnderlay
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;

    // Source identification
    public string? FilePath { get; init; }
    public string? ImageKey { get; init; }

    // Image native size in pixels (optional; useful for aspect when transform is used)
    public Size ImagePixelSize { get; init; }

    // Placement: either provide WorldQuad (four corners) or a WorldTransform that maps image rect to world
    public Point[]? WorldQuad { get; init; }
    public Matrix? WorldTransform { get; init; }

    // Optional clipping boundary (EvenOdd)
    public IReadOnlyList<IReadOnlyList<Point>>? ClipLoops { get; init; }

    // Display parameters
    public double Opacity { get; init; } = 1.0; // 0..1
    public bool Monochrome { get; init; } = false;
    public double Fade { get; init; } = 0.0; // 0..1
    public double Contrast { get; init; } = 0.0; // -1..1
}

public sealed class CadShape
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Position { get; init; }
    public double RotationDeg { get; init; } = 0.0;
    public double SizeW { get; init; } = 1.0;
    public string? GlyphName { get; init; }
}

public sealed class CadTolerance
{
    public string Id { get; init; } = string.Empty;
    public string Layer { get; init; } = string.Empty;
    public Point Position { get; init; }
    public double RotationDeg { get; init; } = 0.0;
    public double HeightW { get; init; } = 2.5;
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// Rendering options used by viewport and tools to control visibility.
/// Mutable with explicit change notifications; no silent state changes.
/// </summary>
public sealed class CadRenderOptions
{
    private readonly Dictionary<string, bool> _visibleLayers;
    private HashSet<string> _selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public CadRenderOptions(IEnumerable<CadLayer> layers)
    {
        if (layers is null)
        {
            throw new ArgumentNullException(nameof(layers));
        }
        _visibleLayers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in layers)
        {
            _visibleLayers[l.Name] = l.IsOn && !l.IsFrozen;
        }
    }

    public event Action? Changed;

    public bool IsLayerVisible(string? layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName))
        {
            return true;
        }
        return _visibleLayers.TryGetValue(layerName, out var v) ? v : true;
    }

    public bool IsSelected(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }
        return _selectedIds.Contains(id);
    }

    public void SetLayerVisible(string layerName, bool visible)
    {
        if (string.IsNullOrWhiteSpace(layerName))
        {
            throw new ArgumentException("Layer name must not be empty.", nameof(layerName));
        }
        _visibleLayers[layerName] = visible;
        Changed?.Invoke();
    }

    public void SetSelectedIds(IEnumerable<string> ids)
    {
        if (ids is null)
        {
            if (_selectedIds.Count == 0)
            {
                return; // no change
            }
            _selectedIds.Clear();
            return; // silent (no invalidation during render)
        }
        var newSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        if (_selectedIds.SetEquals(newSet))
        {
            return; // no change
        }
        _selectedIds = newSet;
        // silent (no invalidation during render)
    }

    public IReadOnlyCollection<string> SelectedIds => _selectedIds;

    public IReadOnlyDictionary<string, bool> SnapshotVisibleLayers() => _visibleLayers;

    // Hatch rendering options
    public HatchRenderMode HatchMode { get; set; } = HatchRenderMode.Auto;

    // If estimated line spacing in screen px is below this, prefer raster mode or skip detail
    public double MinVectorSpacingPx { get; set; } = 2.0;

    // Maximum number of pattern lines per hatch per frame to generate (safety cap)
    public int MaxLinesPerHatch { get; set; } = 1200;

    // Raster tile size in pixels (square)
    public int RasterTileSizePx { get; set; } = 256;

    // Debug visualization flags
    public bool ShowExtentsDebug { get; set; } = false;

    // LOD thresholds
    public double MinTextPixelHeight { get; set; } = 2.0;
    public double MinCurvePixelRadius { get; set; } = 1.0;
    public double MinCurvePixelLength { get; set; } = 0.5;

    // Insert rendering options
    public bool ShowInsertLabels { get; set; } = false;

    // Edge culling expansion in pixels to reduce popping at viewport borders
    public double EdgeCullingMarginPx { get; set; } = 64.0;

    // Minimum world-units expansion regardless of zoom level (helps at extreme zoom)
    public double EdgeCullingMinWorld { get; set; } = 5.0;

    // Additional expansion as percent of the larger side of visible world rect (0.0 - 1.0)
    public double EdgeCullingPercent { get; set; } = 0.10;
}
