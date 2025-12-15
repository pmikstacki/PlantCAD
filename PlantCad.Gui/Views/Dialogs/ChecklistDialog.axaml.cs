using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PlantCad.Gui.ViewModels.Dialogs;

namespace PlantCad.Gui.Views.Dialogs;

public partial class ChecklistDialog : Window
{
    public ChecklistDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ChecklistDialogViewModel vm)
        {
            var ids = vm.Items.Where(i => i.IsChecked).Select(i => i.Id).ToArray();
            Close(ids);
            return;
        }
        Close(null);
    }
}
