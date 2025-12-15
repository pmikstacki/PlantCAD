using System.Data;
using Dapper;
using PlantCad.Core.Entities;

namespace PlantCad.Core.Repositories;

public interface IPlantBlockRepository
{
    void Assign(IDbConnection conn, IDbTransaction tx, int plantId, int blockId, string? notes);
    void Unassign(IDbConnection conn, IDbTransaction tx, int plantId, int blockId);
    IEnumerable<BlockDef> GetBlocksForPlant(IDbConnection conn, IDbTransaction tx, int plantId);
    IEnumerable<int> GetPlantIdsUsingBlock(IDbConnection conn, IDbTransaction tx, int blockId);
}

public sealed class PlantBlockRepository : IPlantBlockRepository
{
    public void Assign(IDbConnection conn, IDbTransaction tx, int plantId, int blockId, string? notes)
    {
        conn.Execute(@"INSERT INTO PlantBlock(plant_id, block_id, notes) VALUES (@plantId,@blockId,@notes)
                      ON CONFLICT(plant_id, block_id) DO UPDATE SET notes=excluded.notes;",
            new { plantId, blockId, notes }, tx);
    }

    public void Unassign(IDbConnection conn, IDbTransaction tx, int plantId, int blockId)
    {
        conn.Execute("DELETE FROM PlantBlock WHERE plant_id=@plantId AND block_id=@blockId;", new { plantId, blockId }, tx);
    }

    public IEnumerable<BlockDef> GetBlocksForPlant(IDbConnection conn, IDbTransaction tx, int plantId)
    {
        const string sql = @"SELECT b.* FROM BlockDef b
                             INNER JOIN PlantBlock pb ON pb.block_id = b.id
                             WHERE pb.plant_id=@plantId
                             ORDER BY b.block_name;";
        return conn.Query<BlockDef>(sql, new { plantId }, tx);
    }

    public IEnumerable<int> GetPlantIdsUsingBlock(IDbConnection conn, IDbTransaction tx, int blockId)
    {
        return conn.Query<int>("SELECT plant_id FROM PlantBlock WHERE block_id=@blockId;", new { blockId }, tx);
    }
}
