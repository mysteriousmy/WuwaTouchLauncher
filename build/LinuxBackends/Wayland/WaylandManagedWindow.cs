using Avalonia.Input;
using Avalonia.Wayland.Server;

namespace Avalonia
{
    /// <summary>Native xdg-toplevel interaction; the compositor owns placement and stacking.</summary>
    public interface IWaylandManagedWindow
    {
        void SetInputRegion(Rect? logicalRegion);
        void ShowWindowMenu(PointerPressedEventArgs input, Point logicalPosition);
    }
}

namespace Avalonia.Wayland
{
    internal partial class WindowImpl : IWaylandManagedWindow
    {
        private Rect? _inputRegion;

        public void SetInputRegion(Rect? logicalRegion)
        {
            _inputRegion = logicalRegion;
            _surfaceProxy?.SetInputRegion(logicalRegion);
        }

        public void ShowWindowMenu(PointerPressedEventArgs input, Point logicalPosition)
        {
            input.Pointer.Capture(null);
            _surfaceProxy?.ShowWindowMenu(input.PlatformInputEventCookie, logicalPosition,
                WaylandDispatchPriority.Oob);
        }
    }
}
