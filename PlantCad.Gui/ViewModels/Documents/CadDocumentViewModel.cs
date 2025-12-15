using System;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Models;
using PlantCad.Gui.ViewModels.Tools;

namespace PlantCad.Gui.ViewModels.Documents;

public partial class CadDocumentViewModel : Document
{
    [ObservableProperty]
    private CadModel? model;

    [ObservableProperty]
    private bool isSelecting;

    [ObservableProperty]
    private CadRenderOptions? renderOptions;

    // Single-selection reference shared across tools (viewport, layers, etc.)
    [ObservableProperty]
    private SelectedEntityRef? selectedEntity;

    // Persist the current viewport transform (world-to-screen) per document.
    // When the document is re-shown, the viewport can restore this transform.
    [ObservableProperty]
    private Matrix? viewportTransform;

    public CadDocumentViewModel()
    {
        Title = "CAD Document";
        CanClose = true;
    }

    partial void OnModelChanged(CadModel? value)
    {
        // When model changes, rebuild default render options bound to the viewport and tools
        RenderOptions = value != null ? new CadRenderOptions(value.Layers) : null;
    }
}

/// <summary>
/// Identifies a single CAD entity by its ID and kind. Used for selection synchronization.
/// </summary>
public sealed class SelectedEntityRef
{
    public string Id { get; }
    public EntityKind Kind { get; }

    public SelectedEntityRef(string id, EntityKind kind)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Entity id must not be empty.", nameof(id));
        }
        Id = id;
        Kind = kind;
    }
}
