using System.Collections.Generic;
using Avalonia;

namespace PlantCad.Gui.Controls.Rendering
{
    public sealed class OverlayContext
    {
        public bool IsSelecting { get; }
        public Rect? SelectionScreenRect { get; }
        public IReadOnlyList<Rect> SelectedWorldBounds { get; }
        public IReadOnlyList<IReadOnlyList<Point>> HoveredWorldPaths { get; }

        // Optional diagnostics
        public double? FramesPerSecond { get; }
        public double? FrameTimeMs { get; }

        public OverlayContext(
            bool isSelecting,
            Rect? selectionScreenRect,
            IReadOnlyList<Rect> selectedWorldBounds,
            IReadOnlyList<IReadOnlyList<Point>> hoveredWorldPaths
        )
            : this(
                isSelecting,
                selectionScreenRect,
                selectedWorldBounds,
                hoveredWorldPaths,
                null,
                null
            ) { }

        public OverlayContext(
            bool isSelecting,
            Rect? selectionScreenRect,
            IReadOnlyList<Rect> selectedWorldBounds,
            IReadOnlyList<IReadOnlyList<Point>> hoveredWorldPaths,
            double? framesPerSecond,
            double? frameTimeMs
        )
        {
            IsSelecting = isSelecting;
            SelectionScreenRect = selectionScreenRect;
            SelectedWorldBounds =
                selectedWorldBounds ?? (IReadOnlyList<Rect>)System.Array.Empty<Rect>();
            HoveredWorldPaths =
                hoveredWorldPaths
                ?? (IReadOnlyList<IReadOnlyList<Point>>)System.Array.Empty<IReadOnlyList<Point>>();
            FramesPerSecond = framesPerSecond;
            FrameTimeMs = frameTimeMs;
        }
    }
}
