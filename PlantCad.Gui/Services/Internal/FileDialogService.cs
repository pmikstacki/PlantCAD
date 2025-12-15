using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PlantCad.Gui.Services.Internal;

public sealed class FileDialogService : IFileDialogService
{
    public async Task<string?> ShowOpenCadAsync()
    {
        var root =
            ServiceRegistry.Root
            ?? throw new InvalidOperationException(
                "Root window is not available for file dialogs."
            );
        var storage = root.StorageProvider;

        var options = new FilePickerOpenOptions
        {
            Title = "Open CAD file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CAD files (*.dwg;*.dxf)")
                {
                    Patterns = new[] { "*.dwg", "*.dxf" },
                },
                new FilePickerFileType("DWG (*.dwg)") { Patterns = new[] { "*.dwg" } },
                new FilePickerFileType("DXF (*.dxf)") { Patterns = new[] { "*.dxf" } },
            },
        };

        var files = await storage.OpenFilePickerAsync(options);
        var file = files?.FirstOrDefault();
        if (file == null)
        {
            return null;
        }

        // Try to get a local path first
        var localPath = TryGetLocalPath(file);
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            return localPath;
        }

        // Fallback: copy to a temporary local file
        await using var src = await file.OpenReadAsync();
        var ext = Path.GetExtension(file.Name);
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N") + (string.IsNullOrWhiteSpace(ext) ? ".cad" : ext)
        );
        await using (var dst = File.Create(tempPath))
        {
            await src.CopyToAsync(dst);
        }
        return tempPath;
    }

    public async Task<string?> ShowOpenDatabaseAsync()
    {
        var root =
            ServiceRegistry.Root
            ?? throw new InvalidOperationException(
                "Root window is not available for file dialogs."
            );
        var storage = root.StorageProvider;

        var options = new FilePickerOpenOptions
        {
            Title = "Open Plant Database",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SQLite (*.sqlite)") { Patterns = new[] { "*.sqlite" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        };

        var files = await storage.OpenFilePickerAsync(options);
        var file = files?.FirstOrDefault();
        if (file == null)
            return null;
        var path = TryGetLocalPath(file);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public async Task<string?> ShowSaveDatabaseAsync(string suggestedFileName)
    {
        var root =
            ServiceRegistry.Root
            ?? throw new InvalidOperationException(
                "Root window is not available for file dialogs."
            );
        var storage = root.StorageProvider;

        if (string.IsNullOrWhiteSpace(suggestedFileName))
        {
            suggestedFileName = "plantcad.sqlite";
        }

        var options = new FilePickerSaveOptions
        {
            Title = "Create Plant Database",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SQLite (*.sqlite)") { Patterns = new[] { "*.sqlite" } },
            },
        };

        var file = await storage.SaveFilePickerAsync(options);
        if (file == null)
            return null;
        var path = TryGetLocalPath(file);
        if (string.IsNullOrWhiteSpace(path))
        {
            // Fallback: create a temp local file
            path = Path.Combine(
                Path.GetTempPath(),
                file.Name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
                    ? file.Name
                    : file.Name + ".sqlite"
            );
            if (!File.Exists(path))
            {
                using var _ = File.Create(path);
            }
        }
        return path;
    }

    public async Task<string?> ShowSaveExcelAsync(string suggestedFileName)
    {
        var root =
            ServiceRegistry.Root
            ?? throw new InvalidOperationException(
                "Root window is not available for file dialogs."
            );
        var storage = root.StorageProvider;

        if (string.IsNullOrWhiteSpace(suggestedFileName))
        {
            suggestedFileName = "counts.xlsx";
        }

        var options = new FilePickerSaveOptions
        {
            Title = "Export Counts to Excel",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Excel (*.xlsx)") { Patterns = new[] { "*.xlsx" } },
            },
        };

        var file = await storage.SaveFilePickerAsync(options);
        if (file == null)
        {
            return null;
        }

        var path = TryGetLocalPath(file);
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        // If not a local path, create a local file and return its path; caller can write to it
        var loc = Path.Combine(
            Path.GetTempPath(),
            file.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? file.Name
                : file.Name + ".xlsx"
        );
        // Ensure file exists
        if (!File.Exists(loc))
        {
            using var _ = File.Create(loc);
        }
        return loc;
    }

    private static string? TryGetLocalPath(IStorageItem item)
    {
        try
        {
            // Avalonia 11: Path is a Uri-like string on some platforms; attempt to map to local path
            var path = (item as IStorageFile)?.Path ?? (item as IStorageFolder)?.Path;
            if (path != null)
            {
                // Path might be like file:///... or plain path depending on platform
                if (Uri.TryCreate(path.ToString(), UriKind.Absolute, out var uri) && uri.IsFile)
                {
                    return uri.LocalPath;
                }
                return path.ToString();
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }
}
