using System;
using System.IO;
using System.Threading;
using ACadSharp;
using ACadSharp.IO;

namespace PlantCad.Gui.Services.Internal;

public sealed class DwgPersistService : IDwgPersistService
{
    private static readonly AsyncLocal<string?> _currentTargetDwgPath = new AsyncLocal<string?>();

    public static string? CurrentTargetDwgPath => _currentTargetDwgPath.Value;

    public void SaveAs(string sourceDwgPath, string targetDwgPath, Action<CadDocument>? mutateDocument = null)
    {
        if (string.IsNullOrWhiteSpace(sourceDwgPath))
        {
            throw new ArgumentException("Source path must not be empty.", nameof(sourceDwgPath));
        }
        if (!File.Exists(sourceDwgPath))
        {
            throw new FileNotFoundException("Source DWG not found.", sourceDwgPath);
        }
        if (string.IsNullOrWhiteSpace(targetDwgPath))
        {
            throw new ArgumentException("Target path must not be empty.", nameof(targetDwgPath));
        }

        // Ensure target directory exists
        var dir = Path.GetDirectoryName(targetDwgPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Load, mutate, write
        var doc = DwgReader.Read(sourceDwgPath);

        try
        {
            _currentTargetDwgPath.Value = targetDwgPath;
            mutateDocument?.Invoke(doc);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to apply DWG mutations before saving.", ex);
        }
        finally
        {
            _currentTargetDwgPath.Value = null;
        }

        // Persist to target
        DwgWriter.Write(targetDwgPath, doc);
    }
}
