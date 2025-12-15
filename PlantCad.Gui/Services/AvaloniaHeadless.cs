using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Skia;

namespace PlantCad.Gui.Services;

/// <summary>
/// Ensures Avalonia platform services are initialized for offscreen rendering
/// when used from non-Avalonia contexts (e.g., CLI).
/// Safe to call multiple times.
/// </summary>
public static class AvaloniaHeadless
{
    private static bool _initialized;

    private sealed class DummyApp : Application { }

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }
        try
        {
            AppBuilder.Configure<DummyApp>().UseSkia().SetupWithoutStarting();
            _initialized = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to initialize Avalonia for headless rendering.",
                ex
            );
        }
    }
}
