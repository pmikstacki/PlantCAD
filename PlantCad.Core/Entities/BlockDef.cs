namespace PlantCad.Core.Entities;

public sealed class BlockDef
{
    public int Id { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string? BlockHandle { get; set; }
    public string? VersionTag { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public double? WidthWorld { get; set; }
    public double? HeightWorld { get; set; }
    public string? CreatedUtc { get; set; }
    public string? UpdatedUtc { get; set; }
}
