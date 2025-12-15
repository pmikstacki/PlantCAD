using System;
using System.Collections.Generic;
using Avalonia;
using Clipper2Lib;
using PlantCad.Gui.Models;
using PlantCad.Gui.Models.Sheets;

namespace PlantCad.Gui.Services.Internal;

public sealed class CountingService : ICountingService
{
    public (IDictionary<string, int> Counts, long Total) CountInsertsInRect(
        CadModel model,
        double minX,
        double minY,
        double maxX,
        double maxY
    )
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var ins in model.Inserts)
        {
            var x = ins.Position.X;
            var y = ins.Position.Y;
            if (x >= minX && x <= maxX && y >= minY && y <= maxY)
            {
                var name = string.IsNullOrWhiteSpace(ins.BlockName) ? "<Unnamed>" : ins.BlockName;
                counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
                total++;
            }
        }
        return (counts, total);
    }

    public (IDictionary<string, int> Counts, long Total) CountInsertsInPolygon(
        CadModel model,
        IReadOnlyList<IReadOnlyList<Point>> polygons,
        LayerFilter? layerFilter = null
    )
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }
        if (polygons == null)
        {
            throw new ArgumentNullException(nameof(polygons));
        }
        if (polygons.Count == 0)
        {
            return (new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), 0);
        }

        var clipperPolys = new List<Path64>(polygons.Count);
        foreach (var poly in polygons)
        {
            if (poly == null || poly.Count < 3)
            {
                continue;
            }
            var path = new Path64(poly.Count);
            foreach (var p in poly)
            {
                path.Add(new Point64(p.X, p.Y));
            }
            if (path.Count >= 3)
            {
                clipperPolys.Add(path);
            }
        }

        if (clipperPolys.Count == 0)
        {
            return (new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), 0);
        }

        var bounds = GetBounds(clipperPolys);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var ins in model.Inserts)
        {
            if (!IsInsertVisible(ins, model, layerFilter))
            {
                continue;
            }

            var x = ins.Position.X;
            var y = ins.Position.Y;
            if (x < bounds.left || x > bounds.right || y < bounds.top || y > bounds.bottom)
            {
                continue;
            }

            var pt = new Point64(x, y);
            var inside = false;
            foreach (var poly in clipperPolys)
            {
                var pip = InternalClipper.PointInPolygon(pt, poly);
                if (pip == PointInPolygonResult.IsInside || pip == PointInPolygonResult.IsOn)
                {
                    inside = true;
                    break;
                }
            }

            if (!inside)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(ins.BlockName) ? "<Unnamed>" : ins.BlockName;
            counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
            total++;
        }
        return (counts, total);
    }

    private static bool IsInsertVisible(CadInsert insert, CadModel model, LayerFilter? filter)
    {
        if (filter == null)
        {
            return true;
        }

        var layerName = insert?.Layer;
        if (string.IsNullOrWhiteSpace(layerName))
        {
            return true;
        }

        if (filter.UseCurrentModelVisibility)
        {
            CadLayer? layer = null;
            foreach (var l in model.Layers)
            {
                if (l.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                {
                    layer = l;
                    break;
                }
            }

            if (layer != null)
            {
                var visible = layer.IsOn && !layer.IsFrozen;
                if (!visible)
                {
                    return false;
                }
            }
        }

        if (filter.Includes.Count > 0 && !filter.Includes.Contains(layerName))
        {
            return false;
        }

        if (filter.Excludes.Contains(layerName))
        {
            return false;
        }

        return true;
    }

    private static Rect64 GetBounds(IReadOnlyList<Path64> polygons)
    {
        var bounds = new Rect64(false);
        foreach (var poly in polygons)
        {
            if (poly == null) continue;
            foreach (var pt in poly)
            {
                if (pt.X < bounds.left) bounds.left = pt.X;
                if (pt.X > bounds.right) bounds.right = pt.X;
                if (pt.Y < bounds.top) bounds.top = pt.Y;
                if (pt.Y > bounds.bottom) bounds.bottom = pt.Y;
            }
        }
        return bounds.left == long.MaxValue ? new Rect64() : bounds;
    }
}
