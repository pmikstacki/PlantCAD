using System;
using System.Collections.Generic;

namespace PlantCad.Gui.Models;

/// <summary>
/// Snapshot of DWG project metadata extracted at load time for UI consumption.
/// Keeps GUI decoupled from ACadSharp types.
/// </summary>
public sealed class CadProjectInfo
{
    // File
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }

    // Summary (from CadSummaryInfo)
    public string Title { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Keywords { get; init; } = string.Empty;
    public string Comments { get; init; } = string.Empty;
    public string LastSavedBy { get; init; } = string.Empty;
    public string RevisionNumber { get; init; } = string.Empty;
    public string HyperlinkBase { get; init; } = string.Empty;
    public DateTime CreatedDate { get; init; }
    public DateTime ModifiedDate { get; init; }
    public IReadOnlyDictionary<string, string> SummaryProperties { get; init; } =
        new Dictionary<string, string>();

    // Header highlights
    public string AcadVersion { get; init; } = string.Empty;
    public string Units { get; init; } = string.Empty; // human-readable units (INSUNITS)
    public string CodePage { get; init; } = string.Empty;

    public string CurrentLayerName { get; init; } = string.Empty;
    public string CurrentTextStyleName { get; init; } = string.Empty;
    public string CurrentDimensionStyleName { get; init; } = string.Empty;

    // Header extents (model space), if available
    public double ModelExtMinX { get; init; }
    public double ModelExtMinY { get; init; }
    public double ModelExtMinZ { get; init; }
    public double ModelExtMaxX { get; init; }
    public double ModelExtMaxY { get; init; }
    public double ModelExtMaxZ { get; init; }

    // Collection counts
    public int EntitiesTotal { get; init; }
    public int LayersCount { get; init; }
    public int BlockRecordsCount { get; init; }
    public int LineTypesCount { get; init; }
    public int TextStylesCount { get; init; }
    public int DimensionStylesCount { get; init; }
    public int ViewsCount { get; init; }
    public int VPortsCount { get; init; }
    public int LayoutsCount { get; init; }
    public int GroupsCount { get; init; }
    public int ColorsCount { get; init; }
    public int PdfDefinitionsCount { get; init; }
}
