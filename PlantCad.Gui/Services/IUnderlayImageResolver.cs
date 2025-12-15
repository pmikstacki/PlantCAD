using PlantCad.Gui.Models;
using SkiaSharp;

namespace PlantCad.Gui.Services
{
    public interface IUnderlayImageResolver
    {
        // Returns a cached image for the given underlay, or null if not available
        SKImage? Resolve(CadUnderlay underlay);
    }
}
