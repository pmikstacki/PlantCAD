using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PlantCad.Gui.Controls;
using PlantCad.Gui.Services;
using PlantCad.Gui.Services.Modules;
using PlantCad.Gui.ViewModels.Documents;
using PlantCad.Gui.Controls.Rendering;

namespace PlantCad.Gui.Views.Documents;

using System.ComponentModel;

public partial class CadDocumentView : UserControl
{
    private CadViewportControl? _viewport;
    private CadDocumentViewModel? _subscribedDoc;

    public CadDocumentView()
    {
        InitializeComponent();
        _viewport = this.FindControl<CadViewportControl>("Viewport");
        if (_viewport != null)
        {
            _viewport.SelectionCompleted += OnSelectionCompleted;
            // Paint modules over CAD: feed overlay from in-memory state (services can set ModulesState later)
            _viewport.ModuleShapesProvider = ModulesState.GetWorldPolygons;
            ModulesOverlayRenderer.LabelsProvider = ModulesState.GetLabels;
            ModulesOverlayRenderer.CardsProvider = ModulesState.GetCards;
            ServiceRegistry.ActiveViewport = _viewport;
        }
        // Ensure modules are loaded for the active document and whenever the CAD model changes
        ServiceRegistry.ActiveDocumentChanged += doc =>
        {
            SubscribeToActiveDocument(doc);
            LoadModulesForActiveDocument();
        };
        SubscribeToActiveDocument(ServiceRegistry.ActiveDocument);
        LoadModulesForActiveDocument();
    }

    private void OnSelectionCompleted(Avalonia.Rect rect)
    {
        try
        {
            if (DataContext is not CadDocumentViewModel vm || vm.Model is null)
            {
                return;
            }
            var counting = ServiceRegistry.CountingService;
            var countsTool = ServiceRegistry.CountsTool;
            if (counting == null || countsTool == null)
            {
                return;
            }
            var (counts, total) = counting.CountInsertsInRect(
                vm.Model,
                rect.Left,
                rect.Top,
                rect.Right,
                rect.Bottom
            );
            countsTool.ShowCounts(counts, total);
        }
        finally
        {
            if (DataContext is CadDocumentViewModel vm)
            {
                vm.IsSelecting = false;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SubscribeToActiveDocument(CadDocumentViewModel? doc)
    {
        if (!ReferenceEquals(_subscribedDoc, null) && _subscribedDoc != null)
        {
            _subscribedDoc.PropertyChanged -= OnActiveDocPropertyChanged;
        }
        _subscribedDoc = doc;
        if (_subscribedDoc != null)
        {
            _subscribedDoc.PropertyChanged += OnActiveDocPropertyChanged;
        }
    }

    private void OnActiveDocPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CadDocumentViewModel.Model))
        {
            // When a DWG is opened or switched, load modules from the matching .plantcad file
            LoadModulesForActiveDocument();
        }
    }

    private static void LoadModulesForActiveDocument()
    {
        try
        {
            var vm = ServiceRegistry.ModulesTool;
            vm?.LoadModulesCommand?.Execute(null);
        }
        catch (System.Exception ex)
        {
            throw new System.InvalidOperationException($"Failed to queue modules load: {ex.Message}", ex);
        }
    }
}
