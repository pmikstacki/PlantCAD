using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Services;
using PlantCad.Gui.Services.Internal;

namespace PlantCad.Gui.ViewModels.Tools;

public sealed partial class BlocksGalleryToolViewModel : Tool
{
    private IBlocksService? _blocksService;
    private CancellationTokenSource? _cts;

    public ObservableCollection<BlockRow> Items { get; } = new();

    [ObservableProperty]
    private string? search;

    [ObservableProperty]
    private int page = 1;

    [ObservableProperty]
    private int pageSize = 48;

    [ObservableProperty]
    private int selectedPlantId;

    [ObservableProperty]
    private string? selectedPlantName;

    [ObservableProperty]
    private int thumbnailSize = 128;

    [ObservableProperty]
    private bool includeAnonymous;

    [ObservableProperty]
    private string thumbBackground = "transparent";

    public BlocksGalleryToolViewModel()
    {
        Title = "Blocks";
        CanClose = false;
        DockGroup = "Tools";
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Items.Clear();
        var db = ServiceRegistry.PlantDbService;
        if (db is null || !db.IsOpen || string.IsNullOrWhiteSpace(db.DatabasePath))
            return;
        _blocksService ??= new BlocksService(db.DatabasePath);

        var rows = await _blocksService.QueryBlocksAsync(Page, PageSize, Search, ct);
        foreach (var b in rows)
        {
            Items.Add(
                new BlockRow(
                    b.Id,
                    b.BlockName ?? string.Empty,
                    b.SourcePath ?? string.Empty,
                    b.WidthWorld,
                    b.HeightWorld
                )
            );
        }
        // Load thumbnails and usage lazily
        _ = LoadThumbnailsAsync(ct);
        _ = LoadUsageCountsAsync(ct);
    }

    private async Task LoadThumbnailsAsync(CancellationToken ct)
    {
        var svc = _blocksService;
        if (svc is null)
            return;
        // Batch in small groups and limit concurrency to keep UI responsive
        var degree = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
        foreach (var chunk in Items.Chunk(24))
        {
            using var gate = new System.Threading.SemaphoreSlim(degree, degree);
            var size = ThumbnailSize; // capture once per batch
            var tasks = chunk
                .Select(async it =>
                {
                    if (ct.IsCancellationRequested)
                        return;
                    await gate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var bmp = await svc.GetThumbnailBitmapAsync(it.Id, size, ct)
                            .ConfigureAwait(false);
                        if (ct.IsCancellationRequested)
                            return;
                        // Marshal to UI thread for property update
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            it.Thumbnail = bmp
                        );
                    }
                    finally
                    {
                        gate.Release();
                    }
                })
                .ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
                break;
        }
    }

    [RelayCommand]
    private async Task OpenAttributesAsync(object? param)
    {
        if (param is not BlockRow row)
            return;
        if (string.IsNullOrWhiteSpace(row.Source) || string.IsNullOrWhiteSpace(row.Name))
        {
            var logger0 = ServiceRegistry.LoggerFactory?.CreateLogger<BlocksGalleryToolViewModel>();
            logger0?.LogWarning(
                "Cannot open attributes: missing SourcePath ('{Source}') or BlockName ('{Name}')",
                row.Source,
                row.Name
            );
            ServiceRegistry.LogsTool?.Append(
                $"Cannot open attributes: missing SourcePath ('{row.Source}') or BlockName ('{row.Name}')."
            );
            return;
        }
        try
        {
            var vm = new PlantCad.Gui.ViewModels.Dialogs.BlockAttributesDialogViewModel();
            vm.Initialize(row.Source, row.Name);
            var dlg = new PlantCad.Gui.Views.Dialogs.BlockAttributesDialog { DataContext = vm };
            await dlg.ShowDialog(ServiceRegistry.Root!);
            // Invalidate cached previews/details for this block
            PlantCad.Gui.Services.BlockModelService.Invalidate(row.Source, row.Name);
        }
        catch (Exception ex)
        {
            var logger = ServiceRegistry.LoggerFactory?.CreateLogger<BlocksGalleryToolViewModel>();
            logger?.LogError(
                ex,
                "Failed to open attributes for '{BlockName}' from '{SourcePath}'",
                row.Name,
                row.Source
            );
            ServiceRegistry.LogsTool?.Append(
                $"Error opening attributes for '{row.Name}' from '{row.Source}': {ex.Message}"
            );
        }
    }

    private async Task LoadUsageCountsAsync(CancellationToken ct)
    {
        var svc = _blocksService;
        if (svc is null)
            return;
        foreach (var chunk in Items.Chunk(16))
        {
            var tasks = chunk.Select(async it =>
            {
                if (ct.IsCancellationRequested)
                    return;
                var cnt = await svc.GetBlockUsageCountAsync(it.Id, ct);
                if (ct.IsCancellationRequested)
                    return;
                it.UsageCount = cnt;
            });
            await Task.WhenAll(tasks);
            if (ct.IsCancellationRequested)
                break;
        }
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        Page++;
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (Page <= 1)
            return;
        Page--;
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task AssignAsync(object? param)
    {
        if (param is not BlockRow row)
            return;
        if (SelectedPlantId <= 0)
            return;
        var db = ServiceRegistry.PlantDbService;
        if (db is null || !db.IsOpen || string.IsNullOrWhiteSpace(db.DatabasePath))
            return;
        _blocksService ??= new BlocksService(db.DatabasePath);
        await _blocksService.AssignBlockToPlantAsync(row.Id, SelectedPlantId);
        row.UsageCount = await _blocksService.GetBlockUsageCountAsync(row.Id);
    }

    [RelayCommand]
    private async Task UnassignAsync(object? param)
    {
        if (param is not BlockRow row)
            return;
        if (SelectedPlantId <= 0)
            return;
        var db = ServiceRegistry.PlantDbService;
        if (db is null || !db.IsOpen || string.IsNullOrWhiteSpace(db.DatabasePath))
            return;
        _blocksService ??= new BlocksService(db.DatabasePath);
        await _blocksService.UnassignBlockFromPlantAsync(row.Id, SelectedPlantId);
        row.UsageCount = await _blocksService.GetBlockUsageCountAsync(row.Id);
    }

    [RelayCommand]
    private async Task PickPlantAsync()
    {
        var db = ServiceRegistry.PlantDbService;
        if (db is null || !db.IsOpen)
            return;
        var dlg = new Views.Dialogs.PlantPickerDialog
        {
            DataContext = new ViewModels.Dialogs.PlantPickerDialogViewModel(),
        };
        var result =
            await dlg.ShowDialog<ViewModels.Dialogs.PlantPickerDialogViewModel.PlantOption?>(
                ServiceRegistry.Root!
            );
        if (result is null)
            return;
        SelectedPlantId = result.Id;
        SelectedPlantName = result.Name;
    }

    [RelayCommand]
    private async Task ImportDwgAsync()
    {
        var dialogs = ServiceRegistry.FileDialogService;
        var db = ServiceRegistry.PlantDbService;
        if (
            dialogs is null
            || db is null
            || !db.IsOpen
            || string.IsNullOrWhiteSpace(db.DatabasePath)
        )
            return;
        var path = await dialogs.ShowOpenCadAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;
        _blocksService ??= new BlocksService(db.DatabasePath);
        await _blocksService.ImportFromDwgAsync(
            path,
            IncludeAnonymous,
            ThumbnailSize,
            ThumbBackground
        );
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task GoToPlantsAsync(object? param)
    {
        if (param is not BlockRow row)
            return;
        var db = ServiceRegistry.PlantDbService;
        if (db is null || !db.IsOpen || string.IsNullOrWhiteSpace(db.DatabasePath))
            return;
        _blocksService ??= new BlocksService(db.DatabasePath);
        var ids = await _blocksService.GetPlantIdsUsingBlockAsync(row.Id);
        if (ids.Count == 0)
            return;
        ServiceRegistry.RequestOpenPlantsDocumentWithFilter(ids);
    }

    [RelayCommand]
    private async Task ShowDetailsAsync(object? param)
    {
        if (param is not BlockRow row)
            return;
        // Build details via BlockModelService
        if (string.IsNullOrWhiteSpace(row.Source) || string.IsNullOrWhiteSpace(row.Name))
        {
            var logger0 = ServiceRegistry.LoggerFactory?.CreateLogger<BlocksGalleryToolViewModel>();
            logger0?.LogWarning(
                "Cannot show details: missing SourcePath ('{Source}') or BlockName ('{Name}')",
                row.Source,
                row.Name
            );
            ServiceRegistry.LogsTool?.Append(
                $"Cannot show details: missing SourcePath ('{row.Source}') or BlockName ('{row.Name}')."
            );
            return;
        }
        try
        {
            var (counts, layers, extents) = PlantCad.Gui.Services.BlockModelService.GetDetails(
                row.Source,
                row.Name
            );
            var vm = new ViewModels.Dialogs.BlockDetailsDialogViewModel
            {
                BlockName = row.Name,
                SourcePath = row.Source,
                Extents = extents,
                Entities = counts
                    .Select(kv => new ViewModels.Dialogs.BlockDetailsDialogViewModel.Entry(
                        kv.Key,
                        kv.Value
                    ))
                    .OrderByDescending(e => e.Count)
                    .ToList(),
                Layers = layers.OrderBy(s => s).ToList(),
            };
            var dlg = new Views.Dialogs.BlockDetailsDialog { DataContext = vm };
            await dlg.ShowDialog(ServiceRegistry.Root!);
        }
        catch (Exception ex)
        {
            var logger = ServiceRegistry.LoggerFactory?.CreateLogger<BlocksGalleryToolViewModel>();
            logger?.LogError(
                ex,
                "Failed to show block details for '{BlockName}' from '{SourcePath}'",
                row.Name,
                row.Source
            );
            ServiceRegistry.LogsTool?.Append(
                $"Error showing details for '{row.Name}' from '{row.Source}': {ex.Message}"
            );
        }
    }

    public sealed partial class BlockRow : ObservableObject
    {
        public int Id { get; }
        public string Name { get; }
        public string Source { get; }
        public double? WidthWorld { get; }
        public double? HeightWorld { get; }

        [ObservableProperty]
        private Bitmap? thumbnail;

        [ObservableProperty]
        private int usageCount;

        public BlockRow(int id, string name, string source, double? w, double? h)
        {
            Id = id;
            Name = name;
            Source = source;
            WidthWorld = w;
            HeightWorld = h;
        }
    }
}
