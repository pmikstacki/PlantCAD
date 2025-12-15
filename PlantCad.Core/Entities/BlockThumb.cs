namespace PlantCad.Core.Entities;

public sealed class BlockThumb
{
    public int BlockId { get; set; }
    public int SizePx { get; set; }
    public byte[] Png { get; set; } = System.Array.Empty<byte>();
    public string Background { get; set; } = "transparent";
    public string? UpdatedUtc { get; set; }
}
