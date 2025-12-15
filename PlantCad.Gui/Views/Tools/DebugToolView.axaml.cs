using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PlantCad.Gui.Views.Tools;

public partial class DebugToolView : UserControl
{
    public DebugToolView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
