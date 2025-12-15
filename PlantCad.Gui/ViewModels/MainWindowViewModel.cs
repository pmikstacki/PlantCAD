using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Serializer;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Docking;
using PlantCad.Gui.Services;
using PlantCad.Gui.ViewModels.Documents;

namespace PlantCad.Gui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DockFactory _factory;
    private readonly DockSerializer _serializer;
    public DockFactory Factory => _factory;
    private readonly string _layoutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DwgTools",
        "layout.json"
    );
    private readonly ILogger _logger;

    [ObservableProperty]
    private IDock? layout;

    // Dynamic Tools menu entries built from current layout
    public ObservableCollection<ToolMenuItem> AvailableTools { get; } = new();

    // UI-level items (per user's request): actual MenuItem controls for the Tools submenu
    public ObservableCollection<MenuItem> MenuChoices { get; } = new();

    public sealed class ToolMenuItem
    {
        public string Id { get; }
        public string Title { get; }
        public ICommand? Command { get; init; }

        public ToolMenuItem(string id, string title)
        {
            Id = id;
            Title = title;
        }
    }

    private void BuildAvailableTools()
    {
        try
        {
            AvailableTools.Clear();
            if (Layout is not IDock root)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var items = new List<ToolMenuItem>();

            foreach (var d in Enumerate(root))
            {
                if (d is Dock.Model.Mvvm.Controls.Tool t && !string.IsNullOrWhiteSpace(t.Id))
                {
                    if (seen.Add(t.Id))
                    {
                        var title = string.IsNullOrWhiteSpace(t.Title) ? t.Id : t.Title;
                        var idCopy = t.Id; // capture for closure
                        items.Add(
                            new ToolMenuItem(t.Id, title)
                            {
                                Command = new RelayCommand(() => ShowTool(idCopy)),
                            }
                        );
                    }
                }
            }

            // Also include any tools registered in the factory locator that might not currently be present in the layout
            var locator = _factory?.DockableLocator;
            if (locator is not null)
            {
                foreach (var kv in locator)
                {
                    var id = kv.Key;
                    if (!seen.Add(id))
                        continue;
                    try
                    {
                        var instance = kv.Value?.Invoke();
                        if (
                            instance is Dock.Model.Mvvm.Controls.Tool t
                            && !string.IsNullOrWhiteSpace(id)
                        )
                        {
                            var title = string.IsNullOrWhiteSpace(t.Title) ? id : t.Title;
                            var idCopy = id; // capture for closure
                            items.Add(
                                new ToolMenuItem(id, title)
                                {
                                    Command = new RelayCommand(() => ShowTool(idCopy)),
                                }
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to probe tool instance for id {Id}", id);
                    }
                }
            }

            foreach (
                var item in items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            )
            {
                AvailableTools.Add(item);
            }

            // Build UI MenuItem collection for direct binding in XAML
            MenuChoices.Clear();
            foreach (
                var entry in items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            )
            {
                MenuChoices.Add(new MenuItem { Header = entry.Title, Command = entry.Command });
            }

            _logger.LogInformation("AvailableTools built: {Count}", AvailableTools.Count);
            foreach (var it in AvailableTools)
            {
                _logger.LogInformation("Tool Menu Item: {Id} - {Title}", it.Id, it.Title);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build AvailableTools list");
            throw;
        }
    }

    public MainWindowViewModel(
        DockFactory factory,
        DockSerializer serializer,
        ILogger<MainWindowViewModel> logger
    )
    {
        _logger = logger;
        _factory = factory;
        _serializer = serializer;
        var root = factory.CreateLayout();
        _factory.InitLayout(root);
        Layout = root;
        NormalizeToolDockSizes(root);
        BuildAvailableTools();
        TryLoadLayoutOnStartup();
        _logger.LogInformation("GUI logger initialized");
        ServiceRegistry.LogsTool?.Append(
            $"[{DateTime.Now:HH:mm:ss}] INFO Bootstrap: GUI log connected"
        );

        // Subscribe to Plants document open request
        ServiceRegistry.OpenPlantsDocumentRequested += () =>
        {
            try
            {
                var doc = new PlantsDocumentViewModel
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Plants",
                    CanClose = true,
                };
                if (Layout is IDock rootDock)
                {
                    var documents = FindDocumentDock(rootDock);
                    if (documents is null)
                    {
                        _logger.LogWarning("No DocumentDock found to host Plants document");
                    }
                    else
                    {
                        _factory.AddDockable(documents, doc);
                        _factory.SetActiveDockable(doc);
                        _factory.SetFocusedDockable(documents, doc);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open Plants document");
            }
        };

        // Subscribe to Plants document open with filter request
        ServiceRegistry.OpenPlantsDocumentWithFilterRequested += (ids) =>
        {
            try
            {
                var doc = new PlantsDocumentViewModel
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Plants",
                    CanClose = true,
                };
                doc.SetIdFilter(ids);
                if (Layout is IDock rootDock)
                {
                    var documents = FindDocumentDock(rootDock);
                    if (documents is null)
                    {
                        _logger.LogWarning(
                            "No DocumentDock found to host Plants document (filtered)"
                        );
                    }
                    else
                    {
                        _factory.AddDockable(documents, doc);
                        _factory.SetActiveDockable(doc);
                        _factory.SetFocusedDockable(documents, doc);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open filtered Plants document");
            }
        };
    }

    private void ActivateOrInsertDockable(IDockable dockable)
    {
        try
        {
            if (Layout is not IDock root)
            {
                return;
            }

            if (dockable.Owner is IDock owner)
            {
                _logger.LogInformation(
                    "Tool {Id} has owner {OwnerType}:{OwnerId}",
                    dockable.Id,
                    owner.GetType().Name,
                    (owner as IDockable)?.Id
                );
                if (owner is Dock.Model.Controls.IToolDock ownerToolDock)
                {
                    EnsureProportion(ownerToolDock);
                    var visibleCount = ownerToolDock.VisibleDockables?.Count ?? 0;
                    _logger.LogInformation(
                        "Owner ToolDock {OwnerId} has {Count} visible dockables before activation",
                        (owner as IDockable)?.Id,
                        visibleCount
                    );
                    _factory.SetActiveDockable(dockable);
                    _factory.SetFocusedDockable(owner, dockable);
                    return;
                }
                // Owner exists but is not a ToolDock -> relocate into a ToolDock so it's visible
                var dockGroup1 = (dockable as Dock.Model.Controls.ITool)?.DockGroup;
                var relocateDock = FindToolDock(root, dockGroup1) ?? FindFirstToolDock(root);
                if (relocateDock is null)
                {
                    _logger.LogWarning("No ToolDock found to host tool {Id}", dockable.Id);
                    return;
                }
                _logger.LogInformation(
                    "Relocating tool {Id} from owner {OwnerType}:{OwnerId} into ToolDock {TargetId}",
                    dockable.Id,
                    owner.GetType().Name,
                    (owner as IDockable)?.Id,
                    (relocateDock as IDockable)?.Id
                );
                EnsureProportion(relocateDock);
                _factory.AddDockable(relocateDock, dockable);
                _factory.SetActiveDockable(dockable);
                _factory.SetFocusedDockable(relocateDock, dockable);
                return;
            }

            // Find a suitable ToolDock
            var dockGroup2 = (dockable as Dock.Model.Controls.ITool)?.DockGroup;
            var targetDock = FindToolDock(root, dockGroup2);
            if (targetDock is null)
            {
                // Fallback to first ToolDock
                targetDock = FindFirstToolDock(root);
            }
            if (targetDock is null)
            {
                _logger.LogWarning("No ToolDock found to host tool {Id}", dockable.Id);
                return;
            }

            _logger.LogInformation(
                "Inserting tool {Id} into ToolDock {DockId}",
                dockable.Id,
                (targetDock as IDockable)?.Id
            );
            EnsureProportion(targetDock);
            var beforeCount = targetDock.VisibleDockables?.Count ?? 0;
            _logger.LogInformation(
                "Target ToolDock {DockId} had {Count} visible dockables before insert",
                (targetDock as IDockable)?.Id,
                beforeCount
            );
            _factory.AddDockable(targetDock, dockable);
            _factory.SetActiveDockable(dockable);
            _factory.SetFocusedDockable(targetDock, dockable);
            var afterCount = targetDock.VisibleDockables?.Count ?? 0;
            _logger.LogInformation(
                "Target ToolDock {DockId} now has {Count} visible dockables after insert",
                (targetDock as IDockable)?.Id,
                afterCount
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate or insert dockable {Id}", dockable?.Id);
        }
    }

    private static Dock.Model.Controls.IToolDock? FindToolDock(IDock root, string? dockGroup)
    {
        foreach (var d in Enumerate(root))
        {
            if (d is Dock.Model.Controls.IToolDock td)
            {
                if (string.IsNullOrEmpty(dockGroup) || td.DockGroup == dockGroup)
                {
                    return td;
                }
            }
        }
        return null;
    }

    private static Dock.Model.Controls.IToolDock? FindFirstToolDock(IDock root)
    {
        foreach (var d in Enumerate(root))
        {
            if (d is Dock.Model.Controls.IToolDock td)
            {
                return td;
            }
        }
        return null;
    }

    private static Dock.Model.Controls.IDocumentDock? FindDocumentDock(IDock root)
    {
        foreach (var d in Enumerate(root))
        {
            if (d is Dock.Model.Controls.IDocumentDock dd)
            {
                return dd;
            }
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<IDockable> Enumerate(IDockable node)
    {
        yield return node;
        if (node is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (var child in children)
            {
                if (child is null)
                    continue;
                foreach (var sub in Enumerate(child))
                {
                    yield return sub;
                }
            }
        }
    }

    private void NormalizeToolDockSizes(IDock root)
    {
        foreach (var d in Enumerate(root))
        {
            if (d is Dock.Model.Controls.IToolDock td)
            {
                EnsureProportion(td);
            }
        }
    }

    private void EnsureProportion(Dock.Model.Controls.IToolDock td)
    {
        if (td.Proportion <= 0.01)
        {
            var before = td.Proportion;
            td.Proportion = 0.25; // sensible default so pane is visible
            _logger.LogInformation(
                "Adjusted ToolDock {Id} proportion from {Before} to {After}",
                (td as IDockable)?.Id,
                before,
                td.Proportion
            );
        }
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        var dialogs = ServiceRegistry.FileDialogService;
        var cad = ServiceRegistry.CadFileService;
        var doc = ServiceRegistry.ActiveDocument;
        if (dialogs == null || cad == null || doc == null)
        {
            return;
        }
        var path = await dialogs.ShowOpenCadAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        var model = await cad.OpenAsync(path);
        doc.Model = model;
        doc.Title = Path.GetFileName(path);
        _logger.LogInformation("Opened CAD file: {File}", path);
    }

    [RelayCommand]
    private void Exit()
    {
        ServiceRegistry.Root?.Close();
    }

    // Plants menu: New/Open database
    [RelayCommand]
    private async Task NewPlantDbAsync()
    {
        try
        {
            var dialogs = ServiceRegistry.FileDialogService;
            var db = ServiceRegistry.PlantDbService;
            if (dialogs is null || db is null)
                return;
            var path = await dialogs.ShowSaveDatabaseAsync("plantcad.sqlite");
            if (string.IsNullOrWhiteSpace(path))
                return;
            await db.CreateNewAsync(path);
            ShowPlantDbTool();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create new plant database");
            throw;
        }
    }

    [RelayCommand]
    private async Task OpenPlantDbAsync()
    {
        try
        {
            var dialogs = ServiceRegistry.FileDialogService;
            var db = ServiceRegistry.PlantDbService;
            if (dialogs is null || db is null)
                return;
            var path = await dialogs.ShowOpenDatabaseAsync();
            if (string.IsNullOrWhiteSpace(path))
                return;
            await db.OpenAsync(path);
            ShowPlantDbTool();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open plant database");
            throw;
        }
    }

    // Layout: Save / Load / Reset
    [RelayCommand]
    private async Task SaveLayoutAsync()
    {
        try
        {
            if (Layout is IDock dock)
            {
                var dir = Path.GetDirectoryName(_layoutPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                await using var stream = File.Create(_layoutPath);
                _serializer.Save(stream, dock);
                _logger.LogInformation("Layout saved to {Path}", _layoutPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save layout");
            throw;
        }
    }

    [RelayCommand]
    private async Task LoadLayoutAsync()
    {
        try
        {
            if (!File.Exists(_layoutPath))
            {
                _logger.LogWarning("Layout file not found: {Path}", _layoutPath);
                return;
            }
            await using var stream = File.OpenRead(_layoutPath);
            var layout = _serializer.Load<IDock?>(stream);
            if (layout is null)
            {
                _logger.LogWarning("Loaded layout is null");
                return;
            }
            _factory.InitLayout(layout);
            Layout = layout;
            RebindToolReferences(layout);
            NormalizeToolDockSizes(layout);
            BuildAvailableTools();
            _logger.LogInformation("Layout loaded from {Path}", _layoutPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load layout");
            throw;
        }
    }

    [RelayCommand]
    private void ResetLayout()
    {
        try
        {
            var root = _factory.CreateLayout();
            _factory.InitLayout(root);
            Layout = root;
            NormalizeToolDockSizes(root);
            BuildAvailableTools();
            _logger.LogInformation("Layout reset to defaults");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset layout");
            throw;
        }
    }

    private async void TryLoadLayoutOnStartup()
    {
        try
        {
            if (File.Exists(_layoutPath))
            {
                await LoadLayoutAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while auto-loading layout");
        }
    }

    // View menu commands to show tools
    [RelayCommand]
    private void ShowTool(string id)
    {
        try
        {
            if (Layout is not IDock root || string.IsNullOrWhiteSpace(id))
            {
                return;
            }
            foreach (var d in Enumerate(root))
            {
                if (
                    d is Dock.Model.Mvvm.Controls.Tool t
                    && string.Equals(t.Id, id, StringComparison.Ordinal)
                )
                {
                    ActivateOrInsertDockable(t);
                    return;
                }
            }
            // Not found in layout: try to create via factory locator
            var locator = _factory?.DockableLocator;
            if (locator != null && locator.TryGetValue(id, out var factory))
            {
                var instance = factory?.Invoke();
                if (instance is Dock.Model.Mvvm.Controls.Tool created)
                {
                    ActivateOrInsertDockable(created);
                    return;
                }
            }
            _logger.LogWarning("Tool with id {Id} not found and could not be created", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show tool {Id}", id);
            throw;
        }
    }

    [RelayCommand]
    private void ShowProjectTool()
    {
        if (ServiceRegistry.ProjectTool is { } vm)
        {
            ActivateOrInsertDockable(vm);
        }
    }

    [RelayCommand]
    private void ShowLayersTool()
    {
        if (ServiceRegistry.LayersTool is { } vm)
        {
            ActivateOrInsertDockable(vm);
        }
    }

    [RelayCommand]
    private void ShowCountsTool()
    {
        if (ServiceRegistry.CountsTool is { } vm)
        {
            ActivateOrInsertDockable(vm);
        }
    }

    [RelayCommand]
    private void ShowPlantDbTool()
    {
        if (ServiceRegistry.PlantDbTool is { } vm)
        {
            ActivateOrInsertDockable(vm);
        }
    }

    [RelayCommand]
    private void ShowPropertiesTool()
    {
        if (ServiceRegistry.PropertiesTool is { } vm)
        {
            ActivateOrInsertDockable(vm);
        }
    }

    [RelayCommand]
    private void ShowLogsTool()
    {
        if (ServiceRegistry.LogsTool is { } vm)
        {
            ActivateOrInsertDockable(vm);
        }
    }

    [RelayCommand]
    private void TestLog()
    {
        _logger.LogInformation("Test log from menu at {Time}", DateTime.Now.ToString("HH:mm:ss"));
        ServiceRegistry.LogsTool?.Append(
            $"[{DateTime.Now:HH:mm:ss}] INFO Manual: GUI Append working"
        );
    }

    private static void RebindToolReferences(IDock root)
    {
        foreach (var d in Enumerate(root))
        {
            if (string.IsNullOrEmpty(d.Id))
            {
                continue;
            }
            switch (d.Id)
            {
                case "ProjectTool":
                    ServiceRegistry.ProjectTool =
                        d as PlantCad.Gui.ViewModels.Tools.ProjectToolViewModel;
                    break;
                case "LayersTool":
                    ServiceRegistry.LayersTool =
                        d as PlantCad.Gui.ViewModels.Tools.LayersToolViewModel;
                    break;
                case "ModulesTool":
                    ServiceRegistry.ModulesTool =
                        d as PlantCad.Gui.ViewModels.Tools.ModulesToolViewModel;
                    // If a CAD document is already active, ensure modules get loaded
                    try
                    {
                        ServiceRegistry.ModulesTool?.LoadModulesCommand?.Execute(null);
                    }
                    catch (Exception ex)
                    {
                        // Re-throw with context for logs; upstream catches and logs
                        throw new InvalidOperationException("Failed to trigger modules load after layout rebind", ex);
                    }
                    break;
                case "CountsTool":
                    ServiceRegistry.CountsTool =
                        d as PlantCad.Gui.ViewModels.Tools.CountsToolViewModel;
                    break;
                case "PlantDbTool":
                    ServiceRegistry.PlantDbTool =
                        d as PlantCad.Gui.ViewModels.Tools.PlantDbToolViewModel;
                    break;
                case "PropertiesTool":
                    ServiceRegistry.PropertiesTool =
                        d as PlantCad.Gui.ViewModels.Tools.PropertiesToolViewModel;
                    break;
                case "LogsTool":
                    ServiceRegistry.LogsTool = d as PlantCad.Gui.ViewModels.Tools.LogsToolViewModel;
                    break;
                case "CadDocument":
                    ServiceRegistry.ActiveDocument =
                        d as PlantCad.Gui.ViewModels.Documents.CadDocumentViewModel;
                    break;
            }
        }

        static System.Collections.Generic.IEnumerable<IDockable> Enumerate(IDockable node)
        {
            yield return node;
            if (node is IDock dock && dock.VisibleDockables is { } children)
            {
                foreach (var child in children)
                {
                    if (child is null)
                        continue;
                    foreach (var sub in Enumerate(child))
                    {
                        yield return sub;
                    }
                }
            }
        }
    }
}
