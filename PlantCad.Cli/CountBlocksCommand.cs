using PlantCad.Core.Cad;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using PlantCad.Cli.Export;
using Spectre.Console;
using Spectre.Console.Cli;
using DXFPolyline2D = netDxf.Entities.Polyline2D;

namespace PlantCad.Cli;

public sealed class CountBlocksCommand : Command<CountSettings>
{
    public override int Execute(CommandContext context, CountSettings settings)
    {
        try
        {
            var path = settings.Path;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

            // Load ignore list if provided
            HashSet<string>? ignore = null;
            if (!string.IsNullOrWhiteSpace(settings.IgnorePath))
            {
                var cfg = IgnoreConfig.Load(settings.IgnorePath);
                ignore = cfg.ToSet();
                AnsiConsole.MarkupLine($"[grey]Ignoring {ignore.Count} block name(s).[/]");
            }

            IDictionary<string, int> counts;
            long totalInserts;
            if (ext == ".dwg")
            {
                // DWG path via DwgQuery utility
                var dwg = DwgQuery.Open(path);

                if (settings.Modules)
                {
                    var any = false;
                    var moduleTables = new List<ModuleTable>();
                    foreach (var result in dwg.GetModuleCounts(settings.AllLayouts, settings.Layout, settings.ModulePrefix))
                    {
                        any = true;
                        var filteredCounts = result.Counts;
                        long filteredTotal = result.Total;

                        if (ignore != null && ignore.Count > 0)
                        {
                            filteredCounts = result.Counts
                                .Where(kv => !ignore.Contains(kv.Key))
                                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                            filteredTotal = filteredCounts.Values.Sum();
                        }

                        moduleTables.Add(new ModuleTable(result.ModuleName, filteredCounts));

                        var titleModule = $"Blocks in {dwg.FileName} (DWG {result.SpaceLabel}) module: {result.ModuleName}";
                        PrintTable(titleModule, filteredCounts, settings);
                        AnsiConsole.MarkupLine($"[grey]Unique blocks:[/] {filteredCounts.Count}    [grey]Total inserts:[/] {filteredTotal}");
                    }

                    if (!any)
                    {
                        AnsiConsole.MarkupLine($"[yellow]No module polylines found with prefix '{settings.ModulePrefix}'.[/]");
                    }

                    if (any && !string.IsNullOrWhiteSpace(settings.ExportPath))
                    {
                        var exporter = new ExcelExport();
                        exporter.Export(moduleTables, settings.ExportPath!);
                        AnsiConsole.MarkupLine($"[green]Exported XLSX:[/] {settings.ExportPath}");
                    }

                    return 0;
                }

                (counts, totalInserts) = dwg.CountInsertsAcross(settings.AllLayouts, settings.Layout);

                if (ignore != null && ignore.Count > 0)
                {
                    counts = counts
                        .Where(kv => !ignore.Contains(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                    totalInserts = counts.Values.Sum();
                }

                var targetLabel = settings.AllLayouts
                    ? "all: Model+Paper"
                    : $"target: {(string.IsNullOrWhiteSpace(settings.Layout) ? "Model" : settings.Layout)}";

                var titleDwg = $"Block references in {dwg.FileName} (DWG, {targetLabel})";

                PrintTable(titleDwg, counts, settings);
                AnsiConsole.MarkupLine($"[grey]Unique blocks:[/] {counts.Count}    [grey]Total inserts:[/] {totalInserts}");
                return 0;
            }

            // Optional: quick version check before loading
            var version = DxfDocument.CheckDxfFileVersion(path);
            if (version < DxfVersion.AutoCad2000)
            {
                AnsiConsole.MarkupLine("[red]Unsupported DXF version. Only AutoCAD 2000 or newer is supported.[/]");
                return -1;
            }

            var doc = DxfDocument.Load(path);

            if (settings.Modules)
            {
                return ExecuteModulesDxf(doc, settings, path, ignore);
            }

            // Select layouts
            IEnumerable<string> targetLayouts;
            if (settings.AllLayouts)
            {
                targetLayouts = doc.Layouts.Select(l => l.Name);
            }
            else if (!string.IsNullOrWhiteSpace(settings.Layout))
            {
                if (!doc.Layouts.Contains(settings.Layout))
                {
                    AnsiConsole.MarkupLine($"[red]Layout not found:[/] {settings.Layout}");
                    return -1;
                }
                targetLayouts = new[] { settings.Layout };
            }
            else
            {
                targetLayouts = new[] { netDxf.Objects.Layout.ModelSpaceName };
            }

            counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            totalInserts = 0;

            foreach (var layoutName in targetLayouts)
            {
                var layout = doc.Layouts[layoutName];
                var inserts = layout.AssociatedBlock.Entities.OfType<Insert>();
                foreach (var ins in inserts)
                {
                    var blockName = ins.Block?.Name ?? "<Unnamed>";
                    if (ignore != null && ignore.Contains(blockName))
                    {
                        continue;
                    }
                    counts[blockName] = counts.TryGetValue(blockName, out var c) ? c + 1 : 1;
                    totalInserts++;
                }
            }

            // Table output
            var fileName = System.IO.Path.GetFileName(path);
            var title = settings.AllLayouts
                ? $"Block references in {fileName} (all layouts)"
                : $"Block references in {fileName} (layout: {string.Join(", ", targetLayouts)})";

            PrintTable(title, counts, settings);
            AnsiConsole.MarkupLine($"[grey]Unique blocks:[/] {counts.Count}    [grey]Total inserts:[/] {totalInserts}");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes | ExceptionFormats.ShortenMethods);
            return -1;
        }
    }

    private static int ExecuteModulesDxf(DxfDocument doc, CountSettings settings, string path, HashSet<string>? ignore)
    {
        // Select layouts
        IEnumerable<string> targetLayouts;
        if (settings.AllLayouts)
        {
            targetLayouts = doc.Layouts.Select(l => l.Name);
        }
        else if (!string.IsNullOrWhiteSpace(settings.Layout))
        {
            if (!doc.Layouts.Contains(settings.Layout))
            {
                AnsiConsole.MarkupLine($"[red]Layout not found:[/] {settings.Layout}");
                return -1;
            }
            targetLayouts = new[] { settings.Layout };
        }
        else
        {
            targetLayouts = new[] { netDxf.Objects.Layout.ModelSpaceName };
        }

        var fileName = System.IO.Path.GetFileName(path);
        var prefix = settings.ModulePrefix;
        var foundAny = false;
        var moduleTables = new List<ModuleTable>();

        foreach (var layoutName in targetLayouts)
        {
            var block = doc.Layouts[layoutName].AssociatedBlock;

            var modulePolys = block.Entities
                .OfType<DXFPolyline2D>()
                .Where(p => p.Layer != null && p.Layer.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (modulePolys.Count == 0)
            {
                continue;
            }

            foundAny = true;

            var inserts = block.Entities.OfType<Insert>().ToList();

            foreach (var poly in modulePolys)
            {
                var rect = GetRectFromPolylineDxf(poly);
                var moduleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                long total = 0;

                foreach (var ins in inserts)
                {
                    var p = ins.Position;
                    if (PointInRect(p.X, p.Y, rect.MinX, rect.MinY, rect.MaxX, rect.MaxY))
                    {
                        var blockName = ins.Block?.Name ?? "<Unnamed>";
                        if (ignore != null && ignore.Contains(blockName))
                        {
                            continue;
                        }
                        moduleCounts[blockName] = moduleCounts.TryGetValue(blockName, out var c) ? c + 1 : 1;
                        total++;
                    }
                }

                var moduleName = poly.Layer?.Name ?? "<no-layer>";
                moduleTables.Add(new ModuleTable(moduleName, moduleCounts));
                var title = "Blocks in " + fileName + " [layout: " + layoutName + "] module: " + moduleName;
                PrintTable(title, moduleCounts, settings);
                AnsiConsole.MarkupLine($"[grey]Unique blocks:[/] {moduleCounts.Count}    [grey]Total inserts:[/] {total}");
            }
        }

        if (!foundAny)
        {
            AnsiConsole.MarkupLine($"[yellow]No module polylines found with prefix '{prefix}'.[/]");
        }

        if (foundAny && !string.IsNullOrWhiteSpace(settings.ExportPath))
        {
            var exporter = new ExcelExport();
            exporter.Export(moduleTables, settings.ExportPath!);
            AnsiConsole.MarkupLine($"[green]Exported XLSX:[/] {settings.ExportPath}");
        }

        return 0;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) GetRectFromPolylineDxf(DXFPolyline2D poly)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var v in poly.Vertexes)
        {
            xs.Add(v.Position.X);
            ys.Add(v.Position.Y);
        }
        return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    private static bool PointInRect(double x, double y, double minX, double minY, double maxX, double maxY)
    {
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    private static void PrintTable(string title, IDictionary<string, int> counts, CountSettings settings)
    {
        if (counts.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No INSERT entities found.[/]");
            return;
        }

        var table = new Table().Title("[bold]" + Markup.Escape(title) + "[/]");
        table.AddColumn("Block Name");
        table.AddColumn(new TableColumn("Count").RightAligned());

        IEnumerable<KeyValuePair<string, int>> ordered = counts;
        if (settings.SortBy.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            ordered = counts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            ordered = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var kv in ordered)
        {
            table.AddRow(kv.Key, kv.Value.ToString());
        }

        AnsiConsole.Write(table);
    }
}