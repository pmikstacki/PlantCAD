using Avalonia;
using Avalonia.Media;
using PlantCad.Gui.Controls.Viewport;

namespace PlantCad.Gui.Controls.Rendering
{
    public sealed class SelectionOverlayRenderer : IOverlayRenderer
    {
        private readonly IBrush _overlay = new SolidColorBrush(Color.FromArgb(60, 30, 144, 255));
        private readonly Pen _stroke = new Pen(Brushes.DodgerBlue, 1);

        public void Render(
            DrawingContext ctx,
            Size viewportSize,
            ViewportState state,
            OverlayContext context
        )
        {
            if (!context.IsSelecting || context.SelectionScreenRect is null)
            {
                return;
            }
            var r = context.SelectionScreenRect.Value;
            ctx.FillRectangle(_overlay, r);
            ctx.DrawRectangle(_stroke, r);
        }
    }
}
