using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using IconPacks.Avalonia.Material;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;
using PlantCad.Gui.Utilities;
using PlantCad.Gui.ViewModels.Documents;

namespace PlantCad.Gui.ViewModels.Tools;

public partial class LayersToolViewModel : Tool
{
    // Full, unfiltered tree built from the active document
    private List<LayerNode> _baseLayers = new();

    // Tree that the view binds to
    public ObservableCollection<LayerNode> FilteredLayers { get; } = new();

    // TreeDataGrid source
    public ITreeDataGridSource? TreeSource { get; private set; }

    // Keep a strong-typed reference for programmatic selection
    private HierarchicalTreeDataGridSource<object>? _tree;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    private CadDocumentViewModel? _currentDoc;

    // Raised after syncing selection to the TreeDataGrid to request the view to bring the row into view.
    public event Action<IndexPath>? BringIntoViewRequested;

    public LayersToolViewModel()
    {
        Title = "Layers";
        CanClose = false;
        DockGroup = "Tools";

        // React to ActiveDocument changes
        ServiceRegistry.ActiveDocumentChanged += OnActiveDocumentChanged;
        // Attach to current doc if present
        OnActiveDocumentChanged(ServiceRegistry.ActiveDocument);
    }

    private void BuildTreeSource()
    {
        // Build a HierarchicalTreeDataGridSource from FilteredLayers
        var roots = FilteredLayers?.Cast<object>() ?? Enumerable.Empty<object>();

        // Child selector
        IEnumerable<object> GetChildren(object o)
        {
            if (o is LayerNode ln)
            {
                return ln.Children.Cast<object>();
            }
            if (o is TypeNode tn)
            {
                return tn.Children.Cast<object>();
            }
            return Array.Empty<object>();
        }

        // Name column cell template
        Control BuildNameCell(object? data, INameScope? _)
        {
            var row = data;
            var grid = new Grid
            {
                Height = 36,
                ColumnDefinitions = new ColumnDefinitions("*"),
                Margin = new Thickness(0),
            };

            var nameText = new TextBlock
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            nameText.Bind(TextBlock.TextProperty, new Binding("Name"));
            Grid.SetColumn(nameText, 0);
            grid.Children.Add(nameText);
            grid.DataContext = row;
            // Single-click on entity row selects the entity in the active document (single selection policy)
            grid.PointerPressed += (_, __) =>
            {
                try
                {
                    if (row is EntityNode en)
                    {
                        var doc = ServiceRegistry.ActiveDocument;
                        if (doc == null)
                        {
                            return;
                        }
                        doc.SelectedEntity = new SelectedEntityRef(en.Id, en.Kind);
                    }
                }
                catch (Exception ex)
                {
                    ServiceRegistry.LogsTool?.Append(
                        $"[{DateTime.Now:HH:mm:ss}] ERROR Layers select: {ex.Message}"
                    );
                    throw;
                }
            };
            return grid;
        }

        // Kind column
        Control BuildKindCell(object? data, INameScope? _)
        {
            var row = data;
            var text = new TextBlock
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
            };
            if (row is TypeNode tn)
            {
                text.Text = tn.Kind.ToString();
            }
            else if (row is EntityNode en)
            {
                text.Text = en.Kind.ToString();
            }
            else
            {
                text.Text = string.Empty;
            }
            return text;
        }

        // Count column (for TypeNode)
        Control BuildCountCell(object? data, INameScope? _)
        {
            var row = data;
            var text = new TextBlock
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 6, 0),
            };
            if (row is TypeNode tn)
            {
                text.Text = tn.Children.Count.ToString();
            }
            else
            {
                text.Text = string.Empty;
            }
            return text;
        }

        // Actions column
        Control BuildActionsCell(object? data, INameScope? _)
        {
            var row = data;
            var panel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 6, 0),
            };

            if (row is LayerNode)
            {
                var toggleIcon = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.Eye,
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(2),
                };
                var toggle = new ToggleButton
                {
                    Width = 36,
                    Height = 36,
                    Content = toggleIcon,
                };
                toggle.Bind(
                    ToggleButton.IsCheckedProperty,
                    new Binding("IsVisible") { Mode = BindingMode.TwoWay }
                );
                panel.Children.Add(toggle);

                var zoomIcon = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.SetCenter,
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(2),
                };
                var btn = new Button
                {
                    Width = 36,
                    Height = 36,
                    Content = zoomIcon,
                };
                btn.Command = ZoomLayerCommand;
                btn.Bind(Button.CommandParameterProperty, new Binding("."));
                panel.Children.Add(btn);
            }
            else if (row is TypeNode)
            {
                var zoomIcon = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.SetCenter,
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(2),
                };
                var btn = new Button
                {
                    Width = 36,
                    Height = 36,
                    Content = zoomIcon,
                };
                btn.Command = ZoomTypeCommand;
                btn.Bind(Button.CommandParameterProperty, new Binding("."));
                panel.Children.Add(btn);
            }
            else if (row is EntityNode)
            {
                var zoomIcon = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.SetCenter,
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(2),
                };
                var btn = new Button
                {
                    Width = 36,
                    Height = 36,
                    Content = zoomIcon,
                };
                btn.Command = ZoomEntityCommand;
                btn.Bind(Button.CommandParameterProperty, new Binding("."));
                panel.Children.Add(btn);
            }
            return panel;
        }

        var nameTemplate = new FuncDataTemplate<object>(BuildNameCell);
        var actionsTemplate = new FuncDataTemplate<object>(BuildActionsCell);

        var nameColumn = new TemplateColumn<object>("Name", nameTemplate);
        var actionsColumn = new TemplateColumn<object>("Actions", actionsTemplate);
        nameColumn.Options.MaxWidth = new GridLength(300);
        var source = new HierarchicalTreeDataGridSource<object>(roots)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<object>(nameColumn, GetChildren),
                actionsColumn,
            },
        };

        _tree = source;
        TreeSource = source;
        OnPropertyChanged(nameof(TreeSource));

        // After (re)building the source, sync selection from current document selection if present
        TrySyncSelectionFromDocument();
    }

    partial void OnSearchQueryChanged(string value)
    {
        try
        {
            Refilter();
            BuildTreeSource();
        }
        catch (Exception ex)
        {
            // Surface errors to logs tool if available
            ServiceRegistry.LogsTool?.Append(
                $"[{DateTime.Now:HH:mm:ss}] ERROR Layers filter: {ex.Message}"
            );
            throw;
        }
    }

    private void OnActiveDocumentChanged(CadDocumentViewModel? newDoc)
    {
        // Detach from previous
        if (_currentDoc != null)
        {
            _currentDoc.PropertyChanged -= OnActiveDocumentPropertyChanged;
        }
        _currentDoc = newDoc;
        if (_currentDoc == null)
        {
            Layers.Clear();
            FilteredLayers.Clear();
            return;
        }
        _currentDoc.PropertyChanged += OnActiveDocumentPropertyChanged;
        // Build base tree and filtered view
        RebuildBaseAndFilter();
        BuildTreeSource();
        ServiceRegistry.LogsTool?.Append(
            $"[{DateTime.Now:HH:mm:ss}] INFO LayersTool: ActiveDocument bound; model set={(newDoc?.Model != null)}"
        );
    }

    private void OnActiveDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var doc = ServiceRegistry.ActiveDocument;
        if (doc == null)
        {
            return;
        }
        if (e.PropertyName == nameof(doc.Model) || e.PropertyName == nameof(doc.RenderOptions))
        {
            RebuildBaseAndFilter();
            BuildTreeSource();
            ServiceRegistry.LogsTool?.Append(
                $"[{DateTime.Now:HH:mm:ss}] INFO LayersTool: Document property changed -> {e.PropertyName}"
            );
        }
        else if (e.PropertyName == nameof(doc.SelectedEntity))
        {
            TrySyncSelectionFromDocument();
        }
    }

    private void TrySyncSelectionFromDocument()
    {
        try
        {
            var doc = ServiceRegistry.ActiveDocument;
            var sel = doc?.SelectedEntity;
            if (doc == null || sel == null || _tree == null)
            {
                return;
            }

            // Find IndexPath [layer, type, entity] in FilteredLayers
            for (int li = 0; li < FilteredLayers.Count; li++)
            {
                var layer = FilteredLayers[li];
                for (int ti = 0; ti < layer.Children.Count; ti++)
                {
                    var type = layer.Children[ti];
                    if (type.Kind != sel.Kind)
                        continue;
                    for (int ei = 0; ei < type.Children.Count; ei++)
                    {
                        var ent = type.Children[ei];
                        if (string.Equals(ent.Id, sel.Id, StringComparison.Ordinal))
                        {
                            // Expand parents to make the entity row visible
                            _tree.Expand(new IndexPath(li));
                            _tree.Expand(new IndexPath(li, ti));
                            var path = new IndexPath(li, ti, ei);
                            _tree.RowSelection!.SelectedIndex = path;
                            BringIntoViewRequested?.Invoke(path);
                            return;
                        }
                    }
                }
            }

            // If not found but there is an active filter, and the entity exists in the base tree,
            // clear the filter to ensure the selection becomes visible and try again via rebuild hooks.
            if (!string.IsNullOrWhiteSpace(SearchQuery) && IsEntityPresentInBase(sel))
            {
                SearchQuery = string.Empty; // triggers Refilter() + BuildTreeSource() + TrySyncSelectionFromDocument()
                return;
            }
        }
        catch (Exception ex)
        {
            ServiceRegistry.LogsTool?.Append(
                $"[{DateTime.Now:HH:mm:ss}] ERROR LayersTool sync selection: {ex.Message}"
            );
            throw;
        }
    }

    private bool IsEntityPresentInBase(SelectedEntityRef sel)
    {
        foreach (var layer in _baseLayers)
        {
            foreach (var type in layer.Children)
            {
                if (type.Kind != sel.Kind)
                    continue;
                foreach (var ent in type.Children)
                {
                    if (string.Equals(ent.Id, sel.Id, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private void RebuildBaseAndFilter()
    {
        var doc = ServiceRegistry.ActiveDocument;
        var model = doc?.Model;
        var options = doc?.RenderOptions;
        _baseLayers = LayerTreeBuilder.Build(model, options);

        // For toggling visibility from the layers tool, wire each base layer node to RenderOptions
        if (options != null)
        {
            foreach (var ln in _baseLayers)
            {
                ln.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(LayerNode.IsVisible))
                    {
                        options.SetLayerVisible(ln.Name, ln.IsVisible);
                    }
                };
            }
        }

        Refilter();
    }

    private sealed record ParsedQuery(
        List<string> LayerFilters,
        HashSet<EntityKind> TypeFilters,
        List<string> Terms
    );

    private static ParsedQuery Parse(string input)
    {
        var layerFilters = new List<string>();
        var typeFilters = new HashSet<EntityKind>();
        var terms = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return new ParsedQuery(layerFilters, typeFilters, terms);
        }
        var tokens = input.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries
        );
        foreach (var tok in tokens)
        {
            var t = tok.Trim();
            var sharp = t.StartsWith('#');
            var idx = t.IndexOf(':');
            if (sharp && idx > 1 && idx < t.Length - 1)
            {
                var key = t.Substring(1, idx - 1).Trim().ToLowerInvariant();
                var val = t.Substring(idx + 1).Trim();
                if (key == "layer")
                {
                    if (!string.IsNullOrWhiteSpace(val))
                        layerFilters.Add(val);
                    continue;
                }
                if (key == "type")
                {
                    foreach (
                        var raw in val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    )
                    {
                        var name = raw.Trim().ToLowerInvariant();
                        if (TryMapType(name, out var kind))
                            typeFilters.Add(kind);
                    }
                    continue;
                }
            }
            terms.Add(t);
        }
        return new ParsedQuery(layerFilters, typeFilters, terms);
    }

    private static bool TryMapType(string name, out EntityKind kind)
    {
        switch (name)
        {
            case "polyline":
            case "polylines":
                kind = EntityKind.Polyline;
                return true;
            case "line":
            case "lines":
                kind = EntityKind.Line;
                return true;
            case "circle":
            case "circles":
                kind = EntityKind.Circle;
                return true;
            case "arc":
            case "arcs":
                kind = EntityKind.Arc;
                return true;
            case "insert":
            case "inserts":
            case "block":
            case "blocks":
                kind = EntityKind.Insert;
                return true;
            case "ellipse":
            case "ellipses":
                kind = EntityKind.Ellipse;
                return true;
            case "text":
            case "texts":
                kind = EntityKind.Text;
                return true;
            case "mtext":
            case "mtexts":
                kind = EntityKind.MText;
                return true;
            case "spline":
            case "splines":
                kind = EntityKind.Spline;
                return true;
            case "solid":
            case "solids":
                kind = EntityKind.Solid;
                return true;
            case "hatch":
            case "hatches":
                kind = EntityKind.Hatch;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private void Refilter()
    {
        var doc = ServiceRegistry.ActiveDocument;
        var options = doc?.RenderOptions;
        var pq = Parse(SearchQuery);

        FilteredLayers.Clear();
        if (_baseLayers.Count == 0)
        {
            return;
        }

        foreach (var layer in _baseLayers)
        {
            if (pq.LayerFilters.Count > 0)
            {
                var match = pq.LayerFilters.Any(f =>
                    layer.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                );
                if (!match)
                    continue;
            }

            var clone = new LayerNode
            {
                Name = layer.Name,
                IsVisible = options?.IsLayerVisible(layer.Name) ?? layer.IsVisible,
            };
            if (options != null)
            {
                clone.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(LayerNode.IsVisible))
                    {
                        options.SetLayerVisible(clone.Name, clone.IsVisible);
                    }
                };
            }

            foreach (var typeGroup in layer.Children)
            {
                if (pq.TypeFilters.Count > 0 && !pq.TypeFilters.Contains(typeGroup.Kind))
                {
                    continue;
                }

                var newType = new TypeNode
                {
                    Kind = typeGroup.Kind,
                    LayerName = typeGroup.LayerName,
                };
                foreach (var ent in typeGroup.Children)
                {
                    if (pq.Terms.Count > 0)
                    {
                        var src = (ent.Name ?? string.Empty) + " " + (ent.Id ?? string.Empty);
                        bool allTerms = pq.Terms.All(term =>
                            src.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                        );
                        if (!allTerms)
                            continue;
                    }
                    newType.Children.Add(
                        new EntityNode
                        {
                            Id = ent.Id ?? string.Empty,
                            Name = ent.Name ?? string.Empty,
                            Kind = ent.Kind,
                            LayerName = ent.LayerName,
                        }
                    );
                }
                if (newType.Children.Count > 0)
                {
                    // Update group title with filtered count
                    newType.Name = GetTypeTitle(newType.Kind, newType.Children.Count);
                    clone.Children.Add(newType);
                }
            }

            if (clone.Children.Count > 0)
            {
                FilteredLayers.Add(clone);
            }
        }
    }

    private static string GetTypeTitle(EntityKind kind, int count)
    {
        return kind switch
        {
            EntityKind.Polyline => $"Polylines ({count})",
            EntityKind.Line => $"Lines ({count})",
            EntityKind.Circle => $"Circles ({count})",
            EntityKind.Arc => $"Arcs ({count})",
            EntityKind.Insert => $"Inserts ({count})",
            EntityKind.Ellipse => $"Ellipses ({count})",
            EntityKind.Text => $"Texts ({count})",
            EntityKind.MText => $"MTexts ({count})",
            EntityKind.Spline => $"Splines ({count})",
            EntityKind.Solid => $"Solids ({count})",
            EntityKind.Hatch => $"Hatches ({count})",
            _ => $"Items ({count})",
        };
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    public ObservableCollection<LayerNode> Layers { get; } = new();

    private void RebuildLayerTree(CadModel? model, CadRenderOptions? options)
    {
        Layers.Clear();
        if (model == null || model.Layers == null || model.Layers.Count == 0)
        {
            return;
        }
        foreach (var layer in model.Layers)
        {
            var node = new LayerNode
            {
                Name = layer.Name,
                IsVisible = options?.IsLayerVisible(layer.Name) ?? (layer.IsOn && !layer.IsFrozen),
            };
            node.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(LayerNode.IsVisible))
                {
                    var opt = ServiceRegistry.ActiveDocument?.RenderOptions;
                    if (opt != null)
                    {
                        opt.SetLayerVisible(layer.Name, node.IsVisible);
                    }
                }
            };

            // Build children: Type -> Entity
            // Polylines
            var polys = model
                .Polylines?.Where(p =>
                    string.Equals(p.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (polys != null && polys.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Polylines ({polys.Count})",
                    Kind = EntityKind.Polyline,
                    LayerName = layer.Name,
                };
                foreach (var p in polys)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = p.Id,
                            Name = $"Polyline {SafeId(p.Id)} ({p.Points.Count} pts)",
                            Kind = EntityKind.Polyline,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Lines
            var lines = model
                .Lines?.Where(l =>
                    string.Equals(l.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (lines != null && lines.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Lines ({lines.Count})",
                    Kind = EntityKind.Line,
                    LayerName = layer.Name,
                };
                foreach (var l in lines)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = l.Id,
                            Name = $"Line {SafeId(l.Id)} ({FormatPt(l.Start)} → {FormatPt(l.End)})",
                            Kind = EntityKind.Line,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Circles
            var circles = model
                .Circles?.Where(c =>
                    string.Equals(c.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (circles != null && circles.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Circles ({circles.Count})",
                    Kind = EntityKind.Circle,
                    LayerName = layer.Name,
                };
                foreach (var c in circles)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = c.Id,
                            Name =
                                $"Circle {SafeId(c.Id)} (R={c.Radius:0.###} @ {FormatPt(c.Center)})",
                            Kind = EntityKind.Circle,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Arcs
            var arcs = model
                .Arcs?.Where(a =>
                    string.Equals(a.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (arcs != null && arcs.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Arcs ({arcs.Count})",
                    Kind = EntityKind.Arc,
                    LayerName = layer.Name,
                };
                foreach (var a in arcs)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = a.Id,
                            Name =
                                $"Arc {SafeId(a.Id)} (R={a.Radius:0.###}, {a.StartAngle:0.#}°–{a.EndAngle:0.#}° @ {FormatPt(a.Center)})",
                            Kind = EntityKind.Arc,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Inserts
            var inserts = model
                .Inserts?.Where(i =>
                    string.Equals(i.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (inserts != null && inserts.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Inserts ({inserts.Count})",
                    Kind = EntityKind.Insert,
                    LayerName = layer.Name,
                };
                foreach (var i in inserts)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = i.Id,
                            Name =
                                $"Insert {SafeId(i.Id)} ({i.BlockName}) @ {FormatPt(i.Position)}",
                            Kind = EntityKind.Insert,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Ellipses
            var ellipses = model
                .Ellipses?.Where(e =>
                    string.Equals(e.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (ellipses != null && ellipses.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Ellipses ({ellipses.Count})",
                    Kind = EntityKind.Ellipse,
                    LayerName = layer.Name,
                };
                foreach (var e in ellipses)
                {
                    var arcTag = e.IsArc
                        ? $", arc {e.StartAngleDeg:0.#}°–{e.EndAngleDeg:0.#}°"
                        : string.Empty;
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = e.Id,
                            Name =
                                $"Ellipse {SafeId(e.Id)} (rx={e.RadiusX:0.###}, ry={e.RadiusY:0.###}, rot={e.RotationDeg:0.#}°{arcTag})",
                            Kind = EntityKind.Ellipse,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Texts
            var texts = model
                .Texts?.Where(ti =>
                    string.Equals(ti.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (texts != null && texts.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Texts ({texts.Count})",
                    Kind = EntityKind.Text,
                    LayerName = layer.Name,
                };
                foreach (var tx in texts)
                {
                    var val = tx.Value ?? string.Empty;
                    if (val.Length > 30)
                        val = val.Substring(0, 30) + "…";
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = tx.Id,
                            Name = $"Text {SafeId(tx.Id)} '{val}' @ {FormatPt(tx.Position)}",
                            Kind = EntityKind.Text,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // MTexts
            var mtexts = model
                .MTexts?.Where(mt =>
                    string.Equals(mt.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (mtexts != null && mtexts.Any())
            {
                var t = new TypeNode
                {
                    Name = $"MTexts ({mtexts.Count})",
                    Kind = EntityKind.MText,
                    LayerName = layer.Name,
                };
                foreach (var mt in mtexts)
                {
                    var val = mt.Value ?? string.Empty;
                    if (val.Length > 30)
                        val = val.Substring(0, 30) + "…";
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = mt.Id,
                            Name = $"MText {SafeId(mt.Id)} '{val}' @ {FormatPt(mt.Position)}",
                            Kind = EntityKind.MText,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Splines
            var splines = model
                .Splines?.Where(sp =>
                    string.Equals(sp.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (splines != null && splines.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Splines ({splines.Count})",
                    Kind = EntityKind.Spline,
                    LayerName = layer.Name,
                };
                foreach (var sp in splines)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = sp.Id,
                            Name = $"Spline {SafeId(sp.Id)} ({sp.Points.Count} pts)",
                            Kind = EntityKind.Spline,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Solids
            var solids = model
                .Solids?.Where(sl =>
                    string.Equals(sl.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (solids != null && solids.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Solids ({solids.Count})",
                    Kind = EntityKind.Solid,
                    LayerName = layer.Name,
                };
                foreach (var sl in solids)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = sl.Id,
                            Name = $"Solid {SafeId(sl.Id)} ({sl.Vertices.Count} vertices)",
                            Kind = EntityKind.Solid,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Hatches
            var hatches = model
                .Hatches?.Where(h =>
                    string.Equals(h.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (hatches != null && hatches.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Hatches ({hatches.Count})",
                    Kind = EntityKind.Hatch,
                    LayerName = layer.Name,
                };
                foreach (var h in hatches)
                {
                    var kind = h.FillKind.ToString();
                    var pat = !string.IsNullOrWhiteSpace(h.PatternName) ? h.PatternName : kind;
                    var loops = h.Loops?.Count ?? 0;
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = h.Id,
                            Name = $"Hatch {SafeId(h.Id)} ({pat}, {loops} loops)",
                            Kind = EntityKind.Hatch,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            Layers.Add(node);
        }
    }

    private static string SafeId(string? id)
    {
        return string.IsNullOrWhiteSpace(id) ? "(no id)" : id;
    }

    private static string FormatPt(Point p)
    {
        return $"{p.X:0.###},{p.Y:0.###}";
    }

    [RelayCommand]
    private void ZoomEntity(EntityNode? node)
    {
        if (node == null)
            return;
        var doc = ServiceRegistry.ActiveDocument;
        var model = doc?.Model;
        if (model == null)
            return;

        var options = doc?.RenderOptions;
        var sel = new SelectedEntityRef(node.Id, node.Kind);
        if (CadEntityExtents.TryGetExtents(model, sel, options, out var ext))
        {
            ServiceRegistry.RequestZoomTo(ext);
        }
    }

    [RelayCommand]
    private void ZoomType(TypeNode? node)
    {
        if (node == null)
            return;
        var doc = ServiceRegistry.ActiveDocument;
        var model = doc?.Model;
        if (model == null)
            return;

        var options = doc?.RenderOptions;
        var ext = CadEntityExtents.TryGetExtentsForKindInLayer(
            model,
            node.Kind,
            node.LayerName,
            options
        );
        if (ext != null)
        {
            ServiceRegistry.RequestZoomTo(ext);
        }
    }

    [RelayCommand]
    private void ZoomLayer(LayerNode? node)
    {
        if (node == null)
            return;
        var doc = ServiceRegistry.ActiveDocument;
        var model = doc?.Model;
        if (model == null)
            return;

        var options = doc?.RenderOptions;
        var ext = CadEntityExtents.TryGetExtentsForLayer(model, node.Name, options);
        if (ext != null)
        {
            ServiceRegistry.RequestZoomTo(ext);
        }
    }
}
