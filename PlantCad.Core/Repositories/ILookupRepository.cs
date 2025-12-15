using System.Data;
using Dapper;

namespace PlantCad.Core.Repositories;

public interface ILookupRepository
{
    int GetOrCreatePlantTypeId(IDbConnection conn, IDbTransaction tx, string code, string namePl);
    int GetOrCreateHabitId(IDbConnection conn, IDbTransaction tx, string code, string namePl);
    int GetOrCreateExposureId(IDbConnection conn, IDbTransaction tx, string code, string namePl);
    int GetOrCreateMoistureLevelId(IDbConnection conn, IDbTransaction tx, int ordinal, string code, string namePl);
    int GetOrCreatePhClassId(IDbConnection conn, IDbTransaction tx, string code, string namePl, double minPh, double maxPh);
    int GetOrCreateSoilTraitId(IDbConnection conn, IDbTransaction tx, string code, string namePl, string category);
    int GetOrCreateColorId(IDbConnection conn, IDbTransaction tx, string canonicalEn, string namePl, string? hex = null);
    int GetOrCreateFeatureId(IDbConnection conn, IDbTransaction tx, string code, string namePl, string groupCode);
    int GetOrCreateFoliagePersistenceId(IDbConnection conn, IDbTransaction tx, string code, string namePl);
    int GetOrCreatePackagingId(IDbConnection conn, IDbTransaction tx, string code, string namePl);
}

public sealed class LookupRepository : ILookupRepository
{
    public int GetOrCreatePlantTypeId(IDbConnection conn, IDbTransaction tx, string code, string namePl)
        => UpsertSimple(conn, tx, "PlantType", code, namePl);

    public int GetOrCreateHabitId(IDbConnection conn, IDbTransaction tx, string code, string namePl)
        => UpsertSimple(conn, tx, "Habit", code, namePl);

    public int GetOrCreateExposureId(IDbConnection conn, IDbTransaction tx, string code, string namePl)
        => UpsertSimple(conn, tx, "Exposure", code, namePl);

    public int GetOrCreateMoistureLevelId(IDbConnection conn, IDbTransaction tx, int ordinal, string code, string namePl)
    {
        var id = conn.ExecuteScalar<int?>("SELECT id FROM MoistureLevel WHERE code=@code;", new { code }, tx);
        if (id.HasValue) return id.Value;
        conn.Execute("INSERT OR IGNORE INTO MoistureLevel(ordinal, code, name_pl) VALUES (@ordinal,@code,@namePl);",
            new { ordinal, code, namePl }, tx);
        return conn.ExecuteScalar<int>("SELECT id FROM MoistureLevel WHERE code=@code;", new { code }, tx);
    }

    public int GetOrCreatePhClassId(IDbConnection conn, IDbTransaction tx, string code, string namePl, double minPh, double maxPh)
    {
        var id = conn.ExecuteScalar<int?>("SELECT id FROM PhClass WHERE code=@code;", new { code }, tx);
        if (id.HasValue) return id.Value;
        conn.Execute("INSERT OR IGNORE INTO PhClass(code, name_pl, min_ph, max_ph) VALUES (@code,@namePl,@minPh,@maxPh);",
            new { code, namePl, minPh, maxPh }, tx);
        return conn.ExecuteScalar<int>("SELECT id FROM PhClass WHERE code=@code;", new { code }, tx);
    }

    public int GetOrCreateSoilTraitId(IDbConnection conn, IDbTransaction tx, string code, string namePl, string category)
        => UpsertWithCategory(conn, tx, "SoilTrait", code, namePl, category);

    public int GetOrCreateColorId(IDbConnection conn, IDbTransaction tx, string canonicalEn, string namePl, string? hex = null)
    {
        var id = conn.ExecuteScalar<int?>("SELECT id FROM Color WHERE canonical_en=@canonicalEn AND name_pl=@namePl;",
            new { canonicalEn, namePl }, tx);
        if (id.HasValue) return id.Value;
        conn.Execute("INSERT INTO Color(canonical_en, name_pl, hex) VALUES (@canonicalEn,@namePl,@hex);",
            new { canonicalEn, namePl, hex }, tx);
        return conn.ExecuteScalar<int>("SELECT id FROM Color WHERE canonical_en=@canonicalEn AND name_pl=@namePl;",
            new { canonicalEn, namePl }, tx);
    }

    public int GetOrCreateFeatureId(IDbConnection conn, IDbTransaction tx, string code, string namePl, string groupCode)
    {
        var id = conn.ExecuteScalar<int?>("SELECT id FROM Feature WHERE code=@code;", new { code }, tx);
        if (id.HasValue) return id.Value;
        conn.Execute("INSERT INTO Feature(code, name_pl, group_code) VALUES (@code,@namePl,@groupCode);",
            new { code, namePl, groupCode }, tx);
        return conn.ExecuteScalar<int>("SELECT id FROM Feature WHERE code=@code;", new { code }, tx);
    }

    public int GetOrCreateFoliagePersistenceId(IDbConnection conn, IDbTransaction tx, string code, string namePl)
        => UpsertSimple(conn, tx, "FoliagePersistence", code, namePl);

    public int GetOrCreatePackagingId(IDbConnection conn, IDbTransaction tx, string code, string namePl)
        => UpsertSimple(conn, tx, "Packaging", code, namePl);

    private static int UpsertSimple(IDbConnection conn, IDbTransaction tx, string table, string code, string namePl)
    {
        EnsureKnownTable(table);
        var id = conn.ExecuteScalar<int?>(@$"SELECT id FROM {table} WHERE code=@code;", new { code }, tx);
        if (id.HasValue) return id.Value;
        conn.Execute(@$"INSERT OR IGNORE INTO {table}(code, name_pl) VALUES (@code,@namePl);", new { code, namePl }, tx);
        return conn.ExecuteScalar<int>(@$"SELECT id FROM {table} WHERE code=@code;", new { code }, tx);
    }

    private static int UpsertWithCategory(IDbConnection conn, IDbTransaction tx, string table, string code, string namePl, string category)
    {
        EnsureKnownTable(table);
        var id = conn.ExecuteScalar<int?>(@$"SELECT id FROM {table} WHERE code=@code;", new { code }, tx);
        if (id.HasValue) return id.Value;
        conn.Execute(@$"INSERT OR IGNORE INTO {table}(code, name_pl, category) VALUES (@code,@namePl,@category);",
            new { code, namePl, category }, tx);
        return conn.ExecuteScalar<int>(@$"SELECT id FROM {table} WHERE code=@code;", new { code }, tx);
    }

    private static void EnsureKnownTable(string table)
    {
        // Guard to avoid SQL injection via table names
        var allowed = new HashSet<string>{ "PlantType","Habit","Exposure","MoistureLevel","PhClass","SoilTrait","Color","Feature","FoliagePersistence","Packaging" };
        if (!allowed.Contains(table))
            throw new ArgumentOutOfRangeException(nameof(table), $"Unknown lookup table: {table}");
    }
}
