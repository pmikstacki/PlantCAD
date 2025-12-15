using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;

namespace PlantCad.Gui.Controls.Viewport
{
    /// <summary>
    /// Time-based zoom animator that eases accumulated logical zoom steps over time.
    /// </summary>
    public sealed class ViewportAnimator
    {
        private readonly ViewportState _state;
        private readonly Action _viewChanged;
        private readonly Func<double> _getZoomBase;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private double _pendingSteps;
        private Point _lastPivot;
        private bool _hasPending;
        private const double TinyLogFactorThreshold = 1e-6; // ~ ln(f) magnitude threshold

        private double _halfLifeMs = 60.0;
        private bool _enabled = true;

        public bool Enabled => _enabled;

        public ViewportAnimator(ViewportState state, Action viewChanged, Func<double> getZoomBase)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _viewChanged = viewChanged ?? throw new ArgumentNullException(nameof(viewChanged));
            _getZoomBase = getZoomBase ?? throw new ArgumentNullException(nameof(getZoomBase));

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0) };
            _timer.Tick += OnTick;
        }

        public void ApplySettings(bool enabled, double halfLifeMs)
        {
            _enabled = enabled;
            _halfLifeMs = halfLifeMs > 0 ? halfLifeMs : 1.0;
            if (!_enabled)
            {
                // If disabled, flush any pending steps immediately on next request
                Stop();
            }
        }

        public void RequestZoom(Point pivotScreen, double logicalSteps)
        {
            if (!_enabled)
            {
                // Immediate zoom path when disabled
                var zoomBase = Math.Max(_getZoomBase(), 1.0001);
                var factor = Math.Pow(zoomBase, logicalSteps);
                _state.Zoom(pivotScreen, factor);
                _viewChanged();
                return;
            }

            // Freeze pivot for the duration of a burst (timer running). Start a new burst when timer is off.
            if (!_timer.IsEnabled)
            {
                _lastPivot = pivotScreen;
            }
            _pendingSteps += logicalSteps;
            _hasPending = Math.Abs(_pendingSteps) > double.Epsilon;

            if (!_timer.IsEnabled)
            {
                _stopwatch.Restart();
                _timer.Start();
            }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var dtMs = _stopwatch.Elapsed.TotalMilliseconds;
            _stopwatch.Restart();

            if (!_hasPending)
            {
                Stop();
                return;
            }

            // Exponential moving towards zero with half-life
            // fraction applied this frame: f = 1 - exp(-dt / tau), tau = halfLife / ln(2)
            var tau = _halfLifeMs / Math.Log(2.0);
            var f = 1.0 - Math.Exp(-dtMs / Math.Max(tau, 0.0001));
            f = Math.Clamp(f, 0.0, 1.0);

            var applySteps = _pendingSteps * f;
            // If very small, skip this frame to avoid near-identity transforms; wait to accumulate more
            if (Math.Abs(applySteps) < 1e-4)
            {
                return;
            }

            var zoomBase = Math.Max(_getZoomBase(), 1.0001);
            var factor = Math.Pow(zoomBase, applySteps);
            // Guard against near-identity factor due to underflow/precision: let ViewportState skip if effectively 1
            if (Math.Abs(Math.Log(factor)) < TinyLogFactorThreshold)
            {
                return;
            }
            _state.Zoom(_lastPivot, factor);
            _pendingSteps -= applySteps;
            _hasPending = Math.Abs(_pendingSteps) > 1e-4;

            _viewChanged();

            if (!_hasPending)
            {
                Stop();
            }
        }

        private void Stop()
        {
            _timer.Stop();
            _stopwatch.Reset();
            _pendingSteps = 0;
            _hasPending = false;
        }
    }
}
