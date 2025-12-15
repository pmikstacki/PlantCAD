namespace PlantCad.Core.Entities;

public sealed class Plant
{
    public int Id { get; set; }
    public string BotanicalGenus { get; set; } = string.Empty;
    public string? BotanicalSpecies { get; set; }
    public string? Cultivar { get; set; }
    public string BotanicalNameDisplay { get; set; } = string.Empty;
    public string? CommonNamePl { get; set; }
    public int TypeId { get; set; }

    public int? FloweringStartMonth { get; set; }
    public int? FloweringEndMonth { get; set; }

    public int? HardinessZone { get; set; }
    public string? HardinessSubzone { get; set; }

    public double? HeightMinM { get; set; }
    public double? HeightMaxM { get; set; }
    public double? WidthMinM { get; set; }
    public double? WidthMaxM { get; set; }
    public double? SpacingMinM { get; set; }
    public double? SpacingMaxM { get; set; }

    public int? FoliagePersistenceId { get; set; }

    public double? PhMin { get; set; }
    public double? PhMax { get; set; }
    public int? PhClassMinId { get; set; }
    public int? PhClassMaxId { get; set; }

    public int? MoistureMinLevel { get; set; }
    public int? MoistureMaxLevel { get; set; }

    public int? HabitPrimaryId { get; set; }

    public string? RawHeight { get; set; }
    public string? RawWidth { get; set; }
    public string? RawSpacing { get; set; }
    public string? RawPh { get; set; }
    public string? RawMoisture { get; set; }
    public string? RawExposure { get; set; }
    public string? RawSoil { get; set; }
    public string? RawFlowerProps { get; set; }
    public string? RawLeafProps { get; set; }
    public string? RawFruitColor { get; set; }
    public string? RawStock { get; set; }
}
