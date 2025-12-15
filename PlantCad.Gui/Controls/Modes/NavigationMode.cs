using Avalonia;
using Avalonia.Input;

namespace PlantCad.Gui.Controls.Modes
{
    public sealed class NavigationMode : IViewportMode
    {
        public void OnEnter(Controls.CadViewportControl viewport)
        {
        }

        public void OnExit(Controls.CadViewportControl viewport)
        {
        }

        public bool OnPointerPressed(Point screen) => false;
        public bool OnPointerMoved(Point screen) => false;
        public bool OnPointerReleased(Point screen) => false;
        public bool OnPointerPressedEx(Point screen, KeyModifiers mods) => false;
        public bool OnKeyDown(Key key, KeyModifiers mods) => false;
        public bool OnWheel(PointerWheelEventArgs e) => false;
    }
}
