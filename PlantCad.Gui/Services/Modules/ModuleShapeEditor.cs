using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using PlantCad.Gui.Controls;
using PlantCad.Gui.Controls.Rendering;

namespace PlantCad.Gui.Services.Modules;

public sealed class ModuleShapeEditor
{
    private readonly CadViewportControl _viewport;
    private readonly List<Point> _current = new();
    private int _dragIndex = -1;
    private int _selectedIndex = -1;
    private bool _active;
    private bool _snapToGrid;
    private int _hoverEdgeIndex = -1;
    private Point _hoverA;
    private Point _hoverB;
    private bool _filterOriginal;
    private List<Point>? _originalForFilter;

    public ModuleShapeEditor(CadViewportControl viewport)
    {
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        WireOverlay();
        // Compose shapes provider to include in-progress polygon
        var baseProvider = _viewport.ModuleShapesProvider;
        _viewport.ModuleShapesProvider = () => ComposePolygons(baseProvider);
    }

    public void Begin()
    {
        _current.Clear();
        _dragIndex = -1;
        _active = true;
        _hoverEdgeIndex = -1;
        _filterOriginal = false;
        _originalForFilter = null;
        _viewport.RequestInvalidate();
    }

    public void BeginWithPolygon(IReadOnlyList<Point> points)
    {
        if (points == null) throw new ArgumentNullException(nameof(points));
        _current.Clear();
        foreach (var p in points)
        {
            _current.Add(p);
        }
        _dragIndex = -1;
        _selectedIndex = -1;
        _active = true;
        _hoverEdgeIndex = -1;
        _filterOriginal = true;
        _originalForFilter = new List<Point>(points);
        _viewport.RequestInvalidate();
    }

    public void Cancel()
    {
        _current.Clear();
        _dragIndex = -1;
        _active = false;
        _hoverEdgeIndex = -1;
        _filterOriginal = false;
        _originalForFilter = null;
        _viewport.RequestInvalidate();
    }

    public IReadOnlyList<Point> Current => _current;
    public int SelectedHandleIndex => _selectedIndex;
    public bool IsActive => _active;
    public bool HasHoveredEdge => _hoverEdgeIndex >= 0;

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set => _snapToGrid = value;
    }

    private IEnumerable<IReadOnlyList<Point>> ComposePolygons(Func<IEnumerable<IReadOnlyList<Point>>>? baseProvider)
    {
        if (baseProvider != null)
        {
            foreach (var p in baseProvider())
            {
                if (_filterOriginal && _originalForFilter != null && PolygonEquals(p, _originalForFilter))
                {
                    continue;
                }
                yield return p;
            }
        }
        if (_active && _current.Count >= 2)
            yield return _current;
    }

    private IEnumerable<Point> GetHandles()
    {
        if (!_active)
            yield break;
        foreach (var p in _current)
            yield return p;
    }

    public void OnPressed(Point screen)
    {
        if (!_active)
            return;
        var world = ToWorldSnapped(screen);
        // Check hit on existing handle
        var hit = HitHandle(world, out var idx);
        if (hit)
        {
            _dragIndex = idx;
            _selectedIndex = idx;
            return;
        }
        // Try to insert on nearest edge (no modifier needed)
        if (TryInsertOnNearestEdge(screen, world))
        {
            _viewport.RequestInvalidate();
            return;
        }
        // Add new vertex (fallback)
        _current.Add(world);
        _selectedIndex = _current.Count - 1;
        _viewport.RequestInvalidate();
    }

    public void OnPressedWithModifiers(Point screen, KeyModifiers mods)
    {
        if (!_active)
            return;
        var world = ToWorldSnapped(screen);
        // Ctrl+Click deletes a vertex (if >= 4 points remain)
        if (mods.HasFlag(KeyModifiers.Control))
        {
            if (HitHandle(world, out var idx) && idx >= 0 && idx < _current.Count)
            {
                if (_current.Count > 3)
                {
                    _current.RemoveAt(idx);
                    _viewport.RequestInvalidate();
                }
            }
            return;
        }
        // Alt+Click inserts a vertex on the nearest edge (within tolerance)
        if (mods.HasFlag(KeyModifiers.Alt))
        {
            if (TryInsertOnNearestEdge(screen, world))
            {
                _viewport.RequestInvalidate();
                return;
            }
        }
    }

    public void OnMoved(Point screen)
    {
        if (!_active)
            return;
        if (_dragIndex >= 0 && _dragIndex < _current.Count)
        {
            _current[_dragIndex] = ToWorldSnapped(screen);
            _viewport.RequestInvalidate();
            UpdateHover(screen);
        }
        else
        {
            // Update hovered edge even when not dragging
            UpdateHover(screen);
        }
    }

    public void OnReleased(Point screen)
    {
        if (!_active)
            return;
        _dragIndex = -1;
    }

    public bool TryFinish(out IReadOnlyList<Point> polygon)
    {
        if (_current.Count >= 3)
        {
            polygon = _current.ToArray();
            _current.Clear();
            _dragIndex = -1;
            _active = false;
            _filterOriginal = false;
            _originalForFilter = null;
            _viewport.RequestInvalidate();
            return true;
        }
        polygon = Array.Empty<Point>();
        return false;
    }

    public void OnKeyDown(Key key, KeyModifiers mods)
    {
        if (!_active)
            return;
        if (key == Key.Escape)
        {
            Cancel();
            return;
        }
        if (key == Key.Enter || key == Key.Return)
        {
            TryFinish(out _);
            return;
        }
        if (key == Key.Delete || key == Key.Back)
        {
            if (_dragIndex >= 0 && _dragIndex < _current.Count && _current.Count > 3)
            {
                _current.RemoveAt(_dragIndex);
                _dragIndex = -1;
                _selectedIndex = -1;
                _viewport.RequestInvalidate();
            }
        }
    }

    public bool DeleteSelectedPoint()
    {
        if (!_active)
            return false;
        if (_selectedIndex >= 0 && _selectedIndex < _current.Count && _current.Count > 3)
        {
            _current.RemoveAt(_selectedIndex);
            _dragIndex = -1;
            _selectedIndex = -1;
            _viewport.RequestInvalidate();
            return true;
        }
        return false;
    }

    public bool InsertMidpointOnHoveredEdge()
    {
        if (!_active)
            return false;
        if (_hoverEdgeIndex < 0 || _current.Count < 2)
            return false;
        var a = _current[_hoverEdgeIndex];
        var b = _current[(_hoverEdgeIndex + 1) % _current.Count];
        var mid = new Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
        _current.Insert(_hoverEdgeIndex + 1, mid);
        _selectedIndex = _hoverEdgeIndex + 1;
        _viewport.RequestInvalidate();
        return true;
    }

    public bool TryGetSelectedWorld(out Point world)
    {
        if (!_active || _selectedIndex < 0 || _selectedIndex >= _current.Count)
        {
            world = default;
            return false;
        }
        world = _current[_selectedIndex];
        return true;
    }

    public bool TryGetHoveredEdgeMidWorld(out Point mid)
    {
        if (!_active || _hoverEdgeIndex < 0 || _current.Count < 2)
        {
            mid = default;
            return false;
        }
        var a = _current[_hoverEdgeIndex];
        var b = _current[(_hoverEdgeIndex + 1) % _current.Count];
        mid = new Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
        return true;
    }

    private bool TryInsertOnNearestEdge(Point screen, Point world)
    {
        if (_current.Count < 2)
            return false;
        int bestI = -1;
        double bestD2 = double.PositiveInfinity;
        var s = _viewport.WorldToScreen(world);
        for (int i = 0; i < _current.Count; i++)
        {
            var a = _viewport.WorldToScreen(_current[i]);
            var b = _viewport.WorldToScreen(_current[(i + 1) % _current.Count]);
            var d2 = DistToSegmentSquared(s, a, b);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                bestI = i;
            }
        }
        const double tol2 = 81.0; // 9px squared
        if (bestI >= 0 && bestD2 <= tol2)
        {
            _current.Insert(bestI + 1, world);
            _selectedIndex = bestI + 1;
            return true;
        }
        return false;
    }

    private void UpdateHover(Point screen)
    {
        int prev = _hoverEdgeIndex;
        _hoverEdgeIndex = -1;
        _hoverA = default;
        _hoverB = default;
        if (_current.Count < 2)
        {
            if (prev != _hoverEdgeIndex)
                _viewport.RequestInvalidate();
            return;
        }
        int bestI = -1;
        double bestD2 = double.PositiveInfinity;
        for (int i = 0; i < _current.Count; i++)
        {
            var a = _viewport.WorldToScreen(_current[i]);
            var b = _viewport.WorldToScreen(_current[(i + 1) % _current.Count]);
            var d2 = DistToSegmentSquared(screen, a, b);
            if (d2 < bestD2)
            {
                bestD2 = d2;
                bestI = i;
                _hoverA = _current[i];
                _hoverB = _current[(i + 1) % _current.Count];
            }
        }
        const double tol2 = 81.0; // 9px squared
        if (bestI >= 0 && bestD2 <= tol2)
        {
            _hoverEdgeIndex = bestI;
        }
        else
        {
            _hoverEdgeIndex = -1;
        }
        if (prev != _hoverEdgeIndex)
        {
            _viewport.RequestInvalidate();
        }
    }

    private (Point a, Point b)? GetHoveredEdge()
    {
        if (!_active)
            return null;
        if (_hoverEdgeIndex < 0)
            return null;
        return (_hoverA, _hoverB);
    }

    private Point? GetSelectedHandle()
    {
        if (!_active || _selectedIndex < 0 || _selectedIndex >= _current.Count)
            return null;
        return _current[_selectedIndex];
    }

    private IReadOnlyList<Point>? GetActivePolygon()
    {
        if (!_active || _current.Count < 2) return null;
        return _current;
    }

    public void WireOverlay()
    {
        ModulesOverlayRenderer.HandlesProvider = GetHandles;
        ModulesOverlayRenderer.HoveredEdgeProvider = GetHoveredEdge;
        ModulesOverlayRenderer.SelectedHandleProvider = GetSelectedHandle;
        ModulesOverlayRenderer.IsEditingProvider = () => _active;
        ModulesOverlayRenderer.ActivePolygonProvider = GetActivePolygon;
    }

    public void UnwireOverlay()
    {
        ModulesOverlayRenderer.HandlesProvider = null;
        ModulesOverlayRenderer.HoveredEdgeProvider = null;
        ModulesOverlayRenderer.SelectedHandleProvider = null;
        ModulesOverlayRenderer.IsEditingProvider = null;
        ModulesOverlayRenderer.ActivePolygonProvider = null;
    }

    private static bool PolygonEquals(IReadOnlyList<Point> a, IReadOnlyList<Point> b)
    {
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;
        const double tol = 1e-6;
        for (int i = 0; i < a.Count; i++)
        {
            if (Math.Abs(a[i].X - b[i].X) > tol || Math.Abs(a[i].Y - b[i].Y) > tol)
                return false;
        }
        return true;
    }

    private static double DistToSegmentSquared(Point p, Point a, Point b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var apx = p.X - a.X;
        var apy = p.Y - a.Y;
        var denom = Math.Max(abx * abx + aby * aby, 1e-12);
        var t = (apx * abx + apy * aby) / denom;
        if (t < 0) t = 0; else if (t > 1) t = 1;
        var cx = a.X + t * abx;
        var cy = a.Y + t * aby;
        var dx = p.X - cx;
        var dy = p.Y - cy;
        return dx * dx + dy * dy;
    }

    private Point ToWorldSnapped(Point screen)
    {
        var w = _viewport.ScreenToWorld(screen);
        if (!_snapToGrid)
            return w;
        if (_viewport.GridMode != GridMode.World)
            return w;
        var step = _viewport.GridStepWorld;
        if (step <= 0)
            return w;
        double sx = Math.Round(w.X / step) * step;
        double sy = Math.Round(w.Y / step) * step;
        return new Point(sx, sy);
    }

    private bool HitHandle(Point w, out int index)
    {
        const double tolWorld = 6.0; // in pixels converted to world inexact, but we only have world; rough tol
        // Use screen tolerance
        var screenPt = _viewport.WorldToScreen(w);
        index = -1;
        double best = double.PositiveInfinity;
        for (int i = 0; i < _current.Count; i++)
        {
            var s = _viewport.WorldToScreen(_current[i]);
            var dx = s.X - screenPt.X;
            var dy = s.Y - screenPt.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 < best && d2 <= tolWorld * tolWorld)
            {
                best = d2;
                index = i;
            }
        }
        return index >= 0;
    }
}
