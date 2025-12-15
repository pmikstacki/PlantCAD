using System.Collections.Generic;
using Avalonia;
using PlantCad.Gui.Models.Modules;

namespace PlantCad.Gui.Services.Modules;

internal static class ModulesState
{
    private static readonly object _lock = new();
    private static ModulesFile? _current;
    private static List<IReadOnlyList<Point>> _worldPolygons = new();

    public static ModulesFile? Current
    {
        get { lock (_lock) { return _current; } }
    }

    public static IEnumerable<IReadOnlyList<Point>> GetWorldPolygons()
    {
        lock (_lock)
        {
            return _worldPolygons;
        }
    }

    public static IEnumerable<(Point pos, string text)> GetLabels()
    {
        lock (_lock)
        {
            var file = _current;
            if (file?.RootModules == null)
            {
                yield break;
            }
            foreach (var m in file.RootModules)
            {
                foreach (var tup in CollectLabels(m))
                    yield return tup;
            }
        }
    }

    public static IEnumerable<(Point pos, string text, string id)> GetCards()
    {
        lock (_lock)
        {
            var file = _current;
            if (file?.RootModules == null)
            {
                yield break;
            }
            foreach (var m in file.RootModules)
            {
                foreach (var tup in CollectCards(m))
                    yield return tup;
            }
        }
    }

    public static void SetCurrent(ModulesFile? file)
    {
        lock (_lock)
        {
            _current = file;
            _worldPolygons = BuildPolygonsCache(file);
        }
    }

    private static List<IReadOnlyList<Point>> BuildPolygonsCache(ModulesFile? file)
    {
        var list = new List<IReadOnlyList<Point>>();
        if (file == null) return list;
        if (file.RootModules == null) return list;
        foreach (var m in file.RootModules)
        {
            CollectModulePolygons(m, list);
        }
        return list;
    }

    private static void CollectModulePolygons(Module m, List<IReadOnlyList<Point>> acc)
    {
        if (m == null) return;
        if (m.Shapes != null)
        {
            foreach (var shp in m.Shapes)
            {
                if (shp?.Points == null || shp.Points.Count < 3) continue;
                var pts = new List<Point>(shp.Points.Count);
                foreach (var p in shp.Points)
                {
                    pts.Add(new Point(p.X, p.Y));
                }
                acc.Add(pts);
            }
        }
        if (m.Children != null)
        {
            foreach (var child in m.Children)
            {
                CollectModulePolygons(child, acc);
            }
        }
    }

    private static IEnumerable<(Point pos, string text)> CollectLabels(Module m)
    {
        if (m == null)
        {
            yield break;
        }
        if (m.Shapes != null)
        {
            foreach (var shp in m.Shapes)
            {
                if (shp?.Points == null || shp.Points.Count < 3) continue;
                double cx = 0, cy = 0;
                int n = shp.Points.Count;
                foreach (var p in shp.Points)
                {
                    cx += p.X;
                    cy += p.Y;
                }
                cx /= n;
                cy /= n;
                yield return (new Point(cx, cy), m.Name ?? string.Empty);
            }
        }
        if (m.Children != null)
        {
            foreach (var child in m.Children)
            {
                foreach (var t in CollectLabels(child))
                    yield return t;
            }
        }
    }

    private static IEnumerable<(Point pos, string text, string id)> CollectCards(Module m)
    {
        if (m == null)
        {
            yield break;
        }
        if (m.Shapes != null)
        {
            foreach (var shp in m.Shapes)
            {
                if (shp?.Points == null || shp.Points.Count < 3) continue;
                double cx = 0, cy = 0;
                int n = shp.Points.Count;
                foreach (var p in shp.Points)
                {
                    cx += p.X;
                    cy += p.Y;
                }
                cx /= n;
                cy /= n;
                yield return (new Point(cx, cy), m.Name ?? string.Empty, m.Id);
            }
        }
        if (m.Children != null)
        {
            foreach (var child in m.Children)
            {
                foreach (var t in CollectCards(child))
                    yield return t;
            }
        }
    }

    public static Module? FindModuleById(string id)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var file = _current;
            if (file?.RootModules == null) return null;
            foreach (var m in file.RootModules)
            {
                var found = FindByIdRecursive(m, id);
                if (found != null) return found;
            }
            return null;
        }
    }

    private static Module? FindByIdRecursive(Module m, string id)
    {
        if (m.Id == id) return m;
        if (m.Children != null)
        {
            foreach (var child in m.Children)
            {
                var f = FindByIdRecursive(child, id);
                if (f != null) return f;
            }
        }
        return null;
    }
}
