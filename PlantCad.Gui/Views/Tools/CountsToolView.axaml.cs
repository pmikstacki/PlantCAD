using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PlantCad.Gui.Views.Tools;

public partial class CountsToolView : UserControl
{
    public CountsToolView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
