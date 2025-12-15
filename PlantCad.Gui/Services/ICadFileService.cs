using System.Threading;
using System.Threading.Tasks;
using PlantCad.Gui.Models;

namespace PlantCad.Gui.Services;

public interface ICadFileService
{
    Task<CadModel> OpenAsync(string path, CancellationToken cancellationToken = default);
}
