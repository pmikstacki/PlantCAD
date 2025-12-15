using System;
using System.Collections.Generic;

namespace PlantCad.Gui.Models.Sheets;

public sealed class LegendItem
{
    public string BlockName { get; set; } = string.Empty;
    public int Count { get; set; }
    public byte[]? ThumbnailPng { get; set; }
}

public sealed class SheetPage
{
    public string ModuleId { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string ModulePath { get; set; } = string.Empty;

    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }

    public double AppliedScale { get; set; }

    public List<LegendItem> LegendItems { get; } = new List<LegendItem>();
}
