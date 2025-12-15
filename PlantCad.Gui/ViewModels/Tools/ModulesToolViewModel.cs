using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Models.Modules;
using PlantCad.Gui.Services;
using PlantCad.Gui.Services.Modules;
using PlantCad.Gui.Controls.Modes;
using PlantCad.Gui.Controls.Rendering;
using Avalonia.Controls.Primitives;
using IconPacks.Avalonia.Material;
using System.IO;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Models;
using PlantCad.Gui.Models.Sheets;

namespace PlantCad.Gui.ViewModels.Tools;

public partial class ModulesToolViewModel : Tool
{
    private readonly IModulesStorage _storage = new ModulesStorage();

    // Simple tree backing collection
    public ObservableCollection<ModuleNode> Roots { get; } = new();

    // TreeDataGrid source
    public ITreeDataGridSource? TreeSource { get; private set; }

    [ObservableProperty]
    private ModuleNode? selectedModule;

    [ObservableProperty]
    private bool snapToGrid;

    [ObservableProperty]
    private bool showModules = true;

    [ObservableProperty]
    private bool isInModuleMode;

    private ModuleShapeEditor? _editor;
    private int? _editingShapeIndex;
    private readonly ILogger _logger;
    
    public ModulesToolViewModel()
    {
        Title = "Modules";
        CanClose = false;
        DockGroup = "Tools";
        _logger = ServiceRegistry.LoggerFactory?.CreateLogger("ModulesTool")
                  ?? throw new InvalidOperationException("LoggerFactory not initialized.");
        // Build from current state if any
        RefreshTreeFromState();
        ModulesOverlayRenderer.Visible = ShowModules;
    }

    // External entry: start editing a module by its id (from overlay wrench click)
    public void EditModuleById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }
        try
        {
            // Ensure tree reflects current state
            RefreshTreeFromState();
            var module = ModulesState.FindModuleById(id);
            if (module == null)
            {
                return;
            }
            var node = FindNodeByModel(module);
            if (node == null)
            {
                return;
            }
            DrawShape(node);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start edit for module id {id}: {ex.Message}", ex);
        }
    }

    private ModuleNode? FindNodeByModel(Module target)
    {
        foreach (var root in Roots)
        {
            var found = FindNodeRecursive(root, target);
            if (found != null) return found;
        }
        return null;
    }

    private static ModuleNode? FindNodeRecursive(ModuleNode node, Module target)
    {
        if (ReferenceEquals(node.Model, target))
        {
            return node;
        }
        foreach (var ch in node.Children)
        {
            var f = FindNodeRecursive(ch, target);
            if (f != null) return f;
        }
        return null;
    }

    private void PersistShape(ModuleNode node, System.Collections.Generic.IReadOnlyList<Point> pts)
    {
        try
        {
            var file = GetOrCreateModulesFile();
            var mod = node.Model;
            var poly = new ModulePolygon
            {
                Points = pts.Select(p => new ModulePoint(p.X, p.Y)).ToList(),
            };
            if (mod.Shapes == null)
            {
                mod.Shapes = new System.Collections.Generic.List<ModulePolygon>();
            }
            if (_editingShapeIndex.HasValue && _editingShapeIndex.Value >= 0 && _editingShapeIndex.Value < mod.Shapes.Count)
            {
                mod.Shapes[_editingShapeIndex.Value] = poly;
            }
            else
            {
                mod.Shapes.Add(poly);
            }
            _editingShapeIndex = null;
            ModulesState.SetCurrent(file);
            RefreshTreeFromState();
            ServiceRegistry.ActiveViewport?.RequestInvalidate();
            SaveModulesSync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to persist shape: {ex.Message}", ex);
        }
    }

    private void SaveModulesSync()
    {
        var doc = ServiceRegistry.ActiveDocument;
        var cadPath = doc?.Model?.ProjectInfo?.FilePath;
        if (string.IsNullOrWhiteSpace(cadPath))
        {
            return;
        }
        var file = ModulesState.Current;
        if (file == null)
        {
            return;
        }
        _storage.SaveAsync(cadPath!, file).GetAwaiter().GetResult();
    }

    private bool IsEditingAllowed()
    {
        return ServiceRegistry.ActiveDocument != null && ServiceRegistry.ActiveViewport != null;
    }

    private void EnsureEditor()
    {
        var vp = ServiceRegistry.ActiveViewport;
        if (vp == null)
        {
            throw new InvalidOperationException("Active viewport is not available.");
        }
        if (_editor == null || !ReferenceEquals(vp, GetEditorViewport()))
        {
            _editor = new ModuleShapeEditor(vp);
        }
        _editor.SnapToGrid = SnapToGrid;
    }

    private PlantCad.Gui.Controls.CadViewportControl? GetEditorViewport()
    {
        // Access private field via stored reference only
        return ServiceRegistry.ActiveViewport;
    }

    private static ModulesFile GetOrCreateModulesFile()
    {
        var file = ModulesState.Current;
        if (file != null)
        {
            return file;
        }
        // Create new file bound to current CAD path if available
        var doc = ServiceRegistry.ActiveDocument;
        var path = doc?.Model?.ProjectInfo?.FilePath ?? string.Empty;
        var created = new ModulesFile
        {
            CadFilePath = path,
        };
        ModulesState.SetCurrent(created);
        return created;
    }

    private void RefreshTreeFromState()
    {
        Roots.Clear();
        var file = ModulesState.Current;
        if (file?.RootModules == null || file.RootModules.Count == 0)
        {
            BuildTreeSource();
            return;
        }
        foreach (var m in file.RootModules)
        {
            Roots.Add(BuildNode(m, parent: null));
        }
        BuildTreeSource();
    }

    private ModuleNode BuildNode(Module m, ModuleNode? parent)
    {
        var node = new ModuleNode(m, parent);
        if (m.Children != null)
        {
            foreach (var ch in m.Children)
            {
                node.Children.Add(BuildNode(ch, node));
            }
        }
        return node;
    }

    private void BuildTreeSource()
    {
        IEnumerable<object> GetChildren(object o)
        {
            if (o is ModuleNode mn)
            {
                return mn.Children;
            }
            return Array.Empty<object>();
        }

        Control BuildNameCell(object? data, Avalonia.Controls.INameScope? _)
        {
            if (data is not ModuleNode row)
            {
                return new Avalonia.Controls.TextBlock { Text = string.Empty };
            }
            var nameBox = new Avalonia.Controls.TextBox
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(6,0,6,0),
                Text = row.Name,
            };
            nameBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == Avalonia.Controls.TextBox.TextProperty)
                {
                    row.Name = nameBox.Text ?? string.Empty;
                    SaveModulesSync();
                    ServiceRegistry.ActiveViewport?.RequestInvalidate();
                }
            };
            return nameBox;
        }

        Control BuildDescriptionCell(object? data, Avalonia.Controls.INameScope? _)
        {
            if (data is not ModuleNode row)
            {
                return new Avalonia.Controls.TextBlock { Text = string.Empty };
            }
            var descBox = new Avalonia.Controls.TextBox
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(0,0,6,0),
                Watermark = "Description",
                Text = row.Description,
                Width = 160,
            };
            descBox.PropertyChanged += (_, e) =>
            {
                if (e.Property == Avalonia.Controls.TextBox.TextProperty)
                {
                    row.Description = descBox.Text;
                    SaveModulesSync();
                    ServiceRegistry.ActiveViewport?.RequestInvalidate();
                }
            };
            return descBox;
        }

        Control BuildShapesCell(object? data, Avalonia.Controls.INameScope? _)
        {
            if (data is not ModuleNode row)
            {
                return new Avalonia.Controls.TextBlock { Text = string.Empty };
            }
            return new Avalonia.Controls.TextBlock
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Text = row.ShapesCount.ToString(),
                Margin = new Thickness(0,0,6,0),
                Width = 60,
            };
        }

        Control BuildEditCell(object? data, Avalonia.Controls.INameScope? _)
        {
            if (data is not ModuleNode row)
            {
                return new Avalonia.Controls.TextBlock { Text = string.Empty };
            }
            var icon = new PackIconMaterial { Kind = PackIconMaterialKind.Wrench, Width = 16, Height = 16 };
            var content = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            content.Children.Add(icon);
            var toggle = new ToggleButton { Content = content, Width = 40, Height = 28 };
            toggle.IsChecked = _editor?.IsActive == true && ReferenceEquals(SelectedModule, row);
            toggle.IsCheckedChanged += (_, __) =>
            {
                if (toggle.IsChecked == true)
                {
                    DrawShape(row);
                }
                else
                {
                    FinishShape(row);
                }
            };
            return toggle;
        }

        Control BuildAddChildCell(object? data, Avalonia.Controls.INameScope? _)
        {
            if (data is not ModuleNode row)
            {
                return new Avalonia.Controls.TextBlock { Text = string.Empty };
            }
            var icon = new PackIconMaterial { Kind = PackIconMaterialKind.Plus, Width = 16, Height = 16 };
            var content = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            content.Children.Add(icon);
            var addBtn = new Avalonia.Controls.Button { Content = content, Width = 40, Height = 28, Margin = new Thickness(6,0,0,0) };
            addBtn.Command = AddModuleCommand;
            addBtn.CommandParameter = row;
            return addBtn;
        }

        Control BuildRemoveCell(object? data, Avalonia.Controls.INameScope? _)
        {
            if (data is not ModuleNode row)
            {
                return new Avalonia.Controls.TextBlock { Text = string.Empty };
            }
            var icon = new PackIconMaterial { Kind = PackIconMaterialKind.Delete, Width = 16, Height = 16 };
            var content = new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            content.Children.Add(icon);
            var remBtn = new Avalonia.Controls.Button { Content = content, Width = 40, Height = 28, Margin = new Thickness(6,0,0,0) };
            remBtn.Command = RemoveModuleCommand;
            remBtn.CommandParameter = row;
            return remBtn;
        }

        var nameColumn = new TemplateColumn<object>("Module", new FuncDataTemplate<object>(BuildNameCell));
        var descColumn = new TemplateColumn<object>("Description", new FuncDataTemplate<object>(BuildDescriptionCell));
        var shapesColumn = new TemplateColumn<object>("Shapes", new FuncDataTemplate<object>(BuildShapesCell));
        var editColumn = new TemplateColumn<object>("Edit", new FuncDataTemplate<object>(BuildEditCell));
        var addColumn = new TemplateColumn<object>("Add child", new FuncDataTemplate<object>(BuildAddChildCell));
        var removeColumn = new TemplateColumn<object>("Remove", new FuncDataTemplate<object>(BuildRemoveCell));
        
        var source = new HierarchicalTreeDataGridSource<object>(Roots.Cast<object>())
        {
            Columns =
            {
                new Avalonia.Controls.Models.TreeDataGrid.HierarchicalExpanderColumn<object>(nameColumn, GetChildren),
                descColumn,
                shapesColumn,
                editColumn,
                addColumn,
                removeColumn,
            }
        };
        editColumn.Options.MaxWidth = new GridLength(40);
        addColumn.Options.MaxWidth = new GridLength(40);
        removeColumn.Options.MaxWidth = new GridLength(40);
        
        TreeSource = source;
        OnPropertyChanged(nameof(TreeSource));
    }

    // Keep editor snapping in sync with VM property changes
    partial void OnSnapToGridChanged(bool value)
    {
        if (_editor != null)
        {
            _editor.SnapToGrid = value;
            ServiceRegistry.ActiveViewport?.RequestInvalidate();
        }
    }

    partial void OnShowModulesChanged(bool value)
    {
        ModulesOverlayRenderer.Visible = value;
        ServiceRegistry.ActiveViewport?.RequestInvalidate();
    }

    [RelayCommand]
    private void AddModule(ModuleNode? parent)
    {
        try
        {
            var file = GetOrCreateModulesFile();
            var m = new Module { Name = "New Module" };
            if (parent?.Model != null)
            {
                parent.Model.Children.Add(m);
            }
            else
            {
                file.RootModules.Add(m);
            }
            ModulesState.SetCurrent(file);
            RefreshTreeFromState();
            ServiceRegistry.ActiveViewport?.RequestInvalidate();
            SaveModulesSync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to add module: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    private void RemoveModule(ModuleNode? node)
    {
        try
        {
            if (node == null)
            {
                return;
            }
            var file = GetOrCreateModulesFile();
            if (node.Parent?.Model != null)
            {
                node.Parent.Model.Children.Remove(node.Model);
            }
            else
            {
                file.RootModules.Remove(node.Model);
            }
            ModulesState.SetCurrent(file);
            RefreshTreeFromState();
            ServiceRegistry.ActiveViewport?.RequestInvalidate();
            SaveModulesSync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to remove module: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    private void DrawShape(ModuleNode? node)
    {
        if (!IsEditingAllowed()) return;
        if (node == null) return;
        SelectedModule = node;
        EnsureEditor();
        ServiceRegistry.ActiveViewport?.SetMode(new ModuleEditMode(
            _editor!,
            pts =>
            {
                try
                {
                    PersistShape(node, pts);
                }
                finally
                {
                    ServiceRegistry.ActiveViewport?.ClearMode();
                }
            },
            () => { _editingShapeIndex = null; ServiceRegistry.ActiveViewport?.ClearMode(); },
            node.Name
        ));
        var shapes = node.Model.Shapes;
        if (shapes != null && shapes.Count > 0)
        {
            _editingShapeIndex = shapes.Count - 1;
            var last = shapes[_editingShapeIndex.Value];
            var pts = last.Points?.Select(mp => new Avalonia.Point(mp.X, mp.Y)).ToList() ?? new System.Collections.Generic.List<Avalonia.Point>();
            _editor!.BeginWithPolygon(pts);
        }
        else
        {
            _editingShapeIndex = null;
            _editor!.Begin();
        }
        IsInModuleMode = true;
    }

    [RelayCommand]
    private void FinishShape(ModuleNode? node)
    {
        if (!IsEditingAllowed()) return;
        if (node == null || _editor == null) return;
        if (_editor.TryFinish(out var pts))
        {
            PersistShape(node, pts);
            ServiceRegistry.ActiveViewport?.ClearMode();
        }
        IsInModuleMode = false;
    }

    [RelayCommand]
    private void DeleteShape(ModuleNode? node)
    {
        try
        {
            if (node?.Model?.Shapes == null || node.Model.Shapes.Count == 0) return;
            node.Model.Shapes.RemoveAt(node.Model.Shapes.Count - 1);
            var file = GetOrCreateModulesFile();
            ModulesState.SetCurrent(file);
            RefreshTreeFromState();
            ServiceRegistry.ActiveViewport?.RequestInvalidate();
            SaveModulesSync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to delete shape: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task LoadModules()
    {
        try
        {
            var doc = ServiceRegistry.ActiveDocument;
            var cadPath = doc?.Model?.ProjectInfo?.FilePath;
            if (string.IsNullOrWhiteSpace(cadPath)) return;
            var file = await _storage.LoadAsync(cadPath);
            ModulesState.SetCurrent(file);
            RefreshTreeFromState();
            ServiceRegistry.ActiveViewport?.RequestInvalidate();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Load modules failed: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SaveModules()
    {
        try
        {
            var doc = ServiceRegistry.ActiveDocument;
            var cadPath = doc?.Model?.ProjectInfo?.FilePath;
            if (string.IsNullOrWhiteSpace(cadPath)) return;
            var file = ModulesState.Current;
            if (file == null) return;
            await _storage.SaveAsync(cadPath, file);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Save modules failed: {ex.Message}", ex);
        }
    }

    [RelayCommand]
    private void GenerateSheetsSaveAs()
    {
        var docVm = ServiceRegistry.ActiveDocument;
        var model = docVm?.Model;
        var cadPath = model?.ProjectInfo?.FilePath;
        if (string.IsNullOrWhiteSpace(cadPath))
        {
            return;
        }
        if (model == null)
        {
            return;
        }
        var dir = Path.GetDirectoryName(cadPath)!;
        var name = Path.GetFileNameWithoutExtension(cadPath);
        var target = Path.Combine(dir, name + "-sheets.dwg");

        var modulesFile = ModulesState.Current;
        if (modulesFile == null || modulesFile.RootModules == null || modulesFile.RootModules.Count == 0)
        {
            return;
        }

        var config = new SheetConfig();

        _logger?.LogInformation("SaveAs Sheets to {Target}", target);
        ServiceRegistry.DwgPersistService?.SaveAs(cadPath!, target, cd =>
        {
            ServiceRegistry.SheetGenerator?.MutateDocument(cd, model, modulesFile, config);
        });
    }
}

public sealed class ModuleNode : ObservableObject
{
    public Module Model { get; }
    public ModuleNode? Parent { get; }

    public ModuleNode(Module model, ModuleNode? parent)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Parent = parent;
    }

    public string Name
    {
        get => Model.Name;
        set => Model.Name = value ?? string.Empty;
    }

    public string? Description
    {
        get => Model.Description;
        set => Model.Description = value;
    }

    public int ShapesCount => (Model.Shapes?.Count ?? 0) + Children.Sum(c => c.ShapesCount);

    public ObservableCollection<ModuleNode> Children { get; } = new();
}
