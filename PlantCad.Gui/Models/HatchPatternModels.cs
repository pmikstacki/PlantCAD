using System;
using System.Collections.Generic;

namespace PlantCad.Gui.Models;

public sealed class PatternLine
{
    public double AngleDeg { get; set; }
    public double BaseX { get; set; }
    public double BaseY { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public IReadOnlyList<double> DashLengths { get; set; } = Array.Empty<double>();
}

public sealed class HatchPatternInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public IReadOnlyList<PatternLine> Lines { get; set; } = Array.Empty<PatternLine>();
    public bool IsSupported { get; set; } = true;
}
