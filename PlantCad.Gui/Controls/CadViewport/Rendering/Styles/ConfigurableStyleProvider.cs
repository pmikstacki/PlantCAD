using System;
using System.Collections.Generic;
using Avalonia.Media;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Controls.Rendering
{
    // A configurable style provider. Supports layer colors with a global stroke width, and global defaults.
    public sealed class ConfigurableStyleProvider : IStyleProvider
    {
        private readonly Dictionary<string, Pen> _layerPens;
        private readonly Dictionary<string, IBrush> _layerBrushes;

        public bool UseLayerColors { get; }
        public double StrokeWidth { get; }
        public double InsertRadius { get; }
        public Typeface TextTypeface { get; }
        public IBrush DefaultTextBrush { get; }
        public IBrush DefaultFillBrush { get; }
        public Pen DefaultStrokePen { get; }

        // Defaults for background
        private readonly IBrush _backgroundBrush = Brushes.White;
        public double PointSizePx { get; }
        public double LeaderArrowSizePx { get; }

        public ConfigurableStyleProvider(
            IEnumerable<CadLayer>? layers,
            bool useLayerColors,
            double strokeWidth,
            double insertRadius,
            string fontFamily,
            Color defaultTextColor,
            Color defaultFillColor,
            Color defaultStrokeColor,
            double pointSizePx = 4.0,
            double leaderArrowSizePx = 8.0
        )
        {
            UseLayerColors = useLayerColors;
            StrokeWidth = Math.Max(0.1, strokeWidth);
            InsertRadius = Math.Max(0.5, insertRadius);
            TextTypeface = new Typeface(
                string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily
            );
            DefaultTextBrush = new SolidColorBrush(defaultTextColor);
            DefaultFillBrush = new SolidColorBrush(defaultFillColor);
            DefaultStrokePen = new Pen(new SolidColorBrush(defaultStrokeColor), StrokeWidth);
            PointSizePx = Math.Max(0.1, pointSizePx);
            LeaderArrowSizePx = Math.Max(0.1, leaderArrowSizePx);

            _layerPens = new Dictionary<string, Pen>(StringComparer.OrdinalIgnoreCase);
            _layerBrushes = new Dictionary<string, IBrush>(StringComparer.OrdinalIgnoreCase);
            if (layers != null)
            {
                foreach (var l in layers)
                {
                    var col = ColorFromArgb(l.ColorArgb);
                    _layerPens[l.Name] = new Pen(new SolidColorBrush(col), StrokeWidth);
                    _layerBrushes[l.Name] = new SolidColorBrush(col);
                }
            }
        }

        public Pen GetPolylinePen(CadPolyline polyline)
        {
            if (polyline == null)
                throw new ArgumentNullException(nameof(polyline));
            if (
                UseLayerColors
                && !string.IsNullOrWhiteSpace(polyline.Layer)
                && _layerPens.TryGetValue(polyline.Layer, out var pen)
            )
            {
                return pen;
            }
            return DefaultStrokePen;
        }

        public IBrush GetInsertBrush(CadInsert insert)
        {
            if (insert == null)
                throw new ArgumentNullException(nameof(insert));
            if (
                UseLayerColors
                && !string.IsNullOrWhiteSpace(insert.Layer)
                && _layerBrushes.TryGetValue(insert.Layer, out var b)
            )
            {
                return b;
            }
            return DefaultFillBrush;
        }

        public double GetInsertRadius(CadInsert insert)
        {
            if (insert == null)
                throw new ArgumentNullException(nameof(insert));
            return InsertRadius;
        }

        public Pen GetStrokePen(string? layerName)
        {
            if (
                UseLayerColors
                && !string.IsNullOrWhiteSpace(layerName)
                && _layerPens.TryGetValue(layerName, out var pen)
            )
            {
                return pen;
            }
            return DefaultStrokePen;
        }

        public Typeface GetTextTypeface(CadText text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            return TextTypeface;
        }

        public IBrush GetTextBrush(CadText text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            if (
                UseLayerColors
                && !string.IsNullOrWhiteSpace(text.Layer)
                && _layerBrushes.TryGetValue(text.Layer, out var b)
            )
            {
                return b;
            }
            return DefaultTextBrush;
        }

        public Typeface GetMTextTypeface(CadMText mtext)
        {
            if (mtext == null)
                throw new ArgumentNullException(nameof(mtext));
            return TextTypeface;
        }

        public IBrush GetMTextBrush(CadMText mtext)
        {
            if (mtext == null)
                throw new ArgumentNullException(nameof(mtext));
            if (
                UseLayerColors
                && !string.IsNullOrWhiteSpace(mtext.Layer)
                && _layerBrushes.TryGetValue(mtext.Layer, out var b)
            )
            {
                return b;
            }
            return DefaultTextBrush;
        }

        public Typeface GetInsertLabelTypeface(CadInsert insert)
        {
            if (insert == null)
                throw new ArgumentNullException(nameof(insert));
            return TextTypeface;
        }

        public IBrush GetInsertLabelBrush(CadInsert insert)
        {
            if (insert == null)
                throw new ArgumentNullException(nameof(insert));
            if (
                UseLayerColors
                && !string.IsNullOrWhiteSpace(insert.Layer)
                && _layerBrushes.TryGetValue(insert.Layer, out var b)
            )
            {
                return b;
            }
            return DefaultTextBrush;
        }

        public IBrush GetFillBrush(string? layerName)
        {
            if (
                UseLayerColors
                && !string.IsNullOrWhiteSpace(layerName)
                && _layerBrushes.TryGetValue(layerName, out var b)
            )
            {
                return b;
            }
            return DefaultFillBrush;
        }

        public IBrush GetBackgroundBrush()
        {
            return _backgroundBrush;
        }

        public double GetPointSizePx()
        {
            return PointSizePx;
        }

        public double GetLeaderArrowSizePx()
        {
            return LeaderArrowSizePx;
        }

        private static Color ColorFromArgb(uint argb)
        {
            var a = (byte)((argb >> 24) & 0xFF);
            var r = (byte)((argb >> 16) & 0xFF);
            var g = (byte)((argb >> 8) & 0xFF);
            var b = (byte)(argb & 0xFF);
            if (a == 0)
                a = 255;
            return Color.FromArgb(a, r, g, b);
        }

        // Tables
        public Pen GetTableGridPen(CadTable table)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));
            return GetStrokePen(table.Layer);
        }

        public IBrush GetTableHeaderFill(CadTable table)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));
            // Lighten the fill color
            var fb = GetFillBrush(table.Layer);
            if (fb is ISolidColorBrush sc)
            {
                var c = sc.Color;
                var header = Color.FromArgb(
                    c.A,
                    (byte)Math.Min(255, c.R + 20),
                    (byte)Math.Min(255, c.G + 20),
                    (byte)Math.Min(255, c.B + 20)
                );
                return new SolidColorBrush(header) { Opacity = 0.5 };
            }
            return DefaultFillBrush;
        }

        public Typeface GetTableTextTypeface(CadTable table)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));
            return TextTypeface;
        }

        public IBrush GetTableTextBrush(CadTable table)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));
            if (
                UseLayerColors
                && !string.IsNullOrWhiteSpace(table.Layer)
                && _layerBrushes.TryGetValue(table.Layer, out var b)
            )
            {
                return b;
            }
            return DefaultTextBrush;
        }

        // Underlays
        public double GetUnderlayOpacity(CadUnderlay underlay)
        {
            if (underlay == null)
                throw new ArgumentNullException(nameof(underlay));
            return underlay.Opacity <= 0 ? 1.0 : Math.Clamp(underlay.Opacity, 0.0, 1.0);
        }

        public IBrush? GetUnderlayTintBrush(CadUnderlay underlay)
        {
            if (underlay == null)
                throw new ArgumentNullException(nameof(underlay));
            // No tint by default
            return null;
        }

        // Shapes
        public Pen GetShapePen(CadShape shape)
        {
            if (shape == null)
                throw new ArgumentNullException(nameof(shape));
            return GetStrokePen(shape.Layer);
        }

        public IBrush? GetShapeFill(CadShape shape)
        {
            if (shape == null)
                throw new ArgumentNullException(nameof(shape));
            return GetFillBrush(shape.Layer);
        }

        // Tolerances
        public Typeface GetToleranceTypeface(CadTolerance tolerance)
        {
            if (tolerance == null)
                throw new ArgumentNullException(nameof(tolerance));
            return TextTypeface;
        }

        public IBrush GetToleranceTextBrush(CadTolerance tolerance)
        {
            if (tolerance == null)
                throw new ArgumentNullException(nameof(tolerance));
            if (
                UseLayerColors
                && !string.IsNullOrWhiteSpace(tolerance.Layer)
                && _layerBrushes.TryGetValue(tolerance.Layer, out var b)
            )
            {
                return b;
            }
            return DefaultTextBrush;
        }

        public Pen GetToleranceFramePen(CadTolerance tolerance)
        {
            if (tolerance == null)
                throw new ArgumentNullException(nameof(tolerance));
            return GetStrokePen(tolerance.Layer);
        }
    }
}
