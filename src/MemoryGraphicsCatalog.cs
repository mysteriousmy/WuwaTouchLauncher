using System.Globalization;

// Selectable memory requests are experimental. A mapping is not a promise that
// changing a CVar reconfigures its consumer, resources, callbacks or GPU backend.
internal static class MemoryGraphicsCatalog
{
    public const string Summary = "内存注入和帧率解锁一样有一定风险，请酌情使用";
    public const string ExperimentalWarning = "不保证所有选项内存注入均可用，请自行实验";
    public static readonly string[] UnresolvedKeys =
    [
        "smooth-fps", "fixed-fps", "npc-density", "vegetation-dither", "dynamic-bones",
        "teammate-effects", "hit-material", "damage-effects", "eye-mode", "eye-temperature",
        "eye-strength", "eye-brightness", "eye-texture"
    ];

    public static bool Supports(string key, string value = "keep") =>
        key is not ("display-mode" or "resolution") &&
        SettingsCatalog.TouchOptions.Any(x => x.Key == key && x.Choices.Any(c => c.Value == value));

    public static string[] Unavailable(TouchSettings settings) => UnresolvedKeys
        .Where(key => settings.UsesMemory(key) && settings.Values.GetValueOrDefault(key, "keep") != "keep")
        .ToArray();

    public static Dictionary<string, decimal> Build(TouchSettings settings)
    {
        var result = new Dictionary<string, decimal>();
        foreach (var (key, value) in settings.Values)
        {
            if (value == "keep" || !settings.UsesMemory(key)) continue;
            decimal number = decimal.Parse(value, CultureInfo.InvariantCulture);
            switch (key)
            {
                case "dlss": result["r.NGX.DLSS.Enable"] = number; break;
                case "dlss-quality":
                    result["r.NGX.DLSS.Quality.Auto"] = number == 99 ? 1 : 0;
                    if (number != 99) result["r.NGX.DLSS.Quality"] = number;
                    break;
                case "anti-aliasing": result["r.DefaultFeature.AntiAliasing"] = number == 0 ? 0 : 2; break;
                case "native-fg":
                    result["r.Streamline.DLSSG.Enable"] = number == 0 ? 0 : 1;
                    if (number > 0) result["r.Streamline.DLSSG.FramesToGenerate"] = number;
                    break;
                case "fps": result["t.MaxFPS"] = number; break;
                case "vsync": result["r.VSync"] = number; result["rhi.SyncInterval"] = number; break;
                case "render-scale": result["r.ScreenPercentage"] = number; break;
                case "anisotropy": result["r.MaxAnisotropy"] = number; break;
                case "lighting": result["r.Kuro.GlobalLightQuality"] = number; break;
                case "dlss-sharpness": result["r.NGX.DLSS.Sharpness"] = number; break;
                case "ambient-occlusion":
                    result["r.AmbientOcclusionLevels"] = number == 0 ? 0 : -1;
                    result["r.DistanceFieldAO"] = number >= 2 ? 1 : 0;
                    if (number >= 2) result["r.DistanceFieldAOQuality"] = number;
                    break;
                case "volumetric-fog":
                    result["r.VolumetricFog"] = number == 0 ? 0 : 1;
                    if (number != 0) result["r.Kuro.VRS.VolumeCloudQuality"] = number;
                    break;
                case "volumetric-light": result["r.LightShaftQuality"] = number; break;
                case "motion-blur": result["r.MotionBlur.Amount"] = number / 4; break;
                case "bloom": result["r.Kuro.KuroBloomEnable"] = number; break;
                case "auto-exposure": result["r.Kuro.AutoExposurePlayerCustom"] = number; break;
                case "auto-quality": result["r.Kuro.AutoCoolUIEnable"] = number; break;
                case "shadow": result["sg.ShadowQuality"] = number; break;
                case "effects": result["sg.EffectsQuality"] = number; break;
                case "detail": result["r.DetailMode"] = number; break;
                case "texture": result["sg.TextureQuality"] = number; break;
                case "view-distance": result["r.Kuro.LODDistanceScale"] = number + 1; break;
                case "vegetation": result["foliage.DensityScale"] = number == 3 ? 1 : 0.6m + number / 10; break;
                case "environment-interaction": result["r.MarchingCubes.EnablePrePass"] = number; break;
                case "fsr": result["r.FidelityFX.FSR3.Enabled"] = number; break;
                case "fsr-quality": result["r.FidelityFX.FSR3.QualityMode"] = 2 - number; break;
                case "fsr-fg": result["r.FidelityFX.FI.Enabled"] = number; break;
                case "fsr-legacy": result["r.FidelityFX.FSR.PrimaryUpscale"] = number; break;
                case "xess": result["r.XeSS.Enabled"] = number; break;
                case "xess-quality": result["r.XeSS.Quality"] = number + 2; break;
                case "xess-fg":
                    result["r.XeFG.Enabled"] = number == 0 ? 0 : 1;
                    if (number > 0) result["r.XeFG.MaxInterpolatedFrames"] = number;
                    break;
                case "ray-tracing": result["r.RayTracing.Enable"] = number; break;
                case "ray-quality":
                    result["sg.RayTracingQuality"] = number;
                    result["r.RayTracing.Enable"] = number == 0 ? 0 : 1;
                    break;
                case "ray-gi": result["r.RayTracing.GlobalIllumination"] = number == 0 ? 0 : 3; break;
                case "ray-reflections": result["r.RayTracing.Reflections"] = number; break;
                case "ray-shadows": result["r.RayTracing.Shadows"] = number; break;
                case "brightness": result["r.Gamma"] = 1 + number; break;
                case "saturation": result["r.Client.Saturation"] = number / 50; break;
                case "contrast": result["r.Client.Contrast"] = number / 50; break;
                case "image-display":
                    result["r.Kuro.KuroEnableScreenFilter"] = number == 1 ? 1 : 0;
                    result["r.BlueLightFilter.Disable"] = number == 2 ? 0 : 1;
                    result["r.Tonemapper.BrightnessAndTextureDisable"] = number == 2 ? 0 : 1;
                    break;
            }
        }
        // Match the file preset precedence even when a conflicting FPS
        // or VSync value is explicitly selected in the same profile.
        if (settings.Values.GetValueOrDefault("unlock-fps", "keep") is var unlock && unlock != "keep")
        {
            bool maintainUnlock = settings.UsesMemory("unlock-fps");
            // The separately selected FPS plugin owns these values even when
            // only fps/vsync is checked in mixed mode. Do not fight it, and do
            // not add writes for unchecked controls just to implement priority.
            if (maintainUnlock || result.ContainsKey("t.MaxFPS"))
                result["t.MaxFPS"] = decimal.Parse(unlock, CultureInfo.InvariantCulture);
            if (maintainUnlock || result.ContainsKey("r.VSync")) result["r.VSync"] = 0;
            if (maintainUnlock || result.ContainsKey("rhi.SyncInterval")) result["rhi.SyncInterval"] = 0;
        }
        return result;
    }
}
