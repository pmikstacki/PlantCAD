using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PlantCad.Gui.ViewModels.Dialogs;

public sealed class ChecklistDialogViewModel
{
    public string Title { get; }
    public ObservableCollection<ChecklistItem> Items { get; }

    public ChecklistDialogViewModel(string title, IList<ChecklistItem> items)
    {
        Title = title;
        Items = new ObservableCollection<ChecklistItem>(items);
    }
}
