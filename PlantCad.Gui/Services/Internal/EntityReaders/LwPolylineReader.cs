using System;
using System.Linq;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class LwPolylineReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is LwPolyline;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not LwPolyline lp)
            return;

        try
        {
            var pts = lp
                .Vertices.Select(v => new Avalonia.Point(v.Location.X, v.Location.Y))
                .ToList();
            var bulges = lp.Vertices.Select(v => (double)v.Bulge).ToList();
            if (pts.Count > 0)
            {
                context.Polylines.Add(
                    new CadPolyline
                    {
                        Id = lp.Handle.ToString(),
                        Layer = lp.Layer?.Name ?? string.Empty,
                        Points = pts,
                        Bulges = bulges,
                        IsClosed = lp.IsClosed,
                    }
                );
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read LWPOLYLINE vertices.", ex);
        }
    }
}
