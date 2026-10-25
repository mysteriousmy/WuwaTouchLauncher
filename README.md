# 鸣潮启动器 · WuwaTouchLauncher

一个面向 **Windows x64、Linux x64** 的中文桌面启动器，统一管理鸣潮普通模式、云触控模式、画质预置和鼠标过滤。GUI、命令行和两个游戏快捷入口共用配置。

[下载 Release](https://github.com/mysteriousmy/WuwaTouchLauncher/releases/latest) · [图形选项](docs/GRAPHICS-OPTIONS.md) · [内存模式](docs/MEMORY-GRAPHICS.md) · [本地构建](#本地编译) · [MIT 许可证](LICENSE)

本项目不是库洛官方启动器，也不负责下载游戏或资源。请先使用官方渠道完成安装、更新和首次启动，再使用本工具。实现使用 Avalonia 12.1.4、AtomUI 6.2.7、.NET 10 NativeAOT；发布主程序内置字体和触控助手，无需安装 .NET。

## 功能

- 普通 / 触控两套启动配置，分别保存资源档位、DirectX、画质预设和自定义参数。
- 56 项触控图形设置，分类与跨分类搜索；触摸、鼠标、键盘均可操作下拉选择。
- 配置 / 内存 / 混合三种应用方式；默认混合模式、全部使用配置，可逐项勾选内存注入。
- DLSS、FSR、XeSS 插帧在场景恢复后重新应用，分别处理游戏把开关写回关闭的情况。
- 普通 SD、高清 HD、极致 UHD 资源检测；未下载档位禁用并提示使用官方安装对应资源。新配置默认高清 HD。
- 触控模式支持按主屏幕实际分辨率自适应全屏、前台游戏鼠标过滤和紧急切换快捷键。
- 默认关闭的帧率解锁选项：120 / 240 / 420；保留 30launchers 的 MIT 开源插件。
- Windows 自动发现安装位置、原生 `.lnk`、按需提升游戏会话权限；Linux 读取 Lutris 或手动选择 Proton / Wine。
- 设置使用临时覆盖与恢复日志。关闭配置窗口后游戏继续运行；游戏退出时恢复本次修改，保留游戏后来修改的其他值。
- 日间、暗夜、跟随系统主题；Windows 云母 / 亚克力，Linux 原生 Wayland / X11。

## 下载与安装

当前 Release 只提供两个运行包：

| 文件 | 适用平台 |
|---|---|
| `wuwa-launcher-0.6.0-linux-x64.tar.gz` | x86-64 Linux，glibc 2.31 或更高，Wayland / X11 桌面 |
| `wuwa-launcher-0.6.0-win-x64.zip` | Windows 10 / 11 x64 |

每个包包含程序、README、配置示例、许可证、说明文档和 `FILES.sha256`。GitHub 自动生成的 Source code 归档用于源码构建，不能直接双击运行。ARM64 与静态 CLI 暂无本次预编译 Release。

### Windows

1. 下载 `win-x64.zip`，解压到自己方便管理的目录。
2. 双击 `wuwa-touch-launcher.exe`。
3. 自动找到一个游戏安装时会选中它；多个安装时自行选择。也可浏览到 `Client\Binaries\Win64\Client-Win64-Shipping.exe`，或选择对应安装目录 / 官方启动程序。
4. 调整设置并保存，点击“启动游戏”。需要提升权限时系统会弹出 UAC；取消后不会启动游戏或应用临时设置。
5. 在设置页生成“普通模式 / 触控模式”快捷方式，之后可以直接启动游戏而不打开 GUI。

GUI 本身不要求管理员权限。游戏及可选帧率插件所需的运行库由已有游戏环境提供；FPS DLL 依赖 Microsoft VC++ x64 运行库。Windows 页面不会显示 Wine、Proton 或 UMU 配置。

### Linux

安装包中的程序可直接运行；也可安装到当前用户目录：

```sh
mkdir -p wuwa-launcher
# 在下载目录执行，或替换压缩包路径
tar -xzf wuwa-launcher-0.6.0-linux-x64.tar.gz -C wuwa-launcher
cd wuwa-launcher
./wuwa-touch-launcher
# 可选：添加程序菜单、命令行入口及普通 / 触控快捷入口
bash install.sh
```

安装目标为 `~/.local/lib/wuwa-touch-launcher/`，命令链接为 `~/.local/bin/wuwa-touch-launcher`。无需 sudo，升级重新运行安装脚本即可；已有配置保留。未加入 PATH 时可通过应用菜单打开。

首次打开自动读取本机鸣潮的 Lutris 条目，扫描 Steam、Lutris、Heroic、UMU 和 PATH 中的运行器。Lutris 模式读取原路径、参数、运行器、环境变量和前置命令，不改写原条目；复杂安装脚本和 pre/post-launch 脚本不由本工具执行。

手动模式需选择游戏 EXE、Proton / Wine 和对应前缀。Wine / UMU 前缀应包含 `drive_c`；直接 Proton 使用包含 `pfx` 的兼容数据目录。请先确保同一游戏能在该运行器中正常启动。本程序不会安装运行器或创建完整游戏环境。

Linux 默认优先 Wayland，必要时可使用 `WUWA_BACKEND=x11 ./wuwa-touch-launcher`。界面固定软件渲染；系统仍需 glibc、显示服务、桌面库及驱动。Skia、字体处理、SQLite、ICU、OpenSSL 等主要原生依赖已静态链接，但 GUI 并非零系统库依赖的 ELF。

## 首次使用

1. **启动游戏页**：选普通 / 触控模式、游戏安装和已下载的资源档位。普通模式新配置默认高清 HD；旧配置保留原选择。
2. **触控画质页**：默认各项“沿用游戏设置”。仅调整需要覆盖的项目；不确定时先使用配置方式。
3. **输入设置**：默认 `Ctrl+Alt+F10` 切换鼠标过滤。只过滤目标游戏在前台时的鼠标消息，保留键盘和触摸；关闭过滤不关闭游戏。
4. **保存并启动**：GUI 修改保存后供下次使用。仅触控选项不会作用于普通模式。

未安装的资源档位显示“（未下载此资源，请使用官方安装对应资源）”。检测基于当前目录的非空资源包，不替代官方完整性校验，也不会自动下载资源。

“自适应全屏”在首页触控启动选项中，默认关闭。启用时每次读取主屏幕实际像素分辨率，覆盖手动显示模式和输出尺寸，退出后恢复。Windows 不按 DPI 缩放尺寸；Linux GUI 通过当前显示后端读取，无界面快捷启动需要可用的 X11 / XWayland `DISPLAY` 与 `xrandr`。无法读取时会明确报错。

## 配置、内存与混合模式

| 顶部单选项 | 应用方式 |
|---|---|
| 配置模式 | 只使用图形数据库 / INI 预置 |
| 内存模式 | 配置预置加所有可选项目的运行时内存请求 |
| **混合模式（默认）** | 默认全部未勾选；只为逐项勾选的项目补充内存请求 |

切换模式保留逐项勾选状态。“沿用游戏设置”不会注入；显示模式与输出分辨率始终只支持配置。其余 **54 项开放实验性内存请求**。当前 41 项已有数值映射，13 项尚无可靠目标，会在预览和状态栏明确报告跳过，继续采用配置预置；详细名单与映射边界见 [内存模式说明](docs/MEMORY-GRAPHICS.md)。

> 内存注入和帧率解锁一样有一定风险，请酌情使用
>
> 不保证所有选项内存注入均可用，请自行实验

DLL 插件“解锁帧率”是独立、默认关闭的功能；明确选定目标后按原逻辑加载插件。为该项勾选内存时，再补充上限与 VSync 维护。配置模式不加载图形内存维护器，但不会取消显式选择的 FPS 插件。

DLSS、FSR、XeSS 插帧各自跟踪场景状态；登录、加载和暂停时关闭，角色和世界计时恢复后重新开启。被游戏写回关闭时会再次等待。该逻辑不伪造 GPU 能力，也不保证显卡实际生成额外帧或实现所选倍率。

图形内存写入目前仅适配 SHA-256 匹配的国服 Windows 3.7 游戏文件。更新后的游戏、其他服或不匹配布局会跳过 / 停止内存维护，仍可使用配置预置。普通模式不使用触控内存设置。0.6 新开放的选项和 FSR / XeSS 恢复逻辑本轮只进行了自动化测试，**没有新增游戏实测或 Windows 真机验收**。

## 注意事项

- 触控云模式对画质、帧率和渲染功能有自身限制。请求 120 / 240 / 不限不等于实际帧率达到目标；最高预设也可能回退。需要按自己的硬件与场景观察。
- 写入成功、回读一致和完整渲染效果是不同层面的结果。阴影 / 纹理组等原始值可能不触发全部引擎回调或资源重建。护眼、滤镜和光追尤其不能仅凭参数判断效果。
- DLSS、FSR3、XeSS 不能同时请求启用多种超分。光追需要 DX12、硬件与场景支持；插帧和多帧生成同样受后端限制。
- 内存注入及 FPS DLL 为实验功能，可能不兼容游戏规则或更新版本；自行决定是否使用。本工具不提供规避检测的功能。
- 临时覆盖使用恢复日志，并尽量保留游戏后来改动的值。不要在游戏运行时让多个工具同时改同一配置；建议自行保留配置备份。异常退出后，下次启动会处理未完成的恢复。
- 关闭 GUI 不关闭游戏。`stop` / 鼠标快捷键只关闭过滤，不会停用已经加载的 FPS 插件或图形内存维护；修改这些选项需重新启动游戏。
- 不安装、不更新、不修改 OptiScaler。已有第三方组件自行管理。历史部分超分实验使用已有 OptiScaler，不能视为原生 NVIDIA 验收。
- FPS 插件保留原作者 [30launchers 的 MIT 许可](https://github.com/30launchers/WutheringWaves-FPS-unlocker/blob/5aa7ecb0fb659b04deedbce43102240c0b4d17bd/LICENSE)。详细来源、哈希和历史测试范围见 [FPS 解锁说明](docs/FPS-UNLOCK.md)。

## 命令行与配置文件

不带参数 / `gui` 打开界面；`launch` 直接启动；`preview` 或 `launch --dry-run` 仅展示启动计划，不修改游戏或启动进程。命令行覆盖只影响本次调用，`save` 才写入配置。

```sh
wuwa-touch-launcher launch --mode normal
wuwa-touch-launcher launch --mode touch --adaptive-fullscreen true
wuwa-touch-launcher preview --mode touch --memory-mode mixed \
  --memory-setting shadow=true --touch-setting shadow=3
wuwa-touch-launcher save --memory-mode mixed \
  --memory-setting fsr-fg=true --touch-setting fsr-fg=1
wuwa-touch-launcher save --memory-mode file
wuwa-touch-launcher launch --mode touch --touch-setting unlock-fps=240
wuwa-touch-launcher doctor
wuwa-touch-launcher shortcuts
wuwa-touch-launcher status
wuwa-touch-launcher toggle
wuwa-touch-launcher stop
wuwa-touch-launcher --help
```

Windows PowerShell 中，在程序目录运行时把命令名写为 `./wuwa-touch-launcher.exe`；续行可写在一行或使用 PowerShell 的反引号。

| 内容 | Linux | Windows |
|---|---|---|
| 配置 | `${XDG_CONFIG_HOME:-~/.config}/wuwa-touch-launcher/profiles.json` | `%LOCALAPPDATA%\WuwaTouchLauncher\profiles.json` |
| 状态 / 助手缓存 | `${XDG_STATE_HOME:-~/.local/state}/wuwa-touch-launcher/` | `%LOCALAPPDATA%\WuwaTouchLauncher\state\` |

`--config FILE` 指定独立配置。保存采用原子替换，上次保存留为 `.bak`。旧 `MemoryInjection: true` 迁移到内存模式；false / 缺省迁移到混合模式且默认未勾选。旧命令 `--memory-injection true|false` 仍映射为 memory / file。配置格式版本保持为 2。

更多内容：[全部图形键值](docs/GRAPHICS-OPTIONS.md)、[窗口外观](docs/APPEARANCE.md)、[帧率插件](docs/FPS-UNLOCK.md)、[历史图形实验](docs/GRAPHICS-EXPERIMENT-20261025.md)。

## 本地编译

正式版本固定 .NET SDK **10.0.402**、依赖锁及原生源码 / 工具哈希。推荐在 Linux x64 上构建两个正式单文件目标。源码仓库附带完整 AtomUI 等重新链接材料和固定的 StaticLink 包，体积较大；无需 Git LFS，也无需为这些材料登录 GitHub。

### Linux x64 正式版

以下依赖安装命令以 Ubuntu 24.04 为例，其他发行版请安装等价工具。需要 Python 3.11+、Clang / LLVM / LLD、CMake、Ninja、C/C++ 工具链、autotools 和 Wine 开发头文件。首次原生依赖构建会联网下载固定源码并占用较多时间与磁盘。

```sh
git clone --depth 1 --branch v0.6.0 https://github.com/mysteriousmy/WuwaTouchLauncher.git
cd WuwaTouchLauncher
sudo apt-get update
sudo apt-get install -y clang lld llvm cmake ninja-build build-essential pkg-config \
  autoconf automake libtool patch meson gperf bison flex texinfo libgmp-dev libmpfr-dev libmpc-dev \
  curl git python3-venv libwine-dev zlib1g-dev libglib2.0-bin
python3 -m venv .tools/python
.tools/python/bin/pip install Brotli==1.2.0
bash tools/install-dotnet.sh
export PATH="$PWD/.tools/python/bin:$PWD/.tools/dotnet:$PATH"
python3 tools/bootstrap-build-inputs.py
bash build.sh
```

产物：`dist/wuwa-touch-launcher`。`bash build.sh --native-only` 只编译并更新嵌入的 Win32 助手。源码自带字体子集；修改中文 UI 文案后用 `tools/build-ui-fonts.py` 重建字体，详见 `assets/fonts/README.md`。

### 在 Linux 上交叉构建 Windows x64 正式版

额外安装 PowerShell Core、LLVM Windows 工具，并用 **xwin 0.10.0** 按固定清单准备 Microsoft SDK / CRT。SDK 许可由 xwin 展示，SDK 本身不在仓库中。已有兼容布局也可使用。详细版本与命令见 [Windows 交叉构建说明](build/WindowsNative/README.md)。

```sh
# 使用已安装的 xwin 0.10.0
xwin --manifest build/WindowsNative/vs-channel.json \
  --cache-dir artifacts/p0/xwin-cache --arch x86_64 \
  splat --output artifacts/p0/windows-sdk
bash build-windows-cross.sh
```

产物：`dist/win-x64/wuwa-touch-launcher.exe`，脚本同时制作 ZIP。正式程序采用 NativeAOT、静态图形链接与 UPX 5.2.1；构建脚本核验 AOT 警告、系统依赖和压缩还原结果。

### Windows 原生开发构建

安装 Visual Studio 2022 C++ 桌面开发工作负载、Windows SDK 和 .NET SDK 10.0.402，在 PowerShell 执行 `./build-windows.ps1`。该路径输出 `dist/win-native-dev/`，使用旁置图形 DLL，是开发包；正式单文件 Release 使用上面的交叉构建流程。Windows 原生脚本没有在本轮真机执行。

### 验证、打包与重新链接

```sh
# 以正式 NativeAOT 产物运行；单文件测试不能用普通托管 apphost 替代
python3 test-product.py
python3 test-gui.py       # 需要 Xvfb、xdotool / 桌面测试依赖
python3 test-ui-fonts.py  # 需要 FontTools
# 原生助手的测试使用自建隔离子进程，不启动游戏，需要 Wine、pefile、Xvfb
WUWA_TEST_WINE=/path/to/wine xvfb-run -a python3 test-memory-wine.py
# Windows 成品回归使用独立 Wine 前缀
WUWA_TEST_WINE=/path/to/wine xvfb-run -a python3 test-windows-wine.py
python3 tools/package-release.py linux-x64
python3 tools/package-release.py win-x64
```

完整构建边界、依赖审计和修改 AtomUI 后重新链接的方法见 [NativeAOT 文档](docs/NATIVEAOT.md)。本版测试范围与需求对应关系见 [0.6.0 发布验证](docs/TESTING-0.6.0.md)。编译材料按 tag 固定，构建日志和本机配置不会提交到公开仓库。源码还保留静态 CLI 开发构建脚本，但本次不发布其二进制。

## 常见问题

- **资源档位灰色**：先在官方启动器下载对应 SD / HD / UHD 资源，再重新检测 / 打开本工具。
- **没有发现游戏或运行器**：手动浏览路径；Lutris 模式需要安装它指定的运行器，程序不会悄悄换成另一套环境。
- **游戏更新后不注入**：这是版本 / 布局校验的结果。先使用配置模式，等待映射更新，不应直接修改哈希或关闭检查。
- **已应用但画面没变**：回读数值不证明完整渲染链路生效，某些参数需初始化、资源重建或特定硬件。参阅实验说明。
- **启动器显示未运行 / 启动失败**：用 `doctor` 查看环境检查，用 `preview` 检查命令；分享输出前删去个人路径及环境变量中的私密值。
- **只想恢复原游戏行为**：将触控图形项设为沿用、帧率解锁关闭，并切回普通模式。等待当前游戏退出后再启动。

## 许可证与致谢

本项目自身代码采用 **MIT**，见 [LICENSE](LICENSE)。第三方材料继续使用各自许可证，并非统一改为 MIT。

- [Avalonia](https://github.com/AvaloniaUI/Avalonia)：MIT。
- [AtomUI](https://github.com/AtomUI/AtomUI)：LGPL v3，完整对应源码、许可及重新链接路径随仓库提供。
- [StaticLink.Avalonia](https://github.com/greepar/StaticLink.Avalonia)、[AotAnywhere](https://github.com/StuDevLabs/AotAnywhere)、[YamlDotNet](https://github.com/aaubry/YamlDotNet)：各自上游开源许可。
- [WutheringWaves-FPS-unlocker](https://github.com/30launchers/WutheringWaves-FPS-unlocker)：MIT，Copyright (c) 2026 30launchers。
- Inter、重命名的 Noto CJK 字体子集：OFL 1.1。

全部依赖、精确版本、许可文本和源归档哈希见 [`licenses/`](licenses) 与 [第三方说明](docs/THIRD-PARTY.md)。允许修改 LGPL 组件、重新链接本应用及调试相关修改；不限制为调试这些修改而进行的逆向工程。图形菜单键值同时参考 [3.7 菜单数据](https://github.com/Arikatsu/WutheringWaves_Data/blob/3.7/BinData/menu/menuconfig.json)。
