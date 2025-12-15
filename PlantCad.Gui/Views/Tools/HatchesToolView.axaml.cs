using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PlantCad.Gui.Views.Tools;

public partial class HatchesToolView : UserControl
{
    public HatchesToolView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
