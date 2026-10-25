using System;
using NWayland.Protocols.ExtBackgroundEffectV1;

namespace Avalonia.Wayland.Server.Transient;

partial class WaylandGlobals
{
    public ExtBackgroundEffectManagerV1? BackgroundEffectManager { get; private set; }
    public bool SupportsBackgroundBlur { get; private set; }
    public event Action? BackgroundEffectCapabilitiesChanged;

    private void InitializeBackgroundEffects()
    {
        BackgroundEffectManager = Bind<ExtBackgroundEffectManagerV1>(1, 1, new BackgroundEffectListener(this));
    }

    private void DisposeBackgroundEffects()
    {
        BackgroundEffectManager?.Destroy();
        BackgroundEffectManager?.Dispose();
        BackgroundEffectManager = null;
        SupportsBackgroundBlur = false;
    }

    private sealed class BackgroundEffectListener(WaylandGlobals owner) : ExtBackgroundEffectManagerV1.Listener
    {
        protected override void Capabilities(ExtBackgroundEffectManagerV1 sender,
            ExtBackgroundEffectManagerV1.CapabilityEnum flags)
        {
            if (!ReferenceEquals(sender, owner.BackgroundEffectManager)) return;
            var available = (flags & ExtBackgroundEffectManagerV1.CapabilityEnum.Blur) != 0;
            if (owner.SupportsBackgroundBlur == available) return;
            owner.SupportsBackgroundBlur = available;
            owner.BackgroundEffectCapabilitiesChanged?.Invoke();
        }
    }
}
