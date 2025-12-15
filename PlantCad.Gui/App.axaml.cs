using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Logging;
using PlantCad.Gui.Services;
using PlantCad.Gui.Services.Internal;
using PlantCad.Gui.ViewModels;
using PlantCad.Gui.Views;

namespace PlantCad.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            // Resolve from Generic Host DI
            var services =
                Program.AppHost?.Services
                ?? throw new InvalidOperationException("AppHost not initialized.");

            ServiceRegistry.CadFileService = services.GetRequiredService<ICadFileService>();
            ServiceRegistry.FileDialogService = services.GetRequiredService<IFileDialogService>();
            ServiceRegistry.CountingService = services.GetRequiredService<ICountingService>();
            ServiceRegistry.ExportService = services.GetRequiredService<IExportService>();
            ServiceRegistry.DwgPersistService = services.GetRequiredService<IDwgPersistService>();
            ServiceRegistry.SheetGenerator = services.GetRequiredService<ISheetGenerator>();
            ServiceRegistry.LoggerFactory = services.GetRequiredService<ILoggerFactory>();
            ServiceRegistry.MouseSettings = services.GetRequiredService<MouseSettings>();
            ServiceRegistry.PlantDbService = services.GetRequiredService<IPlantDbService>();
            // Hatching services
            ServiceRegistry.HatchShaderCache =
                services.GetRequiredService<Controls.CadViewport.Hatching.IHatchShaderCache>();
            ServiceRegistry.HatchShaderFactory =
                services.GetRequiredService<Controls.CadViewport.Hatching.IHatchShaderFactory>();
            var bootLogger = ServiceRegistry.LoggerFactory.CreateLogger("App");
            ServiceRegistry.LogsToolChanged += _ =>
            {
                bootLogger.LogInformation("Logs tool attached");
            };

            var mainVm = services.GetRequiredService<MainWindowViewModel>();

            // Register default underlay image resolver (filesystem-based)
            ServiceRegistry.UnderlayImageResolver ??= new FileSystemUnderlayResolver();

            desktop.MainWindow = new MainWindow { DataContext = mainVm };
            ServiceRegistry.Root = desktop.MainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove = BindingPlugins
            .DataValidators.OfType<DataAnnotationsValidationPlugin>()
            .ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
