using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;

namespace PlantCad.Gui.Services.Internal;

public sealed class ExportService : IExportService
{
    public void ExportCountsToExcel(
        IEnumerable<KeyValuePair<string, int>> counts,
        string sheetName,
        string outputPath
    )
    {
        if (counts == null)
            throw new ArgumentNullException(nameof(counts));
        if (string.IsNullOrWhiteSpace(sheetName))
            sheetName = "Counts";
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path cannot be empty.", nameof(outputPath));

        // Ensure .xlsx extension
        if (string.IsNullOrWhiteSpace(Path.GetExtension(outputPath)))
        {
            outputPath += ".xlsx";
        }
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(SanitizeSheetName(sheetName));

        ws.Cell(1, 1).Value = "Name";
        ws.Cell(1, 2).Value = "Count";
        ws.Range(1, 1, 1, 2).Style.Font.Bold = true;

        var row = 2;
        foreach (var kv in counts)
        {
            ws.Cell(row, 1).Value = kv.Key;
            ws.Cell(row, 2).Value = kv.Value;
            row++;
        }
        ws.Columns(1, 2).AdjustToContents();
        ws.SheetView.FreezeRows(1);

        workbook.SaveAs(outputPath);
    }

    private static string SanitizeSheetName(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "Counts" : name.Trim();
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        foreach (var ch in invalid)
        {
            n = n.Replace(ch.ToString(), string.Empty, StringComparison.Ordinal);
        }
        if (n.Length > 31)
            n = n[..31];
        if (string.IsNullOrWhiteSpace(n))
            n = "Counts";
        return n;
    }
}
