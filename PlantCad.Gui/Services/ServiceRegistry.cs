using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.CadViewport.Hatching;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Models;
using PlantCad.Gui.ViewModels.Documents;
using PlantCad.Gui.ViewModels.Tools;
using PlantCad.Gui.Controls;

namespace PlantCad.Gui.Services;

public static class ServiceRegistry
{
    public static ICadFileService? CadFileService { get; set; }
    public static IFileDialogService? FileDialogService { get; set; }
    public static ICountingService? CountingService { get; set; }
    public static IExportService? ExportService { get; set; }
    public static IDwgPersistService? DwgPersistService { get; set; }
    public static ISheetGenerator? SheetGenerator { get; set; }
    public static IUnderlayImageResolver? UnderlayImageResolver { get; set; }

    // Hatching services (resolved from DI in App)
    public static IHatchShaderFactory? HatchShaderFactory { get; set; }
    public static IHatchShaderCache? HatchShaderCache { get; set; }

    public static Window? Root { get; set; }
    public static CadViewportControl? ActiveViewport { get; set; }
    public static event System.Action<CadDocumentViewModel?>? ActiveDocumentChanged;
    private static CadDocumentViewModel? _activeDocument;
    public static CadDocumentViewModel? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (!ReferenceEquals(_activeDocument, value))
            {
                _activeDocument = value;
                ActiveDocumentChanged?.Invoke(value);
            }
        }
    }
    public static CountsToolViewModel? CountsTool { get; set; }

    // Tool references for programmatic activation
    public static event System.Action<LogsToolViewModel?>? LogsToolChanged;
    private static LogsToolViewModel? _logsTool;
    public static LogsToolViewModel? LogsTool
    {
        get => _logsTool;
        set
        {
            _logsTool = value;
            LogsToolChanged?.Invoke(value);
        }
    }
    public static ProjectToolViewModel? ProjectTool { get; set; }
    public static LayersToolViewModel? LayersTool { get; set; }
    public static event System.Action<ModulesToolViewModel?>? ModulesToolChanged;
    private static ModulesToolViewModel? _modulesTool;
    public static ModulesToolViewModel? ModulesTool
    {
        get => _modulesTool;
        set
        {
            _modulesTool = value;
            ModulesToolChanged?.Invoke(value);
        }
    }
    public static BlocksGalleryToolViewModel? BlocksTool { get; set; }
    public static PropertiesToolViewModel? PropertiesTool { get; set; }
    public static PlantDbToolViewModel? PlantDbTool { get; set; }
    public static MouseToolViewModel? MouseTool { get; set; }
    public static StyleToolViewModel? StyleTool { get; set; }
    public static HatchesToolViewModel? HatchesTool { get; set; }

    // Plant DB service (GUI wrapper)
    public static IPlantDbService? PlantDbService { get; set; }

    // Settings
    public static MouseSettings? MouseSettings { get; set; }

    // Logging
    public static ILoggerFactory? LoggerFactory { get; set; }

    // Global style provider used by viewports and previews. When changed, all listening views should re-render.
    public static event System.Action<IStyleProvider?>? StyleProviderChanged;
    private static IStyleProvider? _styleProvider;
    public static IStyleProvider? StyleProvider
    {
        get => _styleProvider;
        set
        {
            _styleProvider = value;
            StyleProviderChanged?.Invoke(value);
        }
    }

    // Cross-tool viewport interactions
    public static event System.Action<CadExtents>? ZoomToRequested;

    public static void RequestZoomTo(CadExtents extents)
    {
        ZoomToRequested?.Invoke(extents);
    }

    // Plants document opening request
    public static event System.Action? OpenPlantsDocumentRequested;

    public static void RequestOpenPlantsDocument()
    {
        OpenPlantsDocumentRequested?.Invoke();
    }

    // Plants document opening with filter
    public static event System.Action<System.Collections.Generic.IReadOnlyList<int>>? OpenPlantsDocumentWithFilterRequested;

    public static void RequestOpenPlantsDocumentWithFilter(
        System.Collections.Generic.IReadOnlyList<int> plantIds
    )
    {
        if (plantIds is null || plantIds.Count == 0)
        {
            OpenPlantsDocumentRequested?.Invoke();
            return;
        }
        OpenPlantsDocumentWithFilterRequested?.Invoke(plantIds);
    }
}
