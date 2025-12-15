using System;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class TextEntityReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is TextEntity;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not TextEntity txt)
            return;

        try
        {
            context.Texts.Add(
                new CadText
                {
                    Id = txt.Handle.ToString(),
                    Layer = txt.Layer?.Name ?? string.Empty,
                    Position = new Avalonia.Point(txt.InsertPoint.X, txt.InsertPoint.Y),
                    RotationDeg = txt.Rotation * (180.0 / Math.PI),
                    Height = txt.Height,
                    Value = txt.Value ?? string.Empty,
                    HorizontalAlignment = txt.HorizontalAlignment switch
                    {
                        TextHorizontalAlignment.Left => CadTextHAlign.Left,
                        TextHorizontalAlignment.Center => CadTextHAlign.Center,
                        TextHorizontalAlignment.Right => CadTextHAlign.Right,
                        _ => CadTextHAlign.Left,
                    },
                    VerticalAlignment = txt.VerticalAlignment switch
                    {
                        TextVerticalAlignmentType.Baseline => CadTextVAlign.Baseline,
                        TextVerticalAlignmentType.Bottom => CadTextVAlign.Bottom,
                        TextVerticalAlignmentType.Middle => CadTextVAlign.Middle,
                        TextVerticalAlignmentType.Top => CadTextVAlign.Top,
                        _ => CadTextVAlign.Baseline,
                    },
                }
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read TEXT entity.", ex);
        }
    }
}
