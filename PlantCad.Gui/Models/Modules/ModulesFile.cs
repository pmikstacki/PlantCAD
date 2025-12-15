using System.Collections.Generic;
using ProtoBuf;

namespace PlantCad.Gui.Models.Modules;

[ProtoContract]
public sealed class ModulesFile
{
    [ProtoMember(1)]
    public int Version { get; set; } = 1;

    [ProtoMember(2)]
    public string CadFilePath { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string? CadFileHash { get; set; }

    [ProtoMember(4)]
    public List<Module> RootModules { get; set; } = new();
}
