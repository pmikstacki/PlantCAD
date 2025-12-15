using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Controls.Viewport
{
    /// <summary>
    /// Handles pointer and wheel input for panning, zooming, and selection.
    /// </summary>
    public sealed class ViewportInputController
    {
        private readonly ViewportState _state;
        private readonly Action _invalidate;
        private readonly Func<Rect> _getBounds;
        private readonly ILogger? _logger;

        private Point? _lastPan;
        private Point? _pressPos;
        private bool _moved;
        private Point? _selStart;
        private Point? _selEnd;
        private Point? _lastHoverPos;
        private DateTime _lastHoverAt;

        // Wheel/drag zoom behavior (configurable)
        private double _wheelAccumulator;
        private double _zoomBase = 1.2; // per-step base multiplier
        private int _maxStepsPerEvent = 6; // cap applied steps per event
        private bool _scrollUpZoomIn = true; // invert direction mapping
        private double _responseExponent = 0.5; // scale-aware response curve exponent
        private double _responseMin = 0.5; // min response multiplier
        private double _responseMax = 2.0; // max response multiplier
        private double _dragPixelsPerStep = 50.0; // vertical pixels per logical zoom step when dragging
        private bool _zoomDragging;
        private Point _lastZoomPos;
        private Cursor? _previousCursor;
        private ViewportAnimator? _animator;
        private ZoomPivotMode _pivotMode = ZoomPivotMode.Cursor;
        private Point? _lastPointer;

        public bool IsSelecting { get; set; }
        public Rect? CurrentSelectionScreenRect { get; private set; }
        public bool ZoomRequiresCtrl { get; set; }
        public bool ZoomWithCtrlDrag { get; set; }
        public bool ScrollUpZoomIn
        {
            get => _scrollUpZoomIn;
            set => _scrollUpZoomIn = value;
        }
        public double ZoomBase
        {
            get => _zoomBase;
            set => _zoomBase = value > 0 ? value : 1.01;
        }
        public int MaxStepsPerEvent
        {
            get => _maxStepsPerEvent;
            set => _maxStepsPerEvent = Math.Clamp(value, 1, 64);
        }
        public double ResponseExponent
        {
            get => _responseExponent;
            set => _responseExponent = Math.Clamp(value, 0.0, 4.0);
        }
        public double ResponseMin
        {
            get => _responseMin;
            set => _responseMin = Math.Clamp(value, 0.01, 10.0);
        }
        public double ResponseMax
        {
            get => _responseMax;
            set => _responseMax = Math.Clamp(value, _responseMin, 20.0);
        }
        public double DragPixelsPerStep
        {
            get => _dragPixelsPerStep;
            set => _dragPixelsPerStep = Math.Clamp(value, 1.0, 500.0);
        }
        public ZoomPivotMode ZoomPivot
        {
            get => _pivotMode;
            set => _pivotMode = value;
        }

        public event Action<Rect>? SelectionCompleted;
        public event Action? ViewChanged;
        public event Action<Point>? Clicked;
        public event Action<Point>? PointerMovedForHover;
        public event Action<Point>? DoubleClicked;

        public ViewportInputController(ViewportState state, Action invalidate, Func<Rect> getBounds)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _invalidate = invalidate ?? throw new ArgumentNullException(nameof(invalidate));
            _getBounds = getBounds ?? throw new ArgumentNullException(nameof(getBounds));
            _logger = ServiceRegistry.LoggerFactory?.CreateLogger("ViewportInput");
        }

        public ViewportAnimator? Animator
        {
            get => _animator;
            set => _animator = value;
        }

        public void OnPointerPressed(Control owner, PointerPressedEventArgs e)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            var pt = e.GetPosition(owner);
            _lastPointer = pt;
            _logger?.LogDebug(
                "PointerPressed at {X},{Y} selecting={Selecting}",
                pt.X,
                pt.Y,
                IsSelecting
            );

            // Double-click detection (left button)
            if (e.ClickCount >= 2)
            {
                DoubleClicked?.Invoke(pt);
                e.Handled = true;
                return;
            }

            // Start Ctrl+RightMouse drag-zoom if enabled and modifiers/buttons match
            var modsPressed = e.KeyModifiers;
            var props = e.GetCurrentPoint(owner).Properties;
            if (
                ZoomWithCtrlDrag
                && (modsPressed & KeyModifiers.Control) != 0
                && props.IsRightButtonPressed
            )
            {
                _zoomDragging = true;
                _lastZoomPos = pt;
                e.Pointer.Capture(owner);
                _previousCursor = owner.Cursor;
                owner.Cursor = new Cursor(StandardCursorType.Cross);
                _logger?.LogDebug("Zoom drag start at {X},{Y}", pt.X, pt.Y);
                e.Handled = true;
                return;
            }
            // Middle-mouse pan regardless of IsSelecting
            if (props.IsMiddleButtonPressed)
            {
                _lastPan = pt;
                e.Pointer.Capture(owner);
                _previousCursor = owner.Cursor;
                owner.Cursor = new Cursor(StandardCursorType.SizeAll);
                _logger?.LogDebug("Middle pan start at {X},{Y}", pt.X, pt.Y);
                e.Handled = true;
                return;
            }
            _pressPos = pt;
            _moved = false;
            if (IsSelecting)
            {
                _selStart = pt;
                _selEnd = pt;
                CurrentSelectionScreenRect = new Rect(pt, pt);
                _logger?.LogDebug("Selection start at {X},{Y}", pt.X, pt.Y);
                _invalidate();
            }
            else
            {
                _lastPan = pt;
                e.Pointer.Capture(owner);
                _previousCursor = owner.Cursor;
                owner.Cursor = new Cursor(StandardCursorType.SizeAll);
                _logger?.LogDebug("Pan start at {X},{Y}", pt.X, pt.Y);
                // When starting a pan, clear pending hover sequence
                _lastHoverPos = null;
            }
            e.Handled = true;
        }

        private Point ResolvePivot(Control owner, Point eventPos)
        {
            if (_pivotMode == ZoomPivotMode.Cursor)
            {
                return eventPos;
            }
            // Center pivot
            var b = _getBounds();
            return new Point(b.Width / 2.0, b.Height / 2.0);
        }

        public void OnPointerMoved(Control owner, PointerEventArgs e)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (_zoomDragging)
            {
                var pt = e.GetPosition(owner);
                _lastPointer = pt;
                var dy = pt.Y - _lastZoomPos.Y;
                if (Math.Abs(dy) > double.Epsilon)
                {
                    // Scale-aware response similar to wheel zoom. Drag up (negative dy) => zoom in.
                    var currentScale = Math.Max(Math.Abs(_state.Transform.M11), 1e-12);
                    var reference = Math.Max(_state.ReferenceScale, 1e-12);
                    var response = Math.Pow(reference / currentScale, _responseExponent);
                    response = Math.Clamp(response, _responseMin, _responseMax);

                    var logicalSteps = (-dy / _dragPixelsPerStep) * response;
                    var pivot = ResolvePivot(owner, pt);
                    if (_animator != null && _animator.Enabled)
                    {
                        _animator.RequestZoom(pivot, logicalSteps);
                        _logger?.LogDebug(
                            "Zoom drag queued dy={DY:0.###} steps={Steps:0.###}",
                            dy,
                            logicalSteps
                        );
                    }
                    else
                    {
                        var zoomFactor = Math.Pow(_zoomBase, logicalSteps);
                        _state.Zoom(pivot, zoomFactor);
                        _logger?.LogDebug(
                            "Zoom drag dy={DY:0.###} steps={Steps:0.###} factor={Factor:0.###}",
                            dy,
                            logicalSteps,
                            zoomFactor
                        );
                        ViewChanged?.Invoke();
                        _invalidate();
                    }
                }
                _lastZoomPos = pt;
                e.Handled = true;
                return;
            }
            if (IsSelecting && _selStart is { } s)
            {
                var pt = e.GetPosition(owner);
                _selEnd = pt;
                CurrentSelectionScreenRect = NormalizeRect(s, pt);
                _logger?.LogDebug(
                    "Selecting rect screen=({MinX:0},{MinY:0})-({MaxX:0},{MaxY:0})",
                    CurrentSelectionScreenRect.Value.X,
                    CurrentSelectionScreenRect.Value.Y,
                    CurrentSelectionScreenRect.Value.Right,
                    CurrentSelectionScreenRect.Value.Bottom
                );
                _invalidate();
            }
            else if (_lastPan is { } last)
            {
                var pt = e.GetPosition(owner);
                var dx = pt.X - last.X;
                var dy = pt.Y - last.Y;
                var dist2 = dx * dx + dy * dy;
                // Start panning only after a small movement threshold to allow click detection
                const double threshold = 4.0; // pixels
                if (dist2 >= threshold * threshold)
                {
                    _moved = true;
                    // Invert vertical panning so dragging up moves content up (world is Y-up, screen is Y-down)
                    var appliedDy = -dy;
                    _state.Pan(dx, appliedDy);
                    _lastPan = pt;
                    _logger?.LogDebug(
                        "Pan by dx={DX:0.###}, dy={DY:0.###}; Transform=[{M11:0.###} {M12:0.###} {M21:0.###} {M22:0.###} | {M31:0.###} {M32:0.###}]",
                        dx,
                        appliedDy,
                        _state.Transform.M11,
                        _state.Transform.M12,
                        _state.Transform.M21,
                        _state.Transform.M22,
                        _state.Transform.M31,
                        _state.Transform.M32
                    );
                    ViewChanged?.Invoke();
                    _invalidate();
                }
            }
            else if (!_zoomDragging && !IsSelecting && _selStart == null && _lastPan == null)
            {
                // Idle move: notify hover if moved enough and throttle by time
                var pt = e.GetPosition(owner);
                _lastPointer = pt;
                var shouldNotify = false;
                const double hoverMoveThreshold = 2.0; // px
                if (_lastHoverPos is { } hp)
                {
                    var dx = pt.X - hp.X;
                    var dy = pt.Y - hp.Y;
                    if (dx * dx + dy * dy >= hoverMoveThreshold * hoverMoveThreshold)
                    {
                        shouldNotify = true;
                    }
                }
                else
                {
                    shouldNotify = true;
                }
                var now = DateTime.UtcNow;
                if (shouldNotify && (now - _lastHoverAt).TotalMilliseconds >= 33)
                {
                    _lastHoverPos = pt;
                    _lastHoverAt = now;
                    PointerMovedForHover?.Invoke(pt);
                }
            }
            e.Handled = true;
        }

        public void OnPointerReleased(Control owner, PointerReleasedEventArgs e)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (_zoomDragging)
            {
                _zoomDragging = false;
                if (ReferenceEquals(e.Pointer.Captured, owner))
                {
                    e.Pointer.Capture(null);
                }
                owner.Cursor = _previousCursor;
                _previousCursor = null;
                _logger?.LogDebug("Zoom drag end");
            }
            if (IsSelecting && _selStart is { } s && _selEnd is { } ept)
            {
                var screenRect = NormalizeRect(s, ept);
                CurrentSelectionScreenRect = screenRect;
                var worldRect = _state.ScreenToWorld(screenRect);
                _logger?.LogDebug(
                    "Selection complete world=({MinX:0.###},{MinY:0.###})-({MaxX:0.###},{MaxY:0.###})",
                    worldRect.X,
                    worldRect.Y,
                    worldRect.Right,
                    worldRect.Bottom
                );
                SelectionCompleted?.Invoke(worldRect);
            }
            else if (!_zoomDragging && !IsSelecting && _pressPos is { } press)
            {
                var releasePt = e.GetPosition(owner);
                var dx = releasePt.X - press.X;
                var dy = releasePt.Y - press.Y;
                var dist2 = dx * dx + dy * dy;
                const double threshold = 4.0; // pixels
                if (!_moved && dist2 < threshold * threshold)
                {
                    _logger?.LogDebug("Click detected at {X},{Y}", releasePt.X, releasePt.Y);
                    Clicked?.Invoke(releasePt);
                }
            }

            if (ReferenceEquals(e.Pointer.Captured, owner))
            {
                e.Pointer.Capture(null);
                _logger?.LogDebug("Pointer capture released");
            }

            _lastPan = null;
            _pressPos = null;
            _selStart = null;
            _selEnd = null;
            CurrentSelectionScreenRect = null;
            if (_previousCursor != null)
            {
                owner.Cursor = _previousCursor;
                _previousCursor = null;
            }
            e.Handled = true;
        }

        public void OnPointerWheelChanged(Control owner, PointerWheelEventArgs e)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (ZoomRequiresCtrl)
            {
                var mods = e.KeyModifiers;
                if ((mods & KeyModifiers.Control) == 0)
                {
                    // Let event bubble for outer scroll viewers if Ctrl is not pressed.
                    return;
                }
            }
            var pos = e.GetPosition(owner);
            _lastPointer = pos;
            // Accumulate wheel steps to make zoom consistent across devices, and scale response with current scale
            var rawDelta = e.Delta.Y;
            var contribution = _scrollUpZoomIn ? -rawDelta : rawDelta;
            _wheelAccumulator += contribution;

            var absAcc = Math.Abs(_wheelAccumulator);
            var stepsToApply = Math.Clamp((int)Math.Floor(absAcc), 0, _maxStepsPerEvent);
            if (stepsToApply == 0)
            {
                e.Handled = true;
                return;
            }
            var sign = _wheelAccumulator > 0 ? 1 : -1;

            // Scale-aware response: faster when far out, slower when zoomed in
            var currentScale = Math.Max(Math.Abs(_state.Transform.M11), 1e-12);
            var reference = Math.Max(_state.ReferenceScale, 1e-12);
            var response = Math.Pow(reference / currentScale, _responseExponent);
            response = Math.Clamp(response, _responseMin, _responseMax);

            var effectiveSteps = sign * stepsToApply * response;
            var pivot = ResolvePivot(owner, pos);
            if (_animator != null && _animator.Enabled)
            {
                _animator.RequestZoom(pivot, effectiveSteps);
                _logger?.LogDebug(
                    "Wheel queued at {X},{Y} raw={Raw:0.###} acc={Acc:0.###} steps={Steps} resp={Resp:0.###} effSteps={Eff:0.###} pivot=({PX:0.###},{PY:0.###})",
                    pos.X,
                    pos.Y,
                    rawDelta,
                    _wheelAccumulator,
                    stepsToApply * sign,
                    response,
                    effectiveSteps,
                    pivot.X,
                    pivot.Y
                );
            }
            else
            {
                var zoomFactor = Math.Pow(_zoomBase, effectiveSteps);
                _logger?.LogDebug(
                    "Wheel at {X},{Y} raw={Raw:0.###} acc={Acc:0.###} steps={Steps} resp={Resp:0.###} effSteps={Eff:0.###} factor={Factor:0.###} pivot=({PX:0.###},{PY:0.###})",
                    pos.X,
                    pos.Y,
                    rawDelta,
                    _wheelAccumulator,
                    stepsToApply * sign,
                    response,
                    effectiveSteps,
                    zoomFactor,
                    pivot.X,
                    pivot.Y
                );
                _state.Zoom(pivot, zoomFactor);
                ViewChanged?.Invoke();
                _invalidate();
            }

            // Decrease accumulator by applied integer steps (not the scaled steps)
            _wheelAccumulator -= sign * stepsToApply;
            e.Handled = true;
        }

        private static Rect NormalizeRect(Point a, Point b)
        {
            var minX = Math.Min(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxX = Math.Max(a.X, b.X);
            var maxY = Math.Max(a.Y, b.Y);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
