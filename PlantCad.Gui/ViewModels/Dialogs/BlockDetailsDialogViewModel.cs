using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.ViewModels.Dialogs;

public sealed partial class BlockDetailsDialogViewModel : ObservableObject
{
    public sealed record Entry(string Name, int Count);

    [ObservableProperty]
    private string blockName = string.Empty;

    [ObservableProperty]
    private string sourcePath = string.Empty;

    [ObservableProperty]
    private CadExtents extents = new();

    [ObservableProperty]
    private List<Entry> entities = new();

    [ObservableProperty]
    private List<string> layers = new();

    public bool ExtentsValid =>
        !double.IsNaN(Extents.MinX)
        && !double.IsNaN(Extents.MinY)
        && !double.IsNaN(Extents.MaxX)
        && !double.IsNaN(Extents.MaxY)
        && Extents.MaxX >= Extents.MinX
        && Extents.MaxY >= Extents.MinY;
}
