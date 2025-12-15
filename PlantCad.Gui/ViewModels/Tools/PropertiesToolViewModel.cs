using System;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Models;
using PlantCad.Gui.PropertyGrid;
using PlantCad.Gui.Services;
using PlantCad.Gui.ViewModels.Documents;

namespace PlantCad.Gui.ViewModels.Tools;

public partial class PropertiesToolViewModel : Tool
{
    private CadDocumentViewModel? _currentDoc;

    [ObservableProperty]
    private object? selectedObject;

    [ObservableProperty]
    private string? selectionTitle;

    public PropertiesToolViewModel()
    {
        Title = "Properties";
        CanClose = false;
        DockGroup = "Tools";

        ServiceRegistry.PropertiesTool = this;
        ServiceRegistry.ActiveDocumentChanged += OnActiveDocumentChanged;
        OnActiveDocumentChanged(ServiceRegistry.ActiveDocument);
    }

    private void OnActiveDocumentChanged(CadDocumentViewModel? newDoc)
    {
        if (_currentDoc != null)
        {
            _currentDoc.PropertyChanged -= OnActiveDocumentPropertyChanged;
        }
        _currentDoc = newDoc;
        if (_currentDoc == null)
        {
            SelectedObject = null;
            SelectionTitle = "No selection";
            return;
        }
        _currentDoc.PropertyChanged += OnActiveDocumentPropertyChanged;
        UpdateSelection(_currentDoc.SelectedEntity);
    }

    private void OnActiveDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_currentDoc == null)
        {
            return;
        }
        if (
            e.PropertyName == nameof(CadDocumentViewModel.SelectedEntity)
            || e.PropertyName == nameof(CadDocumentViewModel.Model)
        )
        {
            UpdateSelection(_currentDoc.SelectedEntity);
        }
    }

    private void UpdateSelection(SelectedEntityRef? sel)
    {
        try
        {
            var model = _currentDoc?.Model;
            if (model == null || sel == null)
            {
                SelectedObject = null;
                SelectionTitle = "No selection";
                return;
            }
            var presenter = SelectedEntityPresenters.Create(model, sel);
            if (presenter != null)
            {
                SelectedObject = presenter;
                SelectionTitle = $"{sel.Kind} {sel.Id}";
                return;
            }

            SelectedObject = null;
            SelectionTitle = $"Selected entity not found: {sel.Kind} {sel.Id}";
        }
        catch (Exception ex)
        {
            ServiceRegistry.LogsTool?.Append(
                $"[{DateTime.Now:HH:mm:ss}] ERROR PropertiesTool: {ex.Message}"
            );
            throw;
        }
    }
}
