using System.Threading.Tasks;

namespace PlantCad.Gui.Services;

public interface IFileDialogService
{
    Task<string?> ShowOpenCadAsync();
    Task<string?> ShowSaveExcelAsync(string suggestedFileName);

    Task<string?> ShowOpenDatabaseAsync();
    Task<string?> ShowSaveDatabaseAsync(string suggestedFileName);
}
