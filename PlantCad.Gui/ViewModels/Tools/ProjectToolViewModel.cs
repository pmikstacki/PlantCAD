using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;
using PlantCad.Gui.ViewModels.Dialogs;

namespace PlantCad.Gui.ViewModels.Tools;

public partial class ProjectToolViewModel : Tool
{
    [ObservableProperty]
    private CadProjectInfo? info;

    public ObservableCollection<CountItem> EntityCounts { get; } = new();
    public ObservableCollection<BlockUsageItem> Blocks { get; } = new();
    public ObservableCollection<HatchUsageItem> Hatches { get; } = new();

    private PlantCad.Gui.ViewModels.Documents.CadDocumentViewModel? _currentDoc;

    public ProjectToolViewModel()
    {
        Title = "Project";
        CanClose = false;
        DockGroup = "Tools";

        ServiceRegistry.ActiveDocumentChanged += OnActiveDocumentChanged;
        OnActiveDocumentChanged(ServiceRegistry.ActiveDocument);
    }

    private void OnActiveDocumentChanged(
        PlantCad.Gui.ViewModels.Documents.CadDocumentViewModel? doc
    )
    {
        if (_currentDoc != null)
        {
            _currentDoc.PropertyChanged -= OnDocumentPropertyChanged;
        }
        _currentDoc = doc;
        if (_currentDoc == null)
        {
            Info = null;
            EntityCounts.Clear();
            return;
        }
        _currentDoc.PropertyChanged += OnDocumentPropertyChanged;
        RebuildFromModel(_currentDoc.Model);
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var doc = ServiceRegistry.ActiveDocument;
        if (doc == null)
        {
            return;
        }
        if (e.PropertyName == nameof(doc.Model))
        {
            RebuildFromModel(doc.Model);
        }
    }

    private void RebuildFromModel(CadModel? model)
    {
        EntityCounts.Clear();
        Blocks.Clear();
        Hatches.Clear();
        if (model == null)
        {
            Info = null;
            return;
        }
        Info = model.ProjectInfo;

        void add(string name, int count)
        {
            if (count > 0)
            {
                EntityCounts.Add(new CountItem { Name = name, Count = count });
            }
        }

        add("Polylines", model.Polylines?.Count ?? 0);
        add("Lines", model.Lines?.Count ?? 0);
        add("Circles", model.Circles?.Count ?? 0);
        add("Arcs", model.Arcs?.Count ?? 0);
        add("Inserts", model.Inserts?.Count ?? 0);
        add("Ellipses", model.Ellipses?.Count ?? 0);
        add("Texts", model.Texts?.Count ?? 0);
        add("MTexts", model.MTexts?.Count ?? 0);
        add("Splines", model.Splines?.Count ?? 0);
        add("Solids", model.Solids?.Count ?? 0);
        add("Hatches", model.Hatches?.Count ?? 0);
        add("Points", model.Points?.Count ?? 0);
        add("Leaders", model.Leaders?.Count ?? 0);
        add("Dimensions (Aligned)", model.DimensionsAligned?.Count ?? 0);
        add("Dimensions (Linear)", model.DimensionsLinear?.Count ?? 0);
        add("Rays", model.Rays?.Count ?? 0);
        add("XLines", model.XLines?.Count ?? 0);
        add("Wipeouts", model.Wipeouts?.Count ?? 0);
        add("Shapes", model.Shapes?.Count ?? 0);
        add("Tolerances", model.Tolerances?.Count ?? 0);
        add("Tables", model.Tables?.Count ?? 0);
        add("Underlays", model.Underlays?.Count ?? 0);

        // Build blocks usage (by Insert.BlockName)
        if (model.Inserts != null && model.Inserts.Count > 0)
        {
            foreach (
                var grp in model
                    .Inserts.Where(i => !string.IsNullOrWhiteSpace(i.BlockName))
                    .GroupBy(i => i.BlockName)
                    .OrderByDescending(g => g.Count())
            )
            {
                Blocks.Add(new BlockUsageItem { Name = grp.Key, Count = grp.Count() });
            }
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async System.Threading.Tasks.Task OpenBlockAttributesAsync(object? param)
    {
        try
        {
            if (param is not BlockUsageItem row)
            {
                return;
            }
            var src = Info?.FilePath;
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(row.Name))
            {
                return;
            }
            var vm = new BlockAttributesDialogViewModel();
            vm.Initialize(src!, row.Name);
            var dlg = new PlantCad.Gui.Views.Dialogs.BlockAttributesDialog { DataContext = vm };
            await dlg.ShowDialog(ServiceRegistry.Root!);
            // Invalidate cached previews/details for this block
            PlantCad.Gui.Services.BlockModelService.Invalidate(src!, row.Name);
        }
        catch (System.Exception ex)
        {
            ServiceRegistry.LogsTool?.Append(
                $"[{System.DateTime.Now:HH:mm:ss}] ERROR OpenBlockAttributes: {ex.Message}"
            );
            throw;
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async System.Threading.Tasks.Task ShowBlockDetailsAsync(object? param)
    {
        try
        {
            if (param is not BlockUsageItem row)
            {
                return;
            }
            var src = Info?.FilePath;
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(row.Name))
            {
                return;
            }
            var (counts, layers, extents) = PlantCad.Gui.Services.BlockModelService.GetDetails(
                src!,
                row.Name
            );
            var vm = new BlockDetailsDialogViewModel
            {
                BlockName = row.Name,
                SourcePath = src!,
                Extents = extents,
                Entities = counts
                    .Select(kv => new BlockDetailsDialogViewModel.Entry(kv.Key, kv.Value))
                    .OrderByDescending(e => e.Count)
                    .ToList(),
                Layers = layers.OrderBy(s => s).ToList(),
            };
            var dlg = new PlantCad.Gui.Views.Dialogs.BlockDetailsDialog { DataContext = vm };
            await dlg.ShowDialog(ServiceRegistry.Root!);
        }
        catch (System.Exception ex)
        {
            ServiceRegistry.LogsTool?.Append(
                $"[{System.DateTime.Now:HH:mm:ss}] ERROR ShowBlockDetails: {ex.Message}"
            );
            throw;
        }
    }
}

public sealed class BlockUsageItem
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class HatchUsageItem
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}
