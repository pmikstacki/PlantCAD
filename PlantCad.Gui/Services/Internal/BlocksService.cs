using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PlantCad.Core.Data;
using PlantCad.Core.Repositories;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Services.Internal;

public sealed class BlocksService : IBlocksService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IBlockRepository _blocks = new BlockRepository();
    private readonly IPlantBlockRepository _plantBlocks = new PlantBlockRepository();

    // Simple LRU thumbnail cache (by blockId+size). Store PNG bytes to avoid sharing/disposal issues with UI-bound Bitmaps
    private readonly int _maxThumbs;
    private readonly LinkedList<(int key, int size)> _order = new();
    private readonly Dictionary<(int key, int size), byte[]> _pngs = new();

    public BlocksService(string dbPath, int maxThumbs = 200)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("Database path must not be empty.", nameof(dbPath));
        _factory = new SqliteConnectionFactory(dbPath);
        _maxThumbs = Math.Max(16, maxThumbs);
    }

    public async Task<IReadOnlyList<PlantCad.Core.Entities.BlockDef>> QueryBlocksAsync(
        int page,
        int pageSize,
        string? filter = null,
        CancellationToken ct = default
    )
    {
        if (page < 1)
            page = 1;
        if (pageSize <= 0)
            pageSize = 50;
        return await Task.Run(
            () =>
            {
                using var conn = _factory.Create();
                using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                var offset = (page - 1) * pageSize;
                var list = _blocks.Query(conn, tx, filter, offset, pageSize).ToList();
                tx.Commit();
                return (IReadOnlyList<PlantCad.Core.Entities.BlockDef>)list;
            },
            ct
        );
    }

    public async Task<byte[]?> GetThumbnailPngAsync(
        int blockId,
        int sizePx,
        CancellationToken ct = default
    )
    {
        return await Task.Run(
            () =>
            {
                using var conn = _factory.Create();
                using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                var t = _blocks.GetThumbnail(conn, tx, blockId, sizePx);
                tx.Commit();
                return t?.Png;
            },
            ct
        );
    }

    public async Task<Bitmap?> GetThumbnailBitmapAsync(
        int blockId,
        int sizePx,
        CancellationToken ct = default
    )
    {
        var key = (blockId, sizePx);
        if (_pngs.TryGetValue(key, out var cachedPng))
        {
            Touch(key);
            using var cachedStream = new System.IO.MemoryStream(cachedPng, writable: false);
            return new Bitmap(cachedStream);
        }
        var png = await GetThumbnailPngAsync(blockId, sizePx, ct);
        if (png is null)
            return null;
        AddToCache(key, png);
        using var ms = new System.IO.MemoryStream(png, writable: false);
        return new Bitmap(ms);
    }

    public async Task ImportFromDwgAsync(
        string dwgPath,
        bool includeAnonymous,
        int thumbSizePx,
        string thumbBackground,
        CancellationToken ct = default
    )
    {
        // Import blocks off the UI thread
        await Task.Run(
            () =>
            {
                var importer = new PlantCad.Core.Import.BlockImporter(_factory, _blocks);
                importer.ImportFromDwg(dwgPath, includeAnonymous, ct);
            },
            ct
        );

        // Render thumbnails on the UI thread (no headless runtime needed)
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var thumb = new BlockThumbnailRenderer();
            thumb.GenerateAndStoreThumbnailsForDwg(
                _blocks,
                _factory,
                dwgPath,
                thumbSizePx,
                thumbBackground,
                includeAnonymous
            );
        });
    }

    public async Task AssignBlockToPlantAsync(
        int blockId,
        int plantId,
        string? note = null,
        CancellationToken ct = default
    )
    {
        await Task.Run(
            () =>
            {
                using var conn = _factory.Create();
                using var tx = conn.BeginTransaction();
                _plantBlocks.Assign(conn, tx, plantId, blockId, note);
                tx.Commit();
            },
            ct
        );
    }

    public async Task UnassignBlockFromPlantAsync(
        int blockId,
        int plantId,
        CancellationToken ct = default
    )
    {
        await Task.Run(
            () =>
            {
                using var conn = _factory.Create();
                using var tx = conn.BeginTransaction();
                _plantBlocks.Unassign(conn, tx, plantId, blockId);
                tx.Commit();
            },
            ct
        );
    }

    public async Task<int> GetBlockUsageCountAsync(int blockId, CancellationToken ct = default)
    {
        return await Task.Run(
            () =>
            {
                using var conn = _factory.Create();
                using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                var ids = _plantBlocks.GetPlantIdsUsingBlock(conn, tx, blockId);
                var count = ids?.Count() ?? 0;
                tx.Commit();
                return count;
            },
            ct
        );
    }

    public async Task<IReadOnlyList<int>> GetPlantIdsUsingBlockAsync(
        int blockId,
        CancellationToken ct = default
    )
    {
        return await Task.Run(
            () =>
            {
                using var conn = _factory.Create();
                using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);
                var ids = _plantBlocks.GetPlantIdsUsingBlock(conn, tx, blockId).ToList();
                tx.Commit();
                return (IReadOnlyList<int>)ids;
            },
            ct
        );
    }

    private void AddToCache((int key, int size) k, byte[] png)
    {
        if (_pngs.ContainsKey(k))
        {
            _pngs[k] = png;
            Touch(k);
            return;
        }
        _pngs[k] = png;
        _order.AddFirst(k);
        if (_order.Count > _maxThumbs)
        {
            var last = _order.Last!.Value;
            _order.RemoveLast();
            // evict PNG bytes; no disposal needed
            _pngs.Remove(last);
        }
    }

    private void Touch((int key, int size) k)
    {
        var node = _order.Find(k);
        if (node is null)
            return;
        _order.Remove(node);
        _order.AddFirst(node);
    }
}
