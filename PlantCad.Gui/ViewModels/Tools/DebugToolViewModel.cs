using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Controls.CadViewport.Hatching;
using PlantCad.Gui.Controls.Debug;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.ViewModels.Tools;

public partial class DebugToolViewModel : Tool
{
    public ObservableCollection<HatchCacheEntry> CacheEntries { get; } = new();

    [ObservableProperty]
    private bool hatchesEnabled;

    [ObservableProperty]
    private string? debugHatchId;

    [ObservableProperty]
    private int boundsInflation;

    [ObservableProperty]
    private bool useFixedSquareTile;

    [ObservableProperty]
    private double fixedTileSize;

    [ObservableProperty]
    private string debugOutput = string.Empty;

    [ObservableProperty]
    private HatchCacheEntry? selectedCacheEntry;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? selectedPreviewImage;

    private readonly IHatchShaderCache _cache;

    public DebugToolViewModel()
    {
        _cache = ServiceRegistry.HatchShaderCache;
        Title = "Debug";
        CanClose = false;
        DockGroup = "Tools";
        HatchesEnabled = DebugSettings.Hatches.ShowHatchDiagnostics;
        DebugHatchId = DebugSettings.Hatches.DebugHatchId;
        BoundsInflation = DebugSettings.Hatches.BoundsInflation;
        UseFixedSquareTile = DebugSettings.Hatches.UseFixedSquareTile;
        FixedTileSize = DebugSettings.Hatches.FixedTileSize;

        // Hook up log output
        DebugSettings.Hatches.DebugLogOutput = (msg) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Simple circular buffer or just append
                if (DebugOutput.Length > 2000)
                    DebugOutput = DebugOutput.Substring(DebugOutput.Length - 1000);
                DebugOutput += msg + "\n";
                OnPropertyChanged(nameof(DebugOutput));
            });
        };

        // Hook up hatch cache registry
        _cache.Changed += () => RefreshCacheEntries();
        RefreshCacheEntries();
    }

    partial void OnSelectedCacheEntryChanged(HatchCacheEntry? value)
    {
        if (value == null)
        {
            SelectedPreviewImage = null;
            return;
        }

        if (_cache.TryGetPicture(value.Key, out var picture))
        {
            // Create a meaningful preview by tiling the picture
            // The picture is a single horizontal line/dash tile.
            // We want to show what it looks like as a 2D pattern.

            int width = 256;
            int height = 256;

            var info = new SkiaSharp.SKImageInfo(width, height);
            using var surface = SkiaSharp.SKSurface.Create(info);
            if (surface != null)
            {
                var canvas = surface.Canvas;
                canvas.Clear(SkiaSharp.SKColors.Transparent);

                // Draw checkerboard background
                using (var paint = new SkiaSharp.SKPaint())
                {
                    paint.Color = new SkiaSharp.SKColor(240, 240, 240);
                    canvas.DrawRect(0, 0, width, height, paint);
                    paint.Color = new SkiaSharp.SKColor(200, 200, 200);
                    for (int x = 0; x < width; x += 10)
                    for (int y = 0; y < height; y += 10)
                        if ((x / 10 + y / 10) % 2 == 0)
                            canvas.DrawRect(x, y, 10, 10, paint);
                }

                // Construct shader logic similar to HatchShaderFactory
                // 1. Get tile dimension L from the picture bounds
                var tileRect = picture.CullRect;
                float L = tileRect.Width;
                if (L < 1)
                    L = 256; // Fallback

                // 2. Target Spacing and DashPeriod from Cache Entry
                float spacing = (float)value.AbsSpacing;
                float dashPeriod = (float)value.DashPeriod;
                if (dashPeriod < 1e-6)
                    dashPeriod = 1.0f;
                if (spacing < 1e-6)
                    spacing = 1.0f;

                // 3. Matrix: Scale Tile(L) -> Pattern(Dash x Spacing)
                // We only apply scaling to the shader. Rotation and Zoom will be handled by Canvas.
                var shaderMat = SkiaSharp.SKMatrix.CreateScale(dashPeriod / L, spacing / L);

                // 4. Calculate Zoom to ensure visibility
                // We want visual spacing to be roughly 10-20 pixels
                float zoom = 1.0f;
                if (spacing > 0)
                {
                    zoom = 20.0f / spacing;
                }
                if (zoom < 0.5f)
                    zoom = 0.5f; // Don't zoom out too much
                if (zoom > 50.0f)
                    zoom = 50.0f; // Cap max zoom

                using (
                    var shader = SkiaSharp.SKShader.CreatePicture(
                        picture,
                        SkiaSharp.SKShaderTileMode.Repeat,
                        SkiaSharp.SKShaderTileMode.Repeat,
                        shaderMat,
                        tileRect
                    )
                )
                using (var fillPaint = new SkiaSharp.SKPaint { Shader = shader })
                {
                    canvas.Save();
                    // Center origin
                    canvas.Translate(width / 2f, height / 2f);
                    // Rotate 45 degrees to show "hatchiness"
                    canvas.RotateDegrees(45);
                    // Apply calculated zoom
                    canvas.Scale(zoom, zoom);

                    // Draw a large rect centered at the (transformed) origin to fill the view
                    // The clip of the surface will handle the bounds.
                    canvas.DrawRect(-10000, -10000, 20000, 20000, fillPaint);

                    canvas.Restore();
                }

                using var image = surface.Snapshot();
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                using var stream = new System.IO.MemoryStream();
                data.SaveTo(stream);
                stream.Position = 0;
                SelectedPreviewImage = new Avalonia.Media.Imaging.Bitmap(stream);
            }
        }
        else
        {
            SelectedPreviewImage = null;
        }
    }

    private void RefreshCacheEntries()
    {
        var snapshot = _cache.GetSnapshot();
        Dispatcher.UIThread.Post(() =>
        {
            CacheEntries.Clear();
            foreach (var e in snapshot)
            {
                CacheEntries.Add(e);
            }
            OnPropertyChanged(nameof(CacheEntries));
        });
    }

    partial void OnHatchesEnabledChanged(bool value)
    {
        DebugSettings.Hatches.ShowHatchDiagnostics = value;
    }

    partial void OnDebugHatchIdChanged(string? value)
    {
        DebugSettings.Hatches.DebugHatchId = value;
    }

    partial void OnBoundsInflationChanged(int value)
    {
        DebugSettings.Hatches.BoundsInflation = value;
    }

    partial void OnUseFixedSquareTileChanged(bool value)
    {
        DebugSettings.Hatches.UseFixedSquareTile = value;
    }

    partial void OnFixedTileSizeChanged(double value)
    {
        DebugSettings.Hatches.FixedTileSize = value;
    }
}
