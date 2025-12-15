using System;

namespace PlantCad.Gui.Controls.Hatching;

/// <summary>
/// Math helpers for hatch pattern rendering shared by CPU (vector/raster) and GPU paths.
/// All functions are pure and side-effect free. Angles are in degrees unless stated otherwise.
/// </summary>
internal static class HatchPatternMath
{
    /// <summary>
    /// Returns orthonormal axes for a line family at the given angle (in degrees).
    /// u = direction along the line, v = normal (perpendicular) to the line.
    /// </summary>
    public static void GetEffectiveLineAxes(
        double totalAngleDeg,
        out double ux,
        out double uy,
        out double vx,
        out double vy
    )
    {
        double rad = totalAngleDeg * Math.PI / 180.0;
        ux = Math.Cos(rad);
        uy = Math.Sin(rad);
        vx = -uy;
        vy = ux;
    }

    /// <summary>
    /// Computes the world-space step vector for a hatch pattern line family.
    /// The input offsets (localDx, localDy) are interpreted as:
    /// - localDx: displacement ALONG the line (u-axis)
    /// - localDy: displacement PERPENDICULAR to the line (v-axis)
    /// The result is rotated by totalAngleDeg and scaled by patternScale.
    /// </summary>
    public static (double dx, double dy) ComputeWorldStep(
        double localDx,
        double localDy,
        double totalAngleDeg,
        double patternScale
    )
    {
        double scale = patternScale <= 0 ? 1.0 : patternScale;
        double rad = totalAngleDeg * Math.PI / 180.0;
        double c = Math.Cos(rad);
        double s = Math.Sin(rad);

        // v_local = (localDx, localDy) where X is 'along' and Y is 'perp'
        // Rotated: X' = X*c - Y*s, Y' = X*s + Y*c
        // But wait: standard 2D rotation of (x,y) is (x*c - y*s, x*s + y*c).
        // Here, our basis is:
        // u = (c, s)
        // v = (-s, c)
        // So Step = localDx * u + localDy * v
        // Step.x = localDx * c + localDy * (-s) = localDx * c - localDy * s
        // Step.y = localDx * s + localDy * c

        double wx = scale * (localDx * c - localDy * s);
        double wy = scale * (localDx * s + localDy * c);
        return (wx, wy);
    }

    /// <summary>
    /// Rotates (x, y) by patternAngleDeg and scales by patternScale. Returns world-space vector.
    /// </summary>
    public static (double X, double Y) RotateScale(
        double x,
        double y,
        double patternAngleDeg,
        double patternScale
    )
    {
        double scale = patternScale <= 0 ? 1.0 : patternScale;
        double rad = patternAngleDeg * Math.PI / 180.0;
        double cr = Math.Cos(rad);
        double sr = Math.Sin(rad);
        double rx = scale * (x * cr - y * sr);
        double ry = scale * (x * sr + y * cr);
        return (rx, ry);
    }

    /// <summary>
    /// Computes pixel spacing between parallel lines from a world-space offset vector and view scale.
    /// Applies a minimum pixel spacing clamp to avoid excessive overdraw.
    /// </summary>
    public static double ComputePixelSpacingFromVector(
        double offxWorld,
        double offyWorld,
        double viewScale,
        double minSpacingPx = 2.0
    )
    {
        double len = Math.Sqrt(offxWorld * offxWorld + offyWorld * offyWorld);
        double spacingPx = len * Math.Abs(viewScale);
        if (spacingPx < minSpacingPx)
            spacingPx = minSpacingPx;
        return spacingPx;
    }

    /// <summary>
    /// Converts a dash segment length defined in world units (pre-scale) to pixel space,
    /// applying pattern scale and view scale. Clamps to a minimum to avoid infinite loops on tiny segments.
    /// </summary>
    public static double ClampDashSegmentPx(
        double dashWorld,
        double patternScale,
        double viewScale,
        double minSegPx = 0.5
    )
    {
        double scale = patternScale <= 0 ? 1.0 : patternScale;
        double segPx = Math.Abs(dashWorld) * scale * Math.Abs(viewScale);
        if (segPx < minSegPx)
            segPx = minSegPx;
        return segPx;
    }
}
