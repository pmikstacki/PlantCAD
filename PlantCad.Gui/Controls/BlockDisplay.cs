using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.Renderers;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls;

public sealed class BlockDisplay : Control
{
    public static readonly StyledProperty<string?> SourcePathProperty = AvaloniaProperty.Register<
        BlockDisplay,
        string?
    >(nameof(SourcePath));
    public static readonly StyledProperty<string?> BlockNameProperty = AvaloniaProperty.Register<
        BlockDisplay,
        string?
    >(nameof(BlockName));

    public string? SourcePath
    {
        get => GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public string? BlockName
    {
        get => GetValue(BlockNameProperty);
        set => SetValue(BlockNameProperty, value);
    }

    private readonly ICadEntityRenderer[] _entityRenderers =
    {
        new HatchRenderer(),
        new SolidRenderer(),
        new PolylineRenderer(),
        new SplineRenderer(),
        new LineRenderer(),
        new CircleRenderer(),
        new ArcRenderer(),
        new EllipseRenderer(),
        new TextRenderer(),
        new MTextRenderer(),
        new InsertRenderer(),
    };

    private readonly SimpleCadRendererHost _renderer;
    private readonly ViewportState _state = new();

    public BlockDisplay()
    {
        _renderer = new SimpleCadRendererHost(_entityRenderers);
        AffectsRender<BlockDisplay>(SourcePathProperty, BlockNameProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;
        if (string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(BlockName))
            return;
        try
        {
            var model = BlockModelService.GetModel(SourcePath!, BlockName!);

            var layers = model.Layers;
            var style = new DefaultStyleProvider(layers);
            var options = new CadRenderOptions(layers);
            // For previews, force all layers visible regardless of DWG layer on/off/frozen state
            foreach (var layer in layers)
            {
                options.SetLayerVisible(layer.Name, true);
            }

            var rect = new Rect(size);
            _state.FitToExtents(rect, model.Extents, margin: 4);
            _renderer.Render(context, _state, model, size, style, options);
        }
        catch (Exception ex)
        {
            // Log and skip drawing for this frame; do not crash the UI render loop
            var logger = ServiceRegistry.LoggerFactory?.CreateLogger<BlockDisplay>();
            logger?.LogError(
                ex,
                "BlockDisplay render failed for '{BlockName}' from '{SourcePath}'",
                BlockName,
                SourcePath
            );
            ServiceRegistry.LogsTool?.Append(
                $"Block preview error for '{BlockName}' from '{SourcePath}': {ex.Message}"
            );
        }
    }
}
