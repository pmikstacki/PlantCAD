using System.Collections.Generic;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class CircleReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is Circle;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not Circle c)
            return;

        context.Circles.Add(
            new CadCircle
            {
                Id = c.Handle.ToString(),
                Layer = c.Layer?.Name ?? string.Empty,
                Center = new Avalonia.Point(c.Center.X, c.Center.Y),
                Radius = c.Radius,
            }
        );
    }
}
