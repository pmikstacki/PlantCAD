using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACadSharp;
using ACadSharp.IO;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services.Internal.EntityReaders;

namespace PlantCad.Gui.Services.Internal;

public sealed class CadFileService : ICadFileService
{
    public Task<CadModel> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("CAD file not found.", path);
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".dwg")
        {
            return Task.FromResult(OpenDwg(path));
        }
        if (ext == ".dxf")
        {
            throw new NotSupportedException(
                "DXF is not supported in this application. Please load DWG files."
            );
        }

        throw new NotSupportedException(
            "Unsupported CAD file type: " + ext + ". Supported: DWG, DXF."
        );
    }

    private static CadModel OpenDwg(string path)
    {
        var doc = DwgReader.Read(path);

        var compositeReader = new CadFileServiceComposite();
        var model = compositeReader.Read(doc);

        // Fill project info
        var fi = new FileInfo(path);
        var header = doc.Header;
        var sum = doc.SummaryInfo;
        var modelExtMin = header?.ModelSpaceExtMin;
        var modelExtMax = header?.ModelSpaceExtMax;

        var projectInfo = new CadProjectInfo
        {
            FilePath = path,
            FileName = fi.Name,
            FileSizeBytes = fi.Exists ? fi.Length : 0,
            LastWriteTimeUtc = fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue,

            Title = sum?.Title ?? string.Empty,
            Subject = sum?.Subject ?? string.Empty,
            Author = sum?.Author ?? string.Empty,
            Keywords = sum?.Keywords ?? string.Empty,
            Comments = sum?.Comments ?? string.Empty,
            LastSavedBy = sum?.LastSavedBy ?? string.Empty,
            RevisionNumber = sum?.RevisionNumber ?? string.Empty,
            HyperlinkBase = sum?.HyperlinkBase ?? string.Empty,
            CreatedDate = sum?.CreatedDate ?? DateTime.MinValue,
            ModifiedDate = sum?.ModifiedDate ?? DateTime.MinValue,
            SummaryProperties = sum?.Properties ?? new Dictionary<string, string>(),

            AcadVersion = header?.Version.ToString() ?? string.Empty,
            Units = header?.InsUnits.ToString() ?? string.Empty,
            CodePage = header?.CodePage ?? string.Empty,
            CurrentLayerName = header?.CurrentLayerName ?? string.Empty,
            CurrentTextStyleName = header?.CurrentTextStyleName ?? string.Empty,
            CurrentDimensionStyleName = header?.CurrentDimensionStyleName ?? string.Empty,
            ModelExtMinX = modelExtMin?.X ?? 0,
            ModelExtMinY = modelExtMin?.Y ?? 0,
            ModelExtMinZ = modelExtMin?.Z ?? 0,
            ModelExtMaxX = modelExtMax?.X ?? 0,
            ModelExtMaxY = modelExtMax?.Y ?? 0,
            ModelExtMaxZ = modelExtMax?.Z ?? 0,

            EntitiesTotal = doc.Entities?.Count ?? 0,
            LayersCount = doc.Layers?.Count ?? 0,
            BlockRecordsCount = doc.BlockRecords?.Count ?? 0,
            LineTypesCount = doc.LineTypes?.Count ?? 0,
            TextStylesCount = doc.TextStyles?.Count ?? 0,
            DimensionStylesCount = doc.DimensionStyles?.Count ?? 0,
            ViewsCount = doc.Views?.Count ?? 0,
            VPortsCount = doc.VPorts?.Count ?? 0,
            LayoutsCount = doc.Layouts != null ? doc.Layouts.Count() : 0,
            GroupsCount = doc.Groups != null ? doc.Groups.Count() : 0,
            ColorsCount = doc.Colors != null ? doc.Colors.Count() : 0,
            PdfDefinitionsCount = doc.PdfDefinitions != null ? doc.PdfDefinitions.Count() : 0,
        };

        // Create new model with updated project info (records are immutable-ish or at least we want to set this property)
        // Since CadModel uses init only properties, we can't set it after construction.
        // But CadFileServiceComposite.Read returns a CadModel without ProjectInfo.
        // We can either modify CadFileServiceComposite to accept ProjectInfo or create a new CadModel here copying properties.
        // Simpler: Copy properties.

        return new CadModel
        {
            ProjectInfo = projectInfo,
            Extents = model.Extents,
            Polylines = model.Polylines,
            Inserts = model.Inserts,
            Lines = model.Lines,
            Circles = model.Circles,
            Arcs = model.Arcs,
            Ellipses = model.Ellipses,
            Texts = model.Texts,
            MTexts = model.MTexts,
            Splines = model.Splines,
            Solids = model.Solids,
            Hatches = model.Hatches,
            Tables = model.Tables,
            Underlays = model.Underlays,
            Layers = model.Layers,
            Points = model.Points,
            Leaders = model.Leaders,
            DimensionsAligned = model.DimensionsAligned,
            DimensionsLinear = model.DimensionsLinear,
            Rays = model.Rays,
            XLines = model.XLines,
            Wipeouts = model.Wipeouts,
            Shapes = model.Shapes,
            Tolerances = model.Tolerances,
        };
    }
}
