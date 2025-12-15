using System;
using System.Collections.Generic;
using Avalonia;
using PlantCad.Gui.Models.Modules;

namespace PlantCad.Gui.Services.Modules;

internal static class ModuleGeometryUtils
{
    // Ray casting algorithm for point-in-polygon (bounded)
    public static bool ContainsPoint(ModulePolygon poly, Point p)
    {
        if (poly == null) throw new ArgumentNullException(nameof(poly));
        var pts = poly.Points;
        if (pts == null || pts.Count < 3) return false;
        bool inside = false;
        int n = pts.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var pi = pts[i];
            var pj = pts[j];
            // Check if edge (pj -> pi) crosses horizontal ray to the right of p
            bool intersect = ((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                             (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / Math.Max(1e-12, (pj.Y - pi.Y)) + pi.X);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    public static bool ContainsPointAny(IEnumerable<ModulePolygon> polys, Point p)
    {
        if (polys == null) return false;
        foreach (var poly in polys)
        {
            if (poly == null) continue;
            if (ContainsPoint(poly, p)) return true;
        }
        return false;
    }
}
