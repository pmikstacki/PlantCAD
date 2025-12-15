using ACadSharp;
using PlantCad.Gui.Models;
using PlantCad.Gui.Models.Modules;
using PlantCad.Gui.Models.Sheets;

namespace PlantCad.Gui.Services;

public interface ISheetGenerator
{
    void MutateDocument(CadDocument doc, CadModel model, ModulesFile modules, SheetConfig config);
}
