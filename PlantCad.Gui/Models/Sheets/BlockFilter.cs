using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PlantCad.Gui.Models.Sheets;

public sealed class BlockFilter
{
    public HashSet<string> Includes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public Regex? IncludeRegex { get; set; }
    public Regex? ExcludeRegex { get; set; }
    public int? MinCount { get; set; }
    public int? MaxItemsInLegend { get; set; }
}
