using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PlantCad.Cli;

public sealed class CountSettings : CommandSettings
{
    [CommandArgument(0, "<path>")]
    [Description("Path to a DXF/DWG file.")]
    public string Path { get; init; } = string.Empty;

    [CommandOption("--all")]
    [Description("Count inserts across all layouts (Model + PaperSpace layouts). If set, --layout is ignored.")]
    public bool AllLayouts { get; init; }

    [CommandOption("-l|--layout")]
    [Description("Specific layout name to analyze (default: Model). Use --all to include every layout.")]
    public string? Layout { get; init; }

    [CommandOption("-s|--sort")]
    [Description("Sort by 'count' (desc) or 'name' (asc). Default: count")]
    public string SortBy { get; init; } = "count";

    [CommandOption("--modules")]
    [Description("Count blocks per module rectangle defined by a polyline on layers starting with --module-prefix.")]
    public bool Modules { get; init; }

    [CommandOption("--module-prefix")]
    [Description("Layer name prefix for module polylines. Default: _A_M_")]
    public string ModulePrefix { get; init; } = "_A_M_";

    [CommandOption("-i|--ignore")]
    [Description("Path to ignore-list JSON with block names to exclude from counting.")]
    public string? IgnorePath { get; init; }

    [CommandOption("--export")]
    [Description("Path to XLSX file to export module counts (one worksheet per module). Requires --modules.")]
    public string? ExportPath { get; init; }

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

        if (!string.Equals(SortBy, "count", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(SortBy, "name", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Error("--sort must be either 'count' or 'name'.");
        }

        if (Modules && string.IsNullOrWhiteSpace(ModulePrefix))
        {
            return ValidationResult.Error("--module-prefix cannot be empty when --modules is set.");
        }

        if (!string.IsNullOrWhiteSpace(IgnorePath) && !File.Exists(IgnorePath))
        {
            return ValidationResult.Error($"Ignore file not found: {IgnorePath}");
        }

        if (!string.IsNullOrWhiteSpace(ExportPath) && !Modules)
        {
            return ValidationResult.Error("--export can only be used together with --modules.");
        }

        return ValidationResult.Success();
    }
}