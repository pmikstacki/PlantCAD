using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PlantCad.Gui.Models.Modules;
using ProtoBuf;

namespace PlantCad.Gui.Services.Modules;

public sealed class ModulesStorage : IModulesStorage
{
    private const string Extension = ".plantcad";

    public string ResolveModulesPath(string cadPath)
    {
        if (string.IsNullOrWhiteSpace(cadPath))
        {
            throw new ArgumentException("CAD path must not be empty.", nameof(cadPath));
        }
        var dir = Path.GetDirectoryName(cadPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(cadPath);
        return Path.Combine(dir, name + Extension);
    }

    public async Task<ModulesFile?> LoadAsync(string cadPath, CancellationToken cancellationToken = default)
    {
        var path = ResolveModulesPath(cadPath);
        if (!File.Exists(path))
        {
            return null;
        }
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Serializer.Deserialize<ModulesFile>(fs);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load modules file: {path}", ex);
        }
    }

    public async Task SaveAsync(string cadPath, ModulesFile file, CancellationToken cancellationToken = default)
    {
        if (file == null)
        {
            throw new ArgumentNullException(nameof(file));
        }
        var path = ResolveModulesPath(cadPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Serializer.Serialize(fs, file);
            await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save modules file: {path}", ex);
        }
    }
}
