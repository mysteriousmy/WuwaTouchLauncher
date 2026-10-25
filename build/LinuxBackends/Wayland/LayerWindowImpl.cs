using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Wayland.Server;
using Avalonia.Wayland.Server.Persistent;

namespace Avalonia.Wayland;

internal sealed class LayerWindowImpl : WindowBaseImpl, IWindowImpl, IWaylandLayerShellWindow
{
    private WaylandSurfaceCreateResult<WLayerSurfaceProxy>? _handle;
    private WLayerSurfaceProxy? _proxy;
    private PixelPoint _position;
    private PixelPoint _outputOrigin;
    private string? _outputName;
    private Rect? _inputRegion;
    private bool _recovering;
    private bool _desiredVisible;
    private bool _topmost = true;
    private bool _blurRequested;
    private double _backgroundRadius;
    private WindowTransparencyLevel _transparency = WindowTransparencyLevel.Transparent;

    public LayerWindowImpl(WaylandWorkerClient client) : base(client) => ClientSize = new Size(720, 176);

    public override IPlatformRenderSurface[] Surfaces => _handle?.GetRenderSurfaces() ?? [];
    internal override WXdgShellSurfaceProxy? SurfaceProxy => _proxy;
    public override Size MaxAutoSizeHint => new(double.PositiveInfinity, double.PositiveInfinity);
    public override PixelPoint Position => _position;
    public override Point PointToClient(PixelPoint point) => new(point.X - _position.X, point.Y - _position.Y);
    public override PixelPoint PointToScreen(Point point) => new(_position.X + (int)Math.Round(point.X),
        _position.Y + (int)Math.Round(point.Y));
    public override IPopupImpl? CreatePopup() => null; // Avalonia uses in-window popups for the overlay HUD.
    public override WindowTransparencyLevel TransparencyLevel => _transparency;

    public override void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> levels)
    {
        _blurRequested = false;
        foreach (var level in levels)
        {
            if (level == WindowTransparencyLevel.Blur || level == WindowTransparencyLevel.AcrylicBlur)
            {
                _blurRequested = true;
                break;
            }
            if (level == WindowTransparencyLevel.Transparent || level == WindowTransparencyLevel.None) break;
        }
        _proxy?.SetBackgroundBlur(_blurRequested, _backgroundRadius);
        if (!_blurRequested) SetActualBlur(false);
    }

    public void SetBackgroundCornerRadius(double radius)
    {
        _backgroundRadius = double.IsFinite(radius) ? Math.Clamp(radius, 0, 16384) : 0;
        _proxy?.SetBackgroundBlur(_blurRequested, _backgroundRadius);
    }

    private void SetActualBlur(bool enabled)
    {
        var level = enabled ? WindowTransparencyLevel.Blur : WindowTransparencyLevel.Transparent;
        if (_transparency == level) return;
        _transparency = level;
        TransparencyLevelChanged?.Invoke(level);
    }

    public override object? TryGetFeature(Type featureType) =>
        featureType == typeof(IWaylandLayerShellWindow) ? this : base.TryGetFeature(featureType);

    public override void Show(bool activate, bool isDialog)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _desiredVisible = true;
        CurrentSink ??= new LayerSink(this);
        Client.AnyThreadWakeupRenderLoop();
    }

    public override void Hide()
    {
        _desiredVisible = false;
        base.Hide();
    }

    private void HideClosedSurface() => base.Hide();

    public void SetOutput(string? name, PixelPoint logicalOrigin)
    {
        _outputName = name;
        _outputOrigin = logicalOrigin;
        // If the last output disappeared, the compositor closed the surface.
        // Screen discovery on reconnect can now recreate it without remapping
        // a window that the application intentionally hid.
        if (_desiredVisible && CurrentSink is null && !IsDisposed) Show(false, false);
        PushLayout();
    }

    public void SetInputRegion(Rect? logicalRegion)
    {
        _inputRegion = logicalRegion;
        _proxy?.SetInputRegion(logicalRegion);
    }

    public override void SetTopmost(bool value)
    {
        _topmost = value;
        _proxy?.SetOverlayLayer(value);
    }

    public void Move(PixelPoint point)
    {
        if (_position == point) return;
        _position = point;
        PushLayout();
        PositionChanged?.Invoke(point);
    }

    private void PushLayout() => _proxy?.SetLayout(ClientSize, _position, _outputName, _outputOrigin);

    public void Resize(Size clientSize, WindowResizeReason reason = WindowResizeReason.Application)
    {
        var size = new Size(Math.Clamp(Math.Ceiling(clientSize.Width), 1, 32768),
            Math.Clamp(Math.Ceiling(clientSize.Height), 1, 32768));
        if (ClientSize == size) return;
        ClientSize = size;
        PushLayout();
        Resized?.Invoke(size, reason);
    }

    private sealed class LayerSink : Sink, IWLayerEventSink
    {
        private new LayerWindowImpl Parent => (LayerWindowImpl)base.Parent;
        public LayerSink(LayerWindowImpl parent) : base(parent)
        {
            var handle = parent.Client.CreateLayerHandle(new WLayerEventSinkProxy(this, WaylandMarshallers.UIThread),
                parent.ClientSize, parent._position, parent._outputName, parent._outputOrigin);
            parent._handle = handle;
            parent._proxy = handle.Proxy;
            parent._proxy.SetInputRegion(parent._inputRegion);
            parent._proxy.SetOverlayLayer(parent._topmost);
            parent._proxy.SetBackgroundBlur(parent._blurRequested, parent._backgroundRadius);
            parent.ApplyCurrentCursor(parent._proxy);
        }

        protected override void DisconnectFromSurface()
        {
            var proxy = Parent._proxy;
            Parent._proxy = null;
            Parent._handle = null;
            proxy?.Disconnect();
        }

        public void OnConfigure(XdgConfigureBatch batch, PixelPoint outputOrigin, int generation)
        {
            if (IsDisposed) return;
            Parent._recovering = false;
            if (Parent._outputOrigin != outputOrigin)
            {
                Parent._position = outputOrigin + (Parent._position - Parent._outputOrigin);
                Parent._outputOrigin = outputOrigin;
                Parent.PositionChanged?.Invoke(Parent._position);
            }
            // Layer-shell configure sizes are hints. This floating surface is
            // sized by the application, not stretched between opposite anchors.
            // Near an output edge, the hint can be only the remaining space.
            // Adopting it here would feed that smaller size into the next move
            // and permanently shrink the window, even after dragging it back.
            // Preserve ClientSize; explicit Resize still updates layout/buffers.
            Parent._proxy?.AcknowledgeConfigure(batch.Serial, generation);
            Parent.Client.AnyThreadWakeupRenderLoop();
        }

        public void OnClosed()
        {
            if (IsDisposed) return;
            var recover = !Parent._recovering;
            Parent._recovering = true;
            Parent.HideClosedSurface();
            // Output removal closes its layer surfaces. Recreate on a remaining output after screen events.
            if (recover)
                Parent.PostToUiThread(() =>
                {
                    if (!Parent.IsDisposed && Parent._desiredVisible) Parent.Show(false, false);
                });
        }

        public void OnBackgroundBlurChanged(bool enabled)
        {
            if (!IsDisposed) Parent.SetActualBlur(enabled);
        }
    }

    public WindowState WindowState { get => WindowState.Normal; set { } }
    public bool WindowStateGetterIsUsable => true;
    public Action<WindowState>? WindowStateChanged { get; set; }
    public Func<WindowCloseReason, bool>? Closing { get; set; }
    public Action? GotInputWhenDisabled { get; set; }
    public Action<bool>? ExtendClientAreaToDecorationsChanged { get; set; }
    public Action? DrawnDecorationsRequestChanged { get; set; }
    public bool IsClientAreaExtendedToDecorations => false;
    public bool NeedsManagedDecorations => false;
    public PlatformRequestedDrawnDecoration RequestedDrawnDecorations => PlatformRequestedDrawnDecoration.None;
    public Thickness ExtendedMargins => default;
    public Thickness OffScreenMargin => default;
    public void SetTitle(string? title) { }
    public void SetAppId(string? appId) { }
    public void SetParent(IWindowImpl? parent) { }
    public void SetEnabled(bool enabled) => IsEnabled = enabled;
    public void SetWindowDecorations(WindowDecorations decorations) { }
    public void SetIcon(IWindowIconImpl? icon) { }
    public void ShowTaskbarIcon(bool value) { }
    public void CanResize(bool value) { }
    public void SetCanMinimize(bool value) { }
    public void SetCanMaximize(bool value) { }
    public void SetMinMaxSize(Size minSize, Size maxSize) { }
    public void SetExtendClientAreaToDecorationsHint(bool value) { }
    public void SetExtendClientAreaTitleBarHeightHint(double value) { }
    public void SetShadowExtents(Thickness extents) { }
    public void BeginMoveDrag(PointerPressedEventArgs e) { }
    public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e) { }
}
