using System.ComponentModel;
using PlantCad.Core.Cad;
using netDxf;
using netDxf.Entities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PlantCad.Cli;

public sealed class BuildIgnoreListCommand : Command<BuildIgnoreListCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to a DXF/DWG file to scan for block names.")]
        public string Path { get; init; } = string.Empty;

        [CommandOption("-o|--output")]
        [Description("Output JSON path for the ignore list. Default: <input>.ignore.json next to the file.")]
        public string? Output { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return ValidationResult.Error("A file path is required.");
            }

            if (!File.Exists(Path))
            {
                return ValidationResult.Error($"File not found: {Path}");
            }

            var ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
            if (ext != ".dxf" && ext != ".dwg")
            {
                return ValidationResult.Error("Unsupported file type. Please provide a .dxf or .dwg file.");
            }

            return ValidationResult.Success();
        }
    }

    private sealed record BlockItem(string Name, int Count);

    public override int Execute(CommandContext context, Settings settings)
    {
        try
        {
            var path = settings.Path;
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            var fileName = System.IO.Path.GetFileName(path);

            IDictionary<string, int> counts;
            if (ext == ".dwg")
            {
                var dwg = DwgQuery.Open(path);
                (counts, _) = dwg.CountInsertsAcross(allLayouts: true, layoutArg: null);
            }
            else
            {
                // DXF path
                var doc = DxfDocument.Load(path);
                counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var layout in doc.Layouts)
                {
                    foreach (var ins in layout.AssociatedBlock.Entities.OfType<Insert>())
                    {
                        var name = ins.Block?.Name ?? "<Unnamed>";
                        counts[name] = counts.TryGetValue(name, out var c) ? c + 1 : 1;
                    }
                }
            }

            if (counts.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No INSERT entities found. Nothing to ignore.[/]");
                return 0;
            }

            var items = counts
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new BlockItem(kv.Key, kv.Value))
                .ToList();

            // Determine output path and read existing ignore list if present
            var output = settings.Output;
            if (string.IsNullOrWhiteSpace(output))
            {
                var baseName = System.IO.Path.GetFileName(path);
                output = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory(),
                    baseName + ".ignore.json");
            }

            HashSet<string> preselected = new(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(output))
            {
                try
                {
                    var existing = IgnoreConfig.Load(output);
                    preselected = existing.ToSet();
                    if (preselected.Count > 0)
                    {
                        AnsiConsole.MarkupLine($"[grey]Preselected from existing ignore file:[/] {output} ([grey]{preselected.Count} name(s)[/])");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLineInterpolated($"[yellow]Warning:[/] Failed to read existing ignore file '{output}': {Markup.Escape(ex.Message)}");
                }
            }

            var prompt = new MultiSelectionPrompt<BlockItem>()
                .Title($"[bold]Select blocks to [red]ignore[/] in {Markup.Escape(fileName)}[/]")
                .InstructionsText("[grey](Move with arrows, Space to toggle, Enter to accept)[/]")
                .NotRequired()
                .UseConverter(i => $"{i.Name} [grey]({i.Count})[/]")
                .AddChoices(items);

            // Preselect existing ignored names
            foreach (var it in items)
            {
                if (preselected.Contains(it.Name))
                {
                    prompt.Select(it);
                }
            }

            var selected = AnsiConsole.Prompt(prompt);
            var selectedNames = selected.Select(i => i.Name);

            // Merge with any names that exist in current ignore file but are not present in this drawing anymore
            var finalSet = new HashSet<string>(selectedNames, StringComparer.OrdinalIgnoreCase);
            foreach (var name in preselected)
            {
                if (!finalSet.Contains(name))
                {
                    finalSet.Add(name);
                }
            }

            var cfg = new IgnoreConfig { Blocks = finalSet
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList() };
            cfg.Save(output);

            AnsiConsole.MarkupLine($"[green]Saved ignore list:[/] {Markup.Escape(output)}");
            AnsiConsole.MarkupLine($"[grey]Ignored block names:[/] {cfg.Blocks.Count}");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes | ExceptionFormats.ShortenMethods);
            return -1;
        }
    }
}