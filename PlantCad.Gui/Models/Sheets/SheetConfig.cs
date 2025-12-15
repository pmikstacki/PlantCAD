using System;
using System.Collections.Generic;
using ACadSharp.Objects;

namespace PlantCad.Gui.Models.Sheets;

public sealed class SheetConfig
{
    public PageSource PageSource { get; set; } = PageSource.Standard;
    public string? LayoutName { get; set; }

    public string? PaperSizeToken { get; set; } = "ISO_A4_(210.00_x_297.00_MM)";
    public double? PaperWidthMm { get; set; } = 210.0;
    public double? PaperHeightMm { get; set; } = 297.0;
    public PlotPaperUnits PaperUnits { get; set; } = PlotPaperUnits.Millimeters;
    public PlotRotation PaperRotation { get; set; } = PlotRotation.NoRotation;

    public double MarginLeftMm { get; set; } = 5.0;
    public double MarginRightMm { get; set; } = 5.0;
    public double MarginTopMm { get; set; } = 5.0;
    public double MarginBottomMm { get; set; } = 5.0;

    public bool FitToModule { get; set; } = true;
    public double FixedScaleNumerator { get; set; } = 1.0;
    public double? FixedScaleDenominator { get; set; }

    public LayerFilter Layers { get; set; } = new LayerFilter();
    public BlockFilter Blocks { get; set; } = new BlockFilter();

    public LegendPlacement LegendPlacement { get; set; } = LegendPlacement.Right;
    public double LegendSizeMm { get; set; } = 60.0;
    public int? LegendColumns { get; set; }
    public int? LegendRows { get; set; }
    public int ThumbnailSizePx { get; set; } = 128;
}
