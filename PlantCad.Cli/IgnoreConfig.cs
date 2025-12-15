using System.Text.Json;

namespace PlantCad.Cli;

public sealed class IgnoreConfig
{
    public List<string> Blocks { get; set; } = new();

    public static IgnoreConfig Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Ignore config file not found.", path);
        }

        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<IgnoreConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (cfg == null)
        {
            throw new InvalidOperationException("Failed to parse ignore config JSON.");
        }

        cfg.Blocks = cfg.Blocks
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return cfg;
    }

    public void Save(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", nameof(path));
        }

        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        Blocks = Blocks
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(full, json);
    }

    public HashSet<string> ToSet()
    {
        return new HashSet<string>(Blocks, StringComparer.OrdinalIgnoreCase);
    }
}