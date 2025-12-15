using System;
using System.Collections.Generic;
using Avalonia;
using PlantCad.Gui.Models;
using PlantCad.Gui.Models.Modules;

namespace PlantCad.Gui.Services.Modules;

public sealed class ModulesCountingService : IModulesCountingService
{
    public IDictionary<Module, int> CountInsertsByModuleAggregated(CadModel model, ModulesFile modules)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (modules == null) throw new ArgumentNullException(nameof(modules));
        var result = new Dictionary<Module, int>();
        if (modules.RootModules == null || modules.RootModules.Count == 0)
        {
            return result;
        }
        foreach (var m in modules.RootModules)
        {
            CountForSubtree(model, m, result);
        }
        return result;
    }

    public IDictionary<Module, IDictionary<string, int>> CountInsertsByModuleBreakdownAggregated(CadModel model, ModulesFile modules)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (modules == null) throw new ArgumentNullException(nameof(modules));

        var result = new Dictionary<Module, IDictionary<string, int>>();
        if (modules.RootModules == null || modules.RootModules.Count == 0)
        {
            return result;
        }

        foreach (var m in modules.RootModules)
        {
            CountBreakdownForSubtree(model, m, result);
        }

        return result;
    }

    private static void CountForSubtree(CadModel model, Module module, IDictionary<Module, int> acc)
    {
        if (module == null) return;
        // Aggregate all shapes from the module and its descendants
        var polys = new List<ModulePolygon>();
        CollectPolygons(module, polys);
        int count = 0;
        if (polys.Count > 0 && model.Inserts != null)
        {
            foreach (var ins in model.Inserts)
            {
                var p = new Point(ins.Position.X, ins.Position.Y);
                if (ModuleGeometryUtils.ContainsPointAny(polys, p))
                {
                    count++;
                }
            }
        }
        acc[module] = count;
        if (module.Children != null)
        {
            foreach (var ch in module.Children)
            {
                CountForSubtree(model, ch, acc);
            }
        }
    }

    private static void CountBreakdownForSubtree(
        CadModel model,
        Module module,
        IDictionary<Module, IDictionary<string, int>> acc
    )
    {
        if (module == null) return;

        var polys = new List<ModulePolygon>();
        CollectPolygons(module, polys);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (polys.Count > 0 && model.Inserts != null)
        {
            foreach (var ins in model.Inserts)
            {
                var p = new Point(ins.Position.X, ins.Position.Y);
                if (!ModuleGeometryUtils.ContainsPointAny(polys, p))
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(ins.BlockName) ? "<Unnamed>" : ins.BlockName;
                counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
            }
        }

        acc[module] = counts;
        if (module.Children != null)
        {
            foreach (var ch in module.Children)
            {
                CountBreakdownForSubtree(model, ch, acc);
            }
        }
    }

    private static void CollectPolygons(Module m, List<ModulePolygon> acc)
    {
        if (m.Shapes != null)
        {
            foreach (var s in m.Shapes)
            {
                if (s != null && s.Points != null && s.Points.Count >= 3)
                {
                    acc.Add(s);
                }
            }
        }
        if (m.Children != null)
        {
            foreach (var ch in m.Children)
            {
                CollectPolygons(ch, acc);
            }
        }
    }
}
