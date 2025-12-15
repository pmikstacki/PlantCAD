using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlantCad.Gui.ViewModels.Tools
{
    public enum EntityKind
    {
        Polyline,
        Line,
        Circle,
        Arc,
        Insert,
        Ellipse,
        Text,
        MText,
        Spline,
        Solid,
        Hatch,
        Point,
        Leader,
        DimAligned,
        DimLinear,
    }

    public partial class LayerNode : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private bool isVisible;

        public ObservableCollection<TypeNode> Children { get; } = new();
    }

    public partial class TypeNode : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private EntityKind kind;

        [ObservableProperty]
        private string layerName = string.Empty;

        public ObservableCollection<EntityNode> Children { get; } = new();
    }

    public partial class EntityNode : ObservableObject
    {
        [ObservableProperty]
        private string id = string.Empty;

        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private EntityKind kind;

        [ObservableProperty]
        private string layerName = string.Empty;
    }
}
