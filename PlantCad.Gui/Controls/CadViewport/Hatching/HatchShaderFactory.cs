using System;
using System.Collections.Concurrent;
using System.Linq;
using Avalonia;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.Debug;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;
using SkiaSharp;

namespace PlantCad.Gui.Controls.CadViewport.Hatching
{
    public class HatchShaderFactory(IHatchShaderCache shaderCache) : IHatchShaderFactory
    {
        // SKPicture cache is centralized in HatchShaderCacheRegistry

        public SKShader? CreateShader(
            CadHatch hatch,
            SKColor color,
            double strokeWidth,
            Point renderOrigin,
            SKMatrix? postLocalMatrix = null,
            ILogger? logger = null
        )
        {
            if (logger != null && DebugSettings.Hatches.ShowHatchDiagnostics)
            {
                logger.LogInformation(
                    $"[HatchShaderFactory] CreateShader ID={hatch.Id} Pattern={hatch.PatternName} Color={color} StrokeWidth={strokeWidth} Origin={renderOrigin}"
                );
            }
            logger?.LogDebug("[HatchShaderFactory] Creating shader for hatch {hatch}", hatch.Id);
            SKShader? combinedShader = null;
            int lineIndex = 0;

            // Iterate over each line family in the pattern definition
            foreach (var lineDef in hatch.PatternLines)
            {
                var familyShader = CreateFamilyShader(
                    hatch,
                    lineDef,
                    color,
                    strokeWidth,
                    renderOrigin,
                    postLocalMatrix,
                    lineIndex,
                    logger
                );
                lineIndex++;

                if (familyShader == null)
                {
                    logger?.LogWarning(
                        "HatchShaderFactory]Family Shader was null for line {lineIndex}",
                        lineIndex
                    );
                    continue;
                }

                if (logger != null && DebugSettings.Hatches.ShowHatchDiagnostics)
                {
                    logger.LogInformation(
                        $"[HatchShaderFactory] Family {lineIndex} Shader Created. Composing..."
                    );
                }

                if (combinedShader == null)
                {
                    // First family initializes the composition; this is expected, not a warning.
                    logger?.LogDebug(
                        "[HatchShaderFactory] Initialize combined shader with family {line}",
                        lineIndex
                    );
                    combinedShader = familyShader;
                }
                else
                {
                    // Compose with existing shader (Union)
                    logger?.LogDebug(
                        "[HatchShaderFactory] Composing final hatch made of {lines} lines",
                        hatch.PatternLines.Count
                    );

                    combinedShader = SKShader.CreateCompose(
                        combinedShader,
                        familyShader,
                        SKBlendMode.SrcOver
                    );
                }
            }

            if (combinedShader == null)
            {
                logger?.LogWarning(
                    "[HatchShaderFactory] No family shader produced output. Using fallback stripes shader for hatch {hatchId}",
                    hatch.Id
                );
                combinedShader = CreateFallbackShader(color);
            }

            return combinedShader;
        }

        private SKShader? CreateFamilyShader(
            CadHatch hatch,
            CadHatchPatternLine line,
            SKColor color,
            double strokeWidth,
            Point renderOrigin,
            SKMatrix? postLocalMatrix,
            int lineIndex,
            ILogger? logger
        )
        {
            try
            {
                // 1. Analyze the Pattern Line Definition
                // The definition provides Angle, Base, Offset, and Dashes in the "Pattern Space".
                // We need to construct a tile that repeats to match this definition.

                // Decompose Offset (Vector to next line) into Parallel (Shift) and Perpendicular (Spacing) components relative to the Line Angle.
                double rad = line.AngleDeg * Math.PI / 180.0;
                double ux = Math.Cos(rad);
                double uy = Math.Sin(rad);
                double vx = -uy;
                double vy = ux;

                // Project Offset onto the line basis (u, v)
                // Offset is the step from one line to the next.
                // u-component = Parallel Shift (Stagger)
                // v-component = Perpendicular Spacing
                double shift = line.OffsetX * ux + line.OffsetY * uy;
                double spacing = line.OffsetX * vx + line.OffsetY * vy;

                // If spacing is effectively zero, it's a degenerate pattern (single line? infinite density?).
                // Avoid divide by zero.
                if (Math.Abs(spacing) < 1e-6)
                {
                    // Fallback: treat as non-repeating or huge spacing?
                    // Realistically, spacing=0 implies all lines are collinear.
                    // We'll skip or use a safe minimum.
                    spacing = 1.0;
                }

                double absSpacing = Math.Abs(spacing);
                float halfSpacing = (float)absSpacing / 2.0f;

                // 2. Tile Geometry Calculation
                // Center the tile vertically to ensure the line (at y=0) is not clipped at the edge.
                // Tile Y range: [-Height/2, Height/2].

                double dashPeriod = 0;
                string dashKey = "SOLID";
                if (line.DashLengths != null && line.DashLengths.Count > 0)
                {
                    foreach (var d in line.DashLengths)
                        dashPeriod += Math.Abs(d);
                    dashKey = string.Join(",", line.DashLengths.Select(d => d.ToString("F4")));
                }

                // If solid line, use an arbitrary width (e.g. 10.0) since it's infinite.
                if (dashPeriod < 1e-6)
                    dashPeriod = 10.0;

                logger?.LogInformation(
                    $"[HatchShaderFactory] Family {lineIndex}: Spacing={spacing:F6}, AbsSpacing={absSpacing:F6}, DashPeriod={dashPeriod:F6}, Color={color}, Shift={shift:F6}"
                );

                // Determine instance scale upfront (used for tile stroke mapping and later in matInstance)
                float scale = (float)((hatch.PatternScale <= 0 ? 1.0 : hatch.PatternScale));

                // 3. CACHE CHECK (delegated to cache)
                // Include stroke width in cache key because picture content depends on it.
                float tileStrokeWidth =
                    strokeWidth > 0 ? (float)(strokeWidth / Math.Max(scale, 1e-9)) : 0f;
                if (tileStrokeWidth > 0)
                {
                    // When PatternScale is very small, tileStrokeWidth becomes huge and the recorded picture
                    // gets fully covered/clipped by a single line (tile becomes a solid band).
                    // Clamp to tile height so the cached picture remains meaningful.
                    float tileHeight = DebugSettings.Hatches.UseFixedSquareTile
                        ? (float)Math.Max(1.0, DebugSettings.Hatches.FixedTileSize)
                        : (float)Math.Max(absSpacing, 1e-6);
                    float maxStroke = tileHeight * 0.85f;
                    if (tileStrokeWidth > maxStroke)
                    {
                        tileStrokeWidth = maxStroke;
                    }
                }
                string cacheKey = DebugSettings.Hatches.UseFixedSquareTile
                    ? $"F=1_L={(float)Math.Max(1.0, DebugSettings.Hatches.FixedTileSize):F2}_D={dashKey}_C={color}_W={tileStrokeWidth:F3}"
                    : $"S={absSpacing:F4}_D={dashKey}_C={color}_W={tileStrokeWidth:F3}";

                // Tile rect is deterministic from configuration and inputs
                SKRect shaderTileRect = DebugSettings.Hatches.UseFixedSquareTile
                    ? new SKRect(
                        0,
                        -(float)Math.Max(1.0, DebugSettings.Hatches.FixedTileSize) / 2f,
                        (float)Math.Max(1.0, DebugSettings.Hatches.FixedTileSize),
                        (float)Math.Max(1.0, DebugSettings.Hatches.FixedTileSize) / 2f
                    )
                    : new SKRect(0, -halfSpacing, (float)dashPeriod, halfSpacing);

                // Creator that records the picture on cache miss
                SKPicture picture = shaderCache.GetOrCreatePicture(
                    cacheKey,
                    () =>
                    {
                        using var recorder = new SKPictureRecorder();
                        using var canvas = recorder.BeginRecording(shaderTileRect);
                        using var paint = new SKPaint
                        {
                            Color = color,
                            StrokeWidth = tileStrokeWidth,
                            IsAntialias = true,
                            Style = SKPaintStyle.Stroke,
                            StrokeCap = SKStrokeCap.Square,
                        };

                        if (line.DashLengths != null && line.DashLengths.Count > 0)
                        {
                            double x = 0;
                            float scaleX = 1f;
                            if (DebugSettings.Hatches.UseFixedSquareTile)
                            {
                                float L = (float)Math.Max(1.0, DebugSettings.Hatches.FixedTileSize);
                                scaleX = (float)(L / dashPeriod);
                            }
                            for (int i = 0; i < line.DashLengths.Count; i++)
                            {
                                double d = line.DashLengths[i];
                                double absD = Math.Abs(d);
                                if (d >= 0)
                                {
                                    canvas.DrawLine(
                                        (float)(x * scaleX),
                                        0,
                                        (float)((x + absD) * scaleX),
                                        0,
                                        paint
                                    );
                                }
                                x += absD;
                            }
                        }
                        else
                        {
                            if (DebugSettings.Hatches.UseFixedSquareTile)
                            {
                                float L = (float)Math.Max(1.0, DebugSettings.Hatches.FixedTileSize);
                                canvas.DrawLine(0, 0, L, 0, paint);
                            }
                            else
                            {
                                canvas.DrawLine(0, 0, (float)dashPeriod, 0, paint);
                            }
                        }

                        return recorder.EndRecording();
                    },
                    absSpacing,
                    dashPeriod,
                    dashKey,
                    FormatColor(color),
                    shaderTileRect.ToString()
                );

                // 4. Construct the Matrix

                // Initial mapping from tile space to pattern space (apply in mapping order: center -> skew -> rotate -> base)
                SKMatrix matDef = SKMatrix.Identity;
                // Translate to Base (global) should be last in mapping, so it is first in concatenation chain
                matDef = matDef.PostConcat(
                    SKMatrix.CreateTranslation((float)line.BaseX, (float)line.BaseY)
                );
                // Rotate Line Angle
                matDef = matDef.PostConcat(SKMatrix.CreateRotationDegrees((float)line.AngleDeg));
                // Skew in line-local frame
                if (Math.Abs(spacing) > 1e-9)
                {
                    float skewX = (float)(shift / spacing);
                    if (float.IsNaN(skewX) || float.IsInfinity(skewX))
                    {
                        logger?.LogWarning(
                            $"[HatchShaderFactory] Invalid SkewX: {skewX}. Shift={shift}, Spacing={spacing}"
                        );
                        return null;
                    }
                    matDef = matDef.PostConcat(SKMatrix.CreateSkew(skewX, 0));
                }
                // Centered tile: translate from [-S/2..S/2] to [0..S] before skew (mapping order -> applied first)
                if (DebugSettings.Hatches.UseFixedSquareTile)
                {
                    float L = (float)Math.Max(1.0, DebugSettings.Hatches.FixedTileSize);
                    var defScale = SKMatrix.CreateScale(
                        (float)(dashPeriod / L),
                        (float)(absSpacing / L)
                    );
                    matDef = matDef.PostConcat(defScale);
                    matDef = matDef.PostConcat(SKMatrix.CreateTranslation(0, halfSpacing));
                }
                else
                {
                    matDef = matDef.PostConcat(SKMatrix.CreateTranslation(0, halfSpacing));
                }

                // Global Hatch Transforms (Instance)
                var matInstance = SKMatrix.Identity;
                // Pattern Scale (reuse computed scale)
                matInstance = matInstance.PostConcat(SKMatrix.CreateScale(scale, scale));

                // Pattern Angle
                matInstance = matInstance.PostConcat(
                    SKMatrix.CreateRotationDegrees((float)hatch.PatternAngleDeg)
                );

                // --- Precision Logic: Pattern Origin Snapping ---

                // Effective Angle in World
                double totalAngleRad = (line.AngleDeg + hatch.PatternAngleDeg) * Math.PI / 180.0;
                double uWx = Math.Cos(totalAngleRad);
                double uWy = Math.Sin(totalAngleRad);
                double vWx = -uWy;
                double vWy = uWx;

                // Spacing in World
                double spacingWorld = spacing * scale;
                // Shift in World (along line per step)
                double shiftWorld = shift * scale;

                // Dash Period in World
                double dashPeriodWorld = dashPeriod * scale;

                // Compute pixel spacing along v for LOD decision
                double pxPerWorldV_now = 0;
                double spacingPx_now = 0;
                if (postLocalMatrix.HasValue)
                {
                    var m_now = postLocalMatrix.Value;
                    double a0 = m_now.ScaleX,
                        b0 = m_now.SkewX,
                        d0 = m_now.SkewY,
                        e0 = m_now.ScaleY;
                    double sx0 = a0 * vWx + b0 * vWy;
                    double sy0 = d0 * vWx + e0 * vWy;
                    pxPerWorldV_now = Math.Sqrt(sx0 * sx0 + sy0 * sy0);
                    spacingPx_now = (absSpacing * scale) * pxPerWorldV_now;
                }
                if (logger != null && DebugSettings.Hatches.ShowHatchDiagnostics)
                {
                    logger.LogInformation(
                        "[HatchShaderFactory] Family {Index} WorldScale Debug: PatternScale={PatternScale:F4} SpacingWorld={SpacingWorld:F6} DashPeriodWorld={DashWorld:F6} PxPerWorldV={PxPerWorldV:F3} SpacingPx~={SpacingPx:F3}",
                        lineIndex,
                        hatch.PatternScale,
                        absSpacing * scale,
                        dashPeriodWorld,
                        pxPerWorldV_now,
                        spacingPx_now
                    );
                }

                // We want to find Delta = k * V_step + m * V_dash such that Origin + Delta ~= Target.
                var originalOrigin = hatch.PatternOrigin ?? new Point(0, 0);
                var diffX = renderOrigin.X - originalOrigin.X;
                var diffY = renderOrigin.Y - originalOrigin.Y;

                // Project Diff onto V (Perpendicular to line)
                double distPerp = diffX * vWx + diffY * vWy;

                // Number of steps in perpendicular direction
                double numSteps = 0;
                if (Math.Abs(spacingWorld) > 1e-9)
                {
                    numSteps = Math.Round(distPerp / spacingWorld);
                }

                if (double.IsNaN(numSteps) || double.IsInfinity(numSteps))
                {
                    logger?.LogWarning(
                        $"[HatchShaderFactory] Invalid numSteps: {numSteps}. SpacingW={spacingWorld}"
                    );
                    return null;
                }

                // Approximate shift due to steps
                double stepShiftX = numSteps * (shiftWorld * uWx + spacingWorld * vWx);
                double stepShiftY = numSteps * (shiftWorld * uWy + spacingWorld * vWy);

                // Residual vector
                double resX = diffX - stepShiftX;
                double resY = diffY - stepShiftY;

                // Project Residual onto U (Along line)
                double distPara = resX * uWx + resY * uWy;

                double numDashes = 0;
                // If dashed, snap to dash period. If solid, we can shift arbitrarily.
                if (Math.Abs(dashPeriodWorld) > 1e-9)
                {
                    // Dashed: snap to nearest period
                    numDashes = Math.Round(distPara / dashPeriodWorld);
                }

                if (double.IsNaN(numDashes) || double.IsInfinity(numDashes))
                {
                    logger?.LogWarning(
                        $"[HatchShaderFactory] Invalid numDashes: {numDashes}. DashPeriodW={dashPeriodWorld}"
                    );
                    return null;
                }

                double finalShiftX,
                    finalShiftY;
                if (Math.Abs(dashPeriodWorld) > 1e-9)
                {
                    finalShiftX = stepShiftX + numDashes * dashPeriodWorld * uWx;
                    finalShiftY = stepShiftY + numDashes * dashPeriodWorld * uWy;
                }
                else
                {
                    // Solid: exact match along the line
                    finalShiftX = stepShiftX + distPara * uWx;
                    finalShiftY = stepShiftY + distPara * uWy;
                }

                // New Effective Origin
                double newOriginX = originalOrigin.X + finalShiftX;
                double newOriginY = originalOrigin.Y + finalShiftY;

                // Calculate delta in double precision first.
                double deltaX = newOriginX - renderOrigin.X;
                double deltaY = newOriginY - renderOrigin.Y;

                // Validate Delta
                if (
                    double.IsNaN(deltaX)
                    || double.IsInfinity(deltaX)
                    || double.IsNaN(deltaY)
                    || double.IsInfinity(deltaY)
                )
                {
                    logger?.LogWarning(
                        $"[HatchShaderFactory] Invalid Delta: ({deltaX}, {deltaY}). Steps={numSteps}, SpacingW={spacingWorld}"
                    );
                    return null;
                }

                // Apply the small delta
                matInstance = matInstance.PostConcat(
                    SKMatrix.CreateTranslation((float)deltaX, (float)deltaY)
                );

                // Combine with correct order (mapping order: def -> instance -> postLocal)
                // Using PostConcat, which composes as M = M * other and applies 'other' first when mapping.
                var finalMatrix = SKMatrix.Identity;
                if (postLocalMatrix.HasValue)
                {
                    finalMatrix = finalMatrix.PostConcat(postLocalMatrix.Value);
                }
                finalMatrix = finalMatrix.PostConcat(matInstance);
                finalMatrix = finalMatrix.PostConcat(matDef);

                if (logger != null && DebugSettings.Hatches.ShowHatchDiagnostics)
                {
                    logger.LogInformation(
                        $"[HatchShaderFactory] Line {lineIndex}: Matrix={finalMatrix}, ShaderTileRect={shaderTileRect}"
                    );
                }

                // 4. Create Shader (single vector path)
                var resultShader = SKShader.CreatePicture(
                    picture,
                    SKShaderTileMode.Repeat,
                    SKShaderTileMode.Repeat,
                    finalMatrix,
                    shaderTileRect
                );
                if (logger != null && DebugSettings.Hatches.ShowHatchDiagnostics)
                {
                    logger.LogInformation(
                        $"[HatchShaderFactory] Line {lineIndex}: Shader Created. TileRect={shaderTileRect} Matrix={finalMatrix}"
                    );
                }
                return resultShader;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"[HatchShaderFactory] Exception in CreateFamilyShader");
                return null;
            }
        }

        private static string FormatColor(SKColor c)
        {
            return $"#{c.Alpha:X2}{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
        }

        private static SKShader CreateFallbackShader(SKColor color)
        {
            // Simple 8x8 diagonal stripes pattern
            var tileRect = new SKRect(0, 0, 8, 8);
            using var recorder = new SKPictureRecorder();
            using var canvas = recorder.BeginRecording(tileRect);

            using (
                var paint = new SKPaint
                {
                    Color = color,
                    StrokeWidth = 0,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                }
            )
            {
                // Two diagonals to form an "X" style stripe per tile
                canvas.DrawLine(-4, 8, 8, -4, paint);
                canvas.DrawLine(0, 8, 8, 0, paint);
            }

            var picture = recorder.EndRecording();
            var matrix = SKMatrix.Identity;
            return SKShader.CreatePicture(
                picture,
                SKShaderTileMode.Repeat,
                SKShaderTileMode.Repeat,
                matrix,
                tileRect
            );
        }
    }
}
