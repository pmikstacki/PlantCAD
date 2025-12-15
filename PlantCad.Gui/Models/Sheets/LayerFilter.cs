using System;
using System.Collections.Generic;

namespace PlantCad.Gui.Models.Sheets;

public sealed class LayerFilter
{
    public bool UseCurrentModelVisibility { get; set; } = true;
    public HashSet<string> Includes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Excludes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool ApplyToThumbnails { get; set; } = false;
}
