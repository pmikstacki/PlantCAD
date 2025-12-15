using System;
using System.Collections.Generic;
using Avalonia.Media;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Controls.Rendering
{
    public sealed class DefaultStyleProvider : IStyleProvider
    {
        private static readonly Pen FallbackPen = new Pen(Brushes.Black, 1.5);
        private static readonly IBrush FallbackInsertBrush = Brushes.DarkGreen;
        private static readonly Typeface FallbackTypeface = new Typeface("Segoe UI");

        private readonly Dictionary<string, Pen> _layerPens = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IBrush> _layerBrushes = new(
            StringComparer.OrdinalIgnoreCase
        );

        public DefaultStyleProvider() { }

        public DefaultStyleProvider(IEnumerable<CadLayer> layers)
        {
            if (layers is null)
            {
                throw new ArgumentNullException(nameof(layers));
            }
            foreach (var l in layers)
            {
                var color = ColorFromArgb(l.ColorArgb);
                var pen = new Pen(new SolidColorBrush(color), 1.5);
                _layerPens[l.Name] = pen;
                _layerBrushes[l.Name] = new SolidColorBrush(color);
            }
        }

        public Pen GetPolylinePen(CadPolyline polyline)
        {
            if (polyline == null)
                throw new ArgumentNullException(nameof(polyline));
            if (
                !string.IsNullOrWhiteSpace(polyline.Layer)
                && _layerPens.TryGetValue(polyline.Layer, out var pen)
            )
            {
                return pen;
            }
            return FallbackPen;
        }

        public IBrush GetInsertBrush(CadInsert insert)
        {
            if (insert == null)
                throw new ArgumentNullException(nameof(insert));
            if (
                !string.IsNullOrWhiteSpace(insert.Layer)
                && _layerBrushes.TryGetValue(insert.Layer, out var b)
            )
            {
                return b;
            }
            return FallbackInsertBrush;
        }

        public double GetInsertRadius(CadInsert insert)
        {
            return 3.0;
        }

        private static Color ColorFromArgb(uint argb)
        {
            var a = (byte)((argb >> 24) & 0xFF);
            var r = (byte)((argb >> 16) & 0xFF);
            var g = (byte)((argb >> 8) & 0xFF);
            var b = (byte)(argb & 0xFF);
            // If alpha is zero, treat as opaque. Many CAD sources store 24-bit RGB without alpha.
            if (a == 0)
                a = 255;
            return Color.FromArgb(a, r, g, b);
        }

        public Pen GetStrokePen(string? layerName)
        {
            if (
                !string.IsNullOrWhiteSpace(layerName)
                && _layerPens.TryGetValue(layerName, out var pen)
            )
            {
                return pen;
            }
            return FallbackPen;
        }

        public Typeface GetTextTypeface(CadText text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            // For now, return a default cross-platform typeface
            return FallbackTypeface;
        }

        public IBrush GetTextBrush(CadText text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            if (
                !string.IsNullOrWhiteSpace(text.Layer)
                && _layerBrushes.TryGetValue(text.Layer, out var b)
            )
            {
                return b;
            }
            return Brushes.Black;
        }

        public Typeface GetInsertLabelTypeface(CadInsert insert)
        {
            if (insert == null)
            {
                throw new ArgumentNullException(nameof(insert));
            }
            return FallbackTypeface;
        }

        public IBrush GetInsertLabelBrush(CadInsert insert)
        {
            if (insert == null)
            {
                throw new ArgumentNullException(nameof(insert));
            }
            if (
                !string.IsNullOrWhiteSpace(insert.Layer)
                && _layerBrushes.TryGetValue(insert.Layer, out var b)
            )
            {
                return b;
            }
            return Brushes.Black;
        }

        public Typeface GetMTextTypeface(CadMText mtext)
        {
            if (mtext == null)
            {
                throw new ArgumentNullException(nameof(mtext));
            }
            return FallbackTypeface;
        }

        public IBrush GetMTextBrush(CadMText mtext)
        {
            if (mtext == null)
            {
                throw new ArgumentNullException(nameof(mtext));
            }
            if (
                !string.IsNullOrWhiteSpace(mtext.Layer)
                && _layerBrushes.TryGetValue(mtext.Layer, out var b)
            )
            {
                return b;
            }
            return Brushes.Black;
        }

        public IBrush GetFillBrush(string? layerName)
        {
            if (
                !string.IsNullOrWhiteSpace(layerName)
                && _layerBrushes.TryGetValue(layerName, out var b)
            )
            {
                return b;
            }
            return new SolidColorBrush(Color.FromArgb(200, 200, 200, 200));
        }

        public IBrush GetBackgroundBrush()
        {
            // Default background: white. Consumers can supply a different provider via ServiceRegistry.StyleProvider.
            return Brushes.White;
        }

        public double GetPointSizePx()
        {
            // Reasonable default marker size; configurable providers can override.
            return 4.0;
        }

        public double GetLeaderArrowSizePx()
        {
            // Reasonable default arrow size in pixels; configurable providers can override.
            return 8.0;
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
            // Slightly tinted layer fill or light gray fallback
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
            return new SolidColorBrush(Color.FromArgb(128, 230, 230, 230));
        }

        public Typeface GetTableTextTypeface(CadTable table)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));
            return FallbackTypeface;
        }

        public IBrush GetTableTextBrush(CadTable table)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));
            if (
                !string.IsNullOrWhiteSpace(table.Layer)
                && _layerBrushes.TryGetValue(table.Layer, out var b)
            )
            {
                return b;
            }
            return Brushes.Black;
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
            return FallbackTypeface;
        }

        public IBrush GetToleranceTextBrush(CadTolerance tolerance)
        {
            if (tolerance == null)
                throw new ArgumentNullException(nameof(tolerance));
            if (
                !string.IsNullOrWhiteSpace(tolerance.Layer)
                && _layerBrushes.TryGetValue(tolerance.Layer, out var b)
            )
            {
                return b;
            }
            return Brushes.Black;
        }

        public Pen GetToleranceFramePen(CadTolerance tolerance)
        {
            if (tolerance == null)
                throw new ArgumentNullException(nameof(tolerance));
            return GetStrokePen(tolerance.Layer);
        }
    }
}
