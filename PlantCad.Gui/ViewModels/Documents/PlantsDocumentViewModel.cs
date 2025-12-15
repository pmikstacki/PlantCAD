using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.ViewModels.Documents;

public sealed partial class PlantsDocumentViewModel : Document
{
    public ObservableCollection<PlantRow> Items { get; } = new();

    public ObservableCollection<Option> TypeOptions { get; } = new();
    public ObservableCollection<MoistureOption> MoistureOptions { get; } = new();
    public ObservableCollection<PhClassOption> PhClassOptions { get; } = new();

    [ObservableProperty]
    private PlantRow? selectedItem;

    [ObservableProperty]
    private string? search;

    [ObservableProperty]
    private int page = 1;

    [ObservableProperty]
    private int pageSize = 50;

    // Quick filters
    [ObservableProperty]
    private Option? selectedTypeFilter;

    [ObservableProperty]
    private MoistureOption? selectedMoistureMinFilter;

    [ObservableProperty]
    private MoistureOption? selectedMoistureMaxFilter;

    [ObservableProperty]
    private PhClassOption? selectedPhClassMinFilter;

    [ObservableProperty]
    private PhClassOption? selectedPhClassMaxFilter;

    public PlantsDocumentViewModel()
    {
        Title = "Plants";
        CanClose = true;
        _ = LoadLookupsAsync();
        _ = ReloadAsync();
    }

    // Optional ID filter (show only these plant IDs)
    [ObservableProperty]
    private System.Collections.Generic.HashSet<int>? idFilter;

    public void SetIdFilter(System.Collections.Generic.IReadOnlyList<int> ids)
    {
        IdFilter = ids is null ? null : new System.Collections.Generic.HashSet<int>(ids);
        _ = ReloadAsync();
    }

    partial void OnSelectedTypeFilterChanged(Option? value)
    {
        _ = ReloadAsync();
    }

    partial void OnSelectedMoistureMinFilterChanged(MoistureOption? value)
    {
        _ = ReloadAsync();
    }

    partial void OnSelectedMoistureMaxFilterChanged(MoistureOption? value)
    {
        _ = ReloadAsync();
    }

    partial void OnSelectedPhClassMinFilterChanged(PhClassOption? value)
    {
        _ = ReloadAsync();
    }

    partial void OnSelectedPhClassMaxFilterChanged(PhClassOption? value)
    {
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        var svc = ServiceRegistry.PlantDbService;
        Items.Clear();
        if (svc is null || !svc.IsOpen)
            return;
        var rows = await svc.GetPlantsAsync(page: Page, pageSize: PageSize, search: Search);
        // Build moisture id -> ordinal map for filtering
        var moistOrd = MoistureOptions.ToDictionary(m => m.Id, m => m.Ordinal);
        foreach (var r in rows)
        {
            if (IdFilter is not null && !IdFilter.Contains(r.Id))
                continue;
            // Apply quick filters
            if (SelectedTypeFilter is not null && r.TypeId != SelectedTypeFilter.Id)
                continue;
            if (SelectedMoistureMinFilter is not null)
            {
                if (!r.MoistureMinLevelId.HasValue)
                    continue;
                if (!moistOrd.TryGetValue(r.MoistureMinLevelId.Value, out var rowMinOrd))
                    continue;
                if (rowMinOrd < SelectedMoistureMinFilter.Ordinal)
                    continue;
            }
            if (SelectedMoistureMaxFilter is not null)
            {
                if (!r.MoistureMaxLevelId.HasValue)
                    continue;
                if (!moistOrd.TryGetValue(r.MoistureMaxLevelId.Value, out var rowMaxOrd))
                    continue;
                if (rowMaxOrd > SelectedMoistureMaxFilter.Ordinal)
                    continue;
            }
            if (SelectedPhClassMinFilter is not null)
            {
                if (!r.PhMin.HasValue)
                    continue;
                if (r.PhMin.Value < SelectedPhClassMinFilter.MinPh)
                    continue;
            }
            if (SelectedPhClassMaxFilter is not null)
            {
                if (!r.PhMax.HasValue)
                    continue;
                if (r.PhMax.Value > SelectedPhClassMaxFilter.MaxPh)
                    continue;
            }
            Items.Add(
                new PlantRow
                {
                    Id = r.Id,
                    Genus = r.Genus,
                    Species = r.Species,
                    Cultivar = r.Cultivar,
                    BotanicalNameDisplay = r.BotanicalNameDisplay,
                    TypeId = r.TypeId,
                    PhMin = r.PhMin,
                    PhMax = r.PhMax,
                    MoistureMinLevelId = r.MoistureMinLevelId,
                    MoistureMaxLevelId = r.MoistureMaxLevelId,
                }
            );
        }
        // assign selection references for dropdowns
        foreach (var it in Items)
        {
            it.SelectedType = it.TypeId.HasValue
                ? TypeOptions.FirstOrDefault(o => o.Id == it.TypeId.Value)
                : null;
            it.SelectedMoistureMin = it.MoistureMinLevelId.HasValue
                ? MoistureOptions.FirstOrDefault(o => o.Id == it.MoistureMinLevelId.Value)
                : null;
            it.SelectedMoistureMax = it.MoistureMaxLevelId.HasValue
                ? MoistureOptions.FirstOrDefault(o => o.Id == it.MoistureMaxLevelId.Value)
                : null;
        }
    }

    private async Task LoadLookupsAsync()
    {
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen)
            return;
        TypeOptions.Clear();
        MoistureOptions.Clear();
        PhClassOptions.Clear();
        var types = await svc.GetPlantTypesAsync();
        foreach (var t in types)
            TypeOptions.Add(new Option(t.Id, t.NamePl));
        var moist = await svc.GetMoistureLevelsAsync();
        foreach (var m in moist)
            MoistureOptions.Add(new MoistureOption(m.Id, m.NamePl, m.Ordinal));
        var phs = await svc.GetPhClassesAsync();
        foreach (var p in phs)
            PhClassOptions.Add(new PhClassOption(p.Id, p.NamePl, p.MinPh, p.MaxPh));
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        Page += 1;
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (Page > 1)
        {
            Page -= 1;
            await ReloadAsync();
        }
    }

    [RelayCommand]
    private void AddNew()
    {
        var row = new PlantRow
        {
            Id = 0,
            Genus = "Genus",
            Species = null,
            Cultivar = null,
            BotanicalNameDisplay = "",
        };
        Items.Insert(0, row);
        SelectedItem = row;
    }

    [RelayCommand]
    private async Task SaveSelectedAsync()
    {
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen || SelectedItem is null)
            return;
        // Validation: if both pH present and min>max, swap
        if (
            SelectedItem.PhMin.HasValue
            && SelectedItem.PhMax.HasValue
            && SelectedItem.PhMin.Value > SelectedItem.PhMax.Value
        )
        {
            (SelectedItem.PhMin, SelectedItem.PhMax) = (SelectedItem.PhMax, SelectedItem.PhMin);
        }
        // Validation: ensure moisture ordinal order if both set
        if (SelectedItem.MoistureMinLevelId.HasValue && SelectedItem.MoistureMaxLevelId.HasValue)
        {
            var minOpt = MoistureOptions.FirstOrDefault(o =>
                o.Id == SelectedItem.MoistureMinLevelId.Value
            );
            var maxOpt = MoistureOptions.FirstOrDefault(o =>
                o.Id == SelectedItem.MoistureMaxLevelId.Value
            );
            if (minOpt is not null && maxOpt is not null && minOpt.Ordinal > maxOpt.Ordinal)
            {
                (SelectedItem.MoistureMinLevelId, SelectedItem.MoistureMaxLevelId) = (
                    SelectedItem.MoistureMaxLevelId,
                    SelectedItem.MoistureMinLevelId
                );
            }
        }
        var dto = new PlantListRowDto
        {
            Id = SelectedItem.Id,
            Genus = SelectedItem.Genus,
            Species = SelectedItem.Species,
            Cultivar = SelectedItem.Cultivar,
            TypeId = SelectedItem.SelectedType?.Id ?? SelectedItem.TypeId,
            BotanicalNameDisplay = SelectedItem.BotanicalNameDisplay,
            PhMin = SelectedItem.PhMin,
            PhMax = SelectedItem.PhMax,
            MoistureMinLevelId =
                SelectedItem.SelectedMoistureMin?.Id ?? SelectedItem.MoistureMinLevelId,
            MoistureMaxLevelId =
                SelectedItem.SelectedMoistureMax?.Id ?? SelectedItem.MoistureMaxLevelId,
        };
        var id = await svc.UpsertPlantBasicAsync(dto);
        SelectedItem.Id = id;
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen || SelectedItem is null)
            return;
        if (SelectedItem.Id > 0)
        {
            await svc.DeletePlantAsync(SelectedItem.Id);
        }
        Items.Remove(SelectedItem);
        SelectedItem = null;
    }

    // M2M edit commands using a checklist dialog
    [RelayCommand]
    private async Task EditExposuresAsync()
    {
        if (SelectedItem is null)
            return;
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen)
            return;
        var all = await svc.GetExposuresAsync();
        var selected = await svc.GetPlantExposuresAsync(SelectedItem.Id);
        var picked = await ShowChecklistDialog(
            "Exposures",
            all.Select(a => (a.Id, a.NamePl)),
            selected
        );
        if (picked is not null)
        {
            await svc.SetPlantExposuresAsync(SelectedItem.Id, picked);
        }
    }

    [RelayCommand]
    private async Task EditHabitsAsync()
    {
        if (SelectedItem is null)
            return;
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen)
            return;
        var all = await svc.GetHabitsAsync();
        var selected = await svc.GetPlantHabitsAsync(SelectedItem.Id);
        var picked = await ShowChecklistDialog(
            "Habits",
            all.Select(a => (a.Id, a.NamePl)),
            selected
        );
        if (picked is not null)
        {
            await svc.SetPlantHabitsAsync(SelectedItem.Id, picked);
        }
    }

    [RelayCommand]
    private async Task EditSoilTraitsAsync()
    {
        if (SelectedItem is null)
            return;
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen)
            return;
        var all = await svc.GetSoilTraitsAsync();
        var selected = await svc.GetPlantSoilTraitsAsync(SelectedItem.Id);
        var picked = await ShowChecklistDialog(
            "Soil Traits",
            all.Select(a => (a.Id, a.NamePl)),
            selected
        );
        if (picked is not null)
        {
            await svc.SetPlantSoilTraitsAsync(SelectedItem.Id, picked);
        }
    }

    [RelayCommand]
    private async Task EditFeaturesAsync()
    {
        if (SelectedItem is null)
            return;
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen)
            return;
        var all = await svc.GetFeaturesAsync();
        var selected = await svc.GetPlantFeaturesAsync(SelectedItem.Id);
        var picked = await ShowChecklistDialog(
            "Features",
            all.Select(a => (a.Id, a.NamePl)),
            selected
        );
        if (picked is not null)
        {
            await svc.SetPlantFeaturesAsync(SelectedItem.Id, picked);
        }
    }

    // Colors by attribute
    [RelayCommand]
    private async Task EditFlowerColorsAsync() => await EditColorsForAttribute("flower");

    [RelayCommand]
    private async Task EditFruitColorsAsync() => await EditColorsForAttribute("fruit");

    [RelayCommand]
    private async Task EditAutumnFoliageColorsAsync() =>
        await EditColorsForAttribute("autumn_foliage");

    private async Task EditColorsForAttribute(string attribute)
    {
        if (SelectedItem is null)
            return;
        var svc = ServiceRegistry.PlantDbService;
        if (svc is null || !svc.IsOpen)
            return;
        var all = await svc.GetColorsAsync();
        var selected = await svc.GetPlantColorsAsync(SelectedItem.Id, attribute);
        var picked = await ShowChecklistDialog(
            $"Colors: {attribute}",
            all.Select(a => (a.Id, a.NamePl)),
            selected
        );
        if (picked is not null)
        {
            await svc.SetPlantColorsAsync(SelectedItem.Id, attribute, picked);
        }
    }

    private async Task<IReadOnlyList<int>?> ShowChecklistDialog(
        string title,
        IEnumerable<(int Id, string Label)> allItems,
        IList<int> preselected
    )
    {
        var dialog = new Views.Dialogs.ChecklistDialog
        {
            DataContext = new ViewModels.Dialogs.ChecklistDialogViewModel(
                title,
                allItems
                    .Select(x => new ViewModels.Dialogs.ChecklistItem(
                        x.Id,
                        x.Label,
                        preselected.Contains(x.Id)
                    ))
                    .ToList()
            ),
        };
        var result = await dialog.ShowDialog<int[]?>(ServiceRegistry.Root!);
        return result;
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SelectedTypeFilter = null;
        SelectedMoistureMinFilter = null;
        SelectedMoistureMaxFilter = null;
        SelectedPhClassMinFilter = null;
        SelectedPhClassMaxFilter = null;
        IdFilter = null;
        await ReloadAsync();
    }

    public sealed class PlantRow : ObservableObject
    {
        public int Id { get; set; }
        public string Genus { get; set; } = string.Empty;
        public string? Species { get; set; }
        public string? Cultivar { get; set; }
        public string BotanicalNameDisplay { get; set; } = string.Empty;
        public int? TypeId { get; set; }
        public Option? SelectedType { get; set; }
        public double? PhMin { get; set; }
        public double? PhMax { get; set; }
        public int? MoistureMinLevelId { get; set; }
        public int? MoistureMaxLevelId { get; set; }
        public MoistureOption? SelectedMoistureMin { get; set; }
        public MoistureOption? SelectedMoistureMax { get; set; }
        public PhClassOption? SelectedPhClassMin
        {
            get => _selectedPhClassMin;
            set
            {
                _selectedPhClassMin = value;
                if (value is not null)
                {
                    PhMin = value.MinPh;
                    OnPropertyChanged(nameof(PhMin));
                }
            }
        }
        private PhClassOption? _selectedPhClassMin;
        public PhClassOption? SelectedPhClassMax
        {
            get => _selectedPhClassMax;
            set
            {
                _selectedPhClassMax = value;
                if (value is not null)
                {
                    PhMax = value.MaxPh;
                    OnPropertyChanged(nameof(PhMax));
                }
            }
        }
        private PhClassOption? _selectedPhClassMax;
    }

    public sealed record Option(int Id, string Name);

    public sealed record MoistureOption(int Id, string Name, int Ordinal);

    public sealed record PhClassOption(int Id, string Name, double MinPh, double MaxPh);
}
