using System;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Controls.Rendering;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.ViewModels.Tools;

public partial class StyleToolViewModel : Tool
{
    [ObservableProperty]
    private bool useLayerColors = true;

    [ObservableProperty]
    private string strokeWidth = "1.5";

    [ObservableProperty]
    private string insertRadius = "3.0";

    [ObservableProperty]
    private string fontFamily = "Segoe UI";

    [ObservableProperty]
    private string defaultStrokeColor = "#FF000000"; // ARGB

    [ObservableProperty]
    private string defaultTextColor = "#FF000000"; // ARGB

    [ObservableProperty]
    private string defaultFillColor = "#FFC8C8C8"; // ARGB

    [ObservableProperty]
    private string pointSizePx = "4.0";

    [ObservableProperty]
    private string leaderArrowSizePx = "8.0";

    public StyleToolViewModel()
    {
        Title = "Style";
        CanClose = false;
        DockGroup = "Tools";
    }

    [RelayCommand]
    private void Apply()
    {
        try
        {
            var doc = ServiceRegistry.ActiveDocument;
            var layers = doc?.Model?.Layers;
            if (!TryParseDouble(StrokeWidth, out var strokeW) || strokeW <= 0)
            {
                throw new InvalidOperationException("Invalid Stroke Width.");
            }
            if (!TryParseDouble(InsertRadius, out var insertR) || insertR <= 0)
            {
                throw new InvalidOperationException("Invalid Insert Radius.");
            }
            if (!TryParseDouble(PointSizePx, out var ptSize) || ptSize <= 0)
            {
                throw new InvalidOperationException("Invalid Point Size (px).");
            }
            if (!TryParseDouble(LeaderArrowSizePx, out var arrowPx) || arrowPx <= 0)
            {
                throw new InvalidOperationException("Invalid Leader Arrow Size (px).");
            }
            var strokeCol = ParseColor(DefaultStrokeColor);
            var textCol = ParseColor(DefaultTextColor);
            var fillCol = ParseColor(DefaultFillColor);

            var provider = new ConfigurableStyleProvider(
                layers,
                UseLayerColors,
                strokeW,
                insertR,
                FontFamily ?? "Segoe UI",
                textCol,
                fillCol,
                strokeCol,
                ptSize,
                arrowPx
            );

            ServiceRegistry.StyleProvider = provider;
        }
        catch (Exception ex)
        {
            ServiceRegistry.LogsTool?.Append(
                $"[{DateTime.Now:HH:mm:ss}] ERROR Style Apply: {ex.Message}"
            );
            throw;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        try
        {
            UseLayerColors = true;
            StrokeWidth = "1.5";
            InsertRadius = "3.0";
            FontFamily = "Segoe UI";
            DefaultStrokeColor = "#FF000000";
            DefaultTextColor = "#FF000000";
            DefaultFillColor = "#FFC8C8C8";
            PointSizePx = "4.0";
            LeaderArrowSizePx = "8.0";
            ServiceRegistry.StyleProvider = null; // fallback to default provider behavior in viewport
        }
        catch (Exception ex)
        {
            ServiceRegistry.LogsTool?.Append(
                $"[{DateTime.Now:HH:mm:ss}] ERROR Style Reset: {ex.Message}"
            );
            throw;
        }
    }

    private static bool TryParseDouble(string? s, out double value)
    {
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static Color ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            throw new InvalidOperationException("Color value is empty.");
        }
        // Accept #AARRGGBB or #RRGGBB
        var h = hex.Trim();
        if (h.StartsWith("#"))
            h = h.Substring(1);
        if (h.Length == 6)
        {
            h = "FF" + h; // assume opaque
        }
        if (h.Length != 8)
        {
            throw new InvalidOperationException("Color must be in #AARRGGBB or #RRGGBB format.");
        }
        var a = byte.Parse(h.Substring(0, 2), NumberStyles.HexNumber);
        var r = byte.Parse(h.Substring(2, 2), NumberStyles.HexNumber);
        var g = byte.Parse(h.Substring(4, 2), NumberStyles.HexNumber);
        var b = byte.Parse(h.Substring(6, 2), NumberStyles.HexNumber);
        return Color.FromArgb(a, r, g, b);
    }
}
