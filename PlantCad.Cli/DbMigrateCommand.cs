using PlantCad.Core.Data;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PlantCad.Cli;

public sealed class DbMigrateCommand : Command<DbMigrateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--db <PATH>")]
        [Description("SQLite database file path. Defaults to 'plantcad.sqlite' in the working directory.")]
        public string DatabasePath { get; init; } = "plantcad.sqlite";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        try
        {
            AnsiConsole.MarkupLine($"[grey]Applying migrations to[/] [green]{settings.DatabasePath}[/] ...");
            var factory = new SqliteConnectionFactory(settings.DatabasePath);
            var runner = new MigrationRunner(factory);
            runner.ApplyPendingMigrations();
            AnsiConsole.MarkupLine("[green]Migrations applied successfully.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            return -1;
        }
    }
}
