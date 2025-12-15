using CsvHelper.Configuration.Attributes;

namespace PlantCad.Core.Import.CsvModels;

public sealed class PlantCsvRow
{
    [Name("Nazwa")]
    public string? Name { get; set; }

    [Name("Czas_kwitnienia_-_pocz.")]
    public string? FloweringStartRaw { get; set; }

    [Name("Gatunek_botaniczny")]
    public string? BotanicalName { get; set; }

    [Name("Gleba_pH")]
    public string? PhRaw { get; set; }

    [Name("Kolor_jesienią")]
    public string? AutumnColor { get; set; }

    [Name("Kolor_kwiatów")]
    public string? FlowerColor { get; set; }

    [Name("Kolor_owoców")]
    public string? FruitColor { get; set; }

    [Name("Kwiaty_-_właściwości")]
    public string? FlowerProps { get; set; }

    [Name("Liście_-_właściwości")]
    public string? LeafProps { get; set; }

    [Name("Odstęp_m._roślinami")]
    public string? SpacingRaw { get; set; }

    [Name("Pokrój")]
    public string? Habit { get; set; }

    [Name("Polska_nazwa")]
    public string? CommonNamePl { get; set; }

    [Name("Rodzaj_gleby")]
    public string? SoilKind { get; set; }

    [Name("Stanowisko")]
    public string? Exposure { get; set; }

    [Name("Strefa_roślinna")]
    public string? HardinessRaw { get; set; }

    [Name("Szerokość_rośliny_dorosłej")]
    public string? WidthRaw { get; set; }

    [Name("Wielkość_sadzonki")]
    public string? StockRaw { get; set; }

    [Name("Wilgotność")]
    public string? MoistureRaw { get; set; }

    [Name("Wysokość_rośliny_dorosłej")]
    public string? HeightRaw { get; set; }

    [Name("Typ")]
    public string? TypePl { get; set; }
}
