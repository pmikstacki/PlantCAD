using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Models;
using PlantCad.Gui.Models.Modules;
using PlantCad.Gui.Models.Sheets;
using PlantCad.Gui.Services.Internal;

namespace PlantCad.Gui.Services;

public sealed class SheetGenerator : ISheetGenerator
{
    private readonly ILogger<SheetGenerator> _logger;

    public SheetGenerator(ILogger<SheetGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void MutateDocument(CadDocument doc, CadModel model, ModulesFile modules, SheetConfig config)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (modules == null) throw new ArgumentNullException(nameof(modules));
        if (modules.RootModules == null || modules.RootModules.Count == 0) return;
        _logger.LogInformation("Generating sheets for {Count} root modules", modules.RootModules.Count);
        foreach (var tuple in EnumerateModulesWithPath(modules.RootModules, null))
        {
            var m = tuple.M;
            var path = tuple.Path;
            if (!TryGetModuleExtents(m, out var x0, out var y0, out var x1, out var y1))
            {
                _logger.LogDebug("Skipping module '{Path}' due to missing shapes", path);
                continue;
            }

            var layoutName = BuildLayoutName(path);
            if (doc.Layouts.Any(l => l.Name.Equals(layoutName, StringComparison.OrdinalIgnoreCase)))
            {
                layoutName = UniqueLayoutName(doc, layoutName);
            }
            var layout = new Layout(layoutName);
            switch (config.PageSource)
            {
                case PageSource.Standard:
                    layout.PaperUnits = config.PaperUnits;
                    layout.PaperRotation = config.PaperRotation;
                    if (!string.IsNullOrWhiteSpace(config.PaperSizeToken))
                    {
                        layout.PaperSize = config.PaperSizeToken!;
                    }
                    break;
                case PageSource.Custom:
                    layout.PaperUnits = config.PaperUnits;
                    layout.PaperRotation = config.PaperRotation;
                    if (config.PaperWidthMm.HasValue) layout.PaperWidth = config.PaperWidthMm.Value;
                    if (config.PaperHeightMm.HasValue) layout.PaperHeight = config.PaperHeightMm.Value;
                    break;
                case PageSource.FromLayout:
                default:
                    // Keep defaults from ACadSharp
                    break;
            }
            // Map margins onto PlotSettings.UnprintableMargin for better preview/plot alignment
            layout.UnprintableMargin = new PaperMargin(
                config.MarginLeftMm,
                config.MarginBottomMm,
                config.MarginRightMm,
                config.MarginTopMm
            );
            doc.Layouts.Add(layout);
            if (!TryComputeViewportSize(layout, config, out var vpW, out var vpH, out var vpCenterX, out var vpCenterY))
            {
                _logger.LogWarning(
                    "Computed non-positive viewport size for layout '{Layout}'. PaperSize='{PaperSize}', PaperWidth={PaperWidth}, PaperHeight={PaperHeight}, Rotation={Rotation}, Margins(L={MarginLeft},R={MarginRight},T={MarginTop},B={MarginBottom}), Legend({LegendPlacement},{LegendSize}), ViewportW={ViewportW}, ViewportH={ViewportH}",
                    layoutName,
                    layout.PaperSize,
                    layout.PaperWidth,
                    layout.PaperHeight,
                    config.PaperRotation,
                    config.MarginLeftMm,
                    config.MarginRightMm,
                    config.MarginTopMm,
                    config.MarginBottomMm,
                    config.LegendPlacement,
                    config.LegendSizeMm,
                    vpW,
                    vpH
                );
                continue;
            }
            if (vpW <= 0 || vpH <= 0)
            {
                continue;
            }

            var vp = new ACadSharp.Entities.Viewport
            {
                Width = vpW,
                Height = vpH,
                Center = new CSMath.XYZ(vpCenterX, vpCenterY, 0),
            };

            var mx = (x0 + x1) / 2.0;
            var my = (y0 + y1) / 2.0;
            var mw = Math.Abs(x1 - x0);
            var mh = Math.Abs(y1 - y0);
            if (mw <= 0) mw = 1.0;
            if (mh <= 0) mh = 1.0;
            vp.ViewCenter = new CSMath.XY(mx, my);
            vp.ViewHeight = ComputeViewHeight(vpW, vpH, mw, mh, config);

            ApplyViewportLayerVisibility(vp, doc, model, config.Layers);
            layout.AssociatedBlock.Entities.Add(vp);
            _logger.LogDebug("Created layout '{Layout}' with viewport {W}x{H}", layout.Name, vpW, vpH);

            try
            {
                ComposeLegend(doc, layout, config, model, m, x0, y0, x1, y1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Legend composition failed for layout {Layout}", layout.Name);
                throw;
            }
        }
    }

    private static bool TryComputeViewportSize(Layout layout, SheetConfig config, out double vpW, out double vpH, out double centerX, out double centerY)
    {
        var mmLeft = config.MarginLeftMm;
        var mmRight = config.MarginRightMm;
        var mmTop = config.MarginTopMm;
        var mmBottom = config.MarginBottomMm;

        // Account for paper rotation: swap effective width/height for 90/270
        if (!TryGetPaperSizeMm(layout, config, out var effW, out var effH))
        {
            vpW = 0;
            vpH = 0;
            centerX = 0;
            centerY = 0;
            return false;
        }
        if (config.PaperRotation == PlotRotation.Degrees90 || config.PaperRotation == PlotRotation.Degrees270)
        {
            var tmp = effW; effW = effH; effH = tmp;
        }
        if (config.LegendPlacement == LegendPlacement.Right)
        {
            var legendWidth = config.LegendSizeMm;
            vpW = effW - mmLeft - mmRight - legendWidth;
            vpH = effH - mmTop - mmBottom;
        }
        else
        {
            var legendHeight = config.LegendSizeMm;
            vpW = effW - mmLeft - mmRight;
            vpH = effH - mmTop - mmBottom - legendHeight;
        }
        centerX = mmLeft + vpW / 2.0;
        centerY = mmBottom + vpH / 2.0;
        return vpW > 0 && vpH > 0;
    }

    private static bool TryGetPaperSizeMm(Layout layout, SheetConfig config, out double widthMm, out double heightMm)
    {
        widthMm = layout.PaperWidth;
        heightMm = layout.PaperHeight;
        if (widthMm > 0 && heightMm > 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(layout.PaperSize) && TryParsePaperSizeToken(layout.PaperSize, out var parsedW, out var parsedH))
        {
            widthMm = parsedW;
            heightMm = parsedH;
            return widthMm > 0 && heightMm > 0;
        }

        if (config.PaperWidthMm.HasValue && config.PaperHeightMm.HasValue)
        {
            widthMm = config.PaperWidthMm.Value;
            heightMm = config.PaperHeightMm.Value;
            return widthMm > 0 && heightMm > 0;
        }

        return false;
    }

    private static bool TryParsePaperSizeToken(string token, out double widthMm, out double heightMm)
    {
        widthMm = 0;
        heightMm = 0;

        var open = token.LastIndexOf('(');
        if (open < 0)
        {
            return false;
        }

        var close = token.LastIndexOf(')');
        if (close <= open)
        {
            return false;
        }

        var inside = token.Substring(open + 1, close - open - 1);
        var parts = inside.Split("_x_", StringSplitOptions.None);
        if (parts.Length != 2)
        {
            return false;
        }

        var wStr = parts[0];
        var hWithUnit = parts[1];
        var unitSep = hWithUnit.LastIndexOf('_');
        if (unitSep <= 0 || unitSep == hWithUnit.Length - 1)
        {
            return false;
        }

        var hStr = hWithUnit.Substring(0, unitSep);
        var unit = hWithUnit.Substring(unitSep + 1);

        if (!double.TryParse(wStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
        {
            return false;
        }

        if (!double.TryParse(hStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
        {
            return false;
        }

        if (unit.Equals("MM", StringComparison.OrdinalIgnoreCase))
        {
            widthMm = w;
            heightMm = h;
            return true;
        }

        if (unit.Equals("IN", StringComparison.OrdinalIgnoreCase) || unit.Equals("INCHES", StringComparison.OrdinalIgnoreCase))
        {
            widthMm = w * 25.4;
            heightMm = h * 25.4;
            return true;
        }

        return false;
    }

    private static double ComputeViewHeight(double vpW, double vpH, double mw, double mh, SheetConfig config)
    {
        if (config.FitToModule)
        {
            var vpAspect = vpW / vpH;
            var modAspect = mw / mh;
            var vh = modAspect > vpAspect ? mw / vpAspect : mh;
            return vh * 1.05;
        }
        if (config.FixedScaleDenominator.HasValue && config.FixedScaleDenominator.Value > 0)
        {
            var scale = config.FixedScaleNumerator / config.FixedScaleDenominator.Value;
            return vpH / scale;
        }
        return mh * 1.05;
    }

    private void ComposeLegend(
        CadDocument doc,
        Layout layout,
        SheetConfig config,
        CadModel model,
        Module module,
        double x0,
        double y0,
        double x1,
        double y1
    )
    {
        if (!TryComputeLegendRect(layout, config, out var lx, out var ly, out var lw, out var lh))
        {
            _logger.LogDebug("Legend area computed as empty, skipping legend");
            return;
        }

        var counter = ServiceRegistry.CountingService;
        if (counter == null)
        {
            _logger.LogWarning("CountingService is not available; legend will be empty");
            return;
        }

        var polygons = new List<IReadOnlyList<Avalonia.Point>>();
        if (module?.Shapes != null)
        {
            foreach (var shape in module.Shapes)
            {
                if (shape?.Points == null || shape.Points.Count < 3)
                {
                    continue;
                }

                var pts = new List<Avalonia.Point>(shape.Points.Count);
                foreach (var p in shape.Points)
                {
                    pts.Add(new Avalonia.Point(p.X, p.Y));
                }
                if (pts.Count >= 3)
                {
                    polygons.Add(pts);
                }
            }
        }

        (IDictionary<string, int> Counts, long Total) result = polygons.Count > 0
            ? counter.CountInsertsInPolygon(model, polygons, config.Layers)
            : counter.CountInsertsInRect(model, Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));

        var (counts, total) = result;
        var items = FilterLegendItems(counts, config.Blocks).ToList();
        if (items.Count == 0)
        {
            _logger.LogDebug("No legend items after filtering");
            return;
        }

        // Draw legend frame
        DrawRect(layout, lx, ly, lw, lh);

        // Grid layout based on rows/cols if provided, else auto rows to fit height
        const double rowHeight = 8.0; // mm
        const double textHeight = 3.5; // mm
        const double margin = 3.0; // mm inside legend area
        var innerW = Math.Max(0, lw - 2 * margin);
        var innerH = Math.Max(0, lh - 2 * margin);
        var rows = config.LegendRows ?? Math.Max(1, (int)Math.Floor(innerH / rowHeight));
        var cols = config.LegendColumns ?? 1;
        if (rows <= 0) rows = 1;
        if (cols <= 0) cols = 1;
        var perPage = rows * cols;
        var colWidth = innerW / cols;
        var xLeft0 = lx + margin;
        var yTop0 = ly + lh - margin;

        var targetDwgPath = DwgPersistService.CurrentTargetDwgPath;
        if (string.IsNullOrWhiteSpace(targetDwgPath))
        {
            _logger.LogDebug("Target DWG path not available; skipping thumbnails");
        }
        else if (!Dispatcher.UIThread.CheckAccess())
        {
            _logger.LogDebug("Not on UI thread; skipping thumbnails");
            targetDwgPath = null;
        }
        else
        {
            doc.UpdateCollections(true);
        }

        var thumbnailRenderer = new BlockThumbnailRenderer();
        var rendered = 0;
        foreach (var (name, count) in items)
        {
            if (rendered >= perPage) break;
            var col = rendered / rows;
            var row = rendered % rows;
            var xLeft = xLeft0 + col * colWidth;
            var cellTop = yTop0 - row * rowHeight;
            var cellBottom = cellTop - rowHeight;

            var thumbWorldSize = Math.Min(rowHeight - 1.0, colWidth * 0.45);
            if (thumbWorldSize < 2.0)
            {
                thumbWorldSize = 2.0;
            }

            if (!string.IsNullOrWhiteSpace(targetDwgPath) && doc.ImageDefinitions != null)
            {
                TryAddLegendThumbnail(
                    doc,
                    layout,
                    thumbnailRenderer,
                    targetDwgPath!,
                    name,
                    config.ThumbnailSizePx,
                    xLeft,
                    cellBottom + (rowHeight - thumbWorldSize) / 2.0,
                    thumbWorldSize,
                    thumbWorldSize
                );
            }

            var textX = xLeft + thumbWorldSize + 2.0;
            var textY = cellBottom + (rowHeight - textHeight) / 2.0;

            var text = new TextEntity
            {
                Value = $"{name} — {count}",
                Height = textHeight,
                InsertPoint = new CSMath.XYZ(textX, textY, 0),
            };
            layout.AssociatedBlock.Entities.Add(text);
            rendered++;
        }
    }

    private void TryAddLegendThumbnail(
        CadDocument doc,
        Layout layout,
        BlockThumbnailRenderer renderer,
        string targetDwgPath,
        string blockName,
        int sizePx,
        double x,
        double y,
        double w,
        double h
    )
    {
        if (doc.ImageDefinitions == null)
        {
            return;
        }
        if (layout?.AssociatedBlock == null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(blockName))
        {
            return;
        }
        if (sizePx <= 0)
        {
            return;
        }
        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (!doc.BlockRecords.TryGetValue(blockName, out var blockRecord))
        {
            return;
        }

        var targetDir = Path.GetDirectoryName(targetDwgPath);
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            return;
        }

        var sidecarDir = Path.Combine(targetDir!, Path.GetFileNameWithoutExtension(targetDwgPath) + "-thumbnails");
        Directory.CreateDirectory(sidecarDir);

        var safeName = SanitizeFileName(blockName);
        var fileName = $"{safeName}-{sizePx}.png";
        var fullPngPath = Path.Combine(sidecarDir, fileName);

        if (!File.Exists(fullPngPath))
        {
            var png = renderer.RenderBlockToPng(doc, blockRecord, sizePx, background: "transparent");
            File.WriteAllBytes(fullPngPath, png);
        }

        var relativePath = Path.GetRelativePath(targetDir!, fullPngPath);

        var defName = $"IMG_{safeName}_{sizePx}";
        if (!doc.ImageDefinitions.TryGetValue(defName, out var imageDef))
        {
            imageDef = new ImageDefinition
            {
                Name = defName,
                FileName = relativePath,
                Size = new CSMath.XY(sizePx, sizePx),
                DefaultSize = new CSMath.XY(1, 1),
                Units = ResolutionUnit.None,
                IsLoaded = true,
            };
            doc.ImageDefinitions.Add(imageDef);
        }
        else
        {
            imageDef.FileName = relativePath;
        }

        var u = w / sizePx;
        var v = h / sizePx;

        var img = new RasterImage(imageDef)
        {
            InsertPoint = new CSMath.XYZ(x, y, 0),
            UVector = new CSMath.XYZ(u, 0, 0),
            VVector = new CSMath.XYZ(0, v, 0),
            Size = new CSMath.XY(sizePx, sizePx),
            Flags = ImageDisplayFlags.ShowImage | ImageDisplayFlags.ShowNotAlignedImage | ImageDisplayFlags.UseClippingBoundary,
            ClippingState = true,
            ClipType = ClipType.Rectangular,
            ClipBoundaryVertices = new List<CSMath.XY>
            {
                new CSMath.XY(-0.5, -0.5),
                new CSMath.XY(sizePx - 0.5, sizePx - 0.5),
            },
        };

        layout.AssociatedBlock.Entities.Add(img);
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private static bool TryComputeLegendRect(Layout layout, SheetConfig config, out double x, out double y, out double w, out double h)
    {
        var mmLeft = config.MarginLeftMm;
        var mmRight = config.MarginRightMm;
        var mmTop = config.MarginTopMm;
        var mmBottom = config.MarginBottomMm;
        var effW = layout.PaperWidth;
        var effH = layout.PaperHeight;
        if (config.PaperRotation == PlotRotation.Degrees90 || config.PaperRotation == PlotRotation.Degrees270)
        {
            var tmp = effW; effW = effH; effH = tmp;
        }
        if (config.LegendPlacement == LegendPlacement.Right)
        {
            var legendWidth = config.LegendSizeMm;
            x = effW - mmRight - legendWidth;
            y = mmBottom;
            w = legendWidth;
            h = effH - mmTop - mmBottom;
        }
        else
        {
            var legendHeight = config.LegendSizeMm;
            x = mmLeft;
            y = mmBottom;
            w = effW - mmLeft - mmRight;
            h = legendHeight;
        }
        return w > 0 && h > 0;
    }

    private static IEnumerable<(string Name, int Count)> FilterLegendItems(IDictionary<string, int> counts, BlockFilter filter)
    {
        IEnumerable<KeyValuePair<string, int>> items = counts;
        if (filter.Includes.Count > 0)
        {
            items = items.Where(kv => filter.Includes.Contains(kv.Key));
        }
        if (filter.IncludeRegex != null)
        {
            items = items.Where(kv => filter.IncludeRegex!.IsMatch(kv.Key));
        }
        if (filter.ExcludeRegex != null)
        {
            items = items.Where(kv => !filter.ExcludeRegex!.IsMatch(kv.Key));
        }
        if (filter.MinCount.HasValue && filter.MinCount.Value > 0)
        {
            items = items.Where(kv => kv.Value >= filter.MinCount.Value);
        }
        var projected = items
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (kv.Key, kv.Value));
        if (filter.MaxItemsInLegend.HasValue && filter.MaxItemsInLegend.Value > 0)
        {
            projected = projected.Take(filter.MaxItemsInLegend.Value);
        }
        return projected;
    }

    private static void DrawRect(Layout layout, double x, double y, double w, double h)
    {
        if (layout?.AssociatedBlock == null) return;
        if (w <= 0 || h <= 0) return;
        var poly = new ACadSharp.Entities.LwPolyline
        {
            IsClosed = true,
        };
        poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex { Location = new CSMath.XY(x, y) });
        poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex { Location = new CSMath.XY(x + w, y) });
        poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex { Location = new CSMath.XY(x + w, y + h) });
        poly.Vertices.Add(new ACadSharp.Entities.LwPolyline.Vertex { Location = new CSMath.XY(x, y + h) });
        layout.AssociatedBlock.Entities.Add(poly);
    }

    private static void ApplyViewportLayerVisibility(ACadSharp.Entities.Viewport vp, CadDocument cd, CadModel? model, LayerFilter layers)
    {
        if (vp == null || cd == null) return;
        var allLayers = cd.Layers.ToDictionary(l => l.Name, StringComparer.OrdinalIgnoreCase);
        var toFreeze = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (layers.UseCurrentModelVisibility && model != null)
        {
            foreach (var ml in model.Layers)
            {
                var visible = ml.IsOn && !ml.IsFrozen;
                if (!visible)
                {
                    toFreeze.Add(ml.Name);
                }
            }
        }
        if (layers.Includes.Count > 0)
        {
            foreach (var name in allLayers.Keys)
            {
                if (!layers.Includes.Contains(name)) toFreeze.Add(name);
            }
        }
        foreach (var ex in layers.Excludes)
        {
            toFreeze.Add(ex);
        }
        foreach (var name in toFreeze)
        {
            if (allLayers.TryGetValue(name, out var layer))
            {
                if (!vp.FrozenLayers.Contains(layer))
                {
                    vp.FrozenLayers.Add(layer);
                }
            }
        }
    }

    private static IEnumerable<(Module M, string Path)> EnumerateModulesWithPath(IEnumerable<Module> roots, string? parentPath)
    {
        foreach (var r in roots)
        {
            var n = string.IsNullOrWhiteSpace(r.Name) ? "Module" : r.Name;
            var path = string.IsNullOrEmpty(parentPath) ? n : parentPath + "/" + n;
            yield return (r, path);
            if (r.Children != null && r.Children.Count > 0)
            {
                foreach (var c in EnumerateModulesWithPath(r.Children, path))
                {
                    yield return c;
                }
            }
        }
    }

    private static bool TryGetModuleExtents(Module m, out double x0, out double y0, out double x1, out double y1)
    {
        x0 = y0 = double.PositiveInfinity;
        x1 = y1 = double.NegativeInfinity;
        var any = false;
        if (m.Shapes != null)
        {
            foreach (var s in m.Shapes)
            {
                if (s.Points == null) continue;
                foreach (var p in s.Points)
                {
                    any = true;
                    if (p.X < x0) x0 = p.X;
                    if (p.Y < y0) y0 = p.Y;
                    if (p.X > x1) x1 = p.X;
                    if (p.Y > y1) y1 = p.Y;
                }
            }
        }
        if (!any)
        {
            x0 = y0 = x1 = y1 = 0;
            return false;
        }
        return true;
    }

    private static string BuildLayoutName(string path)
    {
        var raw = path.Replace(' ', '_');
        foreach (var ch in Path.GetInvalidFileNameChars()) raw = raw.Replace(ch.ToString(), "");
        if (raw.Length > 60) raw = raw[..60];
        return "Sheet_" + raw;
    }

    private static string UniqueLayoutName(CadDocument cd, string baseName)
    {
        var i = 2;
        var name = baseName + "_" + i;
        while (cd.Layouts.Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            i++;
            name = baseName + "_" + i;
        }
        return name;
    }
}
