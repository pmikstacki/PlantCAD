using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Services;
using PlantCad.Gui.Services.Modules;

namespace PlantCad.Gui.ViewModels.Tools;

public partial class CountsToolViewModel : Tool
{
    public ObservableCollection<CountsGroupViewModel> Groups { get; } = new();

    public ObservableCollection<BlockFilterItemViewModel> BlockFilters { get; } = new();

    private readonly List<(string GroupName, IDictionary<string, int> Counts)> _rawGroups = new();

    [ObservableProperty]
    private long total;

    public CountsToolViewModel()
    {
        Title = "Counts";
        CanClose = false;
        DockGroup = "Tools";
    }

    [RelayCommand]
    private void RecountModules()
    {
        var doc = ServiceRegistry.ActiveDocument;
        var model = doc?.Model;
        var modules = ModulesState.Current;
        ClearRawGroups();
        if (model == null || modules == null)
        {
            return;
        }
        var svc = new ModulesCountingService();
        var dict = svc.CountInsertsByModuleBreakdownAggregated(model, modules);

        foreach (var kv in dict.OrderBy(kv => kv.Key.Name, System.StringComparer.OrdinalIgnoreCase))
        {
            var name = string.IsNullOrWhiteSpace(kv.Key.Name) ? "<Unnamed>" : kv.Key.Name;
            _rawGroups.Add((name, kv.Value));
        }

        RefreshBlockFiltersFromRaw();
        RebuildGroupsFromRaw();
    }

    public void ShowCounts(System.Collections.Generic.IDictionary<string, int> counts, long total)
    {
        if (counts == null)
        {
            throw new System.ArgumentNullException(nameof(counts));
        }

        ClearRawGroups();
        _rawGroups.Add(("Selection", counts));

        RefreshBlockFiltersFromRaw();
        RebuildGroupsFromRaw();
    }

    [RelayCommand]
    private void SelectAllBlocks()
    {
        foreach (var item in BlockFilters)
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearAllBlocks()
    {
        foreach (var item in BlockFilters)
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task Export()
    {
        var export = ServiceRegistry.ExportService;
        var dialog = ServiceRegistry.FileDialogService;
        var doc = ServiceRegistry.ActiveDocument;
        if (export == null || dialog == null || doc == null)
        {
            return;
        }
        var suggested = (string.IsNullOrWhiteSpace(doc.Title) ? "counts" : doc.Title) + ".xlsx";
        var path = await dialog.ShowSaveExcelAsync(suggested);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        export.ExportCountsToExcel(BuildFlatExportItems(), doc.Title, path);
    }

    private IEnumerable<KeyValuePair<string, int>> BuildFlatExportItems()
    {
        foreach (var group in Groups)
        {
            var groupName = string.IsNullOrWhiteSpace(group.Name) ? "<Unnamed>" : group.Name;
            foreach (var item in group.Items)
            {
                yield return new KeyValuePair<string, int>($"{groupName} | {item.Name}", item.Count);
            }
        }
    }

    private void ClearRawGroups()
    {
        _rawGroups.Clear();
        Groups.Clear();
        Total = 0;
    }

    private void RefreshBlockFiltersFromRaw()
    {
        var selectedByName = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var item in BlockFilters)
        {
            selectedByName[item.Name] = item.IsSelected;
        }

        foreach (var item in BlockFilters)
        {
            item.PropertyChanged -= OnBlockFilterChanged;
        }
        BlockFilters.Clear();

        var allNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var g in _rawGroups)
        {
            foreach (var name in g.Counts.Keys)
            {
                allNames.Add(name);
            }
        }

        foreach (var name in allNames.OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase))
        {
            var isSelected = selectedByName.TryGetValue(name, out var selected) ? selected : true;
            var item = new BlockFilterItemViewModel { Name = name, IsSelected = isSelected };
            item.PropertyChanged += OnBlockFilterChanged;
            BlockFilters.Add(item);
        }
    }

    private void OnBlockFilterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlockFilterItemViewModel.IsSelected))
        {
            RebuildGroupsFromRaw();
        }
    }

    private void RebuildGroupsFromRaw()
    {
        var selected = new HashSet<string>(
            BlockFilters.Where(b => b.IsSelected).Select(b => b.Name),
            System.StringComparer.OrdinalIgnoreCase
        );

        Groups.Clear();
        long total = 0;

        foreach (var raw in _rawGroups)
        {
            var group = new CountsGroupViewModel { Name = raw.GroupName };
            long groupTotal = 0;

            foreach (var item in raw.Counts.OrderByDescending(i => i.Value).ThenBy(i => i.Key, System.StringComparer.OrdinalIgnoreCase))
            {
                if (selected.Count > 0 && !selected.Contains(item.Key))
                {
                    continue;
                }

                group.Items.Add(new CountItem { Name = item.Key, Count = item.Value });
                groupTotal += item.Value;
            }

            group.Total = groupTotal;
            Groups.Add(group);
            total += groupTotal;
        }

        Total = total;
    }
}

public sealed partial class CountsGroupViewModel : ObservableObject
{
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private long total;

    public ObservableCollection<CountItem> Items { get; } = new();
}

public sealed class CountItem
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed partial class BlockFilterItemViewModel : ObservableObject
{
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private bool isSelected;
}
