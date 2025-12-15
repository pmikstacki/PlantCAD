using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PlantCad.Gui.Views.Tools;

public partial class PlantDbToolView : UserControl
{
    public PlantDbToolView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
