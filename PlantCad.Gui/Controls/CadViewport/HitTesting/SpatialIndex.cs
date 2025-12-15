using System;
using System.Collections.Generic;
using Avalonia;
using PlantCad.Gui.Models;
using PlantCad.Gui.ViewModels.Documents;

namespace PlantCad.Gui.Controls.HitTesting
{
    /// <summary>
    /// Simple uniform-grid spatial index for fast viewport hit-query candidate retrieval.
    /// Stores entity world-space bounding boxes in grid cells.
    /// </summary>
    internal sealed class SpatialIndex
    {
        private readonly double _cellSize;
        private readonly Dictionary<(int x, int y), List<SelectedEntityRef>> _cells = new();

        public SpatialIndex(double cellSize)
        {
            if (cellSize <= 0 || double.IsNaN(cellSize) || double.IsInfinity(cellSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    "Cell size must be positive and finite."
                );
            }
            _cellSize = cellSize;
        }

        private static (int cx, int cy) ToCell(double x, double y, double size)
        {
            var cx = (int)Math.Floor(x / size);
            var cy = (int)Math.Floor(y / size);
            return (cx, cy);
        }

        private static void AddToCell(
            Dictionary<(int x, int y), List<SelectedEntityRef>> map,
            (int x, int y) key,
            SelectedEntityRef id
        )
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<SelectedEntityRef>();
                map[key] = list;
            }
            list.Add(id);
        }

        private void Insert(Rect worldBounds, SelectedEntityRef id)
        {
            if (worldBounds.Width <= 0 && worldBounds.Height <= 0)
            {
                // Treat as a point
                var cell = ToCell(worldBounds.X, worldBounds.Y, _cellSize);
                AddToCell(_cells, cell, id);
                return;
            }
            var min = ToCell(worldBounds.X, worldBounds.Y, _cellSize);
            var max = ToCell(worldBounds.Right, worldBounds.Bottom, _cellSize);
            for (int cy = min.cy; cy <= max.cy; cy++)
            {
                for (int cx = min.cx; cx <= max.cx; cx++)
                {
                    AddToCell(_cells, (cx, cy), id);
                }
            }
        }

        public static SpatialIndex Build(CadModel model)
        {
            // Choose a default cell size that is roughly 128 world units; actual efficiency depends on model scale.
            var index = new SpatialIndex(128.0);

            void AddRect(Rect bb, SelectedEntityRef r)
            {
                if (
                    double.IsNaN(bb.X)
                    || double.IsNaN(bb.Y)
                    || double.IsNaN(bb.Width)
                    || double.IsNaN(bb.Height)
                )
                    return;
                if (
                    double.IsInfinity(bb.X)
                    || double.IsInfinity(bb.Y)
                    || double.IsInfinity(bb.Width)
                    || double.IsInfinity(bb.Height)
                )
                    return;
                index.Insert(bb, r);
            }

            // Lines
            if (model.Lines != null)
            {
                foreach (var ln in model.Lines)
                {
                    var minX = Math.Min(ln.Start.X, ln.End.X);
                    var minY = Math.Min(ln.Start.Y, ln.End.Y);
                    var maxX = Math.Max(ln.Start.X, ln.End.X);
                    var maxY = Math.Max(ln.Start.Y, ln.End.Y);
                    AddRect(
                        new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)),
                        new SelectedEntityRef(ln.Id, ViewModels.Tools.EntityKind.Line)
                    );
                }
            }
            // Polylines
            if (model.Polylines != null)
            {
                foreach (var pl in model.Polylines)
                {
                    if (pl.Points == null || pl.Points.Count == 0)
                        continue;
                    var bb = BoundsFromPoints(pl.Points);
                    AddRect(bb, new SelectedEntityRef(pl.Id, ViewModels.Tools.EntityKind.Polyline));
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
                    AddRect(bb, new SelectedEntityRef(sp.Id, ViewModels.Tools.EntityKind.Spline));
                }
            }
            // Circles
            if (model.Circles != null)
            {
                foreach (var c in model.Circles)
                {
                    AddRect(
                        new Rect(
                            c.Center.X - c.Radius,
                            c.Center.Y - c.Radius,
                            2 * c.Radius,
                            2 * c.Radius
                        ),
                        new SelectedEntityRef(c.Id, ViewModels.Tools.EntityKind.Circle)
                    );
                }
            }
            // Arcs (approximate as circle bbox)
            if (model.Arcs != null)
            {
                foreach (var a in model.Arcs)
                {
                    AddRect(
                        new Rect(
                            a.Center.X - a.Radius,
                            a.Center.Y - a.Radius,
                            2 * a.Radius,
                            2 * a.Radius
                        ),
                        new SelectedEntityRef(a.Id, ViewModels.Tools.EntityKind.Arc)
                    );
                }
            }
            // Ellipses
            if (model.Ellipses != null)
            {
                foreach (var el in model.Ellipses)
                {
                    var bb = BoundsFromEllipse(el);
                    AddRect(bb, new SelectedEntityRef(el.Id, ViewModels.Tools.EntityKind.Ellipse));
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
                    AddRect(bb, new SelectedEntityRef(so.Id, ViewModels.Tools.EntityKind.Solid));
                }
            }
            // Hatches
            if (model.Hatches != null)
            {
                foreach (var ha in model.Hatches)
                {
                    if (ha.Loops == null || ha.Loops.Count == 0)
                        continue;
                    var bb = BoundsFromLoops(ha.Loops);
                    AddRect(bb, new SelectedEntityRef(ha.Id, ViewModels.Tools.EntityKind.Hatch));
                }
            }
            // Texts
            if (model.Texts != null)
            {
                foreach (var t in model.Texts)
                {
                    var h = Math.Max(t.Height, 0.0);
                    var w = h * 0.6 * (t.Value?.Length ?? 0);
                    var tl = new Point(t.Position.X, t.Position.Y - 0.8 * h);
                    var br = new Point(t.Position.X + Math.Max(0, w), t.Position.Y + 0.2 * h);
                    AddRect(
                        new Rect(tl, br),
                        new SelectedEntityRef(t.Id, ViewModels.Tools.EntityKind.Text)
                    );
                }
            }
            if (model.MTexts != null)
            {
                foreach (var mt in model.MTexts)
                {
                    var h = Math.Max(mt.Height, 0.0);
                    var w = h * 0.6 * (mt.Value?.Length ?? 0);
                    var tl = new Point(mt.Position.X, mt.Position.Y - 0.8 * h);
                    var br = new Point(mt.Position.X + Math.Max(0, w), mt.Position.Y + 0.2 * h);
                    AddRect(
                        new Rect(tl, br),
                        new SelectedEntityRef(mt.Id, ViewModels.Tools.EntityKind.MText)
                    );
                }
            }

            return index;
        }

        public IReadOnlyCollection<SelectedEntityRef> Query(Rect worldRect)
        {
            var result = new HashSet<SelectedEntityRef>();
            var min = ToCell(worldRect.X, worldRect.Y, _cellSize);
            var max = ToCell(worldRect.Right, worldRect.Bottom, _cellSize);
            for (int cy = min.cy; cy <= max.cy; cy++)
            {
                for (int cx = min.cx; cx <= max.cx; cx++)
                {
                    if (_cells.TryGetValue((cx, cy), out var list))
                    {
                        foreach (var id in list)
                        {
                            result.Add(id);
                        }
                    }
                }
            }
            return result;
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
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private static Rect BoundsFromLoops(IReadOnlyList<IReadOnlyList<Point>> loops)
        {
            double minX = double.PositiveInfinity,
                minY = double.PositiveInfinity,
                maxX = double.NegativeInfinity,
                maxY = double.NegativeInfinity;
            foreach (var loop in loops)
            {
                if (loop == null)
                    continue;
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
            }
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private static Rect BoundsFromEllipse(CadEllipse el)
        {
            // Sample as in CadViewport.BoundsFromEllipse with 72 segments
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
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }

        private static double DegreesToRadians(double deg) => deg * Math.PI / 180.0;
    }
}
