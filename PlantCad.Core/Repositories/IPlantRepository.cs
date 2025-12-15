using System.Data;
using Dapper;
using PlantCad.Core.Entities;

namespace PlantCad.Core.Repositories;

public interface IPlantRepository
{
    int? FindPlantId(IDbConnection conn, IDbTransaction tx, string genus, string? species, string? cultivar);
    int InsertPlant(IDbConnection conn, IDbTransaction tx, Plant plant);

    void AddPlantExposure(IDbConnection conn, IDbTransaction tx, int plantId, int exposureId);
    void AddPlantHabit(IDbConnection conn, IDbTransaction tx, int plantId, int habitId, bool isPrimary = false);
    void AddPlantSoilTrait(IDbConnection conn, IDbTransaction tx, int plantId, int soilTraitId);
    void AddPlantColor(IDbConnection conn, IDbTransaction tx, int plantId, string attribute, int colorId, int sortOrder = 0);
    void AddPlantFeature(IDbConnection conn, IDbTransaction tx, int plantId, int featureId);
}

public sealed class PlantRepository : IPlantRepository
{
    public int? FindPlantId(IDbConnection conn, IDbTransaction tx, string genus, string? species, string? cultivar)
    {
        return conn.ExecuteScalar<int?>(
            "SELECT id FROM Plant WHERE botanical_genus=@genus AND IFNULL(botanical_species,'')=IFNULL(@species,'') AND IFNULL(cultivar,'')=IFNULL(@cultivar,'');",
            new { genus, species, cultivar }, tx);
    }

    public int InsertPlant(IDbConnection conn, IDbTransaction tx, Plant p)
    {
        const string sql = @"INSERT INTO Plant (
            botanical_genus, botanical_species, cultivar, botanical_name_display, common_name_pl, type_id,
            flowering_start_month, flowering_end_month, hardiness_zone, hardiness_subzone,
            height_min_m, height_max_m, width_min_m, width_max_m, spacing_min_m, spacing_max_m,
            foliage_persistence_id, ph_min, ph_max, ph_class_min_id, ph_class_max_id,
            moisture_min_level, moisture_max_level, habit_primary_id,
            raw_height, raw_width, raw_spacing, raw_ph, raw_moisture, raw_exposure, raw_soil, raw_flower_props, raw_leaf_props, raw_fruit_color, raw_stock
        ) VALUES (
            @BotanicalGenus, @BotanicalSpecies, @Cultivar, @BotanicalNameDisplay, @CommonNamePl, @TypeId,
            @FloweringStartMonth, @FloweringEndMonth, @HardinessZone, @HardinessSubzone,
            @HeightMinM, @HeightMaxM, @WidthMinM, @WidthMaxM, @SpacingMinM, @SpacingMaxM,
            @FoliagePersistenceId, @PhMin, @PhMax, @PhClassMinId, @PhClassMaxId,
            @MoistureMinLevel, @MoistureMaxLevel, @HabitPrimaryId,
            @RawHeight, @RawWidth, @RawSpacing, @RawPh, @RawMoisture, @RawExposure, @RawSoil, @RawFlowerProps, @RawLeafProps, @RawFruitColor, @RawStock
        ); SELECT last_insert_rowid();";

        var id = conn.ExecuteScalar<long>(sql, p, tx);
        return (int)id;
    }

    public void AddPlantExposure(IDbConnection conn, IDbTransaction tx, int plantId, int exposureId)
    {
        conn.Execute("INSERT OR IGNORE INTO PlantExposure(plant_id, exposure_id) VALUES (@plantId,@exposureId);", new { plantId, exposureId }, tx);
    }

    public void AddPlantHabit(IDbConnection conn, IDbTransaction tx, int plantId, int habitId, bool isPrimary = false)
    {
        conn.Execute("INSERT OR IGNORE INTO PlantHabit(plant_id, habit_id, is_primary) VALUES (@plantId,@habitId,@isPrimary);", new { plantId, habitId, isPrimary = isPrimary ? 1 : 0 }, tx);
    }

    public void AddPlantSoilTrait(IDbConnection conn, IDbTransaction tx, int plantId, int soilTraitId)
    {
        conn.Execute("INSERT OR IGNORE INTO PlantSoilTrait(plant_id, soil_trait_id) VALUES (@plantId,@soilTraitId);", new { plantId, soilTraitId }, tx);
    }

    public void AddPlantColor(IDbConnection conn, IDbTransaction tx, int plantId, string attribute, int colorId, int sortOrder = 0)
    {
        conn.Execute("INSERT OR IGNORE INTO PlantColor(plant_id, attribute, color_id, sort_order) VALUES (@plantId,@attribute,@colorId,@sortOrder);",
            new { plantId, attribute, colorId, sortOrder }, tx);
    }

    public void AddPlantFeature(IDbConnection conn, IDbTransaction tx, int plantId, int featureId)
    {
        conn.Execute("INSERT OR IGNORE INTO PlantFeature(plant_id, feature_id) VALUES (@plantId,@featureId);", new { plantId, featureId }, tx);
    }
}
