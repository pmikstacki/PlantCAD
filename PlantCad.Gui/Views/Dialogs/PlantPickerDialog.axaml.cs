using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PlantCad.Gui.ViewModels.Dialogs;

namespace PlantCad.Gui.Views.Dialogs;

public partial class PlantPickerDialog : Window
{
    public PlantPickerDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PlantPickerDialogViewModel vm)
        {
            var selected = vm.Selected;
            Close(selected);
            return;
        }
        Close(null);
    }
}
