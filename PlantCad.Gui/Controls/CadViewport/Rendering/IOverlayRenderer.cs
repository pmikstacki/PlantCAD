using Avalonia;
using Avalonia.Media;
using PlantCad.Gui.Controls.Viewport;

namespace PlantCad.Gui.Controls.Rendering
{
    public interface IOverlayRenderer
    {
        void Render(
            DrawingContext ctx,
            Size viewportSize,
            ViewportState state,
            OverlayContext context
        );
    }
}
