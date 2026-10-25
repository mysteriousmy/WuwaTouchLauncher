using Avalonia.Platform;
using Avalonia.X11;

namespace Avalonia;

/// <summary>Repository-local factory; normal application windows remain managed.</summary>
public static class X11UnmanagedWindow
{
    public static IWindowImpl? TryCreate()
        => AvaloniaLocator.Current.GetService<IWindowingPlatform>() is AvaloniaX11Platform platform
            ? new X11Window(platform, null, overrideRedirect: true)
            : null;
}
