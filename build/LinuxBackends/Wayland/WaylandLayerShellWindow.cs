using Avalonia.Platform;
using Avalonia.Wayland;

namespace Avalonia;

/// <summary>Logical coordinates, independent of the surface's buffer/render scaling.</summary>
public interface IWaylandLayerShellWindow
{
    void SetOutput(string? name, PixelPoint logicalOrigin);
    void SetInputRegion(Rect? logicalRegion);
    void SetBackgroundCornerRadius(double logicalRadius);
}

/// <summary>Creates a layer-shell overlay only on the native Wayland backend.</summary>
public static class WaylandLayerShellWindow
{
    public static IWindowImpl? TryCreate()
        => AvaloniaLocator.Current.GetService<IWindowingPlatform>() is WaylandTopLevelFactory factory
            ? factory.CreateLayerWindow()
            : null;
}
