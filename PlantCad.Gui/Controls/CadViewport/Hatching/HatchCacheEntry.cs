using System;

namespace PlantCad.Gui.Controls.CadViewport.Hatching
{
    public sealed class HatchCacheEntry
    {
        public string Key { get; set; } = string.Empty;
        public double AbsSpacing { get; set; }
        public double DashPeriod { get; set; }
        public string DashKey { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string TileRect { get; set; } = string.Empty;
        public int Hits { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
