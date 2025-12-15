using System.Threading;
using System.Threading.Tasks;
using PlantCad.Gui.Models.Modules;

namespace PlantCad.Gui.Services.Modules;

public interface IModulesStorage
{
    string ResolveModulesPath(string cadPath);
    Task<ModulesFile?> LoadAsync(string cadPath, CancellationToken cancellationToken = default);
    Task SaveAsync(string cadPath, ModulesFile file, CancellationToken cancellationToken = default);
}
