# 触控模式图形选项

56 项设置。`keep` 表示沿用游戏原值，帧率解锁的 `keep` 表示关闭；通过 `--touch-setting 键=值` 使用，与 GUI 保存的值相同。

首页触控模式的“自适应全屏”独立于下面 56 项设置，默认关闭。开启后，每次启动读取主屏幕的实际像素尺寸，临时设置独占全屏和匹配的输出分辨率；“显示模式”“输出分辨率”两项暂停手动编辑，保存的手动值仍保留。CLI 使用 `--adaptive-fullscreen true|false`。Windows 读取当前显示模式，不受 DPI 缩放影响；Linux GUI 读取显示后端的屏幕信息，无界面快捷启动通过 X11 / XWayland 的 `xrandr` 查询。无法读取屏幕时会报错，不猜测分辨率。

光追配置仅表示发送请求，不保证云模式已经实际渲染硬件光追。完整滤镜曲线在游戏内编辑。

图形应用方式提供配置、内存、混合三个单选模式，默认混合且全部未勾选。除显示模式和输出分辨率外，54 项均可勾选实验性内存请求；其中尚无可靠目标的 13 项会明确报告跳过。DLSS / FSR / XeSS 插帧各自等待场景恢复并重新应用。参见 [完整范围和限制](MEMORY-GRAPHICS.md)。

| 选项 | CLI 键 | 可选值 |
|---|---|---|
| 帧率上限（t.MaxFPS） | `fps` | keep ／ 30 ／ 45 ／ 60 ／ 90 ／ 120 ／ 144 ／ 165 ／ 240 ／ 0 |
| 解锁帧率（实验性插件） | `unlock-fps` | keep（关闭） ／ 120 ／ 240 ／ 420 |
| 垂直同步（r.VSync / PcVsync） | `vsync` | keep ／ 1 ／ 0 |
| 显示模式（FullscreenMode） | `display-mode` | keep ／ 1 ／ 2 ／ 0 |
| 输出分辨率（ResolutionSizeX / Y） | `resolution` | keep ／ 1280x720 ／ 1920x1080 ／ 1920x1200 ／ 2376x1080 ／ 2560x1440 ／ 2560x1600 ／ 3440x1440 ／ 3840x2160 |
| 渲染比例（r.ScreenPercentage） | `render-scale` | keep ／ 50 ／ 67 ／ 75 ／ 85 ／ 100 ／ 125 ／ 150 ／ 200 |
| 平滑帧率（bSmoothFrameRate） | `smooth-fps` | keep ／ 1 ／ 0 |
| 固定帧率（bUseFixedFrameRate） | `fixed-fps` | keep ／ 1 ／ 0 |
| 自动调节画质（AutoAdjustImageQuality） | `auto-quality` | keep ／ 1 ／ 0 |
| 阴影质量（ShadowQuality） | `shadow` | keep ／ 0 ／ 1 ／ 2 ／ 3 |
| 特效质量（NiagaraQuality） | `effects` | keep ／ 0 ／ 1 ／ 2 |
| 画面细节（ImageDetail） | `detail` | keep ／ 0 ／ 1 ／ 2 ／ 3 |
| 纹理质量（sg.TextureQuality） | `texture` | keep ／ 0 ／ 1 ／ 2 ／ 3 ／ 4 |
| 抗锯齿（AntiAliasing） | `anti-aliasing` | keep ／ 1 ／ 0 |
| 各向异性过滤（r.MaxAnisotropy） | `anisotropy` | keep ／ 1 ／ 2 ／ 4 ／ 8 ／ 16 |
| 渲染距离（LoadingRangeScaleLevel） | `view-distance` | keep ／ 0 ／ 1 ／ 2 |
| 人群密度（NpcDensity） | `npc-density` | keep ／ 0 ／ 1 ／ 2 |
| 植被密度（VegetationDensity） | `vegetation` | keep ／ 0 ／ 1 ／ 2 ／ 3 |
| 植被虚化（VegetationDither） | `vegetation-dither` | keep ／ 1 ／ 0 |
| 飘带动画（DynamicBones） | `dynamic-bones` | keep ／ 1 ／ 0 |
| 环境交互（WaterInteract） | `environment-interaction` | keep ／ 1 ／ 0 |
| 联机队友特效（TeammateFx） | `teammate-effects` | keep ／ 1 ／ 0 |
| 怪物受击材质效果（EnemyHitDisplayMode） | `hit-material` | keep ／ 1 ／ 0 |
| 受伤表现（SkinDamageMode） | `damage-effects` | keep ／ 1 ／ 0 |
| 全局光照等级（r.Kuro.GlobalLightQuality） | `lighting` | keep ／ 1 ／ 2 ／ 3 ／ 4 |
| 环境遮蔽 AO（SceneAo） | `ambient-occlusion` | keep ／ 0 ／ 1 ／ 2 ／ 3 |
| 体积雾（VolumeFog） | `volumetric-fog` | keep ／ 0 ／ 1 ／ 2 ／ 3 |
| 体积光（VolumeLight） | `volumetric-light` | keep ／ 1 ／ 0 |
| 泛光 Bloom（BloomEnable） | `bloom` | keep ／ 1 ／ 0 |
| 运动模糊（MotionBlur） | `motion-blur` | keep ／ 0 ／ 1 ／ 2 ／ 3 |
| 自动曝光（AutoExposure） | `auto-exposure` | keep ／ 1 ／ 0 |
| NVIDIA DLSS（r.NGX.DLSS.Enable） | `dlss` | keep ／ 1 ／ 0 |
| DLSS 超分辨率质量（r.NGX.DLSS.Quality） | `dlss-quality` | keep ／ 99 ／ -2 ／ -1 ／ 0 ／ 1 ／ 3 |
| DLSS 锐化（NvidiaSuperSamplingSharpness） | `dlss-sharpness` | keep ／ -1 ／ -0.9 ／ -0.8 ／ -0.7 ／ -0.6 ／ -0.5 ／ -0.4 ／ -0.3 ／ -0.2 ／ -0.1 ／ 0 ／ 0.1 ／ 0.2 ／ 0.3 ／ 0.4 ／ 0.5 ／ 0.6 ／ 0.7 ／ 0.8 ／ 0.9 ／ 1 |
| DLSS 原生插帧（r.Streamline.DLSSG.Enable） | `native-fg` | keep ／ 0 ／ 1 ／ 2 ／ 3 ／ 4 ／ 5 |
| AMD FSR3 超分辨率（Fsr3Enable） | `fsr` | keep ／ 1 ／ 0 |
| FSR3 超分辨率质量（Fsr3Quality） | `fsr-quality` | keep ／ -2 ／ -1 ／ 0 ／ 1 ／ 2 |
| FSR3 原生插帧（Fsr3Fg） | `fsr-fg` | keep ／ 1 ／ 0 |
| AMD FSR 画面锐化（FsrEnable） | `fsr-legacy` | keep ／ 1 ／ 0 |
| Intel XeSS 超分辨率（Xess2Enable） | `xess` | keep ／ 1 ／ 0 |
| XeSS 超分辨率质量（Xess2Quality） | `xess-quality` | keep ／ -2 ／ -1 ／ 0 ／ 1 ／ 2 ／ 3 ／ 4 |
| XeSS 原生插帧（Xess2Fg） | `xess-fg` | keep ／ 0 ／ 1 ／ 2 ／ 3 |
| 光线追踪总开关（r.RayTracing.Enable） | `ray-tracing` | keep ／ 0 ／ 1 |
| 光追质量（sg.RayTracingQuality / RayTracing） | `ray-quality` | keep ／ 0 ／ 1 ／ 2 ／ 3 |
| 光追全局光照（RayTracedGI） | `ray-gi` | keep ／ 1 ／ 0 |
| 光追反射（RayTracedReflection） | `ray-reflections` | keep ／ 1 ／ 0 |
| 光追阴影（RayTracedShadow） | `ray-shadows` | keep ／ 1 ／ 0 |
| 亮度（Brightness） | `brightness` | keep ／ -0.8 ／ -0.7 ／ -0.6 ／ -0.5 ／ -0.4 ／ -0.3 ／ -0.2 ／ -0.1 ／ 0 ／ 0.1 ／ 0.2 ／ 0.3 ／ 0.4 ／ 0.5 ／ 0.6 ／ 0.7 ／ 0.8 |
| 饱和度（SaturationNew） | `saturation` | keep ／ 32 ／ 40 ／ 45 ／ 50 ／ 55 ／ 60 ／ 68 |
| 对比度（ContrastNew） | `contrast` | keep ／ 32 ／ 40 ／ 45 ／ 50 ／ 55 ／ 60 ／ 68 |
| 图像显示模式（ImageDisplayMode） | `image-display` | keep ／ 0 ／ 1 ／ 2 |
| 护眼模式方案（EyeProtectionMode） | `eye-mode` | keep ／ 0 ／ 1 ／ 2 |
| 护眼色温（EyeProtectionTemp） | `eye-temperature` | keep ／ 3300 ／ 3500 ／ 4000 ／ 4500 ／ 5000 ／ 5500 ／ 6000 ／ 6500 |
| 护眼强度（EyeProtectionStrength） | `eye-strength` | keep ／ 0 ／ 0.1 ／ 0.2 ／ 0.3 ／ 0.4 ／ 0.5 ／ 0.6 ／ 0.7 ／ 0.8 |
| 护眼亮度（EyeProtectionBrightness） | `eye-brightness` | keep ／ 0 ／ 0.1 ／ 0.2 ／ 0.3 ／ 0.4 ／ 0.5 ／ 0.6 ／ 0.7 ／ 0.8 ／ 0.9 ／ 1 |
| 护眼纹理强度（EyeProtectionTexture） | `eye-texture` | keep ／ 0 ／ 0.1 ／ 0.2 ／ 0.3 ／ 0.4 ／ 0.5 ／ 0.6 ／ 0.7 ／ 0.8 ／ 0.9 ／ 1 |
