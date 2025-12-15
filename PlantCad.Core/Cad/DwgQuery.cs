using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;

namespace PlantCad.Core.Cad;

/// <summary>
/// Utility for querying DWG files via ACadSharp. Provides helpers to select spaces,
/// enumerate inserts, query module polylines, and perform basic spatial checks.
/// </summary>
public sealed class DwgQuery
{
    public CadDocument Document { get; }
    public string FileName { get; }

    private DwgQuery(CadDocument document, string fileName)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        FileName = fileName;
    }

    /// <summary>
    /// Opens a DWG file and creates a DwgQuery instance.
    /// </summary>
    public static DwgQuery Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", nameof(path));
        }

        var doc = DwgReader.Read(path);
        var fileName = System.IO.Path.GetFileName(path);
        return new DwgQuery(doc, fileName);
    }

    /// <summary>
    /// Gets the set of target spaces (Model and/or Paper) based on CLI-like options.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown if an unsupported layout name is requested.</exception>
    public IEnumerable<BlockRecord> GetTargetSpaces(bool allLayouts, string? layoutArg)
    {
        if (allLayouts)
        {
            yield return Document.BlockRecords["*Model_Space"];
            yield return Document.BlockRecords["*Paper_Space"];
            yield break;
        }

        if (string.IsNullOrWhiteSpace(layoutArg))
        {
            yield return Document.BlockRecords["*Model_Space"];
            yield break;
        }

        var arg = layoutArg.Trim();
        if (arg.Equals("model", StringComparison.OrdinalIgnoreCase) || arg.Equals("modelspace", StringComparison.OrdinalIgnoreCase))
        {
            yield return Document.BlockRecords["*Model_Space"];
            yield break;
        }

        if (arg.Equals("paper", StringComparison.OrdinalIgnoreCase) || arg.Equals("paperspace", StringComparison.OrdinalIgnoreCase))
        {
            yield return Document.BlockRecords["*Paper_Space"];
            yield break;
        }

        throw new NotSupportedException("DWG layout selection not supported for: " + layoutArg + ". Use 'Model' or 'Paper', or pass --all.");
    }

    /// <summary>
    /// Counts INSERT entities across the provided spaces, grouped by block name.
    /// </summary>
    public (IDictionary<string, int> Counts, long TotalInserts) CountInserts(IEnumerable<BlockRecord> spaces)
    {
        if (spaces == null)
        {
            throw new ArgumentNullException(nameof(spaces));
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long total = 0;

        foreach (var block in spaces)
        {
            foreach (var ins in block.Entities.OfType<Insert>())
            {
                var blockName = ins.Block?.Name ?? "<Unnamed>";
                counts[blockName] = counts.TryGetValue(blockName, out var c) ? c + 1 : 1;
                total++;
            }
        }

        return (counts, total);
    }

    /// <summary>
    /// Counts INSERT entities across selected spaces based on the provided layout flags.
    /// </summary>
    public (IDictionary<string, int> Counts, long TotalInserts) CountInsertsAcross(bool allLayouts, string? layoutArg)
    {
        var spaces = GetTargetSpaces(allLayouts, layoutArg).ToList();
        return CountInserts(spaces);
    }

    /// <summary>
    /// Returns all INSERT entities in a given space.
    /// </summary>
    public IEnumerable<Insert> GetInserts(BlockRecord space)
    {
        if (space == null)
        {
            throw new ArgumentNullException(nameof(space));
        }

        return space.Entities.OfType<Insert>();
    }

    /// <summary>
    /// Returns all lightweight polylines on layers that start with the given prefix.
    /// </summary>
    public IEnumerable<LwPolyline> GetModulePolylines(BlockRecord space, string layerPrefix)
    {
        if (space == null)
        {
            throw new ArgumentNullException(nameof(space));
        }

        if (layerPrefix == null)
        {
            throw new ArgumentNullException(nameof(layerPrefix));
        }

        return space.Entities
            .OfType<LwPolyline>()
            .Where(p => p.Layer != null && p.Layer.Name.StartsWith(layerPrefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Computes module (rectangle) based counts for inserts across selected spaces.
    /// </summary>
    public IEnumerable<ModuleCountResult> GetModuleCounts(bool allLayouts, string? layoutArg, string layerPrefix)
    {
        foreach (var space in GetTargetSpaces(allLayouts, layoutArg))
        {
            var inserts = GetInserts(space).ToList();
            var spaceLabel = GetSpaceLabel(space);
            var polys = GetModulePolylines(space, layerPrefix).ToList();

            foreach (var poly in polys)
            {
                var rect = GetBoundingRect(poly);
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                long total = 0;

                foreach (var ins in inserts)
                {
                    var p = ins.InsertPoint;
                    if (Contains(rect, p.X, p.Y))
                    {
                        var blockName = ins.Block?.Name ?? "<Unnamed>";
                        counts[blockName] = counts.TryGetValue(blockName, out var c) ? c + 1 : 1;
                        total++;
                    }
                }

                yield return new ModuleCountResult(
                    SpaceLabel: spaceLabel,
                    ModuleName: poly.Layer?.Name ?? "<no-layer>",
                    Counts: counts,
                    Total: total
                );
            }
        }
    }

    /// <summary>
    /// Gets an axis-aligned bounding rectangle for an LWPolyline.
    /// </summary>
    public static RectD GetBoundingRect(LwPolyline poly)
    {
        if (poly == null)
        {
            throw new ArgumentNullException(nameof(poly));
        }

        var bb = poly.GetBoundingBox();
        return new RectD(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y);
    }

    /// <summary>
    /// Checks if a point is inside or on the edge of the rectangle.
    /// </summary>
    public static bool Contains(RectD rect, double x, double y)
    {
        return x >= rect.MinX && x <= rect.MaxX && y >= rect.MinY && y <= rect.MaxY;
    }

    /// <summary>
    /// Returns a user-friendly label for a space (Model/Paper/other).
    /// </summary>
    public string GetSpaceLabel(BlockRecord space)
    {
        if (space == null)
        {
            throw new ArgumentNullException(nameof(space));
        }

        if (space.Name.Equals("*Model_Space", StringComparison.OrdinalIgnoreCase))
        {
            return "Model";
        }

        if (space.Name.Equals("*Paper_Space", StringComparison.OrdinalIgnoreCase))
        {
            return "Paper";
        }

        return space.Name;
    }

    /// <summary>
    /// Simple rectangle struct for double precision coordinates.
    /// </summary>
    public readonly struct RectD
    {
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }

        public RectD(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }
    }

    /// <summary>
    /// Result for per-module counting query.
    /// </summary>
    public sealed record ModuleCountResult(string SpaceLabel, string ModuleName, IDictionary<string, int> Counts, long Total);
}
