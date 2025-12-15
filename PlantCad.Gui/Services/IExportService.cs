using System.Collections.Generic;

namespace PlantCad.Gui.Services;

public interface IExportService
{
    void ExportCountsToExcel(
        IEnumerable<KeyValuePair<string, int>> counts,
        string sheetName,
        string outputPath
    );
}
