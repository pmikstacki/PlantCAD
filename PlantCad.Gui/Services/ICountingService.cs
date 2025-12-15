using System.Collections.Generic;
using Avalonia;
using PlantCad.Gui.Models;
using PlantCad.Gui.Models.Sheets;

namespace PlantCad.Gui.Services;

public interface ICountingService
{
    (IDictionary<string, int> Counts, long Total) CountInsertsInRect(
        CadModel model,
        double minX,
        double minY,
        double maxX,
        double maxY
    );

    (IDictionary<string, int> Counts, long Total) CountInsertsInPolygon(
        CadModel model,
        IReadOnlyList<IReadOnlyList<Point>> polygons,
        LayerFilter? layerFilter = null
    );
}
