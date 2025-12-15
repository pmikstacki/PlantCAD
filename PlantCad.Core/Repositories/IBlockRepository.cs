using System.Data;
using Dapper;
using PlantCad.Core.Entities;

namespace PlantCad.Core.Repositories;

public interface IBlockRepository
{
    int UpsertBlockDef(IDbConnection conn, IDbTransaction tx, BlockDef block);
    BlockDef? GetById(IDbConnection conn, IDbTransaction tx, int id);
    BlockDef? GetBySourceAndName(IDbConnection conn, IDbTransaction tx, string sourcePath, string blockName);
    BlockDef? GetByHash(IDbConnection conn, IDbTransaction tx, string contentHash);
    IEnumerable<BlockDef> Query(IDbConnection conn, IDbTransaction tx, string? textFilter, int offset, int limit);

    void UpdateExtents(IDbConnection conn, IDbTransaction tx, int id, double? widthWorld, double? heightWorld);

    void ReplaceThumbnail(IDbConnection conn, IDbTransaction tx, int blockId, int sizePx, byte[] pngBytes, string background = "transparent");
    BlockThumb? GetThumbnail(IDbConnection conn, IDbTransaction tx, int blockId, int sizePx);

    // Composition persistence (serialized geometry from importer)
    void UpsertComposition(IDbConnection conn, IDbTransaction tx, int blockId, string composition);
    string? GetComposition(IDbConnection conn, IDbTransaction tx, int blockId);
}

public sealed class BlockRepository : IBlockRepository
{
    public int UpsertBlockDef(IDbConnection conn, IDbTransaction tx, BlockDef b)
    {
        if (conn == null) throw new ArgumentNullException(nameof(conn));
        if (tx == null) throw new ArgumentNullException(nameof(tx));
        const string sql = @"INSERT INTO BlockDef(
            source_path, block_name, block_handle, version_tag, content_hash, unit, width_world, height_world
        ) VALUES (
            @SourcePath, @BlockName, @BlockHandle, @VersionTag, @ContentHash, @Unit, @WidthWorld, @HeightWorld
        )
        ON CONFLICT(source_path, block_name) DO UPDATE SET
            block_handle=excluded.block_handle,
            version_tag=excluded.version_tag,
            content_hash=excluded.content_hash,
            unit=excluded.unit,
            width_world=excluded.width_world,
            height_world=excluded.height_world,
            updated_utc=(CURRENT_TIMESTAMP);
        SELECT id FROM BlockDef WHERE source_path=@SourcePath AND block_name=@BlockName;";
        var id = conn.ExecuteScalar<long>(sql, b, tx);
        return (int)id;
    }

    public BlockDef? GetById(IDbConnection conn, IDbTransaction tx, int id)
    {
        const string sql = @"SELECT 
                id AS Id,
                source_path AS SourcePath,
                block_name AS BlockName,
                block_handle AS BlockHandle,
                version_tag AS VersionTag,
                content_hash AS ContentHash,
                unit AS Unit,
                width_world AS WidthWorld,
                height_world AS HeightWorld,
                created_utc AS CreatedUtc,
                updated_utc AS UpdatedUtc
            FROM BlockDef WHERE id=@id;";
        return conn.QuerySingleOrDefault<BlockDef>(sql, new { id }, tx);
    }

    public BlockDef? GetBySourceAndName(IDbConnection conn, IDbTransaction tx, string sourcePath, string blockName)
    {
        const string sql = @"SELECT 
                id AS Id,
                source_path AS SourcePath,
                block_name AS BlockName,
                block_handle AS BlockHandle,
                version_tag AS VersionTag,
                content_hash AS ContentHash,
                unit AS Unit,
                width_world AS WidthWorld,
                height_world AS HeightWorld,
                created_utc AS CreatedUtc,
                updated_utc AS UpdatedUtc
            FROM BlockDef WHERE source_path=@sourcePath AND block_name=@blockName;";
        return conn.QuerySingleOrDefault<BlockDef>(sql, new { sourcePath, blockName }, tx);
    }

    public BlockDef? GetByHash(IDbConnection conn, IDbTransaction tx, string contentHash)
    {
        const string sql = @"SELECT 
                id AS Id,
                source_path AS SourcePath,
                block_name AS BlockName,
                block_handle AS BlockHandle,
                version_tag AS VersionTag,
                content_hash AS ContentHash,
                unit AS Unit,
                width_world AS WidthWorld,
                height_world AS HeightWorld,
                created_utc AS CreatedUtc,
                updated_utc AS UpdatedUtc
            FROM BlockDef WHERE content_hash=@contentHash ORDER BY updated_utc DESC LIMIT 1;";
        return conn.QueryFirstOrDefault<BlockDef>(sql, new { contentHash }, tx);
    }

    public IEnumerable<BlockDef> Query(IDbConnection conn, IDbTransaction tx, string? textFilter, int offset, int limit)
    {
        textFilter = string.IsNullOrWhiteSpace(textFilter) ? null : textFilter;
        const string select = @"SELECT 
                id AS Id,
                source_path AS SourcePath,
                block_name AS BlockName,
                block_handle AS BlockHandle,
                version_tag AS VersionTag,
                content_hash AS ContentHash,
                unit AS Unit,
                width_world AS WidthWorld,
                height_world AS HeightWorld,
                created_utc AS CreatedUtc,
                updated_utc AS UpdatedUtc
            FROM BlockDef";
        if (textFilter == null)
        {
            var sql = select + " ORDER BY updated_utc DESC LIMIT @limit OFFSET @offset;";
            return conn.Query<BlockDef>(sql, new { limit, offset }, tx);
        }
        var like = "%" + textFilter + "%";
        var sqlFiltered = select + " WHERE block_name LIKE @like OR source_path LIKE @like ORDER BY updated_utc DESC LIMIT @limit OFFSET @offset;";
        return conn.Query<BlockDef>(sqlFiltered, new { like, limit, offset }, tx);
    }

    public void UpdateExtents(IDbConnection conn, IDbTransaction tx, int id, double? widthWorld, double? heightWorld)
    {
        conn.Execute("UPDATE BlockDef SET width_world=@widthWorld, height_world=@heightWorld, updated_utc=(CURRENT_TIMESTAMP) WHERE id=@id;",
            new { id, widthWorld, heightWorld }, tx);
    }

    public void ReplaceThumbnail(IDbConnection conn, IDbTransaction tx, int blockId, int sizePx, byte[] pngBytes, string background = "transparent")
    {
        conn.Execute(@"INSERT INTO BlockThumb(block_id, size_px, png, background)
                      VALUES (@blockId, @sizePx, @pngBytes, @background)
                      ON CONFLICT(block_id, size_px) DO UPDATE SET png=excluded.png, background=excluded.background, updated_utc=(CURRENT_TIMESTAMP);",
            new { blockId, sizePx, pngBytes, background }, tx);
    }

    public BlockThumb? GetThumbnail(IDbConnection conn, IDbTransaction tx, int blockId, int sizePx)
    {
        return conn.QuerySingleOrDefault<BlockThumb>("SELECT block_id as BlockId, size_px as SizePx, png as Png, background as Background, updated_utc as UpdatedUtc FROM BlockThumb WHERE block_id=@blockId AND size_px=@sizePx;",
            new { blockId, sizePx }, tx);
    }

    public void UpsertComposition(IDbConnection conn, IDbTransaction tx, int blockId, string composition)
    {
        if (string.IsNullOrWhiteSpace(composition))
        {
            throw new ArgumentException("Composition must not be empty.", nameof(composition));
        }
        conn.Execute(@"INSERT INTO BlockComposition(block_id, composition)
                      VALUES (@blockId, @composition)
                      ON CONFLICT(block_id) DO UPDATE SET composition=excluded.composition, updated_utc=(CURRENT_TIMESTAMP);",
            new { blockId, composition }, tx);
    }

    public string? GetComposition(IDbConnection conn, IDbTransaction tx, int blockId)
    {
        return conn.QuerySingleOrDefault<string>("SELECT composition FROM BlockComposition WHERE block_id=@blockId;", new { blockId }, tx);
    }
}
