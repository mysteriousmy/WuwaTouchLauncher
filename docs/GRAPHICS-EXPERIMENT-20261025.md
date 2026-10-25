# 2026-10-25 触控图形内存实验逐项记录

> 历史记录：下表是 0.5 阶段的实机验收边界。0.6 已开放更多实验性请求并加入混合模式，当前行为请以 [内存模式说明](MEMORY-GRAPHICS.md) 为准；新入口不改变这些实验事实。

覆盖界面全部 56 项，其中 17 项支持内存应用（帧生成仅关闭/2×），解锁帧率仍使用已有插件。未确认完整映射或运行时效果的项目保留配置文件方式。

实验环境：本机 Linux / DW-Proton / AMD RX 9070 XT，既有 OptiScaler。没有连接 Windows 机器。仅支持文档中列出的游戏散列；这些结论不表示原生 NVIDIA、Intel 或 Windows 真机验收。

判定依据分为实际画面/帧率变化、普通菜单语义标定、触控写入回读。回读成功不代表某个场景一定出现该效果；表中保留这一差别。对自动控制器、资源重建和硬件能力相关项目，没有仅凭回读成功就开放支持。

| 选项键 | 内存应用 | 实验与判定 |
|---|---|---|
| `unlock-fps` | 配置文件 / 既有功能 | 沿用已有帧率插件；同时启用内存模式时帧率目标优先于 fps / vsync，已有功能。 |
| `fps` | 支持 | t.MaxFPS；30/45/不限制实测约 31.8/45/116.8 原生帧（叠加统计有平均延迟）。 |
| `vsync` | 支持 | r.VSync + rhi.SyncInterval；两份线程值回读一致。 |
| `display-mode` | 配置文件 / 既有功能 | r.FullScreenMode 可回读，但只改变偏好，未完成窗口切换；继续配置文件与已有自适应窗口逻辑。 |
| `resolution` | 配置文件 / 既有功能 | r.SetRes 是字符串/命令，未找到已验证的类型化写入与窗口尺寸同步路径。 |
| `render-scale` | 支持 | r.ScreenPercentage；关 DLSS 后 50% / 150% 画面与性能变化。 |
| `smooth-fps` | 配置文件 / 既有功能 | bSmoothFrameRate 为引擎对象设置，注册数值 CVar 目录中没有可靠同名入口。 |
| `fixed-fps` | 配置文件 / 既有功能 | bUseFixedFrameRate 同上，未写入未知引擎对象。 |
| `auto-quality` | 配置文件 / 既有功能 | 菜单映射到 r.Kuro.AutoCoolUIEnable；可写回读，未证明自动降档控制器随之生效。 |
| `shadow` | 配置文件 / 既有功能 | sg.ShadowQuality 原始写入不触发完整级联。菜单高/低切换改变 23 个变量；r.ShadowQuality=0 虽能关闭阴影，但不等价于菜单“低”。 |
| `effects` | 配置文件 / 既有功能 | 尝试 sg.EffectsQuality；菜单 NiagaraQuality 改变没有对应数值 CVar 差异，未确认粒子实例重新配置。 |
| `detail` | 配置文件 / 既有功能 | 尝试 r.DetailMode；菜单只观察到描边距离的一部分变化，不能代表完整细节档位。 |
| `texture` | 配置文件 / 既有功能 | sg.TextureQuality 可写回读，未验证重新设置流送池/资源驻留。 |
| `anti-aliasing` | 支持 | 已有验证：关闭超分后边缘画面对照。 |
| `anisotropy` | 支持 | r.MaxAnisotropy；1×–16×数值目标，实际触控写入/回读。 |
| `view-distance` | 配置文件 / 既有功能 | 尝试 r.Kuro.LODDistanceScale；菜单 LoadingRangeScaleLevel 没有对应数值 CVar 差异，语义未确认。 |
| `npc-density` | 配置文件 / 既有功能 | 正常菜单 NpcDensity 切换成功，4735 个可读数值 CVar 中无已识别映射。 |
| `vegetation` | 配置文件 / 既有功能 | 菜单映射 foliage.DensityScale=.6/.7/.8/1；直接写值未观察到现有草丛重建，0 也未移除草丛。 |
| `vegetation-dither` | 配置文件 / 既有功能 | 正常菜单切换成功，无已识别数值 CVar 映射。 |
| `dynamic-bones` | 配置文件 / 既有功能 | 正常菜单切换成功，无已识别数值 CVar 映射。 |
| `environment-interaction` | 配置文件 / 既有功能 | 菜单映射 r.MarchingCubes.EnablePrePass；可写回读，未验证完整环境交互更新。 |
| `teammate-effects` | 配置文件 / 既有功能 | 正常菜单切换成功，无已识别数值 CVar 映射；没有进行联机场景验证。 |
| `hit-material` | 配置文件 / 既有功能 | 正常菜单切换成功，无已识别数值 CVar 映射。 |
| `damage-effects` | 配置文件 / 既有功能 | 正常菜单 1→0 切换成功，无已识别数值 CVar 映射；误点的 0→1 记录排除。 |
| `lighting` | 支持 | r.Kuro.GlobalLightQuality；1–4 数值目标，场景可能自行选择适用光照效果。 |
| `ambient-occlusion` | 支持 | 普通菜单标定 0/-1 与 DistanceFieldAO、Quality 的完整档位；触控逐档写入回读。 |
| `volumetric-fog` | 支持 | 普通菜单标定 Fog 开关及 VolumeCloudQuality=1/2/3；触控逐档写入回读。 |
| `volumetric-light` | 支持 | 普通菜单标定 r.LightShaftQuality=0/1；触控写入回读。 |
| `bloom` | 支持 | 普通菜单标定 r.Kuro.KuroBloomEnable=0/1；触控写入回读。 |
| `motion-blur` | 支持 | 普通菜单标定 Amount=0/.25/.5/.75；触控逐档写入回读。 |
| `auto-exposure` | 支持 | 普通菜单标定 r.Kuro.AutoExposurePlayerCustom=0/1；触控写入回读。 |
| `dlss` | 支持 | 已有验证：超分开关进入渲染链路。 |
| `dlss-quality` | 支持 | 已有验证：6 档对应渲染输入分辨率变化。 |
| `dlss-sharpness` | 支持 | r.NGX.DLSS.Sharpness；-1–1 浮点写入，效果取决于活动超分实现。 |
| `native-fg` | 支持 | 已有验证：仅关闭/2×，保留世界/角色/时钟门控；3×–6×不注入。 |
| `fsr` | 配置文件 / 既有功能 | 直接试写 r.FidelityFX.FSR3.Enabled，关闭 DLSS 后仍未确认 FSR 渲染输入缩放。 |
| `fsr-quality` | 配置文件 / 既有功能 | 试写 FSR3 QualityMode=3，尚未确认质量档实际进入渲染链路。 |
| `fsr-fg` | 配置文件 / 既有功能 | 试写 r.FidelityFX.FI.Enabled，尚未确认帧生成输出。 |
| `fsr-legacy` | 配置文件 / 既有功能 | 试写 r.FidelityFX.FSR.PrimaryUpscale，尚未确认该路径启用。 |
| `xess` | 配置文件 / 既有功能 | 试写 r.XeSS.Enabled，本机 Supported=0，未改写能力标志。 |
| `xess-quality` | 配置文件 / 既有功能 | 试写 r.XeSS.Quality，本机不支持，未确认档位。 |
| `xess-fg` | 配置文件 / 既有功能 | 试写 r.XeFG.Enabled，本机 Supported=0、可插帧数=0。 |
| `ray-tracing` | 配置文件 / 既有功能 | 总开关为 BOOL_REF，可读取且试写关闭；不能据此确认硬件光追启用。 |
| `ray-quality` | 配置文件 / 既有功能 | 试写 sg.RayTracingQuality，尚未确认完整光追管线。 |
| `ray-gi` | 配置文件 / 既有功能 | 试写 r.RayTracing.GlobalIllumination=3，尚未确认硬件光追。 |
| `ray-reflections` | 配置文件 / 既有功能 | 试写 r.RayTracing.Reflections=1，尚未确认硬件光追。 |
| `ray-shadows` | 配置文件 / 既有功能 | 试写 r.RayTracing.Shadows=0，尚未确认硬件光追。 |
| `brightness` | 配置文件 / 既有功能 | r.Gamma 候选无可靠对应效果；当前游戏滤镜亮度操作改 FilterSettingValues，没有确认旧 Brightness 键的运行时映射。 |
| `saturation` | 配置文件 / 既有功能 | r.Client.Saturation=.64 能改变画面，但当前滤镜菜单改 FilterSettingValues，未确认旧 SaturationNew=32 与其语义等价。 |
| `contrast` | 配置文件 / 既有功能 | r.Client.Contrast=.64 能改变画面，但未确认与 ContrastNew 档位等价。 |
| `image-display` | 支持 | 普通菜单标定全局滤镜/护眼/亮度纹理三个开关，触控三档画面对照。 |
| `eye-mode` | 配置文件 / 既有功能 | 菜单切换强光/弱光/自定义仅观察到数据库变化，没有确认数值 CVar 消费入口。 |
| `eye-temperature` | 配置文件 / 既有功能 | 正常自定义滑杆修改成功，无已识别数值 CVar 差异。 |
| `eye-strength` | 配置文件 / 既有功能 | 正常自定义滑杆修改成功，无已识别数值 CVar 差异。 |
| `eye-brightness` | 配置文件 / 既有功能 | 正常自定义滑杆修改成功，无已识别数值 CVar 差异。 |
| `eye-texture` | 配置文件 / 既有功能 | 正常自定义滑杆修改成功，无已识别数值 CVar 差异。 |

本次完成 71 次可恢复试写，覆盖 50 个不同数值 CVar；每次均检查目标回读并恢复两个原值副本。实验退出后恢复所涉图形数据库项与两个 INI；个人启动配置和 OptiScaler 配置散列保持不变，无关数据库键未被恢复覆盖。

原始证据在项目的 `artifacts/touch-resources-20261025/`：`trials.json`、`normal-applied-calibration.json`、`trial-*.png`、`native-touch-verification.json`。无数据库预期变化的误点记录，以及误改 PcWindowMode 的 `damage0to1`，不计为对应项目标定证据。正常模式中只读取内存；试写只针对实验启动的触控子进程，并逐项恢复。

原始目录包含本机截图和实验状态，发布包只携带本报告，不携带个人数据库或截图。
