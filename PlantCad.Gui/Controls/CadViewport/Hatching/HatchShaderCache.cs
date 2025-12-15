using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace PlantCad.Gui.Controls.CadViewport.Hatching
{
    public sealed class HatchShaderCache : IHatchShaderCache
    {
        private readonly ConcurrentDictionary<string, HatchCacheEntry> _entries = new();
        private readonly ConcurrentDictionary<string, SKPicture> _pictures = new();
        public event Action? Changed;

        public HatchShaderCache() { }

        private void UpsertOnMiss(
            string key,
            double absSpacing,
            double dashPeriod,
            string dashKey,
            string color,
            string tileRect
        )
        {
            _entries.AddOrUpdate(
                key,
                k => new HatchCacheEntry
                {
                    Key = k,
                    AbsSpacing = absSpacing,
                    DashPeriod = dashPeriod,
                    DashKey = dashKey,
                    Color = color,
                    TileRect = tileRect,
                    Hits = 1,
                    CreatedAt = DateTime.Now,
                },
                (k, existing) =>
                {
                    existing.AbsSpacing = absSpacing;
                    existing.DashPeriod = dashPeriod;
                    existing.DashKey = dashKey;
                    existing.Color = color;
                    existing.TileRect = tileRect;
                    existing.Hits = Math.Max(existing.Hits, 1);
                    return existing;
                }
            );
            Changed?.Invoke();
        }

        public void IncrementHit(string key)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                entry.Hits++;
                // Do not fire Changed here to avoid event flooding on every render frame.
                // The UI can poll for hit counts if needed, or we can throttle this.
            }
        }

        public bool TryGetPicture(string key, out SKPicture picture)
        {
            var ok = _pictures.TryGetValue(key, out picture!);
            if (ok)
            {
                IncrementHit(key);
            }
            return ok;
        }

        public void AddPicture(
            string key,
            SKPicture picture,
            double absSpacing,
            double dashPeriod,
            string dashKey,
            string color,
            string tileRect
        )
        {
            _pictures[key] = picture;
            UpsertOnMiss(key, absSpacing, dashPeriod, dashKey, color, tileRect);
        }

        public SKPicture GetOrCreatePicture(
            string key,
            Func<SKPicture> create,
            double absSpacing,
            double dashPeriod,
            string dashKey,
            string color,
            string tileRect
        )
        {
            if (_pictures.TryGetValue(key, out var existing))
            {
                IncrementHit(key);
                return existing;
            }
            var pic = create();
            _pictures[key] = pic;
            UpsertOnMiss(key, absSpacing, dashPeriod, dashKey, color, tileRect);
            return pic;
        }

        public IReadOnlyList<HatchCacheEntry> GetSnapshot()
        {
            return _entries.Values.OrderByDescending(e => e.Hits).ThenBy(e => e.Key).ToList();
        }
    }
}
