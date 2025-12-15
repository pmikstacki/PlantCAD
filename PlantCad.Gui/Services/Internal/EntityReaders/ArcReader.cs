using System;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class ArcReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is Arc;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not Arc a)
            return;

        context.Arcs.Add(
            new CadArc
            {
                Id = a.Handle.ToString(),
                Layer = a.Layer?.Name ?? string.Empty,
                Center = new Avalonia.Point(a.Center.X, a.Center.Y),
                Radius = a.Radius,
                StartAngle = a.StartAngle * 180.0 / Math.PI,
                EndAngle = a.EndAngle * 180.0 / Math.PI,
            }
        );
    }
}
