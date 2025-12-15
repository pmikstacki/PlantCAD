using ACadSharp.Entities;

namespace PlantCad.Gui.Services.Internal.EntityReaders;

public interface ICadEntityReader
{
    bool CanRead(Entity entity);
    void Read(Entity entity, CadReadContext context);
}
