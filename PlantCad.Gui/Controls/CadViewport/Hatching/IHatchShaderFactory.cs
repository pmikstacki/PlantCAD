using Avalonia;
using Microsoft.Extensions.Logging;
using PlantCad.Gui.Models;
using SkiaSharp;

namespace PlantCad.Gui.Controls.CadViewport.Hatching
{
    public interface IHatchShaderFactory
    {
        SKShader? CreateShader(
            CadHatch hatch,
            SKColor color,
            double strokeWidth,
            Point renderOrigin,
            SKMatrix? postLocalMatrix = null,
            ILogger? logger = null
        );
    }
}
