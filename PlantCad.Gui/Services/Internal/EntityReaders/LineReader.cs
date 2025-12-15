using System.Collections.Generic;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class LineReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is Line;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not Line ln)
            return;

        context.Lines.Add(
            new CadLine
            {
                Id = ln.Handle.ToString(),
                Layer = ln.Layer?.Name ?? string.Empty,
                Start = new Avalonia.Point(ln.StartPoint.X, ln.StartPoint.Y),
                End = new Avalonia.Point(ln.EndPoint.X, ln.EndPoint.Y),
            }
        );
    }
}
