using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class SolidReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is Solid;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not Solid s)
            return;

        try
        {
            var v = new List<Avalonia.Point>
            {
                new Avalonia.Point(s.FirstCorner.X, s.FirstCorner.Y),
                new Avalonia.Point(s.SecondCorner.X, s.SecondCorner.Y),
                new Avalonia.Point(s.ThirdCorner.X, s.ThirdCorner.Y),
                new Avalonia.Point(s.FourthCorner.X, s.FourthCorner.Y),
            };
            // If third==fourth, it is a triangle; drop duplicate
            if (Math.Abs(v[2].X - v[3].X) < 1e-9 && Math.Abs(v[2].Y - v[3].Y) < 1e-9)
            {
                v.RemoveAt(3);
            }

            context.Solids.Add(
                new CadSolid
                {
                    Id = s.Handle.ToString(),
                    Layer = s.Layer?.Name ?? string.Empty,
                    Vertices = v,
                }
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read SOLID entity.", ex);
        }
    }
}
