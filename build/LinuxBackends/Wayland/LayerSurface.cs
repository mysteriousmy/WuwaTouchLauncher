using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.SourceGenerator;
using Avalonia.Threading;
using Avalonia.Wayland.Server;
using Avalonia.Wayland.Server.Interop;
using Avalonia.Wayland.Server.Persistent;
using Avalonia.Wayland.Server.Transient;
using NWayland.Protocols.Wlr.WlrLayerShellUnstableV1;

namespace Avalonia.Wayland.Server.Persistent
{
    [GenerateCrossThreadProxy(typeof(WaylandDispatchPriority),
        "Avalonia.Wayland.Server.WaylandDispatchPriority.Normal", GeneratedClassName = "WLayerSurfaceProxy")]
    internal interface IWLayerSurface : IWXdgShellSurface
    {
        void SetLayout(Size size, PixelPoint position, string? outputName, PixelPoint outputOrigin);
        void AcknowledgeConfigure(uint serial, int generation);
        void SetOverlayLayer(bool topmost);
        void SetBackgroundBlur(bool requested, double cornerRadius);
    }

    [GenerateCrossThreadProxy(typeof(DispatcherPriority), "default", GeneratedClassName = "WLayerEventSinkProxy")]
    internal interface IWLayerEventSink : IWSurfaceEventSink
    {
        void OnConfigure(XdgConfigureBatch batch, PixelPoint outputOrigin, int generation);
        void OnClosed();
        void OnBackgroundBlurChanged(bool enabled);
    }

    internal sealed partial class WLayerSurface : WXdgShellSurface, IWLayerSurface
    {
        private readonly WLayerEventSinkProxy _sink;
        private ZwlrLayerSurfaceV1? _layer;
        private Size _size;
        private PixelPoint _position;
        private PixelPoint _origin;
        private string? _outputName;
        private object? _activeOutputId;
        private int _generation;
        private bool _topmost = true;
        private readonly TaskCompletionSource<XdgConfigureBatch> _init = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<XdgConfigureBatch> BasicInitCompleted => _init.Task;

        public WLayerSurface(WaylandWorker worker, WLayerEventSinkProxy sink, Size size,
            PixelPoint position, string? outputName, PixelPoint origin) : base(worker, sink)
        {
            _sink = sink;
            _size = size;
            _position = position;
            _outputName = outputName;
            _origin = origin;
        }

        protected override bool UsesXdgRole => false;
        protected override void AckConfigure(uint serial) => _layer?.AckConfigure(serial);
        protected override void MaybeEmitWindowGeometry(PixelSize pixelSize, double scale, Size logicalSize) { }

        public override void OnConnected(WaylandConnection connection, WaylandGlobals globals)
        {
            base.OnConnected(connection, globals);
            var output = globals.Outputs.Outputs.FirstOrDefault(o =>
                string.Equals(o.XdgName ?? o.OutputName, _outputName, StringComparison.Ordinal))
                ?? globals.Outputs.Outputs.FirstOrDefault();
            if (output != null)
            {
                var snapshot = output.ToSnapshot();
                var local = _position - _origin;
                _origin = snapshot.LogicalPosition;
                _position = _origin + local;
            }
            _activeOutputId = output?.Id;
            _generation++;
            var shell = globals.LayerShell ?? throw new AvaloniaWaylandException("layer-shell is unavailable");
            _layer = shell.GetLayerSurface(WlSurface!, output?.WlOutput!,
                _topmost ? ZwlrLayerShellV1.LayerEnum.Overlay : ZwlrLayerShellV1.LayerEnum.Bottom,
                "org.wuwatouchlauncher.avalonia.overlay", new Listener(this, _generation));
            _layer.SetAnchor(ZwlrLayerSurfaceV1.AnchorEnum.Top | ZwlrLayerSurfaceV1.AnchorEnum.Left);
            _layer.SetExclusiveZone(-1);
            _layer.SetKeyboardInteractivity(ZwlrLayerSurfaceV1.KeyboardInteractivityEnum.None);
            ConnectBackgroundEffect();
            ApplyLayout();
            WlSurface!.Commit(); // Initial empty commit requests configure, before attaching any buffer.
        }

        public void SetLayout(Size size, PixelPoint position, string? outputName, PixelPoint outputOrigin)
        {
            var target = Globals?.Outputs.Outputs.FirstOrDefault(o =>
                string.Equals(o.XdgName ?? o.OutputName, outputName, StringComparison.Ordinal))
                ?? Globals?.Outputs.Outputs.FirstOrDefault();
            var outputChanged = !ReferenceEquals(target?.Id, _activeOutputId);
            var sizeChanged = _size != size;
            _size = size;
            _position = position;
            _outputName = outputName;
            _origin = outputOrigin;
            if (outputChanged && Connection is { } connection && Globals is { } globals)
            {
                // A surface's target output is immutable; release its EGL targets before recreating it.
                OnDisconnected();
                OnConnected(connection, globals);
                return;
            }
            ApplyLayout();
            if (sizeChanged) ApplyBackgroundEffect();
            if (CanCommitOutOfBand)
                WlSurface?.Commit();
        }

        public void AcknowledgeConfigure(uint serial, int generation)
        {
            if (generation == _generation && _layer != null)
                SetPendingAckSerial(serial);
        }

        public void SetOverlayLayer(bool topmost)
        {
            if (_topmost == topmost) return;
            _topmost = topmost;
            if (_layer is { Version: >= 2 })
            {
                _layer.SetLayer(topmost ? ZwlrLayerShellV1.LayerEnum.Overlay : ZwlrLayerShellV1.LayerEnum.Bottom);
                WlSurface?.Commit();
            }
            else if (_layer != null && Connection is { } connection && Globals is { } globals)
            {
                OnDisconnected();
                OnConnected(connection, globals);
            }
            Worker.WakeupRenderLoop();
        }

        private void ApplyLayout()
        {
            _layer?.SetSize((uint)Math.Clamp(Math.Ceiling(_size.Width), 1, 32768),
                (uint)Math.Clamp(Math.Ceiling(_size.Height), 1, 32768));
            _layer?.SetMargin(_position.Y - _origin.Y, 0, 0, _position.X - _origin.X);
        }

        public override void OnDisconnected()
        {
            DisconnectBackgroundEffect();
            _layer?.Destroy();
            _layer = null;
            _activeOutputId = null;
            base.OnDisconnected();
        }

        private sealed class Listener(WLayerSurface parent, int generation) : ZwlrLayerSurfaceV1.Listener
        {
            protected override void Configure(ZwlrLayerSurfaceV1 sender, uint serial, uint width, uint height)
            {
                if (generation != parent._generation || !ReferenceEquals(sender, parent._layer)) return;
                var batch = new XdgConfigureBatch
                {
                    Serial = serial,
                    Size = new PixelSize(width == 0 ? (int)parent._size.Width : checked((int)width),
                        height == 0 ? (int)parent._size.Height : checked((int)height))
                };
                parent._sink.OnConfigure(batch, parent._origin, generation);
                parent._init.TrySetResult(batch);
                parent.Worker.WakeupRenderLoop();
            }

            protected override void Closed(ZwlrLayerSurfaceV1 sender)
            {
                if (generation != parent._generation || !ReferenceEquals(sender, parent._layer)) return;
                parent.OnDisconnected();
                parent._sink.OnClosed();
            }
        }
    }
}

namespace Avalonia.Wayland.Server
{
    partial class WaylandWorkerClient
    {
        public bool SupportsLayerShell => InvokeOobAsync(() => _worker.Globals?.LayerShell is not null)
            .GetAwaiter().GetResult();

        public WaylandSurfaceCreateResult<WLayerSurfaceProxy> CreateLayerHandle(WLayerEventSinkProxy sink,
            Size size, PixelPoint position, string? output, PixelPoint origin)
        {
            // Construct on the worker, so its queued registration cannot observe half-initialized fields.
            var surface = InvokeOobAsync(() => new WLayerSurface(_worker, sink, size, position, output, origin))
                .GetAwaiter().GetResult();
            return new(new WLayerSurfaceProxy(surface, Marshaller),
                () => surface.RenderSurfaces, surface.BasicInitCompleted);
        }
    }
}
