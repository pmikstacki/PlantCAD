using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.ViewModels.Dialogs;

public sealed partial class PlantPickerDialogViewModel : ObservableObject
{
    public sealed record PlantOption(int Id, string Name);

    public ObservableCollection<PlantOption> Items { get; } = new();

    [ObservableProperty]
    private PlantOption? selected;

    [ObservableProperty]
    private string? search;

    public PlantPickerDialogViewModel()
    {
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        Items.Clear();
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen)
            return;
        var rows = await svc.GetPlantsAsync(page: 1, pageSize: 200, search: Search);
        foreach (var r in rows)
        {
            Items.Add(
                new PlantOption(
                    r.Id,
                    string.IsNullOrWhiteSpace(r.BotanicalNameDisplay)
                        ? $"{r.Genus} {r.Species} {r.Cultivar}".Trim()
                        : r.BotanicalNameDisplay
                )
            );
        }
    }
}
