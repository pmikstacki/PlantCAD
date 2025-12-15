using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class PdfUnderlayReader : ICadEntityReader
{
    public bool CanRead(Entity entity) => entity is PdfUnderlay;

    public void Read(Entity entity, CadReadContext context)
    {
        if (entity is not PdfUnderlay pdf)
            return;

        try
        {
            // Build world quad from insert point, scales and rotation (assume XY plane)
            double tx = pdf.InsertPoint.X;
            double ty = pdf.InsertPoint.Y;
            double sx = pdf.XScale;
            double sy = pdf.YScale;
            double rot = pdf.Rotation; // radians
            double cosR = Math.Cos(rot);
            double sinR = Math.Sin(rot);

            var p0 = new Avalonia.Point(tx, ty);
            var p1 = new Avalonia.Point(tx + sx * cosR, ty + sx * sinR);
            var p3 = new Avalonia.Point(tx - sy * sinR, ty + sy * cosR);
            var p2 = new Avalonia.Point(p1.X + (p3.X - p0.X), p1.Y + (p3.Y - p0.Y));

            // Optional clip (map vertices through same local->world transform)
            IReadOnlyList<IReadOnlyList<Avalonia.Point>>? clipLoops = null;
            if (pdf.ClipBoundaryVertices != null && pdf.ClipBoundaryVertices.Count >= 3)
            {
                var loop = new List<Avalonia.Point>(pdf.ClipBoundaryVertices.Count);
                foreach (var v in pdf.ClipBoundaryVertices)
                {
                    // TransformPoint logic inline
                    // x' = x*sx*cos - y*sy*sin + tx
                    // y' = x*sx*sin + y*sy*cos + ty
                    double x = v.X;
                    double y = v.Y;
                    double px = x * sx * cosR - y * sy * sinR + tx;
                    double py = x * sx * sinR + y * sy * cosR + ty;

                    loop.Add(new Avalonia.Point(px, py));
                }
                clipLoops = new List<IReadOnlyList<Avalonia.Point>> { loop };
            }

            // Flags and display params
            bool monochrome = pdf.Flags.HasFlag(ACadSharp.Entities.UnderlayDisplayFlags.Monochrome);
            // ACadSharp range: Fade 0-100; Contrast 0-100 (default 50)
            double fade = Math.Clamp(pdf.Fade / 100.0, 0.0, 1.0);
            double contrast = Math.Clamp((pdf.Contrast - 50) / 50.0, -1.0, 1.0);

            context.Underlays.Add(
                new CadUnderlay
                {
                    Id = pdf.Handle.ToString(),
                    Layer = pdf.Layer?.Name ?? string.Empty,
                    FilePath = pdf.Definition?.File,
                    ImageKey = pdf.Definition?.File,
                    WorldQuad = new[] { p0, p1, p2, p3 },
                    ClipLoops = clipLoops,
                    Opacity = 1.0,
                    Monochrome = monochrome,
                    Fade = fade,
                    Contrast = contrast,
                }
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to read PDFUNDERLAY entity.", ex);
        }
    }
}
