using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls.Renderers
{
    /// <summary>
    /// Debug renderer that draws the model extents as a red rectangle and a small cross at its center.
    /// </summary>
    public sealed class ExtentsDebugRenderer : ICadEntityRenderer
    {
        private static readonly Pen ExtentsPen = new Pen(Brushes.Red, 2.0);
        private static readonly Pen CenterPen = new Pen(Brushes.Crimson, 1.5);
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "ExtentsDebugRenderer"
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
            if (model == null)
            {
                return;
            }
            var e = model.Extents;
            var p1 = state.WorldToScreen(new Point(e.MinX, e.MinY));
            var p2 = state.WorldToScreen(new Point(e.MaxX, e.MinY));
            var p3 = state.WorldToScreen(new Point(e.MaxX, e.MaxY));
            var p4 = state.WorldToScreen(new Point(e.MinX, e.MaxY));

            // Draw rectangle
            ctx.DrawLine(ExtentsPen, p1, p2);
            ctx.DrawLine(ExtentsPen, p2, p3);
            ctx.DrawLine(ExtentsPen, p3, p4);
            ctx.DrawLine(ExtentsPen, p4, p1);

            // Draw center cross
            var cxWorld = (e.MinX + e.MaxX) / 2.0;
            var cyWorld = (e.MinY + e.MaxY) / 2.0;
            var c = state.WorldToScreen(new Point(cxWorld, cyWorld));
            var dx = 10.0; // pixels
            ctx.DrawLine(CenterPen, new Point(c.X - dx, c.Y), new Point(c.X + dx, c.Y));
            ctx.DrawLine(CenterPen, new Point(c.X, c.Y - dx), new Point(c.X, c.Y + dx));

            // Diagnostics
            Logger?.LogInformation(
                "Extents world=({MinX:0.###},{MinY:0.###})-({MaxX:0.###},{MaxY:0.###}); Screen rect=({P1X:0.###},{P1Y:0.###}) ({P2X:0.###},{P2Y:0.###}) ({P3X:0.###},{P3Y:0.###}) ({P4X:0.###},{P4Y:0.###}); Center=({CX:0.###},{CY:0.###})",
                e.MinX,
                e.MinY,
                e.MaxX,
                e.MaxY,
                p1.X,
                p1.Y,
                p2.X,
                p2.Y,
                p3.X,
                p3.Y,
                p4.X,
                p4.Y,
                c.X,
                c.Y
            );
        }
    }
}
