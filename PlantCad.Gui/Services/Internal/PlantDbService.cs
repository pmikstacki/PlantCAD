using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using PlantCad.Core.Data;
using PlantCad.Core.Repositories;

namespace PlantCad.Gui.Services.Internal;

public sealed class PlantDbService : IPlantDbService
{
    private readonly ILogger<PlantDbService> _logger;
    private SqliteConnectionFactory? _factory;

    public bool IsOpen => _factory is not null;
    public string? DatabasePath { get; private set; }

    public PlantDbService(ILogger<PlantDbService> logger)
    {
        _logger = logger;
    }

    public async Task CreateNewAsync(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("Database path is required.");
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        DatabasePath = dbPath;
        _factory = new SqliteConnectionFactory(dbPath);
        await Task.Run(() => new MigrationRunner(_factory).ApplyPendingMigrations());
        _logger.LogInformation("Created and migrated DB at {Path}", dbPath);
    }

    public async Task OpenAsync(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("Database path is required.");
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Database file not found", dbPath);
        DatabasePath = dbPath;
        _factory = new SqliteConnectionFactory(dbPath);
        await Task.Run(() => new MigrationRunner(_factory).ApplyPendingMigrations());
        _logger.LogInformation("Opened DB at {Path}", dbPath);
    }

    private IDbConnection EnsureConn()
    {
        if (_factory is null)
            throw new InvalidOperationException("Database is not open.");
        return _factory.Create();
    }

    // Lookups: PlantType
    public async Task<IList<PlantTypeDto>> GetPlantTypesAsync()
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<(int id, string code, string name_pl)>(
            "SELECT id, code, name_pl FROM PlantType ORDER BY code ASC;"
        );
        var list = new List<PlantTypeDto>();
        foreach (var r in rows)
            list.Add(
                new PlantTypeDto
                {
                    Id = r.id,
                    Code = r.code,
                    NamePl = r.name_pl,
                }
            );
        return list;
    }

    public async Task<int> UpsertPlantTypeAsync(PlantTypeDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE PlantType SET code=@Code, name_pl=@NamePl WHERE id=@Id;",
                dto
            );
            return dto.Id;
        }
        using var tx = conn.BeginTransaction();
        var repo = new LookupRepository();
        var id = repo.GetOrCreatePlantTypeId(conn, tx, dto.Code, dto.NamePl);
        tx.Commit();
        return id;
    }

    public async Task DeletePlantTypeAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM PlantType WHERE id=@id;", new { id });
    }

    // Lookups: Habit
    public async Task<IList<HabitDto>> GetHabitsAsync()
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<(int id, string code, string name_pl)>(
            "SELECT id, code, name_pl FROM Habit ORDER BY code ASC;"
        );
        var list = new List<HabitDto>();
        foreach (var r in rows)
            list.Add(
                new HabitDto
                {
                    Id = r.id,
                    Code = r.code,
                    NamePl = r.name_pl,
                }
            );
        return list;
    }

    public async Task<int> UpsertHabitAsync(HabitDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE Habit SET code=@Code, name_pl=@NamePl WHERE id=@Id;",
                dto
            );
            return dto.Id;
        }
        using var tx = conn.BeginTransaction();
        var repo = new LookupRepository();
        var id = repo.GetOrCreateHabitId(conn, tx, dto.Code, dto.NamePl);
        tx.Commit();
        return id;
    }

    public async Task DeleteHabitAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM Habit WHERE id=@id;", new { id });
    }

    // Plants listing
    public async Task<IList<PlantListRowDto>> GetPlantsAsync(
        int page,
        int pageSize,
        string? search = null
    )
    {
        if (page < 1)
            page = 1;
        if (pageSize <= 0)
            pageSize = 100;
        var offset = (page - 1) * pageSize;
        using var conn = EnsureConn();
        var where = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : "WHERE botanical_genus LIKE @q OR botanical_species LIKE @q OR IFNULL(cultivar,'') LIKE @q OR botanical_name_display LIKE @q";
        var sql =
            $"SELECT id, botanical_genus AS genus, botanical_species AS species, cultivar, type_id, botanical_name_display AS display, ph_min, ph_max, moisture_min_level, moisture_max_level FROM Plant {where} ORDER BY botanical_genus, botanical_species, cultivar LIMIT @limit OFFSET @offset;";
        var q = string.IsNullOrWhiteSpace(search) ? null : $"%{search}%";
        var rows = await conn.QueryAsync<(
            int id,
            string genus,
            string? species,
            string? cultivar,
            int? type_id,
            string display,
            double? ph_min,
            double? ph_max,
            int? moisture_min_level,
            int? moisture_max_level
        )>(
            sql,
            new
            {
                limit = pageSize,
                offset,
                q,
            }
        );
        var list = new List<PlantListRowDto>();
        foreach (var r in rows)
        {
            list.Add(
                new PlantListRowDto
                {
                    Id = r.id,
                    Genus = r.genus,
                    Species = r.species,
                    Cultivar = r.cultivar,
                    TypeId = r.type_id,
                    BotanicalNameDisplay = r.display,
                    PhMin = r.ph_min,
                    PhMax = r.ph_max,
                    MoistureMinLevelId = r.moisture_min_level,
                    MoistureMaxLevelId = r.moisture_max_level,
                }
            );
        }
        return list;
    }

    // Lookups: Exposure
    public async Task<IList<ExposureDto>> GetExposuresAsync()
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<(int id, string code, string name_pl)>(
            "SELECT id, code, name_pl FROM Exposure ORDER BY code;"
        );
        var list = new List<ExposureDto>();
        foreach (var r in rows)
            list.Add(
                new ExposureDto
                {
                    Id = r.id,
                    Code = r.code,
                    NamePl = r.name_pl,
                }
            );
        return list;
    }

    public async Task<int> UpsertExposureAsync(ExposureDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE Exposure SET code=@Code, name_pl=@NamePl WHERE id=@Id;",
                dto
            );
            return dto.Id;
        }
        using var tx = conn.BeginTransaction();
        var repo = new LookupRepository();
        var id = repo.GetOrCreateExposureId(conn, tx, dto.Code, dto.NamePl);
        tx.Commit();
        return id;
    }

    public async Task DeleteExposureAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM Exposure WHERE id=@id;", new { id });
    }

    // Lookups: MoistureLevel
    public async Task<IList<MoistureLevelDto>> GetMoistureLevelsAsync()
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<(int id, int ordinal, string code, string name_pl)>(
            "SELECT id, ordinal, code, name_pl FROM MoistureLevel ORDER BY ordinal;"
        );
        var list = new List<MoistureLevelDto>();
        foreach (var r in rows)
            list.Add(
                new MoistureLevelDto
                {
                    Id = r.id,
                    Ordinal = r.ordinal,
                    Code = r.code,
                    NamePl = r.name_pl,
                }
            );
        return list;
    }

    public async Task<int> UpsertMoistureLevelAsync(MoistureLevelDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE MoistureLevel SET ordinal=@Ordinal, code=@Code, name_pl=@NamePl WHERE id=@Id;",
                dto
            );
            return dto.Id;
        }
        using var tx = conn.BeginTransaction();
        var repo = new LookupRepository();
        var id = repo.GetOrCreateMoistureLevelId(conn, tx, dto.Ordinal, dto.Code, dto.NamePl);
        tx.Commit();
        return id;
    }

    public async Task DeleteMoistureLevelAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM MoistureLevel WHERE id=@id;", new { id });
    }

    // Lookups: PhClass
    public async Task<IList<PhClassDto>> GetPhClassesAsync()
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<(
            int id,
            string code,
            string name_pl,
            double min_ph,
            double max_ph
        )>("SELECT id, code, name_pl, min_ph, max_ph FROM PhClass ORDER BY min_ph;");
        var list = new List<PhClassDto>();
        foreach (var r in rows)
            list.Add(
                new PhClassDto
                {
                    Id = r.id,
                    Code = r.code,
                    NamePl = r.name_pl,
                    MinPh = r.min_ph,
                    MaxPh = r.max_ph,
                }
            );
        return list;
    }

    public async Task<int> UpsertPhClassAsync(PhClassDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE PhClass SET code=@Code, name_pl=@NamePl, min_ph=@MinPh, max_ph=@MaxPh WHERE id=@Id;",
                dto
            );
            return dto.Id;
        }
        using var tx = conn.BeginTransaction();
        var repo = new LookupRepository();
        var id = repo.GetOrCreatePhClassId(conn, tx, dto.Code, dto.NamePl, dto.MinPh, dto.MaxPh);
        tx.Commit();
        return id;
    }

    public async Task DeletePhClassAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM PhClass WHERE id=@id;", new { id });
    }

    // Lookups: SoilTrait
    public async Task<IList<SoilTraitDto>> GetSoilTraitsAsync()
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<(int id, string code, string name_pl, string category)>(
            "SELECT id, code, name_pl, category FROM SoilTrait ORDER BY code;"
        );
        var list = new List<SoilTraitDto>();
        foreach (var r in rows)
            list.Add(
                new SoilTraitDto
                {
                    Id = r.id,
                    Code = r.code,
                    NamePl = r.name_pl,
                    Category = r.category,
                }
            );
        return list;
    }

    public async Task<int> UpsertSoilTraitAsync(SoilTraitDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE SoilTrait SET code=@Code, name_pl=@NamePl, category=@Category WHERE id=@Id;",
                dto
            );
            return dto.Id;
        }
        using var tx = conn.BeginTransaction();
        var repo = new LookupRepository();
        var id = repo.GetOrCreateSoilTraitId(conn, tx, dto.Code, dto.NamePl, dto.Category);
        tx.Commit();
        return id;
    }

    public async Task DeleteSoilTraitAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM SoilTrait WHERE id=@id;", new { id });
    }

    // Lookups: Color
    public async Task<IList<ColorDto>> GetColorsAsync()
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<(
            int id,
            string canonical_en,
            string name_pl,
            string? hex
        )>("SELECT id, canonical_en, name_pl, hex FROM Color ORDER BY canonical_en;");
        var list = new List<ColorDto>();
        foreach (var r in rows)
            list.Add(
                new ColorDto
                {
                    Id = r.id,
                    CanonicalEn = r.canonical_en,
                    NamePl = r.name_pl,
                    Hex = r.hex,
                }
            );
        return list;
    }

    public async Task<int> UpsertColorAsync(ColorDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE Color SET canonical_en=@CanonicalEn, name_pl=@NamePl, hex=@Hex WHERE id=@Id;",
                dto
            );
            return dto.Id;
        }
        using var tx = conn.BeginTransaction();
        var repo = new LookupRepository();
        var id = repo.GetOrCreateColorId(conn, tx, dto.CanonicalEn, dto.NamePl, dto.Hex);
        tx.Commit();
        return id;
    }

    public async Task DeleteColorAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM Color WHERE id=@id;", new { id });
    }

    // Lookups: Feature
    public async Task<IList<FeatureDto>> GetFeaturesAsync()
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<(int id, string code, string name_pl, string group_code)>(
            "SELECT id, code, name_pl, group_code FROM Feature ORDER BY code;"
        );
        var list = new List<FeatureDto>();
        foreach (var r in rows)
            list.Add(
                new FeatureDto
                {
                    Id = r.id,
                    Code = r.code,
                    NamePl = r.name_pl,
                    GroupCode = r.group_code,
                }
            );
        return list;
    }

    public async Task<int> UpsertFeatureAsync(FeatureDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE Feature SET code=@Code, name_pl=@NamePl, group_code=@GroupCode WHERE id=@Id;",
                dto
            );
            return dto.Id;
        }
        using var tx = conn.BeginTransaction();
        var repo = new LookupRepository();
        var id = repo.GetOrCreateFeatureId(conn, tx, dto.Code, dto.NamePl, dto.GroupCode);
        tx.Commit();
        return id;
    }

    public async Task DeleteFeatureAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM Feature WHERE id=@id;", new { id });
    }

    // Lookups: FoliagePersistence
    public async Task<IList<FoliagePersistenceDto>> GetFoliagePersistencesAsync()
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<(int id, string code, string name_pl)>(
            "SELECT id, code, name_pl FROM FoliagePersistence ORDER BY code;"
        );
        var list = new List<FoliagePersistenceDto>();
        foreach (var r in rows)
            list.Add(
                new FoliagePersistenceDto
                {
                    Id = r.id,
                    Code = r.code,
                    NamePl = r.name_pl,
                }
            );
        return list;
    }

    public async Task<int> UpsertFoliagePersistenceAsync(FoliagePersistenceDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE FoliagePersistence SET code=@Code, name_pl=@NamePl WHERE id=@Id;",
                dto
            );
            return dto.Id;
        }
        using var tx = conn.BeginTransaction();
        var repo = new LookupRepository();
        var id = repo.GetOrCreateFoliagePersistenceId(conn, tx, dto.Code, dto.NamePl);
        tx.Commit();
        return id;
    }

    public async Task DeleteFoliagePersistenceAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM FoliagePersistence WHERE id=@id;", new { id });
    }

    // Lookups: Packaging
    public async Task<IList<PackagingDto>> GetPackagingsAsync()
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<(int id, string code, string name_pl)>(
            "SELECT id, code, name_pl FROM Packaging ORDER BY code;"
        );
        var list = new List<PackagingDto>();
        foreach (var r in rows)
            list.Add(
                new PackagingDto
                {
                    Id = r.id,
                    Code = r.code,
                    NamePl = r.name_pl,
                }
            );
        return list;
    }

    public async Task<int> UpsertPackagingAsync(PackagingDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE Packaging SET code=@Code, name_pl=@NamePl WHERE id=@Id;",
                dto
            );
            return dto.Id;
        }
        using var tx = conn.BeginTransaction();
        var repo = new LookupRepository();
        var id = repo.GetOrCreatePackagingId(conn, tx, dto.Code, dto.NamePl);
        tx.Commit();
        return id;
    }

    public async Task DeletePackagingAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM Packaging WHERE id=@id;", new { id });
    }

    // Plants basic upsert/delete
    public async Task<int> UpsertPlantBasicAsync(PlantListRowDto dto)
    {
        if (dto is null)
            throw new ArgumentNullException(nameof(dto));
        using var conn = EnsureConn();
        var display = BuildBotanicalDisplay(dto.Genus, dto.Species, dto.Cultivar);
        if (dto.Id > 0)
        {
            await conn.ExecuteAsync(
                "UPDATE Plant SET botanical_genus=@Genus, botanical_species=@Species, cultivar=@Cultivar, type_id=@TypeId, botanical_name_display=@display, ph_min=@PhMin, ph_max=@PhMax, moisture_min_level=@MoistureMinLevelId, moisture_max_level=@MoistureMaxLevelId WHERE id=@Id;",
                new
                {
                    dto.Genus,
                    dto.Species,
                    dto.Cultivar,
                    dto.TypeId,
                    display,
                    dto.PhMin,
                    dto.PhMax,
                    dto.MoistureMinLevelId,
                    dto.MoistureMaxLevelId,
                    dto.Id,
                }
            );
            return dto.Id;
        }
        var id = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO Plant(botanical_genus, botanical_species, cultivar, type_id, botanical_name_display, ph_min, ph_max, moisture_min_level, moisture_max_level) VALUES (@Genus,@Species,@Cultivar,@TypeId,@display,@PhMin,@PhMax,@MoistureMinLevelId,@MoistureMaxLevelId); SELECT last_insert_rowid();",
            new
            {
                dto.Genus,
                dto.Species,
                dto.Cultivar,
                dto.TypeId,
                display,
                dto.PhMin,
                dto.PhMax,
                dto.MoistureMinLevelId,
                dto.MoistureMaxLevelId,
            }
        );
        return id;
    }

    public async Task DeletePlantAsync(int id)
    {
        using var conn = EnsureConn();
        await conn.ExecuteAsync("DELETE FROM Plant WHERE id=@id;", new { id });
    }

    private static string BuildBotanicalDisplay(string genus, string? species, string? cultivar)
    {
        var s = string.IsNullOrWhiteSpace(species) ? string.Empty : $" {species}";
        var c = string.IsNullOrWhiteSpace(cultivar) ? string.Empty : $" '{cultivar}'";
        return genus + s + c;
    }

    // M2M setters
    public async Task SetPlantExposuresAsync(int plantId, IReadOnlyList<int> exposureIds)
    {
        using var conn = EnsureConn();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "DELETE FROM PlantExposure WHERE plant_id=@plantId;",
            new { plantId },
            tx
        );
        foreach (var id in exposureIds)
            await conn.ExecuteAsync(
                "INSERT INTO PlantExposure(plant_id, exposure_id) VALUES (@plantId, @id);",
                new { plantId, id },
                tx
            );
        tx.Commit();
    }

    public async Task SetPlantHabitsAsync(int plantId, IReadOnlyList<int> habitIds)
    {
        using var conn = EnsureConn();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "DELETE FROM PlantHabit WHERE plant_id=@plantId;",
            new { plantId },
            tx
        );
        foreach (var id in habitIds)
            await conn.ExecuteAsync(
                "INSERT INTO PlantHabit(plant_id, habit_id, is_primary) VALUES (@plantId, @id, 0);",
                new { plantId, id },
                tx
            );
        tx.Commit();
    }

    public async Task SetPlantSoilTraitsAsync(int plantId, IReadOnlyList<int> soilTraitIds)
    {
        using var conn = EnsureConn();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "DELETE FROM PlantSoilTrait WHERE plant_id=@plantId;",
            new { plantId },
            tx
        );
        foreach (var id in soilTraitIds)
            await conn.ExecuteAsync(
                "INSERT INTO PlantSoilTrait(plant_id, soil_trait_id) VALUES (@plantId, @id);",
                new { plantId, id },
                tx
            );
        tx.Commit();
    }

    public async Task SetPlantFeaturesAsync(int plantId, IReadOnlyList<int> featureIds)
    {
        using var conn = EnsureConn();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "DELETE FROM PlantFeature WHERE plant_id=@plantId;",
            new { plantId },
            tx
        );
        foreach (var id in featureIds)
            await conn.ExecuteAsync(
                "INSERT INTO PlantFeature(plant_id, feature_id) VALUES (@plantId, @id);",
                new { plantId, id },
                tx
            );
        tx.Commit();
    }

    public async Task SetPlantColorsAsync(
        int plantId,
        string attribute,
        IReadOnlyList<int> colorIds
    )
    {
        using var conn = EnsureConn();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "DELETE FROM PlantColor WHERE plant_id=@plantId AND attribute=@attribute;",
            new { plantId, attribute },
            tx
        );
        var i = 0;
        foreach (var id in colorIds)
            await conn.ExecuteAsync(
                "INSERT INTO PlantColor(plant_id, attribute, color_id, sort_order) VALUES (@plantId, @attribute, @id, @sort);",
                new
                {
                    plantId,
                    attribute,
                    id,
                    sort = i++,
                },
                tx
            );
        tx.Commit();
    }

    // M2M getters
    public async Task<IList<int>> GetPlantExposuresAsync(int plantId)
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<int>(
            "SELECT exposure_id FROM PlantExposure WHERE plant_id=@plantId ORDER BY exposure_id;",
            new { plantId }
        );
        return rows.AsList();
    }

    public async Task<IList<int>> GetPlantHabitsAsync(int plantId)
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<int>(
            "SELECT habit_id FROM PlantHabit WHERE plant_id=@plantId ORDER BY habit_id;",
            new { plantId }
        );
        return rows.AsList();
    }

    public async Task<IList<int>> GetPlantSoilTraitsAsync(int plantId)
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<int>(
            "SELECT soil_trait_id FROM PlantSoilTrait WHERE plant_id=@plantId ORDER BY soil_trait_id;",
            new { plantId }
        );
        return rows.AsList();
    }

    public async Task<IList<int>> GetPlantFeaturesAsync(int plantId)
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<int>(
            "SELECT feature_id FROM PlantFeature WHERE plant_id=@plantId ORDER BY feature_id;",
            new { plantId }
        );
        return rows.AsList();
    }

    public async Task<IList<int>> GetPlantColorsAsync(int plantId, string attribute)
    {
        using var conn = EnsureConn();
        var rows = await conn.QueryAsync<int>(
            "SELECT color_id FROM PlantColor WHERE plant_id=@plantId AND attribute=@attribute ORDER BY sort_order;",
            new { plantId, attribute }
        );
        return rows.AsList();
    }
}
