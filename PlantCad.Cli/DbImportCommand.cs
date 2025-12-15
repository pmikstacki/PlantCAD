using PlantCad.Core.Data;
using PlantCad.Core.Import;
using PlantCad.Core.Repositories;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PlantCad.Cli;

public sealed class DbImportCommand : Command<DbImportCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--db <PATH>")]
        [Description("SQLite database file path. Defaults to 'plantcad.sqlite' in the working directory.")]
        public string DatabasePath { get; init; } = "plantcad.sqlite";

        [CommandOption("--csv <PATH>")]
        [Description("Path to the CSV file to import.")]
        public string CsvPath { get; init; } = string.Empty;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settings.CsvPath))
            {
                throw new ArgumentException("--csv is required and must point to the source CSV file.");
            }

            var factory = new SqliteConnectionFactory(settings.DatabasePath);

            // Ensure schema exists
            var runner = new MigrationRunner(factory);
            runner.ApplyPendingMigrations();

            // Import
            var importer = new PlantCsvImporter(factory, new LookupRepository(), new PlantRepository());
            AnsiConsole.MarkupLine($"[grey]Importing[/] [yellow]{settings.CsvPath}[/] [grey]into[/] [green]{settings.DatabasePath}[/] ...");
            var imported = importer.Import(settings.CsvPath);
            AnsiConsole.MarkupLine($"[green]Import complete.[/] Inserted [bold]{imported}[/] plants (skipped duplicates).");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            return -1;
        }
    }
}
