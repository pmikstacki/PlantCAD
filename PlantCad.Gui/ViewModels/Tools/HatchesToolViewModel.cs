using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.ViewModels.Tools;

public sealed partial class HatchesToolViewModel : Tool
{
    public ObservableCollection<HatchItem> Items { get; } = new();

    [ObservableProperty]
    private int thumbnailSize = 128;

    public HatchesToolViewModel()
    {
        Title = "Hatches";
        CanClose = false;
        DockGroup = "Tools";
        Load();
    }

    public void Load()
    {
        Items.Clear();
        var all = HatchPatternService.Instance.GetAll();
        foreach (var p in all)
        {
            if (string.IsNullOrWhiteSpace(p.Name))
                continue;
            Items.Add(new HatchItem(p.Name));
        }
    }

    public sealed class HatchItem
    {
        public string Name { get; }

        public HatchItem(string name)
        {
            Name = name;
        }
    }
}
