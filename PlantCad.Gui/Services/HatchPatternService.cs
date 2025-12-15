using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ACadSharp.Entities;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Services;

public sealed class HatchPatternService
{
    private readonly object _sync = new();
    private bool _initialized;
    private readonly List<HatchPatternInfo> _patterns = new();

    public static HatchPatternService Instance { get; } = new();

    private HatchPatternService() { }

    public IReadOnlyList<HatchPatternInfo> GetAll()
    {
        EnsureLoaded();
        lock (_sync)
        {
            return _patterns.ToList();
        }
    }

    public HatchPatternInfo? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Pattern name must not be empty.", nameof(name));
        }
        EnsureLoaded();
        lock (_sync)
        {
            return _patterns.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    public IReadOnlyList<HatchPatternInfo> Search(string? query)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetAll();
        }
        var q = query.Trim();
        lock (_sync)
        {
            return _patterns
                .Where(p =>
                    p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                )
                .ToList();
        }
    }

    private void EnsureLoaded()
    {
        if (_initialized)
        {
            return;
        }
        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }
            try
            {
                LoadAllInternal();
                _initialized = true;
            }
            catch (Exception ex)
            {
                var logger = ServiceRegistry.LoggerFactory?.CreateLogger<HatchPatternService>();
                logger?.LogError(ex, "Failed to load hatch patterns.");
                ServiceRegistry.LogsTool?.Append($"[HATCH] Error loading patterns: {ex.Message}");
                throw;
            }
        }
    }

    private void LoadAllInternal()
    {
        _patterns.Clear();
        string baseDir = AppContext.BaseDirectory;
        string hatchDir = Path.Combine(baseDir, "Assets", "HATCH");
        if (!Directory.Exists(hatchDir))
        {
            ServiceRegistry.LogsTool?.Append($"[HATCH] Directory not found: {hatchDir}");
            return;
        }
        var files = Directory
            .EnumerateFiles(hatchDir, "*.pat", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(hatchDir, "*.PAT", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var file in files)
        {
            try
            {
                var patterns = HatchPattern.LoadFrom(file);
                foreach (var pat in patterns)
                {
                    // Map to our info DTO
                    var info = new HatchPatternInfo
                    {
                        Name = pat.Name ?? string.Empty,
                        Description = pat.Description,
                        SourceFile = file,
                        Lines =
                            pat.Lines?.Select(l => new PatternLine
                                {
                                    AngleDeg = l.Angle * 180.0 / Math.PI,
                                    BaseX = l.BasePoint.X,
                                    BaseY = l.BasePoint.Y,
                                    // Note: ACadSharp rotates offset into line space; we adopt it as-is.
                                    OffsetX = l.Offset.X,
                                    OffsetY = l.Offset.Y,
                                    DashLengths = l.DashLengths?.ToArray() ?? Array.Empty<double>(),
                                })
                                .ToList() ?? new List<PatternLine>(),
                    };
                    if (!string.IsNullOrWhiteSpace(info.Name))
                    {
                        _patterns.Add(info);
                    }
                }
            }
            catch (Exception ex)
            {
                // Attempt a sanitize-and-retry for common formatting issues (e.g., empty tokens, inline comments)
                try
                {
                    var sanitized = SanitizePatFile(file);
                    if (sanitized != null)
                    {
                        var patterns = HatchPattern.LoadFrom(sanitized);
                        foreach (var pat in patterns)
                        {
                            var info = new HatchPatternInfo
                            {
                                Name = pat.Name ?? string.Empty,
                                Description = pat.Description,
                                SourceFile = file,
                                Lines =
                                    pat.Lines?.Select(l => new PatternLine
                                        {
                                            AngleDeg = l.Angle * 180.0 / Math.PI,
                                            BaseX = l.BasePoint.X,
                                            BaseY = l.BasePoint.Y,
                                            OffsetX = l.Offset.X,
                                            OffsetY = l.Offset.Y,
                                            DashLengths =
                                                l.DashLengths?.ToArray() ?? Array.Empty<double>(),
                                        })
                                        .ToList() ?? new List<PatternLine>(),
                            };
                            if (!string.IsNullOrWhiteSpace(info.Name))
                            {
                                _patterns.Add(info);
                            }
                        }
                        ServiceRegistry.LogsTool?.Append(
                            $"[HATCH] Parsed with sanitize: {Path.GetFileName(file)}"
                        );
                        continue; // go next file
                    }
                }
                catch (Exception rex)
                {
                    ServiceRegistry.LogsTool?.Append(
                        $"[HATCH] Sanitize retry failed for '{file}': {rex.Message}"
                    );
                }

                // If still failing, log concise warning and continue
                var logger = ServiceRegistry.LoggerFactory?.CreateLogger<HatchPatternService>();
                logger?.LogWarning("Failed to parse {File}: {Message}", file, ex.Message);
                ServiceRegistry.LogsTool?.Append($"[HATCH] Failed to parse '{file}': {ex.Message}");

                // Register header-only unsupported entries so UI can indicate status without crashing
                foreach (var header in CollectHeaderOnlyPatterns(file))
                {
                    _patterns.Add(header);
                }
            }
        }
        // Ensure unique by name (last wins if duplicates)
        var distinct = _patterns
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _patterns.Clear();
        _patterns.AddRange(distinct);
        ServiceRegistry.LogsTool?.Append(
            $"[HATCH] Loaded {_patterns.Count} patterns from {files.Count} files."
        );
    }

    private static string? SanitizePatFile(string file)
    {
        var lines = File.ReadAllLines(file);
        if (lines.Length == 0)
        {
            return null;
        }

        List<string> output = new();
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string line = raw;
            // Remove inline comments starting with ';'
            int semi = line.IndexOf(';');
            if (semi >= 0)
            {
                line = line.Substring(0, semi);
            }
            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            // Keep header lines as-is
            if (line.StartsWith("*"))
            {
                output.Add(line);
                continue;
            }

            // Normalize data lines: split by ',', trim tokens, remove empties
            var parts = line.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (parts.Count == 0)
            {
                continue;
            }
            string rebuilt = string.Join(", ", parts);
            output.Add(rebuilt);
        }

        if (output.Count == 0)
        {
            return null;
        }

        string temp = Path.Combine(Path.GetTempPath(), $"sanitized_{Path.GetFileName(file)}");
        File.WriteAllLines(temp, output);
        return temp;
    }

    private static IEnumerable<HatchPatternInfo> CollectHeaderOnlyPatterns(string file)
    {
        var result = new List<HatchPatternInfo>();
        try
        {
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (!line.StartsWith("*"))
                    continue;
                // header format: *NAME[, description]
                var rest = line.Substring(1);
                var firstComma = rest.IndexOf(',');
                string name;
                string? desc = null;
                if (firstComma >= 0)
                {
                    name = rest.Substring(0, firstComma).Trim();
                    desc = rest.Substring(firstComma + 1).Trim();
                }
                else
                {
                    name = rest.Trim();
                }
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                result.Add(
                    new HatchPatternInfo
                    {
                        Name = name,
                        Description = desc,
                        SourceFile = file,
                        Lines = Array.Empty<PatternLine>(),
                        IsSupported = false,
                    }
                );
            }
        }
        catch (Exception)
        {
            // ignore errors while collecting headers
        }
        return result;
    }
}
