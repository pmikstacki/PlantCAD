using System;
using System.Linq;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class Polyline2dReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is Polyline2D;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not Polyline2D p2d)
            return;

        try
        {
            var pts = p2d
                .Vertices.Select(v => new Avalonia.Point(v.Location.X, v.Location.Y))
                .ToList();
            var bulges = p2d.Vertices.Select(v => (double)(v.Bulge)).ToList();
            if (pts.Count > 0)
            {
                context.Polylines.Add(
                    new CadPolyline
                    {
                        Id = p2d.Handle.ToString(),
                        Layer = p2d.Layer?.Name ?? string.Empty,
                        Points = pts,
                        Bulges = bulges,
                        IsClosed = p2d.IsClosed,
                    }
                );
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read POLYLINE2D vertices.", ex);
        }
    }
}
