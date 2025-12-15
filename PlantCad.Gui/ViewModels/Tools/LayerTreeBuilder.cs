using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.ViewModels.Tools;

internal static class LayerTreeBuilder
{
    public static List<LayerNode> Build(CadModel? model, CadRenderOptions? options)
    {
        var result = new List<LayerNode>();
        if (model == null || model.Layers == null || model.Layers.Count == 0)
        {
            return result;
        }

        foreach (var layer in model.Layers)
        {
            var node = new LayerNode
            {
                Name = layer.Name,
                IsVisible = options?.IsLayerVisible(layer.Name) ?? (layer.IsOn && !layer.IsFrozen),
            };

            // Polylines
            var polys = model
                .Polylines?.Where(p =>
                    string.Equals(p.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (polys != null && polys.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Polylines ({polys.Count})",
                    Kind = EntityKind.Polyline,
                    LayerName = layer.Name,
                };
                foreach (var p in polys)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = p.Id,
                            Name = $"Polyline {SafeId(p.Id)} ({p.Points.Count} pts)",
                            Kind = EntityKind.Polyline,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Lines
            var lines = model
                .Lines?.Where(l =>
                    string.Equals(l.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (lines != null && lines.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Lines ({lines.Count})",
                    Kind = EntityKind.Line,
                    LayerName = layer.Name,
                };
                foreach (var l in lines)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = l.Id,
                            Name = $"Line {SafeId(l.Id)} ({FormatPt(l.Start)} → {FormatPt(l.End)})",
                            Kind = EntityKind.Line,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Circles
            var circles = model
                .Circles?.Where(c =>
                    string.Equals(c.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (circles != null && circles.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Circles ({circles.Count})",
                    Kind = EntityKind.Circle,
                    LayerName = layer.Name,
                };
                foreach (var c in circles)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = c.Id,
                            Name =
                                $"Circle {SafeId(c.Id)} (R={c.Radius:0.###} @ {FormatPt(c.Center)})",
                            Kind = EntityKind.Circle,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Arcs
            var arcs = model
                .Arcs?.Where(a =>
                    string.Equals(a.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (arcs != null && arcs.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Arcs ({arcs.Count})",
                    Kind = EntityKind.Arc,
                    LayerName = layer.Name,
                };
                foreach (var a in arcs)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = a.Id,
                            Name =
                                $"Arc {SafeId(a.Id)} (R={a.Radius:0.###}, {a.StartAngle:0.#}°–{a.EndAngle:0.#}° @ {FormatPt(a.Center)})",
                            Kind = EntityKind.Arc,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Inserts
            var inserts = model
                .Inserts?.Where(i =>
                    string.Equals(i.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (inserts != null && inserts.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Inserts ({inserts.Count})",
                    Kind = EntityKind.Insert,
                    LayerName = layer.Name,
                };
                foreach (var i in inserts)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = i.Id,
                            Name =
                                $"Insert {SafeId(i.Id)} ({i.BlockName}) @ {FormatPt(i.Position)}",
                            Kind = EntityKind.Insert,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Ellipses
            var ellipses = model
                .Ellipses?.Where(e =>
                    string.Equals(e.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (ellipses != null && ellipses.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Ellipses ({ellipses.Count})",
                    Kind = EntityKind.Ellipse,
                    LayerName = layer.Name,
                };
                foreach (var e in ellipses)
                {
                    var arcTag = e.IsArc
                        ? $", arc {e.StartAngleDeg:0.#}°–{e.EndAngleDeg:0.#}°"
                        : string.Empty;
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = e.Id,
                            Name =
                                $"Ellipse {SafeId(e.Id)} (rx={e.RadiusX:0.###}, ry={e.RadiusY:0.###}, rot={e.RotationDeg:0.#}°{arcTag})",
                            Kind = EntityKind.Ellipse,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Texts
            var texts = model
                .Texts?.Where(ti =>
                    string.Equals(ti.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (texts != null && texts.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Texts ({texts.Count})",
                    Kind = EntityKind.Text,
                    LayerName = layer.Name,
                };
                foreach (var tx in texts)
                {
                    var val = tx.Value ?? string.Empty;
                    if (val.Length > 30)
                        val = val.Substring(0, 30) + "…";
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = tx.Id,
                            Name = $"Text {SafeId(tx.Id)} '{val}' @ {FormatPt(tx.Position)}",
                            Kind = EntityKind.Text,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // MTexts
            var mtexts = model
                .MTexts?.Where(mt =>
                    string.Equals(mt.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (mtexts != null && mtexts.Any())
            {
                var t = new TypeNode
                {
                    Name = $"MTexts ({mtexts.Count})",
                    Kind = EntityKind.MText,
                    LayerName = layer.Name,
                };
                foreach (var mt in mtexts)
                {
                    var val = mt.Value ?? string.Empty;
                    if (val.Length > 30)
                        val = val.Substring(0, 30) + "…";
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = mt.Id,
                            Name = $"MText {SafeId(mt.Id)} '{val}' @ {FormatPt(mt.Position)}",
                            Kind = EntityKind.MText,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Splines
            var splines = model
                .Splines?.Where(sp =>
                    string.Equals(sp.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (splines != null && splines.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Splines ({splines.Count})",
                    Kind = EntityKind.Spline,
                    LayerName = layer.Name,
                };
                foreach (var sp in splines)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = sp.Id,
                            Name = $"Spline {SafeId(sp.Id)} ({sp.Points.Count} pts)",
                            Kind = EntityKind.Spline,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Solids
            var solids = model
                .Solids?.Where(sl =>
                    string.Equals(sl.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (solids != null && solids.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Solids ({solids.Count})",
                    Kind = EntityKind.Solid,
                    LayerName = layer.Name,
                };
                foreach (var sl in solids)
                {
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = sl.Id,
                            Name = $"Solid {SafeId(sl.Id)} ({sl.Vertices.Count} vertices)",
                            Kind = EntityKind.Solid,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            // Hatches
            var hatches = model
                .Hatches?.Where(h =>
                    string.Equals(h.Layer, layer.Name, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (hatches != null && hatches.Any())
            {
                var t = new TypeNode
                {
                    Name = $"Hatches ({hatches.Count})",
                    Kind = EntityKind.Hatch,
                    LayerName = layer.Name,
                };
                foreach (var h in hatches)
                {
                    var kind = h.FillKind.ToString();
                    var pat = !string.IsNullOrWhiteSpace(h.PatternName) ? h.PatternName : kind;
                    var loops = h.Loops?.Count ?? 0;
                    t.Children.Add(
                        new EntityNode
                        {
                            Id = h.Id,
                            Name = $"Hatch {SafeId(h.Id)} ({pat}, {loops} loops)",
                            Kind = EntityKind.Hatch,
                            LayerName = layer.Name,
                        }
                    );
                }
                node.Children.Add(t);
            }

            result.Add(node);
        }

        return result;
    }

    private static string SafeId(string? id)
    {
        return string.IsNullOrWhiteSpace(id) ? "(no id)" : id;
    }

    private static string FormatPt(Point p)
    {
        return $"{p.X:0.###},{p.Y:0.###}";
    }
}
