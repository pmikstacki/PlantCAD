using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Controls.Viewport;
using PlantCad.Gui.Models;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls.Rendering
{
    /// <summary>
    /// Draws debug info: origin cross, model stats and extents values.
    /// </summary>
    public sealed class DebugInfoOverlayRenderer : IOverlayRenderer
    {
        private static readonly Pen OriginPen = new Pen(Brushes.Blue, 1.0);
        private static readonly Typeface Typeface = new Typeface("Segoe UI");
        private static readonly IBrush TextBrush = Brushes.Black;
        private static readonly SolidColorBrush TextBg = new SolidColorBrush(
            Color.FromArgb(180, 255, 255, 255)
        );
        private static readonly ILogger? Logger = ServiceRegistry.LoggerFactory?.CreateLogger(
            "DebugInfoOverlay"
        );

        public void Render(
            DrawingContext ctx,
            Size viewportSize,
            ViewportState state,
            OverlayContext context
        )
        {
            // Origin cross at world (0,0)
            var origin = state.WorldToScreen(new Point(0, 0));
            ctx.DrawLine(
                OriginPen,
                new Point(origin.X - 10, origin.Y),
                new Point(origin.X + 10, origin.Y)
            );
            ctx.DrawLine(
                OriginPen,
                new Point(origin.X, origin.Y - 10),
                new Point(origin.X, origin.Y + 10)
            );

            // If we have access to model, print stats
            // We cannot access model directly via overlay, so rely on context not having it.
            // As a workaround, we print transform details and viewport size to aid debugging.
            var scale = state.Transform.M11;
            var fpsStr = context.FramesPerSecond.HasValue
                ? $"FPS: {context.FramesPerSecond.Value:0.0} ({context.FrameTimeMs!.Value:0.0} ms)"
                : null;
            var lines =
                fpsStr == null
                    ? new[]
                    {
                        $"Viewport: {viewportSize.Width:0}x{viewportSize.Height:0}",
                        $"Transform: [ {state.Transform.M11:0.###} {state.Transform.M12:0.###} {state.Transform.M21:0.###} {state.Transform.M22:0.###} | {state.Transform.M31:0.###} {state.Transform.M32:0.###} ]",
                        $"CustomView: {state.IsCustom}",
                        $"Scale: {scale:0.###}",
                    }
                    : new[]
                    {
                        $"Viewport: {viewportSize.Width:0}x{viewportSize.Height:0}",
                        $"Transform: [ {state.Transform.M11:0.###} {state.Transform.M12:0.###} {state.Transform.M21:0.###} {state.Transform.M22:0.###} | {state.Transform.M31:0.###} {state.Transform.M32:0.###} ]",
                        $"CustomView: {state.IsCustom}",
                        $"Scale: {scale:0.###}",
                        fpsStr!,
                    };

            double y = 8;
            foreach (var text in lines)
            {
                var ft = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    Typeface,
                    12,
                    TextBrush
                );
                // background
                var bgRect = new Rect(8, y, ft.Width + 8, ft.Height + 4);
                ctx.FillRectangle(TextBg, bgRect);
                ctx.DrawText(ft, new Point(12, y + 2));
                y += ft.Height + 4;
            }

            // Diagnostics
            var inside =
                origin.X >= 0
                && origin.X <= viewportSize.Width
                && origin.Y >= 0
                && origin.Y <= viewportSize.Height;
            Logger?.LogInformation(
                "DebugOverlay: originS=({OX:0.###},{OY:0.###}) insideViewport={Inside} Transform=[{M11:0.###} {M12:0.###} {M21:0.###} {M22:0.###} | {M31:0.###} {M32:0.###}]",
                origin.X,
                origin.Y,
                inside,
                state.Transform.M11,
                state.Transform.M12,
                state.Transform.M21,
                state.Transform.M22,
                state.Transform.M31,
                state.Transform.M32
            );
        }
    }
}
