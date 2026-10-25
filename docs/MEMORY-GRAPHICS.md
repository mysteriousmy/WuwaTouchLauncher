# 配置、内存与混合模式

0.6 在“触控画质”顶部提供三个单选项。所有选择只用于下次触控启动；普通模式始终忽略这些内存设置。

| 模式 | 行为 |
|---|---|
| 配置模式 | 使用现有数据库 / INI 临时预置，不添加图形内存维护 |
| 内存模式 | 在配置预置基础上，为所有非“沿用”的可选项目添加实验性内存请求 |
| 混合模式（默认） | 默认全部未勾选，使用配置预置；只为逐项勾选且非“沿用”的项目添加内存请求 |

切换模式不会清空逐项选择。已有 `MemoryInjection: true` 迁移为内存模式；旧的 false 或没有该字段迁移为默认混合模式。显式 `MemoryMode` 优先。读取旧配置不改文件，下一次保存才写入新格式。

显示模式、输出分辨率仍标记“配置文件”，不能勾选内存。其他 **54 项均开放实验性请求入口**，包括更高倍数 DLSS 插帧、FSR、XeSS、阴影、纹理、光追、颜色等。选择“沿用游戏设置”不会写入该项。图形参数在配置中保持预置，便于需要初始化或设置回调的功能正常进入启动流程；内存维护是运行中的补充，不能替代所有引擎设置回调。

界面显示两条提示：

> 内存注入和帧率解锁一样有一定风险，请酌情使用
>
> 不保证所有选项内存注入均可用，请自行实验

## 当前实现范围

54 项中，41 项有明确的数值目标，共使用 49 个 CVar；其余 13 项尚无可靠内存目标，允许勾选，但会在预览中列出并在状态栏计为“跳过”，继续使用配置预置。不会把未执行的写入报告为成功。

目前跳过：平滑帧率、固定帧率、人群密度、植被虚化、飘带动画、联机队友特效、怪物受击材质、受伤表现，以及护眼方案、色温、强度、亮度、纹理强度。未来找到可靠目标后可沿用已经保存的逐项选择。

| 新增实验项 | 请求目标 / 转换 | 限制 |
|---|---|---|
| 自动调节画质 | `r.Kuro.AutoCoolUIEnable` | 开关值不代表自动降档控制器已经工作 |
| 阴影、特效、纹理 | `sg.ShadowQuality`、`sg.EffectsQuality`、`sg.TextureQuality` | 原始写入不等于完整 scalability 回调或资源重建 |
| 画面细节、渲染距离 | `r.DetailMode`、`r.Kuro.LODDistanceScale`（档位+1） | 候选映射，不保证与菜单完整语义等价 |
| 植被密度 | `foliage.DensityScale`：0.6 / 0.7 / 0.8 / 1 | 现有植被实例不一定重建 |
| 环境交互 | `r.MarchingCubes.EnablePrePass` | 不代表完整交互链路更新 |
| FSR3 超分、质量 | `r.FidelityFX.FSR3.Enabled`、`QualityMode`：菜单值映射为 2−值 | 最高一档请求 Native AA；并非保证等同游戏“超级质量” |
| FSR 插帧 / 旧 FSR | `r.FidelityFX.FI.Enabled` / `r.FidelityFX.FSR.PrimaryUpscale` | 仍需渲染后端和资源支持 |
| XeSS 超分、质量 | `r.XeSS.Enabled`、`r.XeSS.Quality`：档位+2 | 不改写 Supported 或硬件能力标志 |
| DLSS / XeSS 多帧 | Enabled 开关 + FramesToGenerate / MaxInterpolatedFrames | 硬件支持、可插帧数和最终输出未经本轮游戏验证 |
| 光追 | 总开关、质量及 GI / 反射 / 阴影数值 | DX12、硬件及完整渲染管线仍是前提 |
| 亮度、饱和度、对比度 | `r.Gamma=1+亮度`；`r.Client.Saturation/Contrast=值÷50` | 候选映射，与当前滤镜曲线不保证等价 |

此前 17 项已完成过不同程度的菜单标定、写入回读或画面对照，包括 DLSS、抗锯齿、2× 帧生成、帧率、垂直同步、渲染比例、各向异性、光照、DLSS 锐化、AO、雾、体积光、运动模糊、Bloom、自动曝光、图像显示模式。完整历史证据见 [56 项实验记录](GRAPHICS-EXPERIMENT-20261025.md)。该记录是 0.5 阶段的验收结论，不代表 0.6 新开放的选项都已通过实机验证。

## 插帧被游戏关闭后的恢复

DLSS、FSR、XeSS 使用各自的场景恢复状态。辅助程序只读检查当前世界、本地玩家、控制器、角色类型和世界计时；登录、加载、暂停或任一检查失败时保持关闭。同一场景计时连续前进后重新开启，通常约 1–2 秒。游戏主动把某一路插帧关闭时，只有该路重新等待，其他算法的状态不被清空。

“插帧设置已恢复”表示开关写入并回读成功，不代表显卡实际生成了额外帧。倍率参数同样只表示请求。启用多个插帧实现可能不兼容，应按显卡和游戏实际支持选择；启动器不伪造能力标志。

## 帧率插件与配置恢复

“解锁帧率”是独立、默认关闭的功能，明确选择 120 / 240 / 420 后，会按原逻辑加载 30launchers 的 MIT 插件，无论顶部使用哪种图形模式。为该项勾选内存注入时，额外维护 `t.MaxFPS` 与关闭垂直同步。它优先于冲突的帧率或 VSync 目标；详见 [FPS 解锁](FPS-UNLOCK.md)。

数据库 / INI 沿用临时写入和退出恢复机制；关闭鼠标过滤只改变输入，不停止内存维护或卸载 FPS 插件。修改应用方式需退出游戏后重新启动。

## 版本与写入校验

当前直接内存图形映射仅对应国服 Windows 3.7 EXE，SHA-256：

```
61746ca0ccfb39326e56d86f08f907b48cd262a3ea00299b3758b4c909177633
```

只写本程序新建的游戏子进程。每次先校验文件散列、模块边界、变量类型、可写数据页和当前值范围，再写两个线程副本并回读。支持整数、布尔、浮点和引用型值；拒绝越界、NaN / Infinity 或改变的引用地址。每 500 ms 检查，只在值变化时重写。不修改代码或页面权限。

版本不匹配时显示跳过，仍可继续启动游戏并应用配置预置。已识别变量的布局校验失败时停止该批内存维护，并尽可能恢复仍匹配的值；不会使用旧偏移强行写入更新后的游戏。图形内存维护的哈希门槛与独立 FPS 插件的内部适配逻辑是两条路径。

0.6 本轮仅进行自动化配置、GUI 和隔离子进程测试，没有新增实际游戏或 Windows 真机验证。历史实际游戏证据来自 Linux + Wine；部分超分测试使用已有 OptiScaler 转换，不能视为原生 NVIDIA 测试。

## 命令行

```sh
# 默认混合模式，仅维护阴影与 FSR 插帧
wuwa-touch-launcher save --memory-mode mixed \
  --memory-setting shadow=true --memory-setting fsr-fg=true \
  --touch-setting shadow=3 --touch-setting fsr-fg=1
wuwa-touch-launcher preview --mode touch
# 全部配置 / 全部内存
wuwa-touch-launcher save --memory-mode file
wuwa-touch-launcher save --memory-mode memory
# 取消一项；切回混合模式后生效
wuwa-touch-launcher save --memory-mode mixed --memory-setting shadow=false
```

旧参数 `--memory-injection true|false` 继续兼容，分别等价于 memory / file。只有 `save` 才保存命令行覆盖；`preview` 和 `launch` 仅影响本次调用。
