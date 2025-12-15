using Avalonia.Media;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Controls.Rendering
{
    public interface IStyleProvider
    {
        Pen GetPolylinePen(CadPolyline polyline);
        IBrush GetInsertBrush(CadInsert insert);
        double GetInsertRadius(CadInsert insert);
        Pen GetStrokePen(string? layerName);

        // Text
        Typeface GetTextTypeface(CadText text);
        IBrush GetTextBrush(CadText text);

        // MText
        Typeface GetMTextTypeface(CadMText mtext);
        IBrush GetMTextBrush(CadMText mtext);

        // Insert label
        Typeface GetInsertLabelTypeface(CadInsert insert);
        IBrush GetInsertLabelBrush(CadInsert insert);

        // Fills
        IBrush GetFillBrush(string? layerName);

        // Background brush for wipeouts/erasers (theme-aware)
        IBrush GetBackgroundBrush();

        // Point marker size in pixels (constant on screen)
        double GetPointSizePx();

        // Leader arrow size in pixels (constant on screen)
        double GetLeaderArrowSizePx();

        // Tables
        Pen GetTableGridPen(CadTable table);
        IBrush GetTableHeaderFill(CadTable table);
        Typeface GetTableTextTypeface(CadTable table);
        IBrush GetTableTextBrush(CadTable table);

        // Underlays (raster/pdf/underlay)
        double GetUnderlayOpacity(CadUnderlay underlay);
        IBrush? GetUnderlayTintBrush(CadUnderlay underlay);

        // Shapes
        Pen GetShapePen(CadShape shape);
        IBrush? GetShapeFill(CadShape shape);

        // Tolerances
        Typeface GetToleranceTypeface(CadTolerance tolerance);
        IBrush GetToleranceTextBrush(CadTolerance tolerance);
        Pen GetToleranceFramePen(CadTolerance tolerance);
    }
}
