using ProtoBuf;

namespace PlantCad.Gui.Models.Modules;

[ProtoContract]
public sealed class ModulePoint
{
    [ProtoMember(1)]
    public double X { get; set; }

    [ProtoMember(2)]
    public double Y { get; set; }

    public ModulePoint() { }

    public ModulePoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}
