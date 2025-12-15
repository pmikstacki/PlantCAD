using System;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class EllipseReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is Ellipse;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not Ellipse el)
            return;

        try
        {
            // Use available properties without relying on version-specific fields
            var rx = (el.MajorAxis) / 2.0; // MajorAxis is a length (diameter)
            var ry = rx * el.RadiusRatio;
            var rotDeg = el.Rotation * (180.0 / Math.PI);
            bool isArc = !el.IsFullEllipse;
            context.Ellipses.Add(
                new CadEllipse
                {
                    Id = el.Handle.ToString(),
                    Layer = el.Layer?.Name ?? string.Empty,
                    Center = new Avalonia.Point(el.Center.X, el.Center.Y),
                    RadiusX = rx,
                    RadiusY = ry,
                    RotationDeg = rotDeg,
                    IsArc = isArc,
                    StartAngleDeg = el.StartParameter * (180.0 / Math.PI),
                    EndAngleDeg = el.EndParameter * (180.0 / Math.PI),
                }
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read ELLIPSE entity.", ex);
        }
    }
}
