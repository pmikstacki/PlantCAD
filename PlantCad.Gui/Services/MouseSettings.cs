using System;

namespace PlantCad.Gui.Services
{
    public enum ZoomPivotMode
    {
        Cursor,
        Center,
    }

    public sealed class MouseSettings
    {
        public bool ZoomRequiresCtrl { get; set; } = false;
        public bool ZoomWithCtrlDrag { get; set; } = true;
        public bool ScrollUpZoomIn { get; set; } = true;

        public double ZoomBase { get; set; } = 0.2; // per-step multiplier
        public int MaxStepsPerEvent { get; set; } = 6; // wheel steps cap per event
        public double ResponseExponent { get; set; } = 0.5; // scale-aware response exponent
        public double ResponseMin { get; set; } = 0.5; // min response multiplier
        public double ResponseMax { get; set; } = 2.0; // max response multiplier
        public double DragPixelsPerStep { get; set; } = 50.0; // pixels per logical step on drag

        // Zoom pivot selection mode: Cursor locks the world point under the cursor; Center pivots at viewport center
        public ZoomPivotMode ZoomPivot { get; set; } = ZoomPivotMode.Cursor;

        // Keyboard panning in world units, converted to screen pixels using current scale.
        public double KeyboardPanWorldStep { get; set; } = 200.0;

        // Smooth zoom settings
        public bool SmoothZoomEnabled { get; set; } = true;

        // Exponential smoothing half-life in milliseconds (smaller = faster response)
        public double SmoothZoomHalfLifeMs { get; set; } = 60.0;

        public event Action? Changed;

        public void NotifyChanged() => Changed?.Invoke();
    }
}
