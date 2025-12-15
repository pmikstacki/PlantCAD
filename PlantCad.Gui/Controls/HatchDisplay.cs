using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls;

public sealed class HatchDisplay : Control
{
    private bool _unsupported;
    public static readonly StyledProperty<string?> PatternNameProperty = AvaloniaProperty.Register<
        HatchDisplay,
        string?
    >(nameof(PatternName));

    public static readonly StyledProperty<IReadOnlyList<PatternLine>?> PatternLinesProperty =
        AvaloniaProperty.Register<HatchDisplay, IReadOnlyList<PatternLine>?>(nameof(PatternLines));

    public static readonly StyledProperty<double> ScaleProperty = AvaloniaProperty.Register<
        HatchDisplay,
        double
    >(nameof(Scale), 64.0);

    public static readonly StyledProperty<bool> AutoScaleProperty = AvaloniaProperty.Register<
        HatchDisplay,
        bool
    >(nameof(AutoScale), true);

    public static readonly StyledProperty<Color> StrokeColorProperty = AvaloniaProperty.Register<
        HatchDisplay,
        Color
    >(nameof(StrokeColor), Colors.Black);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<HatchDisplay, double>(nameof(StrokeThickness), 1.0);

    public string? PatternName
    {
        get => GetValue(PatternNameProperty);
        set => SetValue(PatternNameProperty, value);
    }

    public IReadOnlyList<PatternLine>? PatternLines
    {
        get => GetValue(PatternLinesProperty);
        set => SetValue(PatternLinesProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public Color StrokeColor
    {
        get => GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool AutoScale
    {
        get => GetValue(AutoScaleProperty);
        set => SetValue(AutoScaleProperty, value);
    }

    static HatchDisplay()
    {
        AffectsRender<HatchDisplay>(
            PatternNameProperty,
            PatternLinesProperty,
            ScaleProperty,
            AutoScaleProperty,
            StrokeColorProperty,
            StrokeThicknessProperty
        );
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        // Switch to CAD-like Y-up coordinate system using a single matrix: y' = -y + Height
        using var _tYUp = context.PushTransform(new Matrix(1, 0, 0, -1, 0, size.Height));

        // Clip all drawing to the control bounds to prevent overflow into neighbors (in Y-up space)
        using var _ = context.PushClip(new Rect(0, 0, size.Width, size.Height));

        var pen = new Pen(new SolidColorBrush(StrokeColor), Math.Max(0.5, StrokeThickness));
        var lines = ResolveLines();
        if (lines == null || lines.Count == 0)
        {
            if (_unsupported)
            {
                DrawUnsupportedOverlay(context, size);
            }
            else
            {
                // Fallback: simple diagonal hatch
                DrawFallback(context, pen, size);
            }
            return;
        }

        double w = size.Width;
        double h = size.Height;
        double diag = Math.Sqrt(w * w + h * h);
        var center = new Point(w / 2.0, h / 2.0);
        double scalePx = AutoScale
            ? ComputeAutoScale(lines, size, StrokeThickness)
            : Math.Max(0.1, Scale);

        foreach (var ln in lines)
        {
            double angleRad = ln.AngleDeg * Math.PI / 180.0;
            // Unit along-line and perpendicular vectors
            double ux = Math.Cos(angleRad);
            double uy = Math.Sin(angleRad);
            double nx = -uy;
            double ny = ux;

            // Rotate base and delta (offset) into screen space and scale to pixels
            double scale = scalePx;
            // Base point: rotate PAT base by angle into screen space
            double baseScreenX = (ln.BaseX * ux - ln.BaseY * uy) * scale;
            double baseScreenY = (ln.BaseX * uy + ln.BaseY * ux) * scale;
            // Offset vector between parallel lines (ACadSharp already rotated it by angle)
            double dX = ln.OffsetX * scale;
            double dY = ln.OffsetY * scale;

            // Effective spacing between lines is the projection of d onto the normal
            double spacingPerp = Math.Abs(dX * nx + dY * ny);
            if (spacingPerp < 1e-3)
            {
                spacingPerp = 8.0; // guardrail against degenerate offsets
            }

            int linesCount = (int)(diag / spacingPerp) + 3;
            linesCount = Math.Clamp(linesCount, 1, 512);

            // Anchor around center plus rotated base point
            double anchorX = center.X + baseScreenX;
            double anchorY = center.Y + baseScreenY;

            // Precompute dash pattern period in pixels (approx), used for modulo phase
            double periodPx = 0;
            if (ln.DashLengths != null && ln.DashLengths.Count > 0)
            {
                foreach (var dlen in ln.DashLengths)
                {
                    if (Math.Abs(dlen) <= 1e-12)
                        continue; // dot does not advance
                    periodPx += Math.Max(0.5, Math.Abs(dlen) * scale);
                }
                if (periodPx < 1e-6)
                    periodPx = 1.0;
            }

            for (int i = -linesCount; i <= linesCount; i++)
            {
                // Step using full delta; if alongShiftPx ~ 0 this is effectively perpendicular
                double bx = anchorX + dX * i;
                double by = anchorY + dY * i;

                if (ln.DashLengths == null || ln.DashLengths.Count == 0)
                {
                    double x0 = bx - ux * (diag + spacingPerp);
                    double y0 = by - uy * (diag + spacingPerp);
                    double x1 = bx + ux * (diag + spacingPerp);
                    double y1 = by + uy * (diag + spacingPerp);
                    context.DrawLine(pen, new Point(x0, y0), new Point(x1, y1));
                    continue;
                }

                // Dashed rendering along the long axis
                // Start from a period boundary relative to the row basepoint (t=0 at bx,by)
                double pos = -diag;
                if (periodPx > 1e-6)
                {
                    double m = (-diag) % periodPx;
                    if (m < 0)
                        m += periodPx;
                    pos = -diag - m; // previous period boundary
                }
                int idx = 0;
                int drawnSegs = 0;
                const int MaxSegsPerLine = 4096;
                bool prevZero = false;
                while (pos < diag + spacingPerp && drawnSegs < MaxSegsPerLine)
                {
                    // PAT: positive = drawn dash, negative = gap, 0 = dot
                    double raw = ln.DashLengths[idx % ln.DashLengths.Count];
                    bool isDash = raw >= 0;
                    double dashWorld = Math.Abs(raw);
                    double segAdvance = raw == 0 ? 0.0 : Math.Max(0.5, dashWorld * scale);
                    double startT = pos;
                    double endT = Math.Min(
                        pos + (segAdvance <= 0 ? 0.0 : segAdvance),
                        diag + spacingPerp
                    );
                    if (isDash)
                    {
                        if (raw == 0)
                        {
                            // compress consecutive dots at same location
                            if (prevZero)
                            {
                                idx++;
                                drawnSegs++;
                                continue;
                            }
                            // dot: draw a tiny dash centered at current position without advancing
                            double dotHalf = Math.Max(0.5, pen.Thickness) * 0.5;
                            double cx = bx + ux * startT;
                            double cy = by + uy * startT;
                            double sx = cx - ux * dotHalf;
                            double sy = cy - uy * dotHalf;
                            double ex = cx + ux * dotHalf;
                            double ey = cy + uy * dotHalf;
                            context.DrawLine(pen, new Point(sx, sy), new Point(ex, ey));
                            prevZero = true;
                        }
                        else
                        {
                            double sx = bx + ux * startT;
                            double sy = by + uy * startT;
                            double ex = bx + ux * endT;
                            double ey = by + uy * endT;
                            context.DrawLine(pen, new Point(sx, sy), new Point(ex, ey));
                            prevZero = false;
                        }
                    }
                    pos = endT;
                    idx++;
                    drawnSegs++;
                    // prevent infinite loops in pathological patterns
                    if (segAdvance <= 1e-9 && raw != 0)
                        break;
                }
            }
        }
    }

    private IReadOnlyList<PatternLine>? ResolveLines()
    {
        _unsupported = false;
        if (PatternLines != null && PatternLines.Count > 0)
        {
            return PatternLines;
        }
        if (!string.IsNullOrWhiteSpace(PatternName))
        {
            try
            {
                var info = HatchPatternService.Instance.GetByName(PatternName!);
                if (info == null)
                {
                    return null;
                }
                _unsupported = !info.IsSupported || info.Lines.Count == 0;
                return info.Lines;
            }
            catch (Exception ex)
            {
                ServiceRegistry.LogsTool?.Append(
                    $"[HATCH] Resolve pattern '{PatternName}': {ex.Message}"
                );
                throw;
            }
        }
        return null;
    }

    private static void DrawFallback(DrawingContext context, Pen pen, Size size)
    {
        double w = size.Width;
        double h = size.Height;
        double diag = Math.Sqrt(w * w + h * h);
        var center = new Point(w / 2.0, h / 2.0);
        double angleRad = 45.0 * Math.PI / 180.0;
        double ux = Math.Cos(angleRad);
        double uy = Math.Sin(angleRad);
        double vx = -uy;
        double vy = ux;
        double spacing = 12.0;
        int lines = (int)(diag / spacing) + 3;
        for (int i = -lines; i <= lines; i++)
        {
            double o = i * spacing;
            double x0 = center.X + vx * o - ux * (diag + spacing);
            double y0 = center.Y + vy * o - uy * (diag + spacing);
            double x1 = center.X + vx * o + ux * (diag + spacing);
            double y1 = center.Y + vy * o + uy * (diag + spacing);
            context.DrawLine(pen, new Point(x0, y0), new Point(x1, y1));
        }
    }

    private static double ComputeAutoScale(
        IReadOnlyList<PatternLine> lines,
        Size size,
        double strokeThickness
    )
    {
        // Heuristic: aim for ~6 rows across and readable dash lengths in the preview tile
        double tileMin = Math.Max(8.0, Math.Min(size.Width, size.Height));
        double desiredSpacingPx = tileMin / 6.0;
        double desiredDashPx = Math.Max(strokeThickness * 2.0, tileMin / 24.0);

        double sumSpacing = 0.0;
        int cntSpacing = 0;
        double sumDash = 0.0;
        int cntDash = 0;

        foreach (var ln in lines)
        {
            double angle = ln.AngleDeg * Math.PI / 180.0;
            double ux = Math.Cos(angle);
            double uy = Math.Sin(angle);
            double nx = -uy,
                ny = ux;

            // ACadSharp offset is already rotated by the angle
            double spacingWorld = Math.Abs(ln.OffsetX * nx + ln.OffsetY * ny);
            if (spacingWorld > 1e-6)
            {
                sumSpacing += spacingWorld;
                cntSpacing++;
            }

            if (ln.DashLengths != null && ln.DashLengths.Count > 0)
            {
                foreach (var d in ln.DashLengths)
                {
                    if (Math.Abs(d) > 1e-9)
                    {
                        sumDash += Math.Abs(d);
                        cntDash++;
                    }
                }
            }
        }

        double sFromSpacing =
            cntSpacing > 0 ? desiredSpacingPx / (sumSpacing / cntSpacing) : double.NaN;
        double sFromDash = cntDash > 0 ? desiredDashPx / (sumDash / cntDash) : double.NaN;

        double scale = 64.0; // fallback default
        if (!double.IsNaN(sFromSpacing) && !double.IsNaN(sFromDash))
        {
            scale = 0.5 * (sFromSpacing + sFromDash);
        }
        else if (!double.IsNaN(sFromSpacing))
        {
            scale = sFromSpacing;
        }
        else if (!double.IsNaN(sFromDash))
        {
            scale = sFromDash;
        }

        // Clamp to reasonable preview bounds
        return Math.Clamp(scale, 4.0, 256.0);
    }

    private void DrawUnsupportedOverlay(DrawingContext context, Size size)
    {
        // Draw a red border and cross to indicate unsupported pattern
        var w = size.Width;
        var h = size.Height;
        var borderBrush = new SolidColorBrush(Color.FromArgb(200, 200, 64, 64));
        var borderPen = new Pen(borderBrush, 1);
        context.DrawRectangle(null, borderPen, new Rect(0, 0, w, h));

        var crossPen = new Pen(borderBrush, 1);
        context.DrawLine(crossPen, new Point(4, 4), new Point(w - 4, h - 4));
        context.DrawLine(crossPen, new Point(4, h - 4), new Point(w - 4, 4));
    }
}
