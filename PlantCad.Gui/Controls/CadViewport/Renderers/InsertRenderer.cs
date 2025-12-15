using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls.Renderers
{
    public sealed class InsertRenderer : ICadEntityRenderer
    {
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "InsertRenderer"
        );

        public void Render(
            DrawingContext ctx,
            ViewportState state,
            CadModel model,
            IStyleProvider style,
            CadRenderOptions? options,
            Rect visibleWorldRect
        )
        {
            if (model.Inserts == null || model.Inserts.Count == 0)
            {
                return;
            }

            int total = 0;
            int drawn = 0;
            Point? sampleS = null;
            double sampleR = 0;
            foreach (var ins in model.Inserts)
            {
                total++;
                if (options != null && !options.IsLayerVisible(ins.Layer))
                {
                    continue;
                }
                // culling
                if (!visibleWorldRect.Contains(ins.Position))
                {
                    continue;
                }
                var p = state.Transform.Transform(ins.Position);
                var brush = style.GetInsertBrush(ins);
                var r = style.GetInsertRadius(ins);
                ctx.DrawEllipse(brush, null, p, r, r);
                // rotation cue (draw a short line in the insert rotation direction)
                if (Math.Abs(ins.RotationDeg) > 1e-6)
                {
                    var pxPerWorld = PixelsPerWorld(state);
                    var lenPx = 12.0;
                    var lenW = lenPx / (pxPerWorld <= 0 ? 1 : pxPerWorld);
                    var ang = ins.RotationDeg * Math.PI / 180.0;
                    var endW = new Point(
                        ins.Position.X + lenW * Math.Cos(ang),
                        ins.Position.Y + lenW * Math.Sin(ang)
                    );
                    var endS = state.WorldToScreen(endW);
                    var pen = style.GetStrokePen(ins.Layer);
                    ctx.DrawLine(pen, p, endS);
                }

                // optional block name label (zoom-aware)
                if (options?.ShowInsertLabels == true && !string.IsNullOrWhiteSpace(ins.BlockName))
                {
                    var pxPerWorld = PixelsPerWorld(state);
                    var nominalTextHWorld = 2.5; // reuse default text height for LOD
                    var fontPxRaw = nominalTextHWorld * pxPerWorld;
                    var minTextPx = options?.MinTextPixelHeight ?? 2.0;
                    if (fontPxRaw >= minTextPx)
                    {
                        var typeface = style.GetInsertLabelTypeface(ins);
                        var labelBrush = style.GetInsertLabelBrush(ins);
                        var fontPx = Math.Min(fontPxRaw, 96.0);
                        var ft = new FormattedText(
                            ins.BlockName,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            fontPx,
                            labelBrush
                        );
                        // place label to the right of symbol, vertically centered on it
                        const double dxPx = 8.0;
                        var x = p.X + r + dxPx;
                        var y = p.Y - ft.Height / 2.0;
                        ctx.DrawText(ft, new Point(x, y));
                    }
                }
                drawn++;
                if (sampleS is null)
                {
                    sampleS = p;
                    sampleR = r;
                }
            }
            if (sampleS is { } sp)
            {
                Logger?.LogInformation(
                    "InsertRenderer: total={Total}, drawn={Drawn}, sampleS=({X:0.###},{Y:0.###}) r={R:0.###}",
                    total,
                    drawn,
                    sp.X,
                    sp.Y,
                    sampleR
                );
            }
            else
            {
                Logger?.LogInformation(
                    "InsertRenderer: total={Total}, drawn={Drawn}",
                    total,
                    drawn
                );
            }
        }

        private static double PixelsPerWorld(ViewportState state)
        {
            var a = state.Transform.Transform(new Point(0, 0));
            var b = state.Transform.Transform(new Point(1, 0));
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
