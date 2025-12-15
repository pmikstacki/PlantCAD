using Avalonia.Media;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Controls.Rendering
{
    public interface ICadEntityRenderer
    {
        void Render(
            DrawingContext ctx,
            ViewportState state,
            CadModel model,
            IStyleProvider style,
            CadRenderOptions? options,
            Avalonia.Rect visibleWorldRect
        );
    }
}
