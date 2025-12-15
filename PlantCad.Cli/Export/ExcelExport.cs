using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace PlantCad.Cli.Export;

/// <summary>
/// Represents a table of counts for a single module to be exported to Excel.
/// </summary>
public sealed record ModuleTable(string ModuleName, IDictionary<string, int> Counts);

/// <summary>
/// Exports module count tables to an XLSX workbook using ClosedXML.
/// Each module goes to its own worksheet.
/// Columns: "Nazwa" (name) and "Ilość" (count).
/// </summary>
public sealed class ExcelExport
{
    public void Export(IEnumerable<ModuleTable> tables, string outputPath)
    {
        if (tables == null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        var tableList = tables.ToList();
        if (tableList.Count == 0)
        {
            throw new ArgumentException("No tables to export.", nameof(tables));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be empty.", nameof(outputPath));
        }

        // Ensure .xlsx extension
        if (string.IsNullOrWhiteSpace(Path.GetExtension(outputPath)))
        {
            outputPath += ".xlsx";
        }

        // Ensure target directory exists
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tableList)
        {
            var sheetName = MakeUniqueSheetName(SanitizeSheetName(table.ModuleName), usedNames);
            var ws = workbook.Worksheets.Add(sheetName);

            // Header
            ws.Cell(1, 1).Value = "Nazwa";
            ws.Cell(1, 2).Value = "ilość";
            ws.Range(1, 1, 1, 2).Style.Font.SetBold();

            // Rows
            var row = 2;
            foreach (var kv in table.Counts)
            {
                ws.Cell(row, 1).Value = kv.Key;
                ws.Cell(row, 2).Value = kv.Value;
                row++;
            }

            ws.Columns(1, 2).AdjustToContents();
            ws.SheetView.FreezeRows(1);
        }

        workbook.SaveAs(outputPath);
    }

    private static string SanitizeSheetName(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "Module" : name.Trim();
        // Remove invalid characters: : \\ / ? * [ ]
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        foreach (var ch in invalid)
        {
            n = n.Replace(ch.ToString(), string.Empty, StringComparison.Ordinal);
        }

        // Excel sheet name limit 31 chars
        if (n.Length > 31)
        {
            n = n[..31];
        }

        // Cannot be empty or only quotes
        if (string.IsNullOrWhiteSpace(n))
        {
            n = "Module";
        }

        return n;
    }

    private static string MakeUniqueSheetName(string baseName, ISet<string> used)
    {
        var name = baseName;
        var i = 1;
        while (used.Contains(name))
        {
            var suffix = $" ({++i})";
            var trimmed = baseName;
            var maxBase = 31 - suffix.Length;
            if (trimmed.Length > maxBase)
            {
                trimmed = trimmed[..maxBase];
            }
            name = trimmed + suffix;
        }
        used.Add(name);
        return name;
    }
}
