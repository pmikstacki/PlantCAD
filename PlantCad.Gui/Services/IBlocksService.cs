using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using PlantCad.Core.Entities;

namespace PlantCad.Gui.Services;

public interface IBlocksService
{
    Task<IReadOnlyList<BlockDef>> QueryBlocksAsync(
        int page,
        int pageSize,
        string? filter = null,
        CancellationToken ct = default
    );
    Task<byte[]?> GetThumbnailPngAsync(int blockId, int sizePx, CancellationToken ct = default);
    Task<Bitmap?> GetThumbnailBitmapAsync(int blockId, int sizePx, CancellationToken ct = default);
    Task ImportFromDwgAsync(
        string dwgPath,
        bool includeAnonymous,
        int thumbSizePx,
        string thumbBackground,
        CancellationToken ct = default
    );

    Task AssignBlockToPlantAsync(
        int blockId,
        int plantId,
        string? note = null,
        CancellationToken ct = default
    );
    Task UnassignBlockFromPlantAsync(int blockId, int plantId, CancellationToken ct = default);

    Task<int> GetBlockUsageCountAsync(int blockId, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetPlantIdsUsingBlockAsync(
        int blockId,
        CancellationToken ct = default
    );
}
