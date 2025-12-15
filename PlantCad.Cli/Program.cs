// See https://aka.ms/new-console-template for more information

using System;
using PlantCad.Cli;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<CountBlocksCommand>();
app.Configure(config =>
{
    config.SetApplicationName("dwgtools");
    config.SetExceptionHandler((ex, _) =>
    {
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes | ExceptionFormats.ShortenMethods);
    });

    // Commands
    config.AddCommand<CountBlocksCommand>("count")
        .WithDescription("Count INSERT entities grouped by block name in a DXF/DWG file.");

    config.AddCommand<BuildIgnoreListCommand>("ignore")
        .WithDescription("Interactively build an ignore-list JSON of block names to exclude from counting.");

    // Database commands
    config.AddBranch("db", db =>
    {
        db.SetDescription("Database management commands");
        db.AddCommand<DbMigrateCommand>("migrate")
            .WithDescription("Apply SQLite schema migrations.");
        db.AddCommand<DbImportCommand>("import")
            .WithDescription("Import plants from CSV into SQLite.");
    });

    // Blocks commands disabled: import is GUI-only now
});
return app.Run(args);