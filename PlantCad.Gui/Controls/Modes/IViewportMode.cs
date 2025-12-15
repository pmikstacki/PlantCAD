using Avalonia;
using Avalonia.Input;

namespace PlantCad.Gui.Controls.Modes
{
    public interface IViewportMode
    {
        void OnEnter(Controls.CadViewportControl viewport);
        void OnExit(Controls.CadViewportControl viewport);
        bool OnPointerPressed(Point screen);
        bool OnPointerMoved(Point screen);
        bool OnPointerReleased(Point screen);
        bool OnPointerPressedEx(Point screen, KeyModifiers mods);
        bool OnKeyDown(Key key, KeyModifiers mods);
        bool OnWheel(PointerWheelEventArgs e);
    }
}
