using System.Collections.Generic;
using ProtoBuf;

namespace PlantCad.Gui.Models.Modules;

[ProtoContract]
public sealed class ModulePolygon
{
    [ProtoMember(1)]
    public List<ModulePoint> Points { get; set; } = new();
}
