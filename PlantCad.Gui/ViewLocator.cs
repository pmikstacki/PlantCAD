using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using PlantCad.Gui.ViewModels;

namespace PlantCad.Gui;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var fullName = param.GetType().FullName!;
        // Map *.ViewModels.*FooViewModel -> *.Views.*FooView
        var name = fullName
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        if (data is null)
        {
            return false;
        }
        // Match only content dockables (Tool/Document) and our app view models.
        // Do NOT match container docks (RootDock, ToolDock, DocumentDock, ProportionalDock).
        return data is ViewModelBase
            || data is Dock.Model.Mvvm.Controls.Tool
            || data is Dock.Model.Mvvm.Controls.Document;
    }
}
