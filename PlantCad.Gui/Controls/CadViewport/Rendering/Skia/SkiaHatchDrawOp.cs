using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.CadViewport.Hatching;
using PlantCad.Gui.Controls.Debug;
using PlantCad.Gui.Controls.Hatching;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;
using SkiaSharp;

namespace PlantCad.Gui.Controls.Rendering.Skia;

internal sealed class SkiaHatchDrawOp : ICustomDrawOperation
{
    private readonly Rect _deviceBounds;
    private readonly SKPoint _renderOriginWorld;
    private readonly CadHatch _hatch;
    private readonly ViewportState _state;
    private readonly Color _strokeColor;
    private readonly double _strokeThickness;
    private readonly ILogger? _logger;
    private readonly IHatchShaderFactory? factory;
    private readonly Point _localOrigin;

    public SkiaHatchDrawOp(
        Rect deviceBounds,
        Point renderOriginWorld,
        CadHatch hatch,
        ViewportState state,
        Color strokeColor,
        double strokeThickness,
        Point localOrigin,
        ILogger? logger = null,
        IHatchShaderFactory? factory = null
    )
    {
        _deviceBounds = deviceBounds;
        _renderOriginWorld = new SKPoint((float)renderOriginWorld.X, (float)renderOriginWorld.Y);
        _hatch = hatch;
        _state = state;
        _strokeColor = strokeColor;
        _strokeThickness = strokeThickness;
        _localOrigin = localOrigin;
        _logger = logger;
        this.factory = factory;
    }

    public void Dispose()
    {
        // no-op
    }

    public Rect Bounds => _deviceBounds;

    public bool HitTest(Point p) => false;

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeatureObj = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature));
        var leaseFeature = leaseFeatureObj as ISkiaSharpApiLeaseFeature;
        if (leaseFeature == null)
        {
            return;
        }

        // Targeted Debug Logging
        bool isDebugTarget =
            DebugSettings.Hatches.ShowHatchDiagnostics
            || (
                !string.IsNullOrEmpty(DebugSettings.Hatches.DebugHatchId)
                && string.Equals(
                    _hatch.Id,
                    DebugSettings.Hatches.DebugHatchId,
                    StringComparison.OrdinalIgnoreCase
                )
            );

        if (isDebugTarget && _logger != null)
        {
            _logger.LogInformation(
                "[HatchDebug] Rendering Hatch ID={Id} Pattern={PatternName} Fill={FillKind}",
                _hatch.Id,
                _hatch.PatternName,
                _hatch.FillKind
            );
            _logger.LogInformation(
                "[HatchDebug] Bounds={Bounds} PatternScale={PatternScale} PatternAngle={PatternAngleDeg}",
                _deviceBounds,
                _hatch.PatternScale,
                _hatch.PatternAngleDeg
            );
            _logger.LogInformation(
                "[HatchDebug] Rendering Hatch ID={Id}. LocalOrigin={Origin}",
                _hatch.Id,
                _localOrigin
            );
        }

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;
        if (canvas == null)
            return;

        // Build hatch polygon path in LOCAL COORDINATES (World - LocalOrigin)
        // Shift coordinates to a local origin near the geometry to preserve float precision in Skia.
        var renderBase = _deviceBounds.TopLeft;

        // Save canvas state before transforming
        int saveCount = canvas.Save();
        try
        {
            // Move the canvas origin to the hatch location
            canvas.Translate((float)renderBase.X, (float)renderBase.Y);

            // Create path relative to renderBase
            using var path = new SKPath();
            path.FillType = SKPathFillType.EvenOdd;

            // Transform points to local space
            foreach (var loop in _hatch.Loops)
            {
                if (loop == null || loop.Count < 3)
                    continue;

                // Shift first point by LocalOrigin and renderBase
                var p0 = loop[0];
                path.MoveTo(
                    (float)(p0.X - _localOrigin.X - renderBase.X),
                    (float)(p0.Y - _localOrigin.Y - renderBase.Y)
                );

                for (int i = 1; i < loop.Count; i++)
                {
                    var pi = loop[i];
                    path.LineTo(
                        (float)(pi.X - _localOrigin.X - renderBase.X),
                        (float)(pi.Y - _localOrigin.Y - renderBase.Y)
                    );
                }
                path.Close();
            }

            // Draw Solid Fill if requested
            if (
                _hatch.FillKind == CadHatchFillKind.Solid
                || _hatch.FillKind == CadHatchFillKind.Gradient
            )
            {
                using var fillPaint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(
                        _strokeColor.R,
                        _strokeColor.G,
                        _strokeColor.B,
                        _strokeColor.A
                    ),
                };
                canvas.DrawPath(path, fillPaint);
                return;
            }

            // Clip to hatch geometry (in local space)
            canvas.ClipPath(path, antialias: true);

            if (_hatch.PatternLines == null || _hatch.PatternLines.Count == 0)
            {
                // Fallback if no pattern lines defined (omitted for now)
                return;
            }

            // Calculate the shader RenderOrigin:
            // TopLeft World = _localOrigin + _deviceBounds.TopLeft.
            var worldTopLeft = _localOrigin + _deviceBounds.TopLeft;

            SKShader? shader;
            var shaderFactory = ServiceRegistry.HatchShaderFactory;
            if (shaderFactory != null)
            {
                // Build postLocalMatrix to map PAT world space -> canvas local space
                // 1) Start from world->screen with local-origin compensation
                var worldToScreen = RenderHelpers.ComposeWithLocalOrigin(
                    _state.Transform,
                    _localOrigin
                );
                var postLocal = ToSkMatrix(worldToScreen);
                // 2) Subtract the canvas translate we applied above to keep coordinates small
                postLocal = postLocal.PostConcat(
                    SKMatrix.CreateTranslation((float)(-renderBase.X), (float)(-renderBase.Y))
                );

                // Compute extra scale for pattern unit conversion (ANSI=inches, ISO=mm)

                shader = shaderFactory.CreateShader(
                    _hatch,
                    new SKColor(_strokeColor.R, _strokeColor.G, _strokeColor.B, _strokeColor.A),
                    _strokeThickness,
                    worldTopLeft,
                    postLocal,
                    _logger
                );
            }
            else
            {
                // Should technically not happen if ServiceRegistry is set up
                return;
            }
            _logger?.LogDebug("[HatchRenderer] Shader: {shader}", shader?.ToString());

            // Fill the entire bounds (now clipped to path) with the pattern shader
            // Note: We are drawing a rect covering the hatch area (0,0, W, H) in local space
            var localRect = new SKRect(
                0,
                0,
                (float)_deviceBounds.Width,
                (float)_deviceBounds.Height
            );

            using var patternPaint = new SKPaint
            {
                Shader = shader,
                IsAntialias = false,
                Style = SKPaintStyle.Fill,
            };

            canvas.DrawRect(localRect, patternPaint);

            // Debug Overlay
            if (isDebugTarget)
            {
                // Draw bounds outline in red.
                using var debugPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Red,
                    StrokeWidth = 2,
                };
                canvas.DrawRect(localRect, debugPaint);
            }
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    private static SKMatrix ToSkMatrix(Matrix m)
    {
        return new SKMatrix
        {
            ScaleX = (float)m.M11,
            SkewX = (float)m.M21,
            TransX = (float)m.M31,
            SkewY = (float)m.M12,
            ScaleY = (float)m.M22,
            TransY = (float)m.M32,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1,
        };
    }

    public bool Equals(ICustomDrawOperation? other)
    {
        return false;
    }
}
