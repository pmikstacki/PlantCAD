using System;
using System.Collections.Generic;
using System.IO;
using PlantCad.Gui.Models;
using SkiaSharp;

namespace PlantCad.Gui.Services.Internal
{
    /// <summary>
    /// Simple file-system based resolver for underlay images.
    /// Supports common raster formats (png, jpg, jpeg, bmp, gif, webp, tiff).
    /// Maintains a small LRU cache of SKImage instances.
    /// </summary>
    public sealed class FileSystemUnderlayResolver : IUnderlayImageResolver, IDisposable
    {
        private readonly int _capacity;
        private readonly LinkedList<string> _order = new();
        private readonly Dictionary<string, SKImage> _images = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly HashSet<string> _supported = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".gif",
            ".webp",
            ".tif",
            ".tiff",
        };

        public FileSystemUnderlayResolver(int capacity = 64)
        {
            _capacity = Math.Max(8, capacity);
        }

        public SKImage? Resolve(CadUnderlay underlay)
        {
            if (underlay is null)
                throw new ArgumentNullException(nameof(underlay));

            var key = GetKey(underlay);
            if (key is null)
            {
                return null;
            }

            if (_images.TryGetValue(key, out var cached))
            {
                Touch(key);
                return cached;
            }

            // Only decode raster formats from the file system
            var ext = Path.GetExtension(key);
            if (string.IsNullOrWhiteSpace(ext) || !_supported.Contains(ext))
            {
                return null;
            }
            if (!File.Exists(key))
            {
                return null;
            }

            using var data = SKData.Create(key);
            if (data == null)
            {
                return null;
            }
            var img = SKImage.FromEncodedData(data);
            if (img == null)
            {
                return null;
            }
            AddToCache(key, img);
            return img;
        }

        private static string? GetKey(CadUnderlay u)
        {
            if (!string.IsNullOrWhiteSpace(u.FilePath))
            {
                try
                {
                    return Path.GetFullPath(u.FilePath);
                }
                catch
                {
                    return u.FilePath; // fallback
                }
            }
            return u.ImageKey; // allow custom key if provided by importer
        }

        private void AddToCache(string key, SKImage img)
        {
            if (_images.ContainsKey(key))
            {
                // Replace existing
                var old = _images[key];
                _images[key] = img;
                Touch(key);
                old.Dispose();
                return;
            }
            _images[key] = img;
            _order.AddFirst(key);
            if (_order.Count > _capacity)
            {
                var last = _order.Last!.Value;
                _order.RemoveLast();
                if (_images.Remove(last, out var evicted))
                {
                    evicted?.Dispose();
                }
            }
        }

        private void Touch(string key)
        {
            var node = _order.Find(key);
            if (node == null)
                return;
            _order.Remove(node);
            _order.AddFirst(node);
        }

        public void Dispose()
        {
            foreach (var kv in _images)
            {
                kv.Value?.Dispose();
            }
            _images.Clear();
            _order.Clear();
        }
    }
}
