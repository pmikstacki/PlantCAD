using Avalonia;
using Avalonia.Media;
using PlantCad.Gui.Controls.Viewport;

namespace PlantCad.Gui.Controls.Rendering
{
    public sealed class GridOverlayRenderer : IOverlayRenderer
    {
        private readonly int _step;
        private readonly Pen _pen;

        public GridOverlayRenderer(int step = 50)
        {
            _step = step;
            _pen = new Pen(Brushes.LightGray, 1);
        }

        public void Render(
            DrawingContext ctx,
            Size viewportSize,
            ViewportState state,
            OverlayContext context
        )
        {
            var bounds = new Rect(viewportSize);
            for (int x = 0; x < bounds.Width; x += _step)
            {
                ctx.DrawLine(_pen, new Point(x, 0), new Point(x, bounds.Height));
            }
            for (int y = 0; y < bounds.Height; y += _step)
            {
                ctx.DrawLine(_pen, new Point(0, y), new Point(bounds.Width, y));
            }
        }
    }
}
