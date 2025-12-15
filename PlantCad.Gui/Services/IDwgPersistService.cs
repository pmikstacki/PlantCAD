namespace PlantCad.Gui.Services;

using System;
using ACadSharp;

public interface IDwgPersistService
{
    void SaveAs(string sourceDwgPath, string targetDwgPath, Action<CadDocument>? mutateDocument = null);
}
