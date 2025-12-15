using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlantCad.Gui.ViewModels.Dialogs;

public sealed partial class BlockAttributesDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string sourcePath = string.Empty;

    [ObservableProperty]
    private string blockName = string.Empty;

    public ObservableCollection<AttributeItem> Items { get; } = new();

    [ObservableProperty]
    private bool hasUnsavedChanges;

    public void Initialize(string sourcePath, string blockName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(blockName))
            throw new ArgumentException("Block name must not be empty.", nameof(blockName));
        SourcePath = sourcePath;
        BlockName = blockName;
        Items.Clear();

        var doc = DwgReader.Read(sourcePath);
        var br = doc.BlockRecords.FirstOrDefault(b =>
            string.Equals(b.Name, blockName, StringComparison.Ordinal)
        );
        if (br == null)
        {
            throw new InvalidOperationException(
                $"Block '{blockName}' not found in '{sourcePath}'."
            );
        }
        foreach (var def in br.AttributeDefinitions)
        {
            Items.Add(
                new AttributeItem
                {
                    Tag = def.Tag ?? string.Empty,
                    Prompt = (def as AttributeDefinition)?.Prompt ?? string.Empty,
                    DefaultValue = def.Value ?? string.Empty,
                    Flags = def.Flags,
                    Type = def.AttributeType,
                }
            );
        }
        HasUnsavedChanges = false;
    }

    [RelayCommand]
    private void Add()
    {
        Items.Add(
            new AttributeItem
            {
                Tag = "NEW_TAG",
                Prompt = string.Empty,
                DefaultValue = string.Empty,
                Flags = AttributeFlags.None,
                Type = AttributeType.SingleLine,
            }
        );
        HasUnsavedChanges = true;
    }

    [RelayCommand]
    private void Remove(object? param)
    {
        if (param is AttributeItem it)
        {
            Items.Remove(it);
            HasUnsavedChanges = true;
        }
    }

    [RelayCommand]
    private void MarkChanged()
    {
        HasUnsavedChanges = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await Task.Run(() => SaveCore());
        HasUnsavedChanges = false;
    }

    private void SaveCore()
    {
        ValidateOrThrow();
        var doc = DwgReader.Read(SourcePath);
        var br = doc.BlockRecords.FirstOrDefault(b =>
            string.Equals(b.Name, BlockName, StringComparison.Ordinal)
        );
        if (br == null)
        {
            throw new InvalidOperationException(
                $"Block '{BlockName}' not found in '{SourcePath}'."
            );
        }
        // Remove existing definitions
        var toRemove = br.Entities.OfType<AttributeDefinition>().ToList();
        foreach (var d in toRemove)
        {
            br.Entities.Remove(d);
        }
        // Add new ones in order
        foreach (var row in Items)
        {
            var def = new AttributeDefinition
            {
                Tag = row.Tag ?? string.Empty,
                Prompt = row.Prompt ?? string.Empty,
                Value = row.DefaultValue ?? string.Empty,
                Flags = row.Flags,
                AttributeType = row.Type,
            };
            br.Entities.Add(def);
        }
        // Persist to same file
        ACadSharp.IO.DwgWriter.Write(SourcePath, doc);
    }

    private void ValidateOrThrow()
    {
        // Ensure all tags are non-empty and unique (case-insensitive)
        var tags = Items.Select(it => (it.Tag ?? string.Empty).Trim()).ToList();
        if (tags.Any(t => string.IsNullOrWhiteSpace(t)))
        {
            throw new InvalidOperationException("All attribute tags must be non-empty.");
        }
        var dup = tags.GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
        {
            throw new InvalidOperationException(
                $"Duplicate attribute tag detected: '{dup.Key}'. Tags must be unique (case-insensitive)."
            );
        }
    }
}

public sealed partial class AttributeItem : ObservableObject
{
    [ObservableProperty]
    private string tag = string.Empty;

    [ObservableProperty]
    private string prompt = string.Empty;

    [ObservableProperty]
    private string defaultValue = string.Empty;

    [ObservableProperty]
    private AttributeFlags flags;

    [ObservableProperty]
    private AttributeType type = AttributeType.SingleLine;
}
