using System.Collections.Generic;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public sealed class CadReadContext
{
    public List<CadPolyline> Polylines { get; } = new();
    public List<CadLine> Lines { get; } = new();
    public List<CadCircle> Circles { get; } = new();
    public List<CadArc> Arcs { get; } = new();
    public List<CadInsert> Inserts { get; } = new();
    public List<CadEllipse> Ellipses { get; } = new();
    public List<CadText> Texts { get; } = new();
    public List<CadMText> MTexts { get; } = new();
    public List<CadSpline> Splines { get; } = new();
    public List<CadSolid> Solids { get; } = new();
    public List<CadHatch> Hatches { get; } = new();
    public List<CadTable> Tables { get; } = new();
    public List<CadUnderlay> Underlays { get; } = new();
}
