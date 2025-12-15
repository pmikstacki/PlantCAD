using System;
using System.Collections.Generic;
using SkiaSharp;

namespace PlantCad.Gui.Controls.CadViewport.Hatching
{
    public interface IHatchShaderCache
    {
        event Action? Changed;
        bool TryGetPicture(string key, out SKPicture picture);
        void AddPicture(
            string key,
            SKPicture picture,
            double absSpacing,
            double dashPeriod,
            string dashKey,
            string color,
            string tileRect
        );
        SKPicture GetOrCreatePicture(
            string key,
            Func<SKPicture> create,
            double absSpacing,
            double dashPeriod,
            string dashKey,
            string color,
            string tileRect
        );
        void IncrementHit(string key);
        IReadOnlyList<HatchCacheEntry> GetSnapshot();
    }
}
