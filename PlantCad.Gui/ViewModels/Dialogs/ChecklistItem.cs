namespace PlantCad.Gui.ViewModels.Dialogs;

public sealed class ChecklistItem
{
    public int Id { get; }
    public string Label { get; }
    public bool IsChecked { get; set; }

    public ChecklistItem(int id, string label, bool isChecked)
    {
        Id = id;
        Label = label;
        IsChecked = isChecked;
    }
}
