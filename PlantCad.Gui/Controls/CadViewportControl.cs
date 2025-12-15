using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.HitTesting;
using PlantCad.Gui.Controls.Renderers;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Controls.Modes;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;
using PlantCad.Gui.Utilities;
using PlantCad.Gui.ViewModels.Documents;

namespace PlantCad.Gui.Controls;

public enum GridMode
{
    Screen,
    World,
}

public sealed class CadViewportControl : Control
{
    public static readonly StyledProperty<CadModel?> ModelProperty = AvaloniaProperty.Register<
        CadViewportControl,
        CadModel?
    >(nameof(Model));
    public static readonly StyledProperty<bool> IsSelectingProperty = AvaloniaProperty.Register<
        CadViewportControl,
        bool
    >(nameof(IsSelecting));
    public static readonly StyledProperty<bool> ShowGridProperty = AvaloniaProperty.Register<
        CadViewportControl,
        bool
    >(nameof(ShowGrid), true);
    public static readonly StyledProperty<GridMode> GridModeProperty = AvaloniaProperty.Register<
        CadViewportControl,
        GridMode
    >(nameof(GridMode), Controls.GridMode.World);
    public static readonly StyledProperty<double> GridStepWorldProperty = AvaloniaProperty.Register<
        CadViewportControl,
        double
    >(nameof(GridStepWorld), 100.0);
    public static readonly StyledProperty<int> GridStepScreenProperty = AvaloniaProperty.Register<
        CadViewportControl,
        int
    >(nameof(GridStepScreen), 50);
    public static readonly StyledProperty<CadRenderOptions?> RenderOptionsProperty =
        AvaloniaProperty.Register<CadViewportControl, CadRenderOptions?>(nameof(RenderOptions));
    public static readonly StyledProperty<bool> ZoomRequiresCtrlProperty =
        AvaloniaProperty.Register<CadViewportControl, bool>(nameof(ZoomRequiresCtrl), false);
    public static readonly StyledProperty<bool> ZoomWithCtrlDragProperty =
        AvaloniaProperty.Register<CadViewportControl, bool>(nameof(ZoomWithCtrlDrag), true);
    public static readonly StyledProperty<SelectedEntityRef?> SelectedEntityProperty =
        AvaloniaProperty.Register<CadViewportControl, SelectedEntityRef?>(nameof(SelectedEntity));
    public static readonly StyledProperty<SelectedEntityRef?> HoveredEntityProperty =
        AvaloniaProperty.Register<CadViewportControl, SelectedEntityRef?>(nameof(HoveredEntity));

    // Optional external provider of module shapes (world-space). If set, overlay will render them.
    public Func<IEnumerable<IReadOnlyList<Point>>>? ModuleShapesProvider { get; set; }

    public CadModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    // Public helpers for external tools
    public Point ScreenToWorld(Point p) => _state.ScreenToWorld(p);
    public Point WorldToScreen(Point p) => _state.WorldToScreen(p);
    public void RequestInvalidate() => InvalidateVisual();

    public Control? HudContent
    {
        get => _hudPopup.Child as Control;
        set
        {
            _hudPopup.Child = value;
            _hudPopup.PlacementTarget = this;
            RepositionHud();
            _hudPopup.IsOpen = value != null;
        }
    }

    private void RepositionHud()
    {
        var cx = Bounds.Width > 0 ? Bounds.Width / 2.0 : 0.0;
        _hudPopup.PlacementRect = new Rect(cx, 0, 1, 1);
        _hudPopup.Placement = PlacementMode.Bottom;
        _hudPopup.HorizontalOffset = 0;
        _hudPopup.VerticalOffset = 8;
    }

    public void SetMode(IViewportMode? mode)
    {
        if (ReferenceEquals(_activeMode, mode))
            return;
        _activeMode?.OnExit(this);
        _activeMode = mode;
        _activeMode?.OnEnter(this);
    }

    public void ClearMode()
    {
        SetMode(new NavigationMode());
    }

    private void OnStyleProviderChanged(IStyleProvider? provider)
    {
        try
        {
            if (provider != null)
            {
                _style = provider;
            }
            else
            {
                var layers = Model?.Layers;
                _style =
                    (layers != null && layers.Count > 0)
                        ? new DefaultStyleProvider(layers)
                        : new DefaultStyleProvider();
            }
            InvalidateVisual();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to apply style provider.", ex);
        }
    }

    private void OnViewChanged()
    {
        // Persist transform per document and invalidate
        var doc = ServiceRegistry.ActiveDocument;
        if (doc != null)
        {
            doc.ViewportTransform = _state.Transform;
        }
        // Invalidate hover hit-test cache on any view change (pan/zoom)
        if (_screenPointCache.Count > 0)
            _screenPointCache.Clear();
        InvalidateVisual();
    }

    private void OnPointerDoubleClicked(Point screenPoint)
    {
        var model = Model;
        if (model == null)
            return;
        const double pixelTol = 6.0;
        if (!TryHit(screenPoint, pixelTol, out var hit))
            return;
        if (CadEntityExtents.TryGetBounds(model, hit, _options, out var bb))
        {
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            _state.FitToExtents(
                bounds,
                new CadExtents
                {
                    MinX = bb.Left,
                    MinY = bb.Top,
                    MaxX = bb.Right,
                    MaxY = bb.Bottom,
                },
                margin: 24
            );
            var fitScale = Math.Max(Math.Abs(_state.Transform.M11), 1e-12);
            _state.SetScaleLimits(fitScale / 100.0, fitScale * 10000.0);
            _needsFit = false;
            InvalidateVisual();
        }
    }

    public bool ZoomRequiresCtrl
    {
        get => GetValue(ZoomRequiresCtrlProperty);
        set => SetValue(ZoomRequiresCtrlProperty, value);
    }

    public bool ZoomWithCtrlDrag
    {
        get => GetValue(ZoomWithCtrlDragProperty);
        set => SetValue(ZoomWithCtrlDragProperty, value);
    }

    public SelectedEntityRef? SelectedEntity
    {
        get => GetValue(SelectedEntityProperty);
        set => SetValue(SelectedEntityProperty, value);
    }

    public SelectedEntityRef? HoveredEntity
    {
        get => GetValue(HoveredEntityProperty);
        set => SetValue(HoveredEntityProperty, value);
    }

    private static bool IsValidExtents(CadExtents e)
    {
        if (
            double.IsNaN(e.MinX)
            || double.IsNaN(e.MinY)
            || double.IsNaN(e.MaxX)
            || double.IsNaN(e.MaxY)
        )
            return false;
        if (
            double.IsInfinity(e.MinX)
            || double.IsInfinity(e.MinY)
            || double.IsInfinity(e.MaxX)
            || double.IsInfinity(e.MaxY)
        )
            return false;
        return e.MaxX > e.MinX && e.MaxY > e.MinY;
    }

    private static CadExtents ComputeModelExtents(CadModel model)
    {
        double minX = double.PositiveInfinity,
            minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity,
            maxY = double.NegativeInfinity;

        void IncludePoint(Point p)
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

        if (model.Polylines != null)
        {
            foreach (var pl in model.Polylines)
            {
                if (pl.Points == null)
                    continue;
                foreach (var p in pl.Points)
                    IncludePoint(p);
            }
        }
        if (model.Leaders != null)
        {
            foreach (var ld in model.Leaders)
            {
                if (ld.Points == null)
                    continue;
                foreach (var p in ld.Points)
                    IncludePoint(p);
            }
        }
        if (model.Lines != null)
        {
            foreach (var ln in model.Lines)
            {
                IncludePoint(ln.Start);
                IncludePoint(ln.End);
            }
        }
        if (model.Circles != null)
        {
            foreach (var c in model.Circles)
            {
                IncludePoint(new Point(c.Center.X - c.Radius, c.Center.Y - c.Radius));
                IncludePoint(new Point(c.Center.X + c.Radius, c.Center.Y + c.Radius));
            }
        }
        if (model.Arcs != null)
        {
            foreach (var a in model.Arcs)
            {
                // approximate via circle bbox
                IncludePoint(new Point(a.Center.X - a.Radius, a.Center.Y - a.Radius));
                IncludePoint(new Point(a.Center.X + a.Radius, a.Center.Y + a.Radius));
            }
        }
        if (model.Inserts != null)
        {
            foreach (var ins in model.Inserts)
            {
                IncludePoint(ins.Position);
            }
        }
        if (model.Ellipses != null)
        {
            foreach (var el in model.Ellipses)
            {
                var bb = BoundsFromEllipse(el);
                IncludePoint(new Point(bb.X, bb.Y));
                IncludePoint(new Point(bb.Right, bb.Bottom));
            }
        }
        if (model.Texts != null)
        {
            foreach (var t in model.Texts)
            {
                // approximate width as 0.6 * height per character
                var h = Math.Max(t.Height, 0.0);
                var w = h * 0.6 * (t.Value?.Length ?? 0);
                IncludePoint(new Point(t.Position.X, t.Position.Y - 0.2 * h));
                IncludePoint(new Point(t.Position.X + w, t.Position.Y + 0.8 * h));
            }
        }
        if (model.MTexts != null)
        {
            foreach (var mt in model.MTexts)
            {
                var h = Math.Max(mt.Height, 0.0);
                var rectW = Math.Max(mt.RectangleWidth, 0.0);
                var text = mt.Value ?? string.Empty;
                // Estimate chars per line using average glyph width 0.6 * height
                var charsPerLine =
                    h > 0 && rectW > 0
                        ? Math.Max((int)Math.Floor(rectW / (0.6 * h)), 1)
                        : text.Length;
                var lines =
                    charsPerLine > 0
                        ? Math.Max((int)Math.Ceiling((double)text.Length / charsPerLine), 1)
                        : 1;
                var totalHeight = lines * h * 1.2; // line spacing
                var bb = new Rect(mt.Position.X, mt.Position.Y, rectW, totalHeight);
                IncludePoint(new Point(bb.X, bb.Y));
                IncludePoint(new Point(bb.Right, bb.Bottom));
            }
        }
        if (model.Splines != null)
        {
            foreach (var sp in model.Splines)
            {
                if (sp.Points == null || sp.Points.Count == 0)
                    continue;
                var bb = BoundsFromPoints(sp.Points);
                IncludePoint(new Point(bb.X, bb.Y));
                IncludePoint(new Point(bb.Right, bb.Bottom));
            }
        }
        if (model.Solids != null)
        {
            foreach (var so in model.Solids)
            {
                if (so.Vertices == null || so.Vertices.Count == 0)
                    continue;
                var bb = BoundsFromPoints(so.Vertices);
                IncludePoint(new Point(bb.X, bb.Y));
                IncludePoint(new Point(bb.Right, bb.Bottom));
            }
        }
        if (model.Hatches != null)
        {
            foreach (var ha in model.Hatches)
            {
                if (ha.Loops == null || ha.Loops.Count == 0)
                    continue;
                foreach (var loop in ha.Loops)
                {
                    if (loop == null || loop.Count == 0)
                        continue;
                    var bb = BoundsFromPoints(loop);
                    IncludePoint(new Point(bb.X, bb.Y));
                    IncludePoint(new Point(bb.Right, bb.Bottom));
                }
            }
        }
        // Dimensions
        if (model.DimensionsAligned != null)
        {
            foreach (var d in model.DimensionsAligned)
            {
                var v = new Point(d.P2.X - d.P1.X, d.P2.Y - d.P1.Y);
                var len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                if (len < 1e-6)
                {
                    IncludePoint(d.P1);
                    IncludePoint(d.P2);
                    continue;
                }
                var ux = v.X / len;
                var uy = v.Y / len;
                var nx = -uy;
                var ny = ux;
                var a = new Point(d.P1.X + nx * d.Offset, d.P1.Y + ny * d.Offset);
                var b = new Point(d.P2.X + nx * d.Offset, d.P2.Y + ny * d.Offset);
                IncludePoint(d.P1);
                IncludePoint(d.P2);
                IncludePoint(a);
                IncludePoint(b);
            }
        }
        if (model.DimensionsLinear != null)
        {
            foreach (var d in model.DimensionsLinear)
            {
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
                IncludePoint(d.P1);
                IncludePoint(d.P2);
                IncludePoint(a);
                IncludePoint(b);
            }
        }

        if (
            double.IsInfinity(minX)
            || double.IsInfinity(minY)
            || double.IsInfinity(maxX)
            || double.IsInfinity(maxY)
        )
        {
            // No geometry, provide a small box around origin
            return new CadExtents
            {
                MinX = -50,
                MinY = -50,
                MaxX = 50,
                MaxY = 50,
            };
        }
        // Add a small padding
        var pad = 1e-3;
        return new CadExtents
        {
            MinX = minX - pad,
            MinY = minY - pad,
            MaxX = maxX + pad,
            MaxY = maxY + pad,
        };
    }

    private void ComputeSelectedBounds(Rect selectionWorld)
    {
        _selectedWorldBounds.Clear();
        var model = Model;
        if (model == null)
        {
            return;
        }

        // Polylines
        if (model.Polylines != null)
        {
            foreach (var pl in model.Polylines)
            {
                if (pl.Points == null || pl.Points.Count == 0)
                    continue;
                var bb = BoundsFromPoints(pl.Points);
                if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                {
                    _selectedWorldBounds.Add(bb);
                }
            }
        }

        // Lines
        if (model.Lines != null)
        {
            foreach (var ln in model.Lines)
            {
                var bb = RectFromMinMax(
                    Math.Min(ln.Start.X, ln.End.X),
                    Math.Min(ln.Start.Y, ln.End.Y),
                    Math.Max(ln.Start.X, ln.End.X),
                    Math.Max(ln.Start.Y, ln.End.Y)
                );
                if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                {
                    _selectedWorldBounds.Add(bb);
                }
            }
        }

        // Circles
        if (model.Circles != null)
        {
            foreach (var c in model.Circles)
            {
                var bb = new Rect(
                    c.Center.X - c.Radius,
                    c.Center.Y - c.Radius,
                    2 * c.Radius,
                    2 * c.Radius
                );
                if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                {
                    _selectedWorldBounds.Add(bb);
                }
            }
        }

        // Arcs (approximate via sampling)
        if (model.Arcs != null)
        {
            foreach (var a in model.Arcs)
            {
                var bb = BoundsFromArc(a);
                if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                {
                    _selectedWorldBounds.Add(bb);
                }
            }
        }

        // Inserts (treat as small boxes around point)
        if (model.Inserts != null)
        {
            foreach (var ins in model.Inserts)
            {
                var r = 1.0; // world units small box
                var bb = new Rect(ins.Position.X - r, ins.Position.Y - r, 2 * r, 2 * r);
                if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                {
                    _selectedWorldBounds.Add(bb);
                }
            }
        }
        // Ellipses
        if (model.Ellipses != null)
        {
            foreach (var el in model.Ellipses)
            {
                var bb = BoundsFromEllipse(el);
                if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                {
                    _selectedWorldBounds.Add(bb);
                }
            }
        }
        // Texts
        if (model.Texts != null)
        {
            foreach (var t in model.Texts)
            {
                var h = Math.Max(t.Height, 0.0);
                var w = h * 0.6 * (t.Value?.Length ?? 0);
                var bb = new Rect(
                    t.Position.X,
                    t.Position.Y - 0.8 * h,
                    Math.Max(0, w),
                    Math.Max(0, h)
                );
                if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                {
                    _selectedWorldBounds.Add(bb);
                }
            }
        }
        // MTexts
        if (model.MTexts != null)
        {
            foreach (var mt in model.MTexts)
            {
                var h = Math.Max(mt.Height, 0.0);
                var rectW = Math.Max(mt.RectangleWidth, 0.0);
                var text = mt.Value ?? string.Empty;
                var charsPerLine =
                    h > 0 && rectW > 0
                        ? Math.Max((int)Math.Floor(rectW / (0.6 * h)), 1)
                        : text.Length;
                var lines =
                    charsPerLine > 0
                        ? Math.Max((int)Math.Ceiling((double)text.Length / charsPerLine), 1)
                        : 1;
                var totalHeight = lines * h * 1.2;
                var bb = new Rect(mt.Position.X, mt.Position.Y, rectW, totalHeight);
                if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                {
                    _selectedWorldBounds.Add(bb);
                }
            }
        }
        // Splines
        if (model.Splines != null)
        {
            foreach (var sp in model.Splines)
            {
                if (sp.Points == null || sp.Points.Count == 0)
                    continue;
                var bb = BoundsFromPoints(sp.Points);
                if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                {
                    _selectedWorldBounds.Add(bb);
                }
            }
        }
        // Solids
        if (model.Solids != null)
        {
            foreach (var so in model.Solids)
            {
                if (so.Vertices == null || so.Vertices.Count == 0)
                    continue;
                var bb = BoundsFromPoints(so.Vertices);
                if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                {
                    _selectedWorldBounds.Add(bb);
                }
            }
        }
        // Hatches
        if (model.Hatches != null)
        {
            foreach (var ha in model.Hatches)
            {
                if (ha.Loops == null || ha.Loops.Count == 0)
                    continue;
                foreach (var loop in ha.Loops)
                {
                    if (loop == null || loop.Count == 0)
                        continue;
                    var bb = BoundsFromPoints(loop);
                    if (bb.Intersects(selectionWorld) || selectionWorld.Contains(bb))
                    {
                        _selectedWorldBounds.Add(bb);
                    }
                }
            }
        }
    }

    private void ApplyMouseSettings()
    {
        var s = ServiceRegistry.MouseSettings;
        if (s == null)
        {
            return;
        }
        // Update control-level switches
        ZoomRequiresCtrl = s.ZoomRequiresCtrl;
        ZoomWithCtrlDrag = s.ZoomWithCtrlDrag;
        // Update input controller parameters
        _input.ScrollUpZoomIn = s.ScrollUpZoomIn;
        // Clamp zoom base to > 1 to avoid near-identity factors that cause rounding drift
        var zoomBase = s.ZoomBase;
        if (zoomBase <= 1.0)
        {
            _logger?.LogWarning(
                "Invalid ZoomBase={ZoomBase:0.###}. Clamping to 1.2 to ensure visible zoom steps.",
                zoomBase
            );
            zoomBase = 1.2;
        }
        _input.ZoomBase = zoomBase;
        _input.MaxStepsPerEvent = s.MaxStepsPerEvent;
        _input.ResponseExponent = s.ResponseExponent;
        _input.ResponseMin = s.ResponseMin;
        _input.ResponseMax = s.ResponseMax;
        _input.DragPixelsPerStep = s.DragPixelsPerStep;
        _input.ZoomPivot = s.ZoomPivot;
        // Update animator smoothing
        _animator.ApplySettings(s.SmoothZoomEnabled, s.SmoothZoomHalfLifeMs);
        _logger?.LogInformation(
            "Mouse settings applied: ctrlWheel={CtrlWheel}, ctrlDrag={CtrlDrag}, base={Base:0.###}, steps={Steps}, respExp={Exp:0.###}",
            ZoomRequiresCtrl,
            ZoomWithCtrlDrag,
            s.ZoomBase,
            s.MaxStepsPerEvent,
            s.ResponseExponent
        );
    }

    private static Rect BoundsFromPoints(IReadOnlyList<Point> points)
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

    private static Rect BoundsFromArc(CadArc a)
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

    private static Rect RectFromMinMax(double minX, double minY, double maxX, double maxY)
    {
        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    private static double DegreesToRadians(double deg) => deg * Math.PI / 180.0;

    private static Rect BoundsFromEllipse(CadEllipse el)
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

    public bool IsSelecting
    {
        get => GetValue(IsSelectingProperty);
        set => SetValue(IsSelectingProperty, value);
    }
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }
    public GridMode GridMode
    {
        get => GetValue(GridModeProperty);
        set => SetValue(GridModeProperty, value);
    }
    public double GridStepWorld
    {
        get => GetValue(GridStepWorldProperty);
        set => SetValue(GridStepWorldProperty, value);
    }
    public int GridStepScreen
    {
        get => GetValue(GridStepScreenProperty);
        set => SetValue(GridStepScreenProperty, value);
    }
    public CadRenderOptions? RenderOptions
    {
        get => GetValue(RenderOptionsProperty);
        set => SetValue(RenderOptionsProperty, value);
    }

    private readonly ViewportState _state;
    private readonly ViewportInputController _input;
    private IStyleProvider _style;
    private CadRendererHost _renderer;
    private bool _needsFit = true;
    private CadRenderOptions? _options;
    private readonly ILogger? _logger;
    private readonly List<Rect> _selectedWorldBounds = new();
    private readonly List<IReadOnlyList<Point>> _hoveredWorldPaths = new();
    private ViewportAnimator _animator;
    private readonly Stopwatch _frameStopwatch = new();
    private double _emaFrameMs = 16.0; // initial guess ~60 FPS
    private IViewportMode? _activeMode;
    private readonly Popup _hudPopup = new Popup();

    // Cache for expensive world->screen polyline/spline conversions during hover/hit-test.
    // Cleared on view changes to avoid stale transforms.
    private readonly Dictionary<string, IReadOnlyList<Point>> _screenPointCache = new();
    private const int MaxCachedScreenPolylines = 512;

    public event Action<Rect>? SelectionCompleted;
    // Expose raw pointer events (screen space) for external tools (e.g., module shape editor)
    public event Action<Point>? RawPointerPressed;
    public event Action<Point>? RawPointerMoved;
    public event Action<Point>? RawPointerReleased;
    // Extended raw input events with modifiers/keys
    public event Action<Point, KeyModifiers>? RawPointerPressedEx;
    public event Action<Key, KeyModifiers>? RawKeyDown;

    public CadViewportControl()
    {
        _state = new ViewportState();
        _style = ServiceRegistry.StyleProvider ?? new DefaultStyleProvider();
        _input = new ViewportInputController(_state, InvalidateVisual, () => new Rect(Bounds.Size));
        _animator = new ViewportAnimator(_state, OnViewChanged, () => _input.ZoomBase);
        _input.Animator = _animator;
        _input.SelectionCompleted += r =>
        {
            try
            {
                ComputeSelectedBounds(r);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to compute selection bounds.", ex);
            }
            SelectionCompleted?.Invoke(r);
            InvalidateVisual();
        };
        _input.Clicked += OnPointerClicked;
        _input.PointerMovedForHover += OnPointerMovedForHover;
        _input.ViewChanged += OnViewChanged;
        _input.DoubleClicked += OnPointerDoubleClicked;
        _input.IsSelecting = IsSelecting;
        _input.ZoomRequiresCtrl = ZoomRequiresCtrl;
        _input.ZoomWithCtrlDrag = ZoomWithCtrlDrag;

        // Apply global mouse settings, if available
        ApplyMouseSettings();
        if (ServiceRegistry.MouseSettings != null)
        {
            ServiceRegistry.MouseSettings.Changed += ApplyMouseSettings;
        }

        _renderer = BuildRenderer();

        // Listen for cross-tool zoom requests
        ServiceRegistry.ZoomToRequested += OnZoomToRequested;
        // Listen for global style changes
        ServiceRegistry.StyleProviderChanged += OnStyleProviderChanged;

        SizeChanged += (_, __) => RepositionHud();

        _logger = ServiceRegistry.LoggerFactory?.CreateLogger("CadViewportControl");
        _logger?.LogInformation("CadViewportControl constructed");

        // Ensure we can receive keyboard input
        Focusable = true;
        _frameStopwatch.Start();

        // Default to navigation mode
        SetMode(new NavigationMode());
    }

    public override void Render(DrawingContext context)
    {
        try
        {
            // Frame timing
            var dtMs = _frameStopwatch.Elapsed.TotalMilliseconds;
            _frameStopwatch.Restart();
            // EMA smoothing
            const double alpha = 0.1;
            _emaFrameMs = alpha * dtMs + (1 - alpha) * _emaFrameMs;
            var fps = _emaFrameMs > 0.001 ? 1000.0 / _emaFrameMs : 0.0;
            var bounds = new Rect(Bounds.Size);
            var model = Model;
            if (model != null && _needsFit)
            {
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    // Try restore from document persisted transform first
                    var doc = ServiceRegistry.ActiveDocument;
                    if (doc?.ViewportTransform is { } vt)
                    {
                        _state.SetCustom(vt);
                        var fitScaleRest = Math.Max(Math.Abs(_state.Transform.M11), 1e-12);
                        _state.SetScaleLimits(fitScaleRest / 100.0, fitScaleRest * 10000.0);
                        _needsFit = false;
                    }
                    else
                    {
                        var ext = IsValidExtents(model.Extents)
                            ? model.Extents
                            : ComputeModelExtents(model);
                        _logger?.LogInformation(
                            "Initial fit: using extents=({MinX:0.###},{MinY:0.###})-({MaxX:0.###},{MaxY:0.###}); model.Extents valid={Valid}",
                            ext.MinX,
                            ext.MinY,
                            ext.MaxX,
                            ext.MaxY,
                            IsValidExtents(model.Extents)
                        );
                        _state.FitToExtents(bounds, ext, margin: 16);
                        // Set dynamic zoom scale limits relative to fitted scale
                        var fitScale = Math.Max(Math.Abs(_state.Transform.M11), 1e-12);
                        var minScale = fitScale / 100.0; // allow 100x zoom out from fit
                        var maxScale = fitScale * 10000.0; // allow 10k x zoom in from fit
                        _state.SetScaleLimits(minScale, maxScale);
                        _needsFit = false;
                    }
                }
            }

            var overlayContext = new OverlayContext(
                IsSelecting,
                _input.CurrentSelectionScreenRect,
                _selectedWorldBounds,
                _hoveredWorldPaths,
                fps,
                _emaFrameMs
            );
            _renderer.Render(context, _state, model, Bounds.Size, _style, overlayContext, _options);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to render CadViewportControl.", ex);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ModelProperty)
        {
            // Reset transform to fit when model changes
            _state.ResetToIdentity();
            _renderer = BuildRenderer();
            _needsFit = true;
            _selectedWorldBounds.Clear();
            // Spatial index disabled
            // Rebuild style provider based on model layers
            var m = Model;
            var layers = m?.Layers;
            _style =
                ServiceRegistry.StyleProvider
                ?? (
                    (layers != null && layers.Count > 0)
                        ? new DefaultStyleProvider(layers)
                        : new DefaultStyleProvider()
                );
            _logger?.LogInformation("Model changed. Layers={LayerCount}", layers?.Count ?? 0);
            InvalidateVisual();
        }
        else if (change.Property == IsSelectingProperty)
        {
            _input.IsSelecting = IsSelecting;
            _logger?.LogInformation("IsSelecting changed: {Selecting}", IsSelecting);
            InvalidateVisual();
        }
        else if (change.Property == ZoomRequiresCtrlProperty)
        {
            _input.ZoomRequiresCtrl = ZoomRequiresCtrl;
            _logger?.LogInformation("ZoomRequiresCtrl changed: {Val}", ZoomRequiresCtrl);
        }
        else if (change.Property == ZoomWithCtrlDragProperty)
        {
            _input.ZoomWithCtrlDrag = ZoomWithCtrlDrag;
            _logger?.LogInformation("ZoomWithCtrlDrag changed: {Val}", ZoomWithCtrlDrag);
        }
        else if (change.Property == BoundsProperty)
        {
            // Refit on size change only if view has not been customized by the user
            if (!_state.IsCustom && Model != null)
            {
                _needsFit = true;
            }
            _logger?.LogInformation(
                "Bounds changed to {W}x{H}. needsFit={NeedsFit}",
                Bounds.Width,
                Bounds.Height,
                _needsFit
            );
            InvalidateVisual();
        }
        else if (change.Property == RenderOptionsProperty)
        {
            if (_options != null)
            {
                _options.Changed -= InvalidateVisual;
            }
            _options = RenderOptions;
            if (_options != null)
            {
                _options.Changed += InvalidateVisual;
            }
            _logger?.LogInformation("Render options changed");
            // Rebuild renderer to reflect option-driven overlays (e.g., extents debug)
            _renderer = BuildRenderer();
            // Recompute selected bounds in case layer visibility affects current selection
            try
            {
                UpdateSelectedEntityBounds();
                // Propagate selected ID(s) into render options for renderer-side logging/behavior
                var sel = SelectedEntity;
                if (_options != null)
                {
                    if (sel != null)
                        _options.SetSelectedIds(new[] { sel.Id });
                    else
                        _options.SetSelectedIds(Array.Empty<string>());
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to update selected entity bounds after render options change.",
                    ex
                );
            }
            InvalidateVisual();
        }
        else if (change.Property == SelectedEntityProperty)
        {
            try
            {
                UpdateSelectedEntityBounds();
                // Propagate selected ID(s) into render options for renderer-side logging/behavior
                var sel = SelectedEntity;
                if (_options != null)
                {
                    if (sel != null)
                        _options.SetSelectedIds(new[] { sel.Id });
                    else
                        _options.SetSelectedIds(Array.Empty<string>());
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to update selected entity bounds.", ex);
            }
            InvalidateVisual();
        }
        else if (
            change.Property == ShowGridProperty
            || change.Property == GridModeProperty
            || change.Property == GridStepWorldProperty
            || change.Property == GridStepScreenProperty
        )
        {
            _renderer = BuildRenderer();
            _logger?.LogInformation(
                "Grid settings changed: show={Show}, mode={Mode}, worldStep={WorldStep}, screenStep={ScreenStep}",
                ShowGrid,
                GridMode,
                GridStepWorld,
                GridStepScreen
            );
            InvalidateVisual();
        }
        else if (change.Property == HoveredEntityProperty)
        {
            try
            {
                UpdateHoveredPaths();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to update hovered entity paths.", ex);
            }
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        RawPointerPressed?.Invoke(pos);
        RawPointerPressedEx?.Invoke(pos, e.KeyModifiers);
        if (_activeMode?.OnPointerPressed(pos) == true
            || _activeMode?.OnPointerPressedEx(pos, e.KeyModifiers) == true)
        {
            e.Handled = true;
            return;
        }
        _input.OnPointerPressed(this, e);
    }

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        RawPointerMoved?.Invoke(pos);
        if (_activeMode?.OnPointerMoved(pos) == true)
        {
            e.Handled = true;
            return;
        }
        _input.OnPointerMoved(this, e);
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pos = e.GetPosition(this);
        RawPointerReleased?.Invoke(pos);
        if (_activeMode?.OnPointerReleased(pos) == true)
        {
            e.Handled = true;
            return;
        }
        _input.OnPointerReleased(this, e);
    }

    protected override void OnPointerWheelChanged(Avalonia.Input.PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_activeMode?.OnWheel(e) == true)
        {
            e.Handled = true;
            return;
        }
        _input.OnPointerWheelChanged(this, e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        RawKeyDown?.Invoke(e.Key, e.KeyModifiers);
        if (_activeMode?.OnKeyDown(e.Key, e.KeyModifiers) == true)
        {
            e.Handled = true;
            return;
        }
        // Keyboard panning in world units converted to screen pixels
        var s = ServiceRegistry.MouseSettings;
        if (s == null)
        {
            return;
        }
        double stepWorld = s.KeyboardPanWorldStep;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            stepWorld *= 5.0;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            stepWorld *= 0.2;

        var scale = Math.Max(Math.Abs(_state.Transform.M11), 1e-12);
        double dx = 0,
            dy = 0;
        switch (e.Key)
        {
            case Key.Left:
                dx = -stepWorld * scale;
                break;
            case Key.Right:
                dx = stepWorld * scale;
                break;
            case Key.Up:
                dy = -stepWorld * scale; // world +Y -> screen -Y
                break;
            case Key.Down:
                dy = stepWorld * scale; // world -Y -> screen +Y
                break;
            default:
                return;
        }
        _state.Pan(dx, dy);
        OnViewChanged();
        e.Handled = true;
    }

    private CadRendererHost BuildRenderer()
    {
        var entities = new ICadEntityRenderer[]
        {
            new HatchRenderer(),
            new SolidRenderer(),
            new WipeoutRenderer(),
            new UnderlayRenderer(),
            new PolylineRenderer(),
            new SplineRenderer(),
            new LineRenderer(),
            new RayRenderer(),
            new XLineRenderer(),
            new PointRenderer(),
            new CircleRenderer(),
            new ArcRenderer(),
            new EllipseRenderer(),
            new LeaderRenderer(),
            new DimensionRenderer(),
            new TableRenderer(),
            new TextRenderer(),
            new MTextRenderer(),
            new InsertRenderer(),
        };

        IOverlayRenderer[] pre;
        if (ShowGrid)
        {
            pre =
                GridMode == Controls.GridMode.World
                    ? new IOverlayRenderer[]
                    {
                        new WorldGridOverlayRenderer(
                            GridStepWorld > 0 ? GridStepWorld : (double?)null,
                            GridStepScreen
                        ),
                    }
                    : new IOverlayRenderer[] { new GridOverlayRenderer(GridStepScreen) };
        }
        else
        {
            pre = Array.Empty<IOverlayRenderer>();
        }

        // Bind module shapes provider for overlays
        ModulesOverlayRenderer.ShapesProvider =
            ModuleShapesProvider ?? (() => Array.Empty<IReadOnlyList<Point>>());

        var post = new IOverlayRenderer[]
        {
            new SelectionOverlayRenderer(),
            new SelectedBoundsOverlayRenderer(),
            new HoverHighlightOverlayRenderer(),
            new ModulesOverlayRenderer(),
            new DebugInfoOverlayRenderer(),
        };
        // Optionally inject a debug extents renderer
        var includeExtents = _options?.ShowExtentsDebug ?? false;
        if (includeExtents)
        {
            var withDebug = new ICadEntityRenderer[entities.Length + 1];
            Array.Copy(entities, withDebug, entities.Length);
            withDebug[entities.Length] = new ExtentsDebugRenderer();
            _logger?.LogInformation(
                "Renderer built (with extents): preOverlays={Pre}, postOverlays={Post}, entityRenderers={Count}",
                pre.Length,
                post.Length,
                withDebug.Length
            );
            return new CadRendererHost(withDebug, pre, post);
        }
        _logger?.LogInformation(
            "Renderer built: preOverlays={Pre}, postOverlays={Post}, entityRenderers={Count}",
            pre.Length,
            post.Length,
            entities.Length
        );
        return new CadRendererHost(entities, pre, post);
    }

    private void OnZoomToRequested(CadExtents ext)
    {
        try
        {
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }
            _logger?.LogInformation(
                "ZoomToRequested: extents=({MinX:0.###},{MinY:0.###})-({MaxX:0.###},{MaxY:0.###})",
                ext.MinX,
                ext.MinY,
                ext.MaxX,
                ext.MaxY
            );
            _state.FitToExtents(bounds, ext, margin: 16);
            // Update dynamic limits after zoom-to
            var fitScale = Math.Max(Math.Abs(_state.Transform.M11), 1e-12);
            var minScale = fitScale / 100.0;
            var maxScale = fitScale * 10000.0;
            _state.SetScaleLimits(minScale, maxScale);
            _needsFit = false;
            InvalidateVisual();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to zoom to requested extents.", ex);
        }
    }

    private void UpdateSelectedEntityBounds()
    {
        _selectedWorldBounds.Clear();
        var model = Model;
        var sel = SelectedEntity;
        if (model == null || sel == null)
        {
            return;
        }

        if (CadEntityExtents.TryGetBounds(model, sel, _options, out var bb))
        {
            _selectedWorldBounds.Add(bb);
        }
    }

    private void OnPointerClicked(Point screenPoint)
    {
        var model = Model;
        if (model == null)
            return;
        // Check module card wrench icon click to start editing that module (overlay UI takes precedence)
        var cardIdPre = Controls.Rendering.ModulesOverlayRenderer.HitTestCardIcon(screenPoint);
        if (!string.IsNullOrEmpty(cardIdPre))
        {
            ServiceRegistry.ModulesTool?.EditModuleById(cardIdPre!);
            return;
        }
        const double pixelTol = 6.0;
        if (TryHit(screenPoint, pixelTol, out var hit))
        {
            var doc = ServiceRegistry.ActiveDocument;
            if (doc != null)
                doc.SelectedEntity = hit;
            // Also set control property to trigger render-options propagation and overlays
            SelectedEntity = hit;
            return;
        }
        var doc2 = ServiceRegistry.ActiveDocument;
        if (doc2 != null)
            doc2.SelectedEntity = null;
        SelectedEntity = null;
    }

    private void OnPointerMovedForHover(Point screenPoint)
    {
        var model = Model;
        if (model == null)
            return;
        const double pixelTol = 6.0;
        if (TryHit(screenPoint, pixelTol, out var hit))
        {
            HoveredEntity = hit;
        }
        else
        {
            HoveredEntity = null;
        }
    }

    private bool TryHit(Point screenPoint, double pixelTol, out SelectedEntityRef hit)
    {
        hit = default!;
        var model = Model;
        if (model == null)
            return false;
        bool IsVisible(string? layer) =>
            string.IsNullOrWhiteSpace(layer) || (_options?.IsLayerVisible(layer) ?? true);
        double bestDistSq = double.PositiveInfinity;
        SelectedEntityRef? best = null;

        double tolSq = pixelTol * pixelTol;
        // Convert pixel tolerance to world units and create a small world pick rect around cursor
        var scale = Math.Max(Math.Abs(_state.Transform.M11), 1e-12);
        var tolWorld = pixelTol / scale;
        var worldPt = _state.ScreenToWorld(screenPoint);
        var pickWorldRect = new Rect(
            worldPt.X - tolWorld,
            worldPt.Y - tolWorld,
            tolWorld * 2,
            tolWorld * 2
        );

        // Spatial index disabled: rely on early world-bbox checks below

        // Helper local to process a polyline in screen space
        static IReadOnlyList<Point> CloseIfNeeded(IReadOnlyList<Point> pts)
        {
            if (pts.Count >= 2 && (pts[0] != pts[^1]))
            {
                var arr = new Point[pts.Count + 1];
                for (int i = 0; i < pts.Count; i++)
                    arr[i] = pts[i];
                arr[^1] = pts[0];
                return arr;
            }
            return pts;
        }
        IReadOnlyList<Point> GetScreenPointsCached(string id, IReadOnlyList<Point> worldPts)
        {
            if (_screenPointCache.TryGetValue(id, out var cached))
                return cached;
            var sptsFull = GeometryHitTest.ToScreen(_state, worldPts);
            var spts = AdaptiveDecimateScreenPolyline(sptsFull, maxPoints: 256, minStepPx: 1.0);
            if (_screenPointCache.Count >= MaxCachedScreenPolylines)
                _screenPointCache.Clear();
            _screenPointCache[id] = spts;
            return spts;
        }

        // Stroke-like entities first for prioritization in ties
        if (model.Lines != null)
        {
            foreach (var ln in model.Lines)
            {
                if (!IsVisible(ln.Layer))
                    continue;
                var a = _state.WorldToScreen(ln.Start);
                var b = _state.WorldToScreen(ln.End);
                var bbox = new Rect(
                    Math.Min(a.X, b.X),
                    Math.Min(a.Y, b.Y),
                    Math.Abs(b.X - a.X),
                    Math.Abs(b.Y - a.Y)
                ).Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2 = GeometryHitTest.DistanceSqToSegment(screenPoint, a, b);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(ln.Id, ViewModels.Tools.EntityKind.Line);
                }
            }
        }
        // Points: screen-space distance test
        if (model.Points != null)
        {
            foreach (var pt in model.Points)
            {
                if (!IsVisible(pt.Layer))
                    continue;
                var sp = _state.WorldToScreen(pt.Position);
                var dx = sp.X - screenPoint.X;
                var dy = sp.Y - screenPoint.Y;
                var d2 = dx * dx + dy * dy;
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(pt.Id, ViewModels.Tools.EntityKind.Point);
                }
            }
        }
        if (model.Polylines != null)
        {
            foreach (var pl in model.Polylines)
            {
                if (!IsVisible(pl.Layer) || pl.Points == null || pl.Points.Count < 2)
                    continue;
                // Early world-space bbox rejection against a tiny rect around pick
                double minX = double.PositiveInfinity,
                    minY = double.PositiveInfinity,
                    maxX = double.NegativeInfinity,
                    maxY = double.NegativeInfinity;
                foreach (var p in pl.Points)
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
                var bbWorld = RectFromMinMax(
                    minX - tolWorld,
                    minY - tolWorld,
                    maxX + tolWorld,
                    maxY + tolWorld
                );
                if (!bbWorld.Intersects(pickWorldRect) && !pickWorldRect.Contains(bbWorld))
                {
                    continue;
                }
                var spts = GetScreenPointsCached(pl.Id, pl.Points);
                var bbox = GeometryHitTest.ComputeScreenBounds(spts).Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2 = GeometryHitTest.DistanceSqToPolyline(screenPoint, spts);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(pl.Id, ViewModels.Tools.EntityKind.Polyline);
                }
                // Early exit if within 1px^2
                if (bestDistSq <= 1.0)
                    break;
            }
        }
        // Leaders: treat as polylines in screen space
        if (model.Leaders != null)
        {
            foreach (var ld in model.Leaders)
            {
                if (!IsVisible(ld.Layer) || ld.Points == null || ld.Points.Count < 2)
                    continue;
                // Early world-space bbox rejection
                double minX = double.PositiveInfinity,
                    minY = double.PositiveInfinity,
                    maxX = double.NegativeInfinity,
                    maxY = double.NegativeInfinity;
                foreach (var p in ld.Points)
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
                var bbWorld = RectFromMinMax(
                    minX - tolWorld,
                    minY - tolWorld,
                    maxX + tolWorld,
                    maxY + tolWorld
                );
                if (!bbWorld.Intersects(pickWorldRect) && !pickWorldRect.Contains(bbWorld))
                {
                    continue;
                }
                var spts = GetScreenPointsCached(ld.Id, ld.Points);
                var bbox = GeometryHitTest.ComputeScreenBounds(spts).Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2 = GeometryHitTest.DistanceSqToPolyline(screenPoint, spts);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(ld.Id, ViewModels.Tools.EntityKind.Leader);
                }
            }
        }
        // Dimensions (Aligned)
        if (model.DimensionsAligned != null)
        {
            foreach (var d in model.DimensionsAligned)
            {
                if (!IsVisible(d.Layer))
                    continue;
                // Build world points for quick reject and for highlight later
                var v = new Point(d.P2.X - d.P1.X, d.P2.Y - d.P1.Y);
                var len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                if (len < 1e-6)
                    continue;
                var ux = v.X / len;
                var uy = v.Y / len;
                var nx = -uy;
                var ny = ux;
                var a = new Point(d.P1.X + nx * d.Offset, d.P1.Y + ny * d.Offset);
                var b = new Point(d.P2.X + nx * d.Offset, d.P2.Y + ny * d.Offset);
                // Early world bbox
                var bbWorld = BoundsFromPoints(new[] { d.P1, d.P2, a, b });
                var infl = new Rect(
                    bbWorld.X - tolWorld,
                    bbWorld.Y - tolWorld,
                    bbWorld.Width + 2 * tolWorld,
                    bbWorld.Height + 2 * tolWorld
                );
                if (!infl.Intersects(pickWorldRect) && !pickWorldRect.Contains(infl))
                    continue;
                // Screen segments
                var P1s = _state.WorldToScreen(d.P1);
                var P2s = _state.WorldToScreen(d.P2);
                var As = _state.WorldToScreen(a);
                var Bs = _state.WorldToScreen(b);
                // Bounding box in screen
                var bbox = GeometryHitTest
                    .ComputeScreenBounds(new[] { P1s, As, Bs, P2s })
                    .Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                // Distance to 3 segments
                var d2_1 = GeometryHitTest.DistanceSqToSegment(screenPoint, P1s, As);
                var d2_2 = GeometryHitTest.DistanceSqToSegment(screenPoint, P2s, Bs);
                var d2_3 = GeometryHitTest.DistanceSqToSegment(screenPoint, As, Bs);
                var d2 = Math.Min(d2_1, Math.Min(d2_2, d2_3));
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(d.Id, ViewModels.Tools.EntityKind.DimAligned);
                }
            }
        }
        // Dimensions (Linear)
        if (model.DimensionsLinear != null)
        {
            foreach (var d in model.DimensionsLinear)
            {
                if (!IsVisible(d.Layer))
                    continue;
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
                var bbWorld = BoundsFromPoints(new[] { d.P1, d.P2, a, b });
                var infl = new Rect(
                    bbWorld.X - tolWorld,
                    bbWorld.Y - tolWorld,
                    bbWorld.Width + 2 * tolWorld,
                    bbWorld.Height + 2 * tolWorld
                );
                if (!infl.Intersects(pickWorldRect) && !pickWorldRect.Contains(infl))
                    continue;
                var P1s = _state.WorldToScreen(d.P1);
                var P2s = _state.WorldToScreen(d.P2);
                var As = _state.WorldToScreen(a);
                var Bs = _state.WorldToScreen(b);
                var bbox = GeometryHitTest
                    .ComputeScreenBounds(new[] { P1s, As, Bs, P2s })
                    .Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2_1 = GeometryHitTest.DistanceSqToSegment(screenPoint, P1s, As);
                var d2_2 = GeometryHitTest.DistanceSqToSegment(screenPoint, P2s, Bs);
                var d2_3 = GeometryHitTest.DistanceSqToSegment(screenPoint, As, Bs);
                var d2 = Math.Min(d2_1, Math.Min(d2_2, d2_3));
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(d.Id, ViewModels.Tools.EntityKind.DimLinear);
                }
            }
        }
        if (model.Splines != null)
        {
            foreach (var sp in model.Splines)
            {
                if (!IsVisible(sp.Layer) || sp.Points == null || sp.Points.Count < 2)
                    continue;
                // Early world-space bbox rejection
                double minX = double.PositiveInfinity,
                    minY = double.PositiveInfinity,
                    maxX = double.NegativeInfinity,
                    maxY = double.NegativeInfinity;
                foreach (var p in sp.Points)
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
                var bbWorld = RectFromMinMax(
                    minX - tolWorld,
                    minY - tolWorld,
                    maxX + tolWorld,
                    maxY + tolWorld
                );
                if (!bbWorld.Intersects(pickWorldRect) && !pickWorldRect.Contains(bbWorld))
                {
                    continue;
                }
                var spts = GetScreenPointsCached(sp.Id, sp.Points);
                var bbox = GeometryHitTest.ComputeScreenBounds(spts).Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2 = GeometryHitTest.DistanceSqToPolyline(screenPoint, spts);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(sp.Id, ViewModels.Tools.EntityKind.Spline);
                }
                if (bestDistSq <= 1.0)
                    break;
            }
        }
        if (model.Circles != null)
        {
            foreach (var c in model.Circles)
            {
                if (!IsVisible(c.Layer))
                    continue;
                var sc = _state.WorldToScreen(c.Center);
                var sr = c.Radius * scale;
                var bbox = new Rect(sc.X - sr, sc.Y - sr, 2 * sr, 2 * sr).Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2 = GeometryHitTest.DistanceSqToCircle(screenPoint, sc, sr);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(c.Id, ViewModels.Tools.EntityKind.Circle);
                }
            }
        }
        if (model.Arcs != null)
        {
            foreach (var a in model.Arcs)
            {
                if (!IsVisible(a.Layer))
                    continue;
                var sc = _state.WorldToScreen(a.Center);
                var sr = a.Radius * scale;
                var startRad = a.StartAngle * Math.PI / 180.0;
                var endRad = a.EndAngle * Math.PI / 180.0;
                var bbox = new Rect(sc.X - sr, sc.Y - sr, 2 * sr, 2 * sr).Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2 = GeometryHitTest.DistanceSqToArc(screenPoint, sc, sr, startRad, endRad);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(a.Id, ViewModels.Tools.EntityKind.Arc);
                }
            }
        }
        if (model.Ellipses != null)
        {
            foreach (var el in model.Ellipses)
            {
                if (!IsVisible(el.Layer))
                    continue;
                // Early world-space rejection using approximate bounds
                var elWorld = BoundsFromEllipse(el);
                var elWorldInflated = new Rect(
                    elWorld.X - tolWorld,
                    elWorld.Y - tolWorld,
                    elWorld.Width + 2 * tolWorld,
                    elWorld.Height + 2 * tolWorld
                );
                if (
                    !elWorldInflated.Intersects(pickWorldRect)
                    && !pickWorldRect.Contains(elWorldInflated)
                )
                {
                    continue;
                }
                // Adaptive sampling: aim for ~4px segments on screen
                var rx = Math.Abs(el.RadiusX);
                var ry = Math.Abs(el.RadiusY);
                var perimApprox =
                    2.0 * Math.PI * Math.Sqrt(Math.Max(1e-12, (rx * rx + ry * ry) / 2.0));
                var screenLen = perimApprox * scale;
                int segs = Math.Clamp((int)Math.Ceiling(screenLen / 4.0), 24, 256);
                var spts = GeometryHitTest.SampleEllipseToScreen(_state, el, segs);
                var bbox = GeometryHitTest.ComputeScreenBounds(spts).Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2 = GeometryHitTest.DistanceSqToPolyline(screenPoint, spts);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(el.Id, ViewModels.Tools.EntityKind.Ellipse);
                }
            }
        }

        // Area-like entities: only use boundary loops to avoid inside picks
        if (model.Solids != null)
        {
            foreach (var so in model.Solids)
            {
                if (!IsVisible(so.Layer) || so.Vertices == null || so.Vertices.Count < 2)
                    continue;
                // Early world-space bbox rejection
                double minX = double.PositiveInfinity,
                    minY = double.PositiveInfinity,
                    maxX = double.NegativeInfinity,
                    maxY = double.NegativeInfinity;
                foreach (var p in so.Vertices)
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
                var bbWorld = RectFromMinMax(
                    minX - tolWorld,
                    minY - tolWorld,
                    maxX + tolWorld,
                    maxY + tolWorld
                );
                if (!bbWorld.Intersects(pickWorldRect) && !pickWorldRect.Contains(bbWorld))
                {
                    continue;
                }
                var spts = GeometryHitTest.ToScreen(_state, so.Vertices);
                spts = CloseIfNeeded(spts);
                var bbox = GeometryHitTest.ComputeScreenBounds(spts).Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2 = GeometryHitTest.DistanceSqToPolyline(screenPoint, spts);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(so.Id, ViewModels.Tools.EntityKind.Solid);
                }
            }
        }
        if (model.Hatches != null)
        {
            foreach (var ha in model.Hatches)
            {
                if (!IsVisible(ha.Layer) || ha.Loops == null || ha.Loops.Count == 0)
                    continue;
                foreach (var loop in ha.Loops)
                {
                    if (loop == null || loop.Count < 2)
                        continue;
                    // Early world-space bbox rejection
                    double minX = double.PositiveInfinity,
                        minY = double.PositiveInfinity,
                        maxX = double.NegativeInfinity,
                        maxY = double.NegativeInfinity;
                    foreach (var p in loop)
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
                    var bbWorld = RectFromMinMax(
                        minX - tolWorld,
                        minY - tolWorld,
                        maxX + tolWorld,
                        maxY + tolWorld
                    );
                    if (!bbWorld.Intersects(pickWorldRect) && !pickWorldRect.Contains(bbWorld))
                    {
                        continue;
                    }
                    var spts = GeometryHitTest.ToScreen(_state, loop);
                    spts = CloseIfNeeded(spts);
                    var bbox = GeometryHitTest.ComputeScreenBounds(spts).Inflate(pixelTol);
                    if (!bbox.Contains(screenPoint))
                        continue;
                    var d2 = GeometryHitTest.DistanceSqToPolyline(screenPoint, spts);
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        best = new SelectedEntityRef(ha.Id, ViewModels.Tools.EntityKind.Hatch);
                    }
                }
            }
        }
        if (model.Texts != null)
        {
            foreach (var t in model.Texts)
            {
                if (!IsVisible(t.Layer))
                    continue;
                var h = Math.Max(t.Height, 0.0);
                var w = h * 0.6 * (t.Value?.Length ?? 0);
                var tlw = new Point(t.Position.X, t.Position.Y - 0.8 * h);
                var trw = new Point(t.Position.X + Math.Max(0, w), t.Position.Y - 0.8 * h);
                var brw = new Point(t.Position.X + Math.Max(0, w), t.Position.Y + 0.2 * h);
                var blw = new Point(t.Position.X, t.Position.Y + 0.2 * h);
                // Early world bbox rejection
                var worldBb = BoundsFromPoints(new[] { tlw, trw, brw, blw });
                var worldBbInfl = new Rect(
                    worldBb.X - tolWorld,
                    worldBb.Y - tolWorld,
                    worldBb.Width + 2 * tolWorld,
                    worldBb.Height + 2 * tolWorld
                );
                if (!worldBbInfl.Intersects(pickWorldRect) && !pickWorldRect.Contains(worldBbInfl))
                {
                    continue;
                }
                var spts = new[]
                {
                    _state.WorldToScreen(tlw),
                    _state.WorldToScreen(trw),
                    _state.WorldToScreen(brw),
                    _state.WorldToScreen(blw),
                    _state.WorldToScreen(tlw),
                };
                var bbox = GeometryHitTest.ComputeScreenBounds(spts).Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2 = GeometryHitTest.DistanceSqToPolyline(screenPoint, spts);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(t.Id, ViewModels.Tools.EntityKind.Text);
                }
            }
        }
        if (model.MTexts != null)
        {
            foreach (var mt in model.MTexts)
            {
                if (!IsVisible(mt.Layer))
                    continue;
                var h = Math.Max(mt.Height, 0.0);
                var w = h * 0.6 * (mt.Value?.Length ?? 0);
                var tlw = new Point(mt.Position.X, mt.Position.Y - 0.8 * h);
                var trw = new Point(mt.Position.X + Math.Max(0, w), mt.Position.Y - 0.8 * h);
                var brw = new Point(mt.Position.X + Math.Max(0, w), mt.Position.Y + 0.2 * h);
                var blw = new Point(mt.Position.X, mt.Position.Y + 0.2 * h);
                var worldBb = BoundsFromPoints(new[] { tlw, trw, brw, blw });
                var worldBbInfl = new Rect(
                    worldBb.X - tolWorld,
                    worldBb.Y - tolWorld,
                    worldBb.Width + 2 * tolWorld,
                    worldBb.Height + 2 * tolWorld
                );
                if (!worldBbInfl.Intersects(pickWorldRect) && !pickWorldRect.Contains(worldBbInfl))
                {
                    continue;
                }
                var spts = new[]
                {
                    _state.WorldToScreen(tlw),
                    _state.WorldToScreen(trw),
                    _state.WorldToScreen(brw),
                    _state.WorldToScreen(blw),
                    _state.WorldToScreen(tlw),
                };
                var bbox = GeometryHitTest.ComputeScreenBounds(spts).Inflate(pixelTol);
                if (!bbox.Contains(screenPoint))
                    continue;
                var d2 = GeometryHitTest.DistanceSqToPolyline(screenPoint, spts);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = new SelectedEntityRef(mt.Id, ViewModels.Tools.EntityKind.MText);
                }
            }
        }

        if (best != null && bestDistSq <= tolSq)
        {
            hit = best!;
            return true;
        }
        return false;
    }

    private void UpdateHoveredPaths()
    {
        _hoveredWorldPaths.Clear();
        var model = Model;
        var sel = HoveredEntity;
        if (model == null || sel == null)
            return;
        var scale = Math.Max(Math.Abs(_state.Transform.M11), 1e-12);

        // Build paths per entity kind
        switch (sel.Kind)
        {
            case ViewModels.Tools.EntityKind.Polyline:
            {
                var pl = model.Polylines.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (pl?.Points != null && pl.Points.Count > 0)
                    _hoveredWorldPaths.Add(pl.Points);
                break;
            }
            case ViewModels.Tools.EntityKind.Leader:
            {
                var ld = model.Leaders.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (ld?.Points != null && ld.Points.Count > 0)
                    _hoveredWorldPaths.Add(ld.Points);
                break;
            }
            case ViewModels.Tools.EntityKind.Line:
            {
                var ln = model.Lines.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (ln != null)
                    _hoveredWorldPaths.Add(new[] { ln.Start, ln.End });
                break;
            }
            case ViewModels.Tools.EntityKind.Circle:
            {
                var c = model.Circles.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (c != null)
                {
                    var screenCirc = 2.0 * Math.PI * c.Radius * scale;
                    int segs = Math.Clamp((int)Math.Ceiling(screenCirc / 4.0), 24, 256);
                    _hoveredWorldPaths.Add(SampleCirclePoints(c.Center, c.Radius, segs));
                }
                break;
            }
            case ViewModels.Tools.EntityKind.Arc:
            {
                var a = model.Arcs.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (a != null)
                    _hoveredWorldPaths.Add(SampleArcPoints(a, 48));
                break;
            }
            case ViewModels.Tools.EntityKind.Insert:
            {
                var i = model.Inserts.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (i != null)
                {
                    var d = 2.0;
                    _hoveredWorldPaths.Add(
                        new[]
                        {
                            new Point(i.Position.X - d, i.Position.Y),
                            new Point(i.Position.X + d, i.Position.Y),
                        }
                    );
                    _hoveredWorldPaths.Add(
                        new[]
                        {
                            new Point(i.Position.X, i.Position.Y - d),
                            new Point(i.Position.X, i.Position.Y + d),
                        }
                    );
                }
                break;
            }
            case ViewModels.Tools.EntityKind.Ellipse:
            {
                var el = model.Ellipses.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (el != null)
                {
                    var rx = Math.Abs(el.RadiusX);
                    var ry = Math.Abs(el.RadiusY);
                    var perimApprox =
                        2.0 * Math.PI * Math.Sqrt(Math.Max(1e-12, (rx * rx + ry * ry) / 2.0));
                    var screenLen = perimApprox * scale;
                    int segs = Math.Clamp((int)Math.Ceiling(screenLen / 4.0), 24, 256);
                    _hoveredWorldPaths.Add(SampleEllipsePoints(el, segs));
                }
                break;
            }
            case ViewModels.Tools.EntityKind.Text:
            {
                var t = model.Texts.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (t != null)
                {
                    var h = Math.Max(t.Height, 0.0);
                    var w = h * 0.6 * (t.Value?.Length ?? 0);
                    var tl = new Point(t.Position.X, t.Position.Y - 0.8 * h);
                    var tr = new Point(t.Position.X + Math.Max(0, w), t.Position.Y - 0.8 * h);
                    var br = new Point(t.Position.X + Math.Max(0, w), t.Position.Y + 0.2 * h);
                    var bl = new Point(t.Position.X, t.Position.Y + 0.2 * h);
                    _hoveredWorldPaths.Add(new[] { tl, tr, br, bl, tl });
                }
                break;
            }
            case ViewModels.Tools.EntityKind.MText:
            {
                var mt = model.MTexts.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (mt != null)
                {
                    var h = Math.Max(mt.Height, 0.0);
                    var w = h * 0.6 * (mt.Value?.Length ?? 0);
                    var tl = new Point(mt.Position.X, mt.Position.Y - 0.8 * h);
                    var tr = new Point(mt.Position.X + Math.Max(0, w), mt.Position.Y - 0.8 * h);
                    var br = new Point(mt.Position.X + Math.Max(0, w), mt.Position.Y + 0.2 * h);
                    var bl = new Point(mt.Position.X, mt.Position.Y + 0.2 * h);
                    _hoveredWorldPaths.Add(new[] { tl, tr, br, bl, tl });
                }
                break;
            }
            case ViewModels.Tools.EntityKind.Spline:
            {
                var sp = model.Splines.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (sp?.Points != null && sp.Points.Count > 0)
                    _hoveredWorldPaths.Add(sp.Points);
                break;
            }
            case ViewModels.Tools.EntityKind.Solid:
            {
                var so = model.Solids.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (so?.Vertices != null && so.Vertices.Count > 0)
                {
                    var pts = new List<Point>(so.Vertices);
                    if (pts.Count > 2)
                        pts.Add(pts[0]);
                    _hoveredWorldPaths.Add(pts);
                }
                break;
            }
            case ViewModels.Tools.EntityKind.Hatch:
            {
                var ha = model.Hatches.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (ha?.Loops != null)
                {
                    foreach (var loop in ha.Loops)
                    {
                        if (loop == null || loop.Count == 0)
                            continue;
                        var pts = new List<Point>(loop);
                        if (pts.Count > 2)
                            pts.Add(pts[0]);
                        _hoveredWorldPaths.Add(pts);
                    }
                }
                break;
            }
            case ViewModels.Tools.EntityKind.DimAligned:
            {
                var d = model.DimensionsAligned.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (d != null)
                {
                    var v = new Point(d.P2.X - d.P1.X, d.P2.Y - d.P1.Y);
                    var len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                    if (len > 1e-6)
                    {
                        var ux = v.X / len;
                        var uy = v.Y / len;
                        var nx = -uy;
                        var ny = ux;
                        var a = new Point(d.P1.X + nx * d.Offset, d.P1.Y + ny * d.Offset);
                        var b = new Point(d.P2.X + nx * d.Offset, d.P2.Y + ny * d.Offset);
                        _hoveredWorldPaths.Add(new[] { d.P1, a });
                        _hoveredWorldPaths.Add(new[] { d.P2, b });
                        _hoveredWorldPaths.Add(new[] { a, b });
                    }
                }
                break;
            }
            case ViewModels.Tools.EntityKind.DimLinear:
            {
                var d = model.DimensionsLinear.FirstOrDefault(x =>
                    x.Id == sel.Id && (_options?.IsLayerVisible(x.Layer) ?? true)
                );
                if (d != null)
                {
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
                    _hoveredWorldPaths.Add(new[] { d.P1, a });
                    _hoveredWorldPaths.Add(new[] { d.P2, b });
                    _hoveredWorldPaths.Add(new[] { a, b });
                }
                break;
            }
        }
    }

    private static IReadOnlyList<Point> SampleCirclePoints(
        Point center,
        double radius,
        int segments
    )
    {
        var pts = new List<Point>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            var ang = (Math.PI * 2.0) * i / segments;
            pts.Add(
                new Point(center.X + radius * Math.Cos(ang), center.Y + radius * Math.Sin(ang))
            );
        }
        return pts;
    }

    private static IReadOnlyList<Point> SampleArcPoints(CadArc a, int segments)
    {
        var pts = new List<Point>(segments + 1);
        var startRad = DegreesToRadians(a.StartAngle);
        var endRad = DegreesToRadians(a.EndAngle);
        while (endRad < startRad)
            endRad += Math.PI * 2;
        var sweep = endRad - startRad;
        for (int i = 0; i <= segments; i++)
        {
            var ang = startRad + sweep * (double)i / segments;
            pts.Add(
                new Point(
                    a.Center.X + a.Radius * Math.Cos(ang),
                    a.Center.Y + a.Radius * Math.Sin(ang)
                )
            );
        }
        return pts;
    }

    private static IReadOnlyList<Point> SampleEllipsePoints(CadEllipse el, int segments)
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
            pts.Add(new Point(wx, wy));
        }
        return pts;
    }

    // Reduce the number of points for hit-testing in screen space while preserving shape fidelity.
    // Ensures vertices are at least minStepPx apart and caps total points to maxPoints.
    private static IReadOnlyList<Point> AdaptiveDecimateScreenPolyline(
        IReadOnlyList<Point> screenPts,
        int maxPoints,
        double minStepPx
    )
    {
        if (screenPts.Count <= 2)
            return screenPts;
        if (screenPts.Count <= maxPoints)
            return screenPts;
        var result = new List<Point>(Math.Min(maxPoints, screenPts.Count));
        var prev = screenPts[0];
        result.Add(prev);
        double acc = 0.0;
        for (int i = 1; i < screenPts.Count - 1; i++)
        {
            var cur = screenPts[i];
            var dx = cur.X - prev.X;
            var dy = cur.Y - prev.Y;
            var d = Math.Sqrt(dx * dx + dy * dy);
            acc += d;
            if (acc >= minStepPx)
            {
                result.Add(cur);
                prev = cur;
                acc = 0.0;
                if (result.Count >= maxPoints - 1)
                    break;
            }
        }
        // Always include last point
        result.Add(screenPts[^1]);
        return result;
    }
}
