using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PlantCad.Gui.Views.Tools;

public partial class ProjectToolView : UserControl
{
    public ProjectToolView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
