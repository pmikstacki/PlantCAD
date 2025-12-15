using System.Collections.Generic;
using PlantCad.Gui.Models;
using PlantCad.Gui.Models.Modules;

namespace PlantCad.Gui.Services.Modules;

public interface IModulesCountingService
{
    // Aggregate counts including children modules
    IDictionary<Module, int> CountInsertsByModuleAggregated(CadModel model, ModulesFile modules);

    IDictionary<Module, IDictionary<string, int>> CountInsertsByModuleBreakdownAggregated(CadModel model, ModulesFile modules);
}
