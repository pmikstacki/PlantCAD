using System.Collections.Generic;
using ProtoBuf;

namespace PlantCad.Gui.Models.Modules;

[ProtoContract]
public sealed class Module
{
    [ProtoMember(1)]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string? Description { get; set; }

    [ProtoMember(4)]
    public List<ModulePolygon> Shapes { get; set; } = new();

    [ProtoMember(5)]
    public List<Module> Children { get; set; } = new();
}
