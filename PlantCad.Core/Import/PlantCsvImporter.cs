using System.Data;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Dapper;
using PlantCad.Core.Data;
using PlantCad.Core.Entities;
using PlantCad.Core.Repositories;
using PlantCad.Core.Import.CsvModels;

namespace PlantCad.Core.Import;

public sealed class PlantCsvImporter
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly ILookupRepository _lookups;
    private readonly IPlantRepository _plants;

    public PlantCsvImporter(ISqliteConnectionFactory factory, ILookupRepository lookups, IPlantRepository plants)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _lookups = lookups ?? throw new ArgumentNullException(nameof(lookups));
        _plants = plants ?? throw new ArgumentNullException(nameof(plants));
    }

    public int Import(string csvPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
            throw new ArgumentException("CSV path must not be empty", nameof(csvPath));
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("CSV file not found", csvPath);

        var config = new CsvConfiguration(CultureInfo.GetCultureInfo("pl-PL"))
        {
            Delimiter = ";",
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
            DetectColumnCountChanges = false
        };

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        var imported = 0;
        using (var reader = new StreamReader(csvPath))
        using (var csv = new CsvReader(reader, config))
        {
            var records = csv.GetRecords<PlantCsvRow>();
            foreach (var row in records)
            {
                ct.ThrowIfCancellationRequested();
                if (row is null) continue;

                // Parse taxonomy
                var (genus, species, cultivar, display) = Parsing.ParseBotanical(row.BotanicalName, row.Name);
                if (string.IsNullOrWhiteSpace(genus))
                    continue; // skip invalid rows

                // Type mapping (default tree)
                var (typeCode, typeNamePl) = MapType(row.TypePl);
                var typeId = _lookups.GetOrCreatePlantTypeId(conn, tx, typeCode, typeNamePl);

                // Flowering months
                var (flowStart, flowEnd) = Parsing.ParseFloweringRange(row.FloweringStartRaw);

                // Hardiness
                var (zone, subzone) = Parsing.ParseHardiness(row.HardinessRaw);

                // Dimensions
                var (hMin, hMax) = Parsing.ParseRangeMetersOrSingle(row.HeightRaw);
                var (wMin, wMax) = Parsing.ParseRangeMetersOrSingle(row.WidthRaw);
                var (sMin, sMax) = Parsing.ParseSpacingMeters(row.SpacingRaw);

                // pH
                var (phMin, phMax, phClassMin, phClassMax) = Parsing.ParsePh(row.PhRaw);
                int? phClassMinId = null, phClassMaxId = null;
                if (phClassMin is not null && phClassMax is not null)
                {
                    var (minPh, maxPh) = MapPhBounds(phClassMin, phClassMax);
                    phClassMinId = _lookups.GetOrCreatePhClassId(conn, tx, phClassMin, MapPhNamePl(phClassMin), minPh, MapPhUpper(phClassMin));
                    phClassMaxId = _lookups.GetOrCreatePhClassId(conn, tx, phClassMax, MapPhNamePl(phClassMax), MapPhLower(phClassMax), maxPh);
                }

                // Moisture
                var (_, _, moistMinOrd, moistMaxOrd) = Parsing.ParseMoisture(row.MoistureRaw);

                // Foliage persistence derived
                int? foliagePersistenceId = null;
                var leafNorm = PolishTermMaps.Normalize(row.LeafProps ?? string.Empty);
                if (leafNorm.Contains("sezonowe"))
                    foliagePersistenceId = _lookups.GetOrCreateFoliagePersistenceId(conn, tx, "deciduous", "Sezonowe");
                else if (leafNorm.Contains("zimozielone"))
                    foliagePersistenceId = _lookups.GetOrCreateFoliagePersistenceId(conn, tx, "evergreen", "Zimozielone");

                // Habit
                int? habitPrimaryId = null;
                var habitTokens = Parsing.SplitTokens(row.Habit).ToList();
                foreach (var ht in habitTokens)
                {
                    var code = MapHabitCode(ht);
                    if (code is null) continue;
                    var habitId = _lookups.GetOrCreateHabitId(conn, tx, code, MapHabitNamePl(code));
                    if (habitPrimaryId is null)
                        habitPrimaryId = habitId;
                }

                var plant = new Plant
                {
                    BotanicalGenus = genus,
                    BotanicalSpecies = species,
                    Cultivar = cultivar,
                    BotanicalNameDisplay = display,
                    CommonNamePl = row.CommonNamePl,
                    TypeId = typeId,

                    FloweringStartMonth = flowStart,
                    FloweringEndMonth = flowEnd,

                    HardinessZone = zone,
                    HardinessSubzone = subzone,

                    HeightMinM = hMin,
                    HeightMaxM = hMax,
                    WidthMinM = wMin,
                    WidthMaxM = wMax,
                    SpacingMinM = sMin,
                    SpacingMaxM = sMax,

                    FoliagePersistenceId = foliagePersistenceId,

                    PhMin = phMin,
                    PhMax = phMax,
                    PhClassMinId = phClassMinId,
                    PhClassMaxId = phClassMaxId,

                    MoistureMinLevel = moistMinOrd,
                    MoistureMaxLevel = moistMaxOrd,

                    HabitPrimaryId = habitPrimaryId,

                    RawHeight = row.HeightRaw,
                    RawWidth = row.WidthRaw,
                    RawSpacing = row.SpacingRaw,
                    RawPh = row.PhRaw,
                    RawMoisture = row.MoistureRaw,
                    RawExposure = row.Exposure,
                    RawSoil = row.SoilKind,
                    RawFlowerProps = row.FlowerProps,
                    RawLeafProps = row.LeafProps,
                    RawFruitColor = row.FruitColor,
                    RawStock = row.StockRaw,
                };

                var existingId = _plants.FindPlantId(conn, tx, plant.BotanicalGenus, plant.BotanicalSpecies, plant.Cultivar);
                if (existingId.HasValue)
                {
                    // Skip duplicates for initial import; future improvements could update
                    continue;
                }

                var plantId = _plants.InsertPlant(conn, tx, plant);

                // Secondary habit associations (including primary)
                foreach (var ht in habitTokens)
                {
                    var code = MapHabitCode(ht);
                    if (code is null) continue;
                    var habitId = _lookups.GetOrCreateHabitId(conn, tx, code, MapHabitNamePl(code));
                    _plants.AddPlantHabit(conn, tx, plantId, habitId, isPrimary: habitId == habitPrimaryId);
                }

                // Exposure
                foreach (var tok in Parsing.SplitTokens(row.Exposure))
                {
                    var key = PolishTermMaps.Normalize(tok);
                    if (PolishTermMaps.Exposure.TryGetValue(key, out var expCode))
                    {
                        var expId = _lookups.GetOrCreateExposureId(conn, tx, expCode, MapExposureNamePl(expCode));
                        _plants.AddPlantExposure(conn, tx, plantId, expId);
                    }
                }

                // Soil traits
                foreach (var tok in Parsing.SplitTokens(row.SoilKind))
                {
                    var key = PolishTermMaps.Normalize(tok);
                    if (PolishTermMaps.SoilTraits.TryGetValue(key, out var st))
                    {
                        var stId = _lookups.GetOrCreateSoilTraitId(conn, tx, st.code, st.namePl, st.category);
                        _plants.AddPlantSoilTrait(conn, tx, plantId, stId);
                    }
                }

                // Colors
                InsertColors(conn, tx, plantId, "autumn_foliage", row.AutumnColor);
                InsertColors(conn, tx, plantId, "flower", row.FlowerColor);
                InsertColors(conn, tx, plantId, "fruit", row.FruitColor);

                // Features: flower props
                foreach (var tok in Parsing.SplitTokens(row.FlowerProps))
                {
                    var key = PolishTermMaps.Normalize(tok);
                    if (PolishTermMaps.Features.TryGetValue(key, out var f))
                    {
                        var fid = _lookups.GetOrCreateFeatureId(conn, tx, f.code, f.namePl, f.group);
                        _plants.AddPlantFeature(conn, tx, plantId, fid);
                    }
                }
                // Features: leaf props
                foreach (var tok in Parsing.SplitTokens(row.LeafProps))
                {
                    var key = PolishTermMaps.Normalize(tok);
                    if (PolishTermMaps.Features.TryGetValue(key, out var f))
                    {
                        var fid = _lookups.GetOrCreateFeatureId(conn, tx, f.code, f.namePl, f.group);
                        _plants.AddPlantFeature(conn, tx, plantId, fid);
                    }
                }

                imported++;
            }
        }

        tx.Commit();
        return imported;
    }

    private void InsertColors(IDbConnection conn, IDbTransaction tx, int plantId, string attribute, string? raw)
    {
        var i = 0;
        foreach (var tok in Parsing.SplitTokens(raw))
        {
            var key = PolishTermMaps.Normalize(tok);
            var canonical = PolishTermMaps.ColorCanonical.TryGetValue(key, out var c) ? c : key;
            var colorId = _lookups.GetOrCreateColorId(conn, tx, canonical, tok);
            _plants.AddPlantColor(conn, tx, plantId, attribute, colorId, i++);
        }
    }

    private static (string code, string namePl) MapType(string? typePl)
    {
        var n = PolishTermMaps.Normalize(typePl ?? string.Empty);
        if (n.Contains("drzewo")) return ("tree", "Drzewo");
        // future: shrub, perennial, etc.
        return ("tree", "Drzewo");
    }

    private static string? MapHabitCode(string token)
    {
        var key = PolishTermMaps.Normalize(token);
        if (PolishTermMaps.Habit.TryGetValue(key, out var code)) return code;
        return null;
    }

    private static string MapHabitNamePl(string code) => code switch
    {
        "columnar" => "Kolumnowy",
        "narrow_columnar" => "Wąskokolumnowy",
        "weeping" => "Płaczący",
        "conical" => "Stożkowaty",
        "broad" => "Szeroki, rozłożysty",
        "oval" => "Owalny",
        "umbrella" => "Parasolowaty",
        "irregular" => "Nieregularny",
        "loose" => "Luźny",
        "multi_stem" => "Wielopniowy",
        _ => tokenToTitle(code)
    };

    private static string MapExposureNamePl(string code) => code switch
    {
        "full_sun" => "Słoneczne",
        "partial_shade" => "Półcień",
        "shade" => "Cień",
        _ => tokenToTitle(code)
    };

    private static (double minPh, double maxPh) MapPhBounds(string minCode, string maxCode)
    {
        return (MapPhLower(minCode), MapPhUpper(maxCode));
    }

    private static double MapPhLower(string code) => code switch
    {
        "acidic" => 5.0,
        "neutral" => 6.5,
        "alkaline" => 7.5,
        _ => 6.5
    };

    private static double MapPhUpper(string code) => code switch
    {
        "acidic" => 6.5,
        "neutral" => 7.5,
        "alkaline" => 8.5,
        _ => 7.5
    };

    private static string MapPhNamePl(string code) => code switch
    {
        "acidic" => "Kwaśny",
        "neutral" => "Obojętny",
        "alkaline" => "Zasadowy",
        _ => tokenToTitle(code)
    };

    private static string tokenToTitle(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Replace('_', ' '));
    }
}
