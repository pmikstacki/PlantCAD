using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using PlantCad.Gui.Models.Modules;

namespace PlantCad.Gui.Services.Modules;

public interface IModulesService
{
    ModulesFile? Current { get; }
    string? CurrentCadPath { get; }
    Module? SelectedModule { get; set; }
    bool EditMode { get; }

    event Action? ModulesChanged;
    event Action<bool>? EditModeChanged;

    void NewForCad(string cadPath);
    Task OpenForCadAsync(string cadPath, CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);

    Module AddModule(string name, string? description, Module? parent = null);
    void RemoveModule(Module module);

    ModulePolygon AddPolygon(Module module, IEnumerable<Avalonia.Point> points);
    void UpdatePolygon(Module module, ModulePolygon polygon, IEnumerable<Avalonia.Point> points);
    void RemovePolygon(Module module, ModulePolygon polygon);

    void SetEditMode(bool on);
}
