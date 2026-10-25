using System;
using Avalonia.Threading;
using static Avalonia.X11.XLib;

namespace Avalonia.X11;

internal unsafe partial class X11Window
{
    private bool _overlayTopmost;
    private bool _overlayRestackPending;
    private IntPtr _overlayWmStateAtom;

    private void ScheduleOverlayRestack()
    {
        if (!_overrideRedirect || _popup || !_overlayTopmost || _overlayRestackPending || _handle == IntPtr.Zero)
            return;
        _overlayRestackPending = true;
        // Coalesce a batch of X events; no polling loop and no desktop/WM-specific commands.
        Dispatcher.UIThread.Post(() =>
        {
            _overlayRestackPending = false;
            if (_handle != IntPtr.Zero && _overlayTopmost) RestackOverlay();
        }, DispatcherPriority.Background);
    }

    private void RestackOverlay()
    {
        var own = new XWindowAttributes();
        if (XGetWindowAttributes(_x11.Display, _handle, ref own) == 0 || own.map_state != MapState.IsViewable)
            return;
        if (XQueryTree(_x11.Display, _x11.RootWindow, out _, out _, out var children, out var count) == 0)
            return;
        try
        {
            // XQueryTree returns bottom-to-top. Only move above a managed sibling
            // currently above us; menus and other override-redirect popups retain their order.
            var windows = (IntPtr*)children;
            for (var i = Math.Min(count, 16384) - 1; i >= 0; i--)
            {
                if (windows[i] == _handle) return;
                var sibling = new XWindowAttributes();
                if (XGetWindowAttributes(_x11.Display, windows[i], ref sibling) == 0 ||
                    sibling.map_state != MapState.IsViewable || sibling.c_class != 1)
                    continue;
                // Reparenting WMs can mark their decoration frame override-redirect.
                // ICCCM's WM_STATE identifies the managed client inside that frame;
                // an ordinary popup has no such client and must keep its stack order.
                var remaining = 256;
                if (sibling.override_direct != 0 && !ContainsManagedClient(windows[i], 4, ref remaining))
                    continue;
                var changes = new XWindowChanges { sibling = windows[i], stack_mode = StackMode.Above };
                XConfigureWindow(_x11.Display, _handle,
                    ChangeWindowFlags.CWSibling | ChangeWindowFlags.CWStackMode, ref changes);
                XFlush(_x11.Display);
                return;
            }
        }
        finally { if (children != IntPtr.Zero) XFree(children); }
    }

    private bool ContainsManagedClient(IntPtr window, int depth, ref int remaining)
    {
        if (remaining-- <= 0) return false;
        if (_overlayWmStateAtom == IntPtr.Zero)
            _overlayWmStateAtom = XInternAtom(_x11.Display, "WM_STATE", false);
        XGetWindowProperty(_x11.Display, window, _overlayWmStateAtom, IntPtr.Zero, IntPtr.Zero,
            false, _overlayWmStateAtom, out var type, out _, out _, out _, out var property);
        if (property != IntPtr.Zero) XFree(property);
        if (type == _overlayWmStateAtom) return true;
        if (depth == 0 || XQueryTree(_x11.Display, window, out _, out _, out var children, out var count) == 0)
            return false;
        try
        {
            var windows = (IntPtr*)children;
            for (var i = 0; i < count && remaining > 0; i++)
                if (ContainsManagedClient(windows[i], depth - 1, ref remaining)) return true;
            return false;
        }
        finally { if (children != IntPtr.Zero) XFree(children); }
    }
}
