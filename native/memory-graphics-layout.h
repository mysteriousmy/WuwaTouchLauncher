// Validated 2026-10-25, CN Windows 3.7 Client-Win64-Shipping.exe.
// RVAs remain relative to the loaded image; never reuse them for another hash.
static const BYTE graphicsExpectedHash[32] = {
    0x61,0x74,0x6c,0xa0,0xcc,0xfb,0x39,0x32,0x6e,0x56,0xd8,0x6f,0x08,0xf9,0x07,0xb4,
    0x8c,0xd2,0x62,0xa3,0xea,0x00,0x29,0x9b,0x37,0x58,0xb4,0xc9,0x09,0x17,0x76,0x33
};
static const GraphicsSpec graphicsSpecs[] = {
    {L"r.NGX.DLSS.Enable",                  0x0983aeb0,0x07e28c20,GRAPHICS_INT, 0,1,0x03,FALSE},
    {L"r.NGX.DLSS.Quality.Auto",            0x0983af40,0x07e28b68,GRAPHICS_BOOL,0,1,0x03,FALSE},
    {L"r.NGX.DLSS.Quality",                 0x0983af28,0x07e28c20,GRAPHICS_INT,-2,3,0x2f,FALSE},
    {L"r.DefaultFeature.AntiAliasing",      0x09b7a970,0x07e28c20,GRAPHICS_INT, 0,3,0x05,FALSE},
    {L"r.Streamline.DLSSG.Enable",          0x0983bea0,0x07e28c20,GRAPHICS_INT, 0,2,0x03,TRUE},
    {L"t.MaxFPS",                         0x09b85308,0x07e28cd8,GRAPHICS_FLOAT,-1,1000,0,FALSE},
    {L"r.VSync",                          0x098f5710,0x07e28c20,GRAPHICS_INT, 0,1,0x03,FALSE},
    {L"rhi.SyncInterval",                 0x09b1a0e0,0x07e28c20,GRAPHICS_INT, 0,4,0x03,FALSE},
    {L"r.ScreenPercentage",               0x098f5500,0x07e28cd8,GRAPHICS_FLOAT,-1,200,0,FALSE},
    {L"r.MaxAnisotropy",                  0x098f5740,0x07e28c20,GRAPHICS_INT, 1,16,0x808b,FALSE},
    {L"r.Kuro.GlobalLightQuality",         0x09a32688,0x07e28c20,GRAPHICS_INT, 0,5,0x1e,FALSE},
    {L"r.NGX.DLSS.Sharpness",              0x0983af58,0x07e28cd8,GRAPHICS_FLOAT,-1,1,0,FALSE},
    {L"r.AmbientOcclusionLevels",          0x099e1888,0x07e28c20,GRAPHICS_INT,-1,3,0x03,FALSE},
    {L"r.DistanceFieldAO",                 0x099e9918,0x07e28f00,GRAPHICS_INT_REF,0,1,0x03,FALSE},
    {L"r.DistanceFieldAOQuality",          0x099e9938,0x07e28f00,GRAPHICS_INT_REF,0,3,0x0c,FALSE},
    {L"r.VolumetricFog",                   0x09aec738,0x07e28f00,GRAPHICS_INT_REF,0,1,0x03,FALSE},
    {L"r.Kuro.VRS.VolumeCloudQuality",     0x0980e040,0x07e28c20,GRAPHICS_INT,0,3,0x0e,FALSE},
    {L"r.LightShaftQuality",               0x09a32a58,0x07e28f00,GRAPHICS_INT_REF,0,1,0x03,FALSE},
    {L"r.MotionBlur.Amount",               0x09b7a9e8,0x07e28cd8,GRAPHICS_FLOAT,-1,1,0,FALSE},
    {L"r.Kuro.KuroBloomEnable",            0x09a88ac8,0x07e28c20,GRAPHICS_INT,0,1,0x03,FALSE},
    {L"r.Kuro.AutoExposurePlayerCustom",    0x09a88958,0x07e28c20,GRAPHICS_INT,0,1,0x03,FALSE},
    {L"r.Kuro.KuroEnableScreenFilter",     0x09a85708,0x07e28c20,GRAPHICS_INT,0,1,0x03,FALSE},
    {L"r.BlueLightFilter.Disable",          0x09a85738,0x07e28c20,GRAPHICS_INT,0,1,0x03,FALSE},
    {L"r.Tonemapper.BrightnessAndTextureDisable",0x09a95898,0x07e28c20,GRAPHICS_INT,0,1,0x03,FALSE},
    // Experimental: addresses/types observed; rendering behavior is not guaranteed.
    {L"r.Streamline.DLSSG.FramesToGenerate",0x0983bfa8,0x07e28c20,GRAPHICS_INT,0,5,0x3e,FALSE},
    {L"r.Kuro.AutoCoolUIEnable",0x0980df80,0x07e28cd8,GRAPHICS_FLOAT,0,1,0,FALSE},
    {L"sg.ShadowQuality",0x09b79180,0x07e28c20,GRAPHICS_INT,0,4,0,FALSE},
    {L"sg.EffectsQuality",0x09b791c8,0x07e28c20,GRAPHICS_INT,0,4,0,FALSE},
    {L"r.DetailMode",0x098f5980,0x07e28c20,GRAPHICS_INT,0,3,0,FALSE},
    {L"sg.TextureQuality",0x09b791b0,0x07e28c20,GRAPHICS_INT,0,4,0,FALSE},
    {L"r.Kuro.LODDistanceScale",0x09b794a8,0x07e28fb8,GRAPHICS_FLOAT_REF,0,10,0,FALSE},
    {L"foliage.DensityScale",0x099b4b70,0x07e28cd8,GRAPHICS_FLOAT,0,1,0,FALSE},
    {L"r.MarchingCubes.EnablePrePass",0x099e3be0,0x07e28c20,GRAPHICS_INT,0,1,0,FALSE},
    {L"r.FidelityFX.FSR3.Enabled",0x09864558,0x07e28c20,GRAPHICS_INT,0,1,0,FALSE},
    {L"r.FidelityFX.FSR3.QualityMode",0x098647b0,0x07e28c20,GRAPHICS_INT,0,4,0,FALSE},
    {L"r.FidelityFX.FI.Enabled",0x09864960,0x07e28c20,GRAPHICS_INT,0,1,0,TRUE},
    {L"r.FidelityFX.FSR.PrimaryUpscale",0x09b7ab60,0x07e28c20,GRAPHICS_INT,0,1,0,FALSE},
    {L"r.XeSS.Enabled",0x09899f38,0x07e28c20,GRAPHICS_INT,0,1,0,FALSE},
    {L"r.XeSS.Quality",0x09899f50,0x07e28c20,GRAPHICS_INT,0,6,0,FALSE},
    {L"r.XeFG.Enabled",0x0989a540,0x07e28c20,GRAPHICS_INT,0,1,0,TRUE},
    {L"r.XeFG.MaxInterpolatedFrames",0x0989a9e0,0x07e28c20,GRAPHICS_INT,0,3,0x0e,FALSE},
    {L"r.RayTracing.Enable",0x099e3c50,0x07e28e48,GRAPHICS_BOOL_REF,0,1,0,FALSE},
    {L"sg.RayTracingQuality",0x09b79350,0x07e28c20,GRAPHICS_INT,0,4,0,FALSE},
    {L"r.RayTracing.GlobalIllumination",0x09aa9fb8,0x07e28c20,GRAPHICS_INT,-1,3,0,FALSE},
    {L"r.RayTracing.Reflections",0x09aab0d8,0x07e28f00,GRAPHICS_INT_REF,-1,1,0,FALSE},
    {L"r.RayTracing.Shadows",0x09a299d8,0x07e28c20,GRAPHICS_INT,-1,1,0,FALSE},
    {L"r.Gamma",0x09a95868,0x07e28cd8,GRAPHICS_FLOAT,0,5,0,FALSE},
    {L"r.Client.Saturation",0x09a856c8,0x07e28cd8,GRAPHICS_FLOAT,0,2,0,FALSE},
    {L"r.Client.Contrast",0x09a856e0,0x07e28cd8,GRAPHICS_FLOAT,0,2,0,FALSE}
};
// GWorld and the concrete scene classes, used only for read-only FG gating.
static const DWORD graphicsWorldSlotRva=0x09b88da0;
static const DWORD graphicsWorldVtableRva=0x08599480;
static const DWORD graphicsInstanceVtableRva=0x084774b8;
static const DWORD graphicsPlayerVtableRva=0x084b7520;
static const DWORD graphicsControllerVtableRva=0x07689b48;
static const DWORD graphicsPawnVtableRva=0x076856e0;
