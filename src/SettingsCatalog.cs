using System.Globalization;

internal sealed record SettingOption(string Value, string Label, bool IsEnabled = true);
internal sealed record TouchOption(string Key, string Label, string Help, SettingOption[] Choices,
    string Group, string? DatabaseKey = null);

internal static class SettingsCatalog
{
    public static readonly SettingOption Keep = new("keep", "沿用游戏设置");
    public static readonly SettingOption[] OnOff = [Keep, new("1", "开启"), new("0", "关闭")];
    private static readonly SettingOption[] LowHigh = Levels("低", "中", "高");
    private static readonly SettingOption[] LowUltra = Levels("低", "中", "高", "极高");
    private static readonly SettingOption[] OffHigh = Levels("关闭", "低", "中", "高");
    private const string Rendering = "画质与场景";
    private const string Lighting = "光照与后处理";
    private const string Display = "显示与帧率";
    private const string Upscaling = "超分辨率与插帧";
    private const string RayTracing = "光线追踪";
    private const string Colors = "颜色与护眼";
    private const string RtHelp = "需要 DX12、兼容显卡及场景支持。云触控模式尚未证实实际硬件光追渲染；这里只配置请求值。";
    private const string UpscaleHelp = "使用游戏自身功能，受显卡、驱动和云模式限制；本程序不安装或配置 OptiScaler。";

    public static readonly TouchOption[] TouchOptions =
    [
        new("unlock-fps", "解锁帧率（实验性）", "选定解锁目标后加载 30launchers 的 MIT 开源帧率插件，接管上限并关闭垂直同步；勾选内存注入可补充维护。默认关闭，实际效果取决于游戏版本。",
            [new("keep", "关闭（默认）"), new("120", "解锁至 120 帧"), new("240", "解锁至 240 帧"), new("420", "解锁至 420 帧")], Display),
        new("fps", "帧率上限（t.MaxFPS）", "启用内存注入后可直接调整运行时上限；实际帧率受硬件与场景影响。仅文件配置时云模式仍可能限制为 60 原生帧。",
            [Keep, new("30", "30 帧"), new("45", "45 帧"), new("60", "60 帧"), new("90", "90 帧"), new("120", "120 帧"),
             new("144", "144 帧"), new("165", "165 帧"), new("240", "240 帧"), new("0", "不额外限制")], Display),
        new("vsync", "垂直同步（r.VSync / PcVsync）", "同时设置游戏菜单、引擎垂直同步、同步间隔与窗口配置。", OnOff, Display, "PcVsync"),
        new("display-mode", "显示模式（FullscreenMode）", "窗口模式适合手动调整窗口大小；实际行为受桌面合成器影响。",
            [Keep, new("1", "无边框全屏"), new("2", "窗口"), new("0", "独占全屏（兼容性取决于游戏）")], Display),
        new("resolution", "输出分辨率（ResolutionSizeX / Y）", "选择与显示器匹配的分辨率；无边框模式可能使用桌面分辨率。",
            [Keep, new("1280x720", "1280 × 720"), new("1920x1080", "1920 × 1080"), new("1920x1200", "1920 × 1200"),
             new("2376x1080", "2376 × 1080"), new("2560x1440", "2560 × 1440"), new("2560x1600", "2560 × 1600"),
             new("3440x1440", "3440 × 1440"), new("3840x2160", "3840 × 2160")], Display),
        new("render-scale", "渲染比例（r.ScreenPercentage）", "100% 为原生渲染比例；超分辨率开启后可能接管内部渲染比例。",
            Numbers(50, 67, 75, 85, 100, 125, 150, 200), Display),
        new("smooth-fps", "平滑帧率（bSmoothFrameRate）", "关闭可避免引擎额外平滑限帧。", OnOff, Display),
        new("fixed-fps", "固定帧率（bUseFixedFrameRate）", "不会解除云模式内部限帧。", OnOff, Display),
        Db("auto-quality", "自动调节画质", "AutoAdjustImageQuality", OnOff, Rendering, "开启后游戏可根据性能压力自动降低画质或帧率。"),
        Db("shadow", "阴影质量", "ShadowQuality", LowUltra, Rendering),
        Db("effects", "特效质量", "NiagaraQuality", LowHigh, Rendering),
        Db("detail", "画面细节", "ImageDetail", LowUltra, Rendering),
        new("texture", "纹理质量（sg.TextureQuality）", "引擎纹理档位；不会下载或替代 HD / UHD 资源。",
            [Keep, new("0", "低"), new("1", "中"), new("2", "高"), new("3", "极高"), new("4", "游戏扩展档 4")], Rendering),
        Db("anti-aliasing", "抗锯齿", "AntiAliasing", OnOff, Rendering, "超分辨率开启时可能由超分算法接管。"),
        new("anisotropy", "各向异性过滤（r.MaxAnisotropy）", "改善地面、墙面等斜视角纹理的清晰度。",
            [Keep, new("1", "1×"), new("2", "2×"), new("4", "4×"), new("8", "8×"), new("16", "16×")], Rendering),
        Db("view-distance", "渲染距离", "LoadingRangeScaleLevel",
            [Keep, new("0", "当前预设默认"), new("1", "远"), new("2", "最远")], Rendering),
        Db("npc-density", "人群密度", "NpcDensity", LowHigh, Rendering),
        Db("vegetation", "植被密度", "VegetationDensity", LowUltra, Rendering),
        Db("vegetation-dither", "植被虚化", "VegetationDither", OnOff, Rendering, "摄像机进入植被内部时的半透明效果。"),
        Db("dynamic-bones", "飘带动画", "DynamicBones", OnOff, Rendering),
        Db("environment-interaction", "环境交互", "WaterInteract", OnOff, Rendering, "环境对攻击、技能等行为的反馈，效果也受画质档位影响。"),
        Db("teammate-effects", "联机队友特效", "TeammateFx", OnOff, Rendering),
        Db("hit-material", "怪物受击材质效果", "EnemyHitDisplayMode", OnOff, Rendering),
        Db("damage-effects", "受伤表现", "SkinDamageMode", OnOff, Rendering),
        new("lighting", "全局光照等级（r.Kuro.GlobalLightQuality）", "游戏会按场景与性能区间调整；请求等级不代表所有场景都采用该等级。",
            [Keep, new("1", "低"), new("2", "中"), new("3", "高"), new("4", "最高（可能回退）")], Lighting),
        Db("ambient-occlusion", "环境遮蔽 AO", "SceneAo", OffHigh, Lighting),
        Db("volumetric-fog", "体积雾", "VolumeFog", OffHigh, Lighting),
        Db("volumetric-light", "体积光", "VolumeLight", OnOff, Lighting),
        Db("bloom", "泛光 Bloom", "BloomEnable", OnOff, Lighting),
        Db("motion-blur", "运动模糊", "MotionBlur", OffHigh, Lighting),
        Db("auto-exposure", "自动曝光", "AutoExposure", OnOff, Lighting),
        new("dlss", "NVIDIA DLSS（r.NGX.DLSS.Enable）", UpscaleHelp, OnOff, Upscaling, "NvidiaSuperSamplingEnable"),
        new("dlss-quality", "DLSS 超分辨率质量（r.NGX.DLSS.Quality）", "自动、超级性能、性能、平衡、质量或 DLAA。需要 DLSS 生效。",
            [Keep, new("99", "自动"), new("-2", "超级性能"), new("-1", "性能"), new("0", "平衡"), new("1", "质量"), new("3", "DLAA")], Upscaling, "NvidiaSuperSamplingQuality"),
        Db("dlss-sharpness", "DLSS 锐化", "NvidiaSuperSamplingSharpness", DecimalSteps(-1, 1, 0.1m), Upscaling),
        new("native-fg", "DLSS 原生插帧（r.Streamline.DLSSG.Enable）", "仅配置游戏原生插帧。多帧生成需要对应显卡与游戏版本；云模式可能不支持。",
            [Keep, new("0", "关闭"), new("1", "开启 / 2×"), new("2", "3×"), new("3", "4×"), new("4", "5×"), new("5", "6×")], Upscaling, "NvidiaSuperSamplingFrameGenerate"),
        Db("fsr", "AMD FSR3 超分辨率", "Fsr3Enable", OnOff, Upscaling, UpscaleHelp),
        Db("fsr-quality", "FSR3 超分辨率质量", "Fsr3Quality",
            [Keep, new("-2", "超级性能"), new("-1", "性能"), new("0", "平衡"), new("1", "质量"), new("2", "超级质量")], Upscaling),
        Db("fsr-fg", "FSR3 原生插帧", "Fsr3Fg", OnOff, Upscaling, UpscaleHelp),
        Db("fsr-legacy", "AMD FSR 画面锐化", "FsrEnable", OnOff, Upscaling, "旧版空间超分 / 锐化开关，与 FSR3 为不同设置。"),
        Db("xess", "Intel XeSS 超分辨率", "Xess2Enable", OnOff, Upscaling, "当前版本界面称 XeSS3，数据库仍使用 Xess2 前缀。" + UpscaleHelp),
        Db("xess-quality", "XeSS 超分辨率质量", "Xess2Quality",
            [Keep, new("-2", "超级性能"), new("-1", "性能"), new("0", "平衡"), new("1", "质量"), new("2", "超级质量"),
             new("3", "超级质量+"), new("4", "超级质量+抗锯齿")], Upscaling),
        Db("xess-fg", "XeSS 原生插帧", "Xess2Fg",
            [Keep, new("0", "关闭"), new("1", "2×"), new("2", "3×"), new("3", "4×")], Upscaling, UpscaleHelp),
        new("ray-tracing", "光线追踪总开关（r.RayTracing.Enable）", "总开关启用光追模块；还需选择非零质量和所需子效果。" + RtHelp,
            [Keep, new("0", "关闭"), new("1", "请求开启（未证实）")], RayTracing),
        new("ray-quality", "光追质量（sg.RayTracingQuality / RayTracing）", RtHelp,
            [Keep, new("0", "关闭"), new("1", "低"), new("2", "中"), new("3", "高")], RayTracing, "RayTracing"),
        Db("ray-gi", "光追全局光照", "RayTracedGI", OnOff, RayTracing, RtHelp),
        Db("ray-reflections", "光追反射", "RayTracedReflection", OnOff, RayTracing, RtHelp),
        Db("ray-shadows", "光追阴影", "RayTracedShadow", OnOff, RayTracing, RtHelp),
        Db("brightness", "亮度", "Brightness", DecimalSteps(-0.8m, 0.8m, 0.1m), Colors, "游戏存储的亮度 / Gamma 偏移，0 为默认。"),
        Db("saturation", "饱和度", "SaturationNew", Numbers(32, 40, 45, 50, 55, 60, 68), Colors, "50 为默认。"),
        Db("contrast", "对比度", "ContrastNew", Numbers(32, 40, 45, 50, 55, 60, 68), Colors, "50 为默认。"),
        Db("image-display", "图像显示模式", "ImageDisplayMode",
            [Keep, new("0", "默认"), new("1", "全局滤镜"), new("2", "护眼模式")], Colors, "全局滤镜使用游戏中已经配置的滤镜方案；滤镜曲线在游戏内调整。"),
        Db("eye-mode", "护眼模式方案", "EyeProtectionMode",
            [Keep, new("0", "强光"), new("1", "弱光"), new("2", "自定义")], Colors),
        Db("eye-temperature", "护眼色温", "EyeProtectionTemp", Numbers(3300, 3500, 4000, 4500, 5000, 5500, 6000, 6500), Colors),
        Db("eye-strength", "护眼强度", "EyeProtectionStrength", DecimalSteps(0, 0.8m, 0.1m), Colors),
        Db("eye-brightness", "护眼亮度", "EyeProtectionBrightness", DecimalSteps(0, 1, 0.1m), Colors),
        Db("eye-texture", "护眼纹理强度", "EyeProtectionTexture", DecimalSteps(0, 1, 0.1m), Colors)
    ];

    private static TouchOption Db(string key, string label, string databaseKey, SettingOption[] choices,
        string group, string help = "使用游戏对应的图形设置；实际效果受游戏版本、场景及硬件支持影响。") =>
        new(key, $"{label}（{databaseKey}）", help, choices, group, databaseKey);
    private static SettingOption[] Levels(params string[] labels) =>
        [Keep, .. labels.Select((label, index) => new SettingOption(index.ToString(CultureInfo.InvariantCulture), label))];
    private static SettingOption[] Numbers(params int[] values) =>
        [Keep, .. values.Select(x => new SettingOption(x.ToString(CultureInfo.InvariantCulture), x.ToString(CultureInfo.InvariantCulture)))];
    private static SettingOption[] DecimalSteps(decimal from, decimal to, decimal step)
    {
        var values = new List<SettingOption> { Keep };
        for (decimal value = from; value <= to; value += step)
        {
            string text = value.ToString("0.#", CultureInfo.InvariantCulture);
            values.Add(new(text, text));
        }
        return values.ToArray();
    }

    public static void Validate(ProductConfig c)
    {
        Choice(c.Mode, ["normal", "touch"], "启动模式");
        Choice(c.TouchOnly.MemoryMode, ["file", "memory", "mixed"], "图形应用模式");
        foreach (string key in c.TouchOnly.MemoryOptions.Keys)
            if (!MemoryGraphicsCatalog.Supports(key))
                throw new ArgumentException($"该选项不能勾选内存注入：{key}");
        Choice(c.Appearance.Theme, ["system", "light", "dark"], "界面主题");
        Choice(c.Appearance.WindowsEffect, ["auto", "acrylic", "mica", "none"], "窗口特效");
        if (c.Appearance.BackgroundOpacity is < 30 or > 100)
            throw new ArgumentException("窗口背景不透明度必须在 30–100% 之间。");
        Choice(c.Linux.Integration, ["auto", "lutris", "manual"], "Linux 启动方式");
        Choice(c.Linux.RunnerKind, ["auto", "proton", "wine"], "运行器类型");
        foreach (var p in new[] { c.Normal, c.Touch })
        {
            Choice(p.Resources, ["keep", "sd", "hd", "uhd"], "资源画质");
            Choice(p.DirectX, ["auto", "dx11", "dx12"], "DirectX");
            Choice(p.Quality, ["keep", "1", "2", "3", "4"], "画质预设");
            CommandLine.Split(p.CustomArguments);
        }
        CommandLine.Split(c.Linux.Wrapper);
        foreach (var pair in c.Linux.Environment) CommandLine.ValidateEnvironment(pair.Key, pair.Value);
        foreach (var pair in c.TouchOnly.Values)
        {
            var option = TouchOptions.FirstOrDefault(x => x.Key == pair.Key)
                ?? throw new ArgumentException($"未知触控选项：{pair.Key}");
            Choice(pair.Value, option.Choices.Select(x => x.Value), option.Label);
        }
        if (new[] { "dlss", "fsr", "xess" }.Count(x => c.TouchOnly.Values.GetValueOrDefault(x) == "1") > 1)
            throw new ArgumentException("DLSS、FSR3 和 XeSS 不能同时请求开启，请只选择一种超分辨率。");
        if (c.TouchOnly.Values.GetValueOrDefault("ray-tracing") == "0" &&
            new[] { "ray-quality", "ray-gi", "ray-reflections", "ray-shadows" }
                .Any(x => c.TouchOnly.Values.GetValueOrDefault(x, "keep") is not ("keep" or "0")))
            throw new ArgumentException("光追总开关已关闭，请将光追质量和子效果设为关闭或沿用。");
        if (c.TouchOnly.Values.GetValueOrDefault("ray-quality") == "0" &&
            new[] { "ray-tracing", "ray-gi", "ray-reflections", "ray-shadows" }
                .Any(x => c.TouchOnly.Values.GetValueOrDefault(x) == "1"))
            throw new ArgumentException("光追质量为关闭，请先选择低、中或高，再开启光追子效果。");
        Hotkey.Parse(c.TouchOnly.Hotkey);
    }

    private static void Choice(string value, IEnumerable<string> choices, string label)
    {
        if (!choices.Contains(value)) throw new ArgumentException($"{label}的值无效：{value}");
    }
}
