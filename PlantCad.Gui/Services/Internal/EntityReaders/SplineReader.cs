using System;
using System.Linq;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class SplineReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is Spline;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not Spline sp)
            return;

        try
        {
            var points = (
                sp.FitPoints != null && sp.FitPoints.Count > 1 ? sp.FitPoints : sp.ControlPoints
            )
                .Select(p => new Avalonia.Point(p.X, p.Y))
                .ToList();

            if (points.Count >= 2)
            {
                context.Splines.Add(
                    new CadSpline
                    {
                        Id = sp.Handle.ToString(),
                        Layer = sp.Layer?.Name ?? string.Empty,
                        Points = points,
                        IsClosed = sp.IsClosed,
                    }
                );
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read SPLINE entity.", ex);
        }
    }
}
