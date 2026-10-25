using System;
using NWayland.Protocols.ExtBackgroundEffectV1;

namespace Avalonia.Wayland.Server.Persistent;

internal sealed partial class WLayerSurface
{
    private ExtBackgroundEffectSurfaceV1? _backgroundEffect;
    private bool _blurRequested;
    private double _backgroundRadius;

    private void ConnectBackgroundEffect()
    {
        Globals!.BackgroundEffectCapabilitiesChanged += ApplyBackgroundEffect;
        ApplyBackgroundEffect();
    }

    private void DisconnectBackgroundEffect()
    {
        if (Globals is { } globals)
            globals.BackgroundEffectCapabilitiesChanged -= ApplyBackgroundEffect;
        DestroyBackgroundEffect();
        _sink.OnBackgroundBlurChanged(false);
    }

    public void SetBackgroundBlur(bool requested, double cornerRadius)
    {
        if (_blurRequested == requested && _backgroundRadius == cornerRadius) return;
        _blurRequested = requested;
        _backgroundRadius = cornerRadius;
        ApplyBackgroundEffect();
    }

    private void DestroyBackgroundEffect()
    {
        _backgroundEffect?.Destroy();
        _backgroundEffect?.Dispose();
        _backgroundEffect = null;
    }

    private void ApplyBackgroundEffect()
    {
        if (WlSurface is null || Globals is not { } globals) return;
        var enabled = _blurRequested && globals.SupportsBackgroundBlur;
        if (enabled && globals.BackgroundEffectManager is { } manager)
        {
            _backgroundEffect ??= manager.GetBackgroundEffect(WlSurface);
            var region = globals.WlCompositor.CreateRegion();
            try
            {
                // Regions use surface-local logical pixels, independent of output scaling.
                // Round the region inward so transparent panel corners remain unblurred.
                var width = Math.Max(1, (int)Math.Ceiling(_size.Width));
                var height = Math.Max(1, (int)Math.Ceiling(_size.Height));
                var radius = Math.Min((int)Math.Ceiling(_backgroundRadius), Math.Min(width, height) / 2);
                if (height > radius * 2) region.Add(0, radius, width, height - radius * 2);
                for (var row = 0; row < radius; row++)
                {
                    // wl_region has no fractional coverage. Only blur pixels fully
                    // inside the panel curve; the old row-centre test included
                    // antialiased edge pixels and exposed a coloured fringe.
                    var dy = radius - row;
                    // Keep the hard region inside both rasterizer coverage and
                    // compositor fractional-scale rounding at the curved edge.
                    var inset = (int)Math.Ceiling(radius - Math.Sqrt(radius * radius - dy * dy) + 2.5);
                    if (width > inset * 2)
                    {
                        region.Add(inset, row, width - inset * 2, 1);
                        region.Add(inset, height - row - 1, width - inset * 2, 1);
                    }
                }
                _backgroundEffect.SetBlurRegion(region);
            }
            finally
            {
                region.Destroy();
                region.Dispose();
            }
        }
        else DestroyBackgroundEffect();

        if (CanCommitOutOfBand) WlSurface.Commit();
        _sink.OnBackgroundBlurChanged(enabled);
        Worker.WakeupRenderLoop();
    }
}
