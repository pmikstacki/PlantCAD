using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using PlantCad.Gui.Services;
using PlantCad.Gui.ViewModels.Documents;
using PlantCad.Gui.ViewModels.Tools;

namespace PlantCad.Gui.Docking;

public sealed class DockFactory : Factory
{
    public override IRootDock CreateLayout()
    {
        // Documents
        var startDocument = new CadDocumentViewModel
        {
            Id = "CadDocument",
            Title = "Untitled",
            CanClose = true,
        };
        ServiceRegistry.ActiveDocument = startDocument;

        var documents = CreateDocumentDock();
        documents.Id = "Documents";
        documents.Title = "Documents";
        documents.EnableWindowDrag = false;
        documents.Proportion = 1.0; // ensure horizontal space shares with tool docks
        documents.ActiveDockable = startDocument;
        documents.VisibleDockables = CreateList<IDockable>(startDocument);

        // Left tools
        var leftTools = CreateToolDock();
        leftTools.Id = "LeftTools";
        leftTools.Title = "Tools";
        leftTools.Alignment = Alignment.Left;
        leftTools.Proportion = 0.25;
        leftTools.DockGroup = "Tools";
        var projectVm = new ProjectToolViewModel { Id = "ProjectTool" };
        var layersVm = new LayersToolViewModel { Id = "LayersTool" };
        var modulesVm = new ModulesToolViewModel { Id = "ModulesTool" };
        ServiceRegistry.ProjectTool = projectVm;
        ServiceRegistry.LayersTool = layersVm;
        ServiceRegistry.ModulesTool = modulesVm;
        leftTools.VisibleDockables = CreateList<IDockable>(projectVm, layersVm, modulesVm);
        leftTools.ActiveDockable = modulesVm;

        // Right tools
        var rightTools = CreateToolDock();
        rightTools.Id = "RightTools";
        rightTools.Title = "Details";
        rightTools.Alignment = Alignment.Right;
        rightTools.Proportion = 0.3;
        rightTools.DockGroup = "Tools";
        var countsVm = new CountsToolViewModel { Id = "CountsTool" };
        ServiceRegistry.CountsTool = countsVm;
        var plantDbVm = new PlantDbToolViewModel { Id = "PlantDbTool" };
        var propertiesVm = new PropertiesToolViewModel { Id = "PropertiesTool" };
        var mouseVm = new MouseToolViewModel { Id = "MouseTool" };
        var blocksVm = new BlocksGalleryToolViewModel { Id = "BlocksTool" };
        var styleVm = new StyleToolViewModel { Id = "StyleTool" };
        var hatchesVm = new HatchesToolViewModel { Id = "HatchesTool" };
        ServiceRegistry.PlantDbTool = plantDbVm;
        ServiceRegistry.PropertiesTool = propertiesVm;
        ServiceRegistry.MouseTool = mouseVm;
        ServiceRegistry.BlocksTool = blocksVm;
        ServiceRegistry.StyleTool = styleVm;
        ServiceRegistry.HatchesTool = hatchesVm;
        rightTools.VisibleDockables = CreateList<IDockable>(
            countsVm,
            plantDbVm,
            propertiesVm,
            mouseVm,
            blocksVm,
            styleVm,
            hatchesVm
        );
        rightTools.ActiveDockable = blocksVm;

        // Bottom tools
        var bottomTools = CreateToolDock();
        bottomTools.Id = "BottomTools";
        bottomTools.Title = "Output";
        bottomTools.Alignment = Alignment.Bottom;
        bottomTools.Proportion = 0.2;
        bottomTools.DockGroup = "Tools";
        var logsVm = new LogsToolViewModel { Id = "LogsTool" };
        ServiceRegistry.LogsTool = logsVm;
        bottomTools.VisibleDockables = CreateList<IDockable>(logsVm);
        bottomTools.ActiveDockable = logsVm;

        // Main horizontal area: Left tools | splitter | Documents | splitter | Right tools
        var mainHorizontal = CreateProportionalDock();
        mainHorizontal.Orientation = Orientation.Horizontal;
        mainHorizontal.Proportion = 0.8; // share vertical space with bottom tools (via parent)
        mainHorizontal.VisibleDockables = CreateList<IDockable>(
            leftTools,
            CreateProportionalDockSplitter(),
            documents,
            CreateProportionalDockSplitter(),
            rightTools
        );

        // Main vertical area: MainHorizontal | splitter | Bottom tools
        var mainVertical = CreateProportionalDock();
        mainVertical.Orientation = Orientation.Vertical;
        mainVertical.VisibleDockables = CreateList<IDockable>(
            mainHorizontal,
            CreateProportionalDockSplitter(),
            bottomTools
        );

        var root = CreateRootDock();
        root.Id = "Root";
        root.Title = "Root";
        root.ActiveDockable = mainVertical;
        root.DefaultDockable = mainVertical;
        root.VisibleDockables = CreateList<IDockable>(mainVertical);

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        // Register dockable and context locators for serialization/deserialization
        DockableLocator = new System.Collections.Generic.Dictionary<string, System.Func<IDockable?>>
        {
            ["CadDocument"] = () =>
                new CadDocumentViewModel
                {
                    Id = "CadDocument",
                    Title = "Untitled",
                    CanClose = true,
                },
            ["ProjectTool"] = () => new ProjectToolViewModel { Id = "ProjectTool" },
            ["LayersTool"] = () => new LayersToolViewModel { Id = "LayersTool" },
            ["ModulesTool"] = () => new ModulesToolViewModel { Id = "ModulesTool" },
            ["CountsTool"] = () => new CountsToolViewModel { Id = "CountsTool" },
            ["PlantDbTool"] = () => new PlantDbToolViewModel { Id = "PlantDbTool" },
            ["PropertiesTool"] = () => new PropertiesToolViewModel { Id = "PropertiesTool" },
            ["LogsTool"] = () => new LogsToolViewModel { Id = "LogsTool" },
            ["MouseTool"] = () => new MouseToolViewModel { Id = "MouseTool" },
            ["BlocksTool"] = () => new BlocksGalleryToolViewModel { Id = "BlocksTool" },
            ["StyleTool"] = () => new StyleToolViewModel { Id = "StyleTool" },
            ["HatchesTool"] = () => new HatchesToolViewModel { Id = "HatchesTool" },
        };

        ContextLocator = new System.Collections.Generic.Dictionary<string, System.Func<object?>>
        {
            ["CadDocument"] = () => ServiceRegistry.ActiveDocument,
            ["ProjectTool"] = () => ServiceRegistry.ProjectTool,
            // Bind LayersTool view to LayersToolViewModel
            ["LayersTool"] = () => ServiceRegistry.LayersTool,
            ["ModulesTool"] = () => ServiceRegistry.ModulesTool,
            ["CountsTool"] = () => ServiceRegistry.CountsTool,
            ["PlantDbTool"] = () => ServiceRegistry.PlantDbTool,
            ["PropertiesTool"] = () => ServiceRegistry.PropertiesTool,
            ["LogsTool"] = () => ServiceRegistry.LogsTool,
            ["MouseTool"] = () => ServiceRegistry.MouseTool,
            ["BlocksTool"] = () => ServiceRegistry.BlocksTool,
            ["StyleTool"] = () => ServiceRegistry.StyleTool,
            ["HatchesTool"] = () => ServiceRegistry.HatchesTool,
        };

        base.InitLayout(layout);
    }

    public void ActivateDockable(IDockable dockable)
    {
        if (dockable is null)
        {
            return;
        }
        SetActiveDockable(dockable);
        if (dockable.Owner is IDock owner)
        {
            SetFocusedDockable(owner, dockable);
        }
    }
}
