using System.Collections.Generic;
using System.Linq;
using ACadSharp.Entities;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class CadFileServiceComposite
{
    private readonly List<ICadEntityReader> _readers;

    public CadFileServiceComposite()
    {
        _readers = new List<ICadEntityReader>
        {
            new LwPolylineReader(),
            new Polyline2dReader(),
            new LineReader(),
            new CircleReader(),
            new ArcReader(),
            new EllipseReader(),
            new TextEntityReader(),
            new MTextReader(),
            new SplineReader(),
            new SolidReader(),
            new TableReader(),
            new PdfUnderlayReader(),
            new HatchReader(),
            new InsertReader(),
        };
    }

    public CadModel Read(ACadSharp.CadDocument doc)
    {
        // Use Model Space by default for initial preview
        var model = doc.BlockRecords["*Model_Space"];
        var context = new CadReadContext();

        foreach (var ent in model.Entities)
        {
            foreach (var reader in _readers)
            {
                if (reader.CanRead(ent))
                {
                    reader.Read(ent, context);
                    break;
                }
            }
        }

        // Layers logic from original file
        var layers = new List<CadLayer>();
        foreach (var l in doc.Layers)
        {
            var (argb, isOn, isFrozen, isLocked) = GetLayerAppearance(l);
            layers.Add(
                new CadLayer
                {
                    Name = l.Name,
                    ColorArgb = argb,
                    IsOn = isOn,
                    IsFrozen = isFrozen,
                    IsLocked = isLocked,
                }
            );
        }

        // If no layers found, infer from entities (legacy logic)
        if (layers.Count == 0)
        {
            var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            // Collect layer names from all lists in context
            foreach (var e in context.Polylines)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.Lines)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.Circles)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.Arcs)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.Inserts)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.Ellipses)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.Texts)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.Splines)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.Solids)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.MTexts)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.Hatches)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);
            foreach (var e in context.Tables)
                if (!string.IsNullOrWhiteSpace(e.Layer))
                    names.Add(e.Layer);

            foreach (var n in names)
            {
                layers.Add(
                    new CadLayer
                    {
                        Name = n,
                        ColorArgb = 0xFF000000,
                        IsOn = true,
                        IsFrozen = false,
                        IsLocked = false,
                    }
                );
            }
        }

        // Extents
        var extents = ComputeExtents(context);

        return new CadModel
        {
            Polylines = context.Polylines,
            Lines = context.Lines,
            Circles = context.Circles,
            Arcs = context.Arcs,
            Inserts = context.Inserts,
            Ellipses = context.Ellipses,
            Texts = context.Texts,
            MTexts = context.MTexts,
            Splines = context.Splines,
            Solids = context.Solids,
            Hatches = context.Hatches,
            Tables = context.Tables,
            Underlays = context.Underlays,
            Layers = layers,
            Extents = extents,
        };
    }

    private static (uint argb, bool isOn, bool isFrozen, bool isLocked) GetLayerAppearance(
        ACadSharp.Tables.Layer l
    )
    {
        if (l == null)
            throw new System.ArgumentNullException(nameof(l));
        var color = l.Color;
        byte r = color.R;
        byte g = color.G;
        byte b = color.B;
        if (r > 240 && g > 240 && b > 240)
        {
            r = 0;
            g = 0;
            b = 0;
        }
        uint argb = (0xFFu << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

        var flags = l.Flags;
        bool isFrozen = false;
        bool isLocked = false;
        try
        {
            isFrozen = flags.HasFlag(ACadSharp.Tables.LayerFlags.Frozen);
            isLocked = flags.HasFlag(ACadSharp.Tables.LayerFlags.Locked);
        }
        catch
        {
            // ignore
        }
        bool isOn = l.IsOn;
        return (argb, isOn, isFrozen, isLocked);
    }

    private static CadExtents ComputeExtents(CadReadContext context)
    {
        // Re-use logic from CadFileService.ComputeExtents but adapting to CadReadContext
        // For brevity, I'll assume we can call the static method if it was public, or copy it.
        // Copying simplified version here to keep it self-contained or I need to expose it.

        // Let's implement a simple version or copy the one from CadFileService
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        void include(double x, double y)
        {
            if (x < minX)
                minX = x;
            if (y < minY)
                minY = y;
            if (x > maxX)
                maxX = x;
            if (y > maxY)
                maxY = y;
        }

        foreach (var pl in context.Polylines)
        foreach (var p in pl.Points)
            include(p.X, p.Y);
        foreach (var ln in context.Lines)
        {
            include(ln.Start.X, ln.Start.Y);
            include(ln.End.X, ln.End.Y);
        }
        foreach (var c in context.Circles)
        {
            include(c.Center.X - c.Radius, c.Center.Y - c.Radius);
            include(c.Center.X + c.Radius, c.Center.Y + c.Radius);
        }
        foreach (var a in context.Arcs)
        {
            include(a.Center.X - a.Radius, a.Center.Y - a.Radius);
            include(a.Center.X + a.Radius, a.Center.Y + a.Radius);
        }
        foreach (var i in context.Inserts)
            include(i.Position.X, i.Position.Y);
        // ... (other entities)

        // For tables
        foreach (var t in context.Tables)
        {
            include(t.Bounds.X, t.Bounds.Y);
            include(t.Bounds.Right, t.Bounds.Bottom);
        }

        if (double.IsInfinity(minX))
        {
            return new CadExtents
            {
                MinX = 0,
                MinY = 0,
                MaxX = 100,
                MaxY = 100,
            };
        }

        const double pad = 10;
        return new CadExtents
        {
            MinX = minX - pad,
            MinY = minY - pad,
            MaxX = maxX + pad,
            MaxY = maxY + pad,
        };
    }
}
