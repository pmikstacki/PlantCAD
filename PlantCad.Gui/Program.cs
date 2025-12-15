using System;
using Avalonia;
using Dock.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.CadViewport.Hatching;
using PlantCad.Gui.Docking;
using PlantCad.Gui.Logging;
using PlantCad.Gui.Services;
using PlantCad.Gui.Services.Internal;

namespace PlantCad.Gui;

sealed class Program
{
    public static IHost? AppHost { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppHost = CreateHostBuilder(args).Build();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        AppHost.Dispose();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddConsole();
                // GUI logger provider writes to Logs tool when available
                logging.AddProvider(new GuiLoggerProvider(() => ServiceRegistry.LogsTool));
            })
            .ConfigureServices(
                (ctx, services) =>
                {
                    // Core app services
                    services.AddSingleton<ICadFileService, CadFileService>();
                    services.AddSingleton<IFileDialogService, FileDialogService>();
                    services.AddSingleton<ICountingService, CountingService>();
                    services.AddSingleton<IExportService, ExportService>();
                    services.AddSingleton<IDwgPersistService, DwgPersistService>();
                    services.AddSingleton<ISheetGenerator, SheetGenerator>();
                    services.AddSingleton<MouseSettings>();

                    // Plant DB GUI service
                    services.AddSingleton<IPlantDbService, PlantDbService>();

                    // Docking
                    services.AddSingleton<DockFactory>();
                    services.AddSingleton<DockSerializer>();

                    // Hatching services (DI)
                    services.AddSingleton<IHatchShaderCache, HatchShaderCache>();
                    services.AddSingleton<IHatchShaderFactory, HatchShaderFactory>();
                    // ViewModels
                    services.AddSingleton<ViewModels.MainWindowViewModel>();
                }
            );
}
