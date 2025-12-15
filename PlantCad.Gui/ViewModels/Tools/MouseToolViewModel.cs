using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.ViewModels.Tools;

public partial class MouseToolViewModel : Tool
{
    [ObservableProperty]
    private bool zoomRequiresCtrl;

    [ObservableProperty]
    private bool zoomWithCtrlDrag;

    [ObservableProperty]
    private bool scrollUpZoomIn;

    [ObservableProperty]
    private double zoomBase;

    [ObservableProperty]
    private int maxStepsPerEvent;

    [ObservableProperty]
    private double responseExponent;

    [ObservableProperty]
    private double responseMin;

    [ObservableProperty]
    private double responseMax;

    [ObservableProperty]
    private double dragPixelsPerStep;

    public MouseToolViewModel()
    {
        Title = "Mouse";
        CanClose = false;
        DockGroup = "Tools";

        var settings = ServiceRegistry.MouseSettings ??= new MouseSettings();
        ZoomRequiresCtrl = settings.ZoomRequiresCtrl;
        ZoomWithCtrlDrag = settings.ZoomWithCtrlDrag;
        ScrollUpZoomIn = settings.ScrollUpZoomIn;
        ZoomBase = settings.ZoomBase;
        MaxStepsPerEvent = settings.MaxStepsPerEvent;
        ResponseExponent = settings.ResponseExponent;
        ResponseMin = settings.ResponseMin;
        ResponseMax = settings.ResponseMax;
        DragPixelsPerStep = settings.DragPixelsPerStep;
    }

    partial void OnZoomRequiresCtrlChanged(bool value) => Update(s => s.ZoomRequiresCtrl = value);

    partial void OnZoomWithCtrlDragChanged(bool value) => Update(s => s.ZoomWithCtrlDrag = value);

    partial void OnScrollUpZoomInChanged(bool value) => Update(s => s.ScrollUpZoomIn = value);

    partial void OnZoomBaseChanged(double value) => Update(s => s.ZoomBase = value);

    partial void OnMaxStepsPerEventChanged(int value) => Update(s => s.MaxStepsPerEvent = value);

    partial void OnResponseExponentChanged(double value) => Update(s => s.ResponseExponent = value);

    partial void OnResponseMinChanged(double value)
    {
        if (value > ResponseMax)
        {
            ResponseMax = value;
        }
        Update(s => s.ResponseMin = value);
    }

    partial void OnResponseMaxChanged(double value)
    {
        if (value < ResponseMin)
        {
            ResponseMin = value;
        }
        Update(s => s.ResponseMax = value);
    }

    partial void OnDragPixelsPerStepChanged(double value) =>
        Update(s => s.DragPixelsPerStep = value);

    private static void Update(System.Action<MouseSettings> mutator)
    {
        var s = ServiceRegistry.MouseSettings;
        if (s == null)
            return;
        mutator(s);
        s.NotifyChanged();
    }
}
