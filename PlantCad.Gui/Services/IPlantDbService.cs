using System.Collections.Generic;
using System.Threading.Tasks;

namespace PlantCad.Gui.Services;

public interface IPlantDbService
{
    bool IsOpen { get; }
    string? DatabasePath { get; }

    Task CreateNewAsync(string dbPath);
    Task OpenAsync(string dbPath);

    // Lookups (PlantType, Habit, Exposure, MoistureLevel, PhClass, SoilTrait, Color, Feature, FoliagePersistence, Packaging)
    Task<IList<PlantTypeDto>> GetPlantTypesAsync();
    Task<int> UpsertPlantTypeAsync(PlantTypeDto dto);
    Task DeletePlantTypeAsync(int id);

    Task<IList<HabitDto>> GetHabitsAsync();
    Task<int> UpsertHabitAsync(HabitDto dto);
    Task DeleteHabitAsync(int id);

    Task<IList<ExposureDto>> GetExposuresAsync();
    Task<int> UpsertExposureAsync(ExposureDto dto);
    Task DeleteExposureAsync(int id);

    Task<IList<MoistureLevelDto>> GetMoistureLevelsAsync();
    Task<int> UpsertMoistureLevelAsync(MoistureLevelDto dto);
    Task DeleteMoistureLevelAsync(int id);

    Task<IList<PhClassDto>> GetPhClassesAsync();
    Task<int> UpsertPhClassAsync(PhClassDto dto);
    Task DeletePhClassAsync(int id);

    Task<IList<SoilTraitDto>> GetSoilTraitsAsync();
    Task<int> UpsertSoilTraitAsync(SoilTraitDto dto);
    Task DeleteSoilTraitAsync(int id);

    Task<IList<ColorDto>> GetColorsAsync();
    Task<int> UpsertColorAsync(ColorDto dto);
    Task DeleteColorAsync(int id);

    Task<IList<FeatureDto>> GetFeaturesAsync();
    Task<int> UpsertFeatureAsync(FeatureDto dto);
    Task DeleteFeatureAsync(int id);

    Task<IList<FoliagePersistenceDto>> GetFoliagePersistencesAsync();
    Task<int> UpsertFoliagePersistenceAsync(FoliagePersistenceDto dto);
    Task DeleteFoliagePersistenceAsync(int id);

    Task<IList<PackagingDto>> GetPackagingsAsync();
    Task<int> UpsertPackagingAsync(PackagingDto dto);
    Task DeletePackagingAsync(int id);

    // Plants: listing, search, CRUD
    Task<IList<PlantListRowDto>> GetPlantsAsync(int page, int pageSize, string? search = null);
    Task<int> UpsertPlantBasicAsync(PlantListRowDto dto);
    Task DeletePlantAsync(int id);

    // Plants: many-to-many setters
    Task SetPlantExposuresAsync(int plantId, IReadOnlyList<int> exposureIds);
    Task SetPlantHabitsAsync(int plantId, IReadOnlyList<int> habitIds);
    Task SetPlantSoilTraitsAsync(int plantId, IReadOnlyList<int> soilTraitIds);
    Task SetPlantFeaturesAsync(int plantId, IReadOnlyList<int> featureIds);
    Task SetPlantColorsAsync(int plantId, string attribute, IReadOnlyList<int> colorIds);

    // Plants: many-to-many getters (for dialog pre-selection)
    Task<IList<int>> GetPlantExposuresAsync(int plantId);
    Task<IList<int>> GetPlantHabitsAsync(int plantId);
    Task<IList<int>> GetPlantSoilTraitsAsync(int plantId);
    Task<IList<int>> GetPlantFeaturesAsync(int plantId);
    Task<IList<int>> GetPlantColorsAsync(int plantId, string attribute);
}

public sealed record PlantTypeDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NamePl { get; init; } = string.Empty;
}

public sealed record HabitDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NamePl { get; init; } = string.Empty;
}

public sealed record ExposureDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NamePl { get; init; } = string.Empty;
}

public sealed record MoistureLevelDto
{
    public int Id { get; init; }
    public int Ordinal { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NamePl { get; init; } = string.Empty;
}

public sealed record PhClassDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NamePl { get; init; } = string.Empty;
    public double MinPh { get; init; }
    public double MaxPh { get; init; }
}

public sealed record SoilTraitDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NamePl { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}

public sealed record ColorDto
{
    public int Id { get; init; }
    public string CanonicalEn { get; init; } = string.Empty;
    public string NamePl { get; init; } = string.Empty;
    public string? Hex { get; init; }
}

public sealed record FeatureDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NamePl { get; init; } = string.Empty;
    public string GroupCode { get; init; } = string.Empty;
}

public sealed record FoliagePersistenceDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NamePl { get; init; } = string.Empty;
}

public sealed record PackagingDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NamePl { get; init; } = string.Empty;
}

public sealed record PlantListRowDto
{
    public int Id { get; init; }
    public string Genus { get; init; } = string.Empty;
    public string? Species { get; init; }
    public string? Cultivar { get; init; }
    public int? TypeId { get; init; }
    public string BotanicalNameDisplay { get; init; } = string.Empty;
    public double? PhMin { get; init; }
    public double? PhMax { get; init; }
    public int? MoistureMinLevelId { get; init; }
    public int? MoistureMaxLevelId { get; init; }
}
