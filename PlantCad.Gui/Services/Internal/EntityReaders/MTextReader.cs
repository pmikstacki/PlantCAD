using System;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class MTextReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is MText;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not MText mt)
            return;

        try
        {
            context.MTexts.Add(
                new CadMText
                {
                    Id = mt.Handle.ToString(),
                    Layer = mt.Layer?.Name ?? string.Empty,
                    Position = new Avalonia.Point(mt.InsertPoint.X, mt.InsertPoint.Y),
                    RotationDeg = mt.Rotation * (180.0 / Math.PI),
                    Height = mt.Height,
                    RectangleWidth = mt.RectangleWidth,
                    Value = mt.Value ?? string.Empty,
                }
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read MTEXT entity.", ex);
        }
    }
}
