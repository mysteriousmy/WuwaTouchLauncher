# 单文件 NativeAOT 发布与源码重建

界面使用 Avalonia 12.1.4、AtomUI 6.2.7，.NET SDK 固定为 10.0.402。发布编译采用 .NET 10.0.13 NativeAOT、裁剪和 Size 优化，最后使用 UPX 5.2.1 `--best`。依赖锁分别保存在 `src/packages.linux-x64.lock.json` 和 `src/packages.win-x64.lock.json`。

主程序内置界面、字体和 Win32 触控助手。单独复制一个可执行文件即可打开界面、配置及启动游戏；首次生成启动计划时，助手释放到本程序状态目录的 `runtime/<内容哈希>/touch-bridge.exe`。损坏的释放副本会重新生成。始终使用主程序内置的匹配版本；同目录残留的旧 `touch-bridge.exe` 不会被调用。开发时修改助手后，应重新构建并嵌入主程序。

可选帧率功能内置用户原程序中的 DLL，默认不提取、不加载。启用时将其释放到同一内容哈希缓存，并加载到新建的游戏进程中。此插件本身仍依赖游戏环境的 VC 运行库；它不属于静态链接到启动器的图形库。来源、哈希与边界见 [帧率解锁说明](FPS-UNLOCK.md)。

## Linux 边界

Linux 目标为 `x86_64-linux-gnu.2.31`。Skia / ANGLE、HarfBuzz、Fontconfig、FreeType、SQLite、ICU、OpenSSL 及相关原生支持库静态链接，ICU 保留全部 locale 的主要全球化数据并以 Brotli 嵌入。字体符号使用参考实现的隔离命名，避免与宿主图形驱动中另一份字体库发生符号冲突。

ELF 审计允许的 `DT_NEEDED` 只有：

```
libc.so.6 libm.so.6 libpthread.so.0 libdl.so.2 librt.so.1 ld-linux-x86-64.so.2
```

本次发布的最高导入版本为 **GLIBC_2.29**，低于 2.31 目标。原生 Wayland 已在本机 niri 中测试，X11 在独立 Xvfb 中测试；压缩后的同一程序也在 **Ubuntu 20.04 / glibc 2.31** 容器运行了完整界面检查。

Wayland / X11、D-Bus/文件选择器等桌面服务仍由系统提供，部分通过运行时加载。启动器固定使用 Skia 软件渲染，不建立应用 OpenGL/EGL GPU 上下文；桌面合成器仍可使用 GPU。该图形程序不承诺在没有桌面库的 scratch 系统运行。源码可另行构建 musl 完全静态 CLI；当前公开 Release 不附该包。

默认优先 Wayland；需要指定 X11 后端时可运行：

```sh
WUWA_BACKEND=x11 ./wuwa-touch-launcher
```

Linux 固定使用软件渲染。Windows 使用 `AngleEgl` 优先、`Software` 回退，以及 Avalonia 默认顺序的 WinUI Composition / DirectComposition / RedirectionSurface。ANGLE 随 StaticLink 静态链接，无须旁置图形 DLL。云母和亚克力由 Avalonia 管理，应用自定义的 DWM P/Invoke 背景适配已移除。

默认 GUI 包只带一个主程序，命令行入口也在其中。musl CLI 可从源码构建，不随本次 GUI Release 提供。

## Windows 边界

Windows 正式发布使用 AotAnywhere 1.1.0 生成 AOT 对象，以 LLVM `lld-link` 和固定 Microsoft SDK / CRT 链接 MSVC ABI 静态图形库。导入表仅允许 Windows 系统 DLL，不需要 .NET 或另装 VC 运行库。使用系统 ICU/NLS，不嵌入 Linux 的 ICU、OpenSSL 或字体库归档。

Windows SDK / CRT 版本和归档哈希位于 `build/WindowsNative/sources.json`，SDK 库内容在构建前核验。SDK 本身不随程序或源码包分发。Windows 程序已在独立 Wine 前缀验证；真机 UI 回归要求检查 GPU 上下文、Avalonia 材质、标题栏和字体；中文错误管道另有针对性检查。Wine 的 ICU 导出不完整，测试进程设置 `DOTNET_SYSTEM_GLOBALIZATION_USENLS=1`；真实 Windows 发布程序没有这个强制设置。

## 重建正式发布

从对应 Release tag 的仓库源码开始。`vendor/build-inputs` 保存完整 AtomUI / Avalonia 等源码和当前实际使用的 StaticLink 包、补丁后端包，避免依赖可能过期的上游 CI 产物；清单记录每个原件及分块的 SHA-256。首次运行 `python3 tools/bootstrap-build-inputs.py` 自动恢复材料。Linux 原生依赖由构建脚本按固定 URL 与 SHA-256 下载。

Linux 构建工具需要 Clang/LLVM、LLD、CMake、Ninja、Make、autoconf/automake/libtool、pkg-config、patch、Python 3 和 Brotli 1.2.0；触控助手需要 Wine 的 Windows 头文件和导入库。先安装 SDK 并准备固定包：

```sh
bash tools/install-dotnet.sh
python3 tools/bootstrap-build-inputs.py
python3 tools/prepare-static-feed.py
# 源码包已包含固定后端 nupkg。若缺失，可从固定源码及补丁重建：
python3 tools/prepare-linux-backends.py
bash build.sh
```

`tools/publish-gui.py` 先进行锁定还原以取得 Zig，再核验或重建原生库、准备 ICU，复制隔离源代码后发布。它不会写入参考工程，也不会用 Windows 还原覆盖 Linux 的中间文件。

交叉构建 Windows 还需要 PowerShell Core，以及用 xwin 0.10.0 准备的固定 SDK 布局，详见 `build/WindowsNative/README.md`：

```sh
bash build-windows-cross.sh
```

正式主程序位于 `artifacts/publish/<RID>/` 和 `dist/`。未压缩程序、符号、binlog、还原锁、完整编译命令、源文件哈希及依赖审计保存在 `artifacts/publish-builds/`。

`build-windows.ps1` 是另一条 Windows 原生开发构建路径：使用官方动态图形包，输出 `dist/win-native-dev/`，不声称生成正式静态单文件发布版。这条脚本已做语法检查，未在 Windows 主机执行。

## 压缩、测试和源码重组

Linux UPX 解压后必须与原文件逐字节一致。PE 格式的 UPX 会重建导入、资源和调试目录，因此 Windows 验证节布局、映射字节、导入地址、全部资源内容和加载元数据；没有跳过整块 `.rdata`。

```sh
python3 test-product.py
python3 test-gui.py
WUWA_TEST_UI_BACKEND=wayland python3 test-gui.py
WUWA_TEST_WINE=/path/to/wine xvfb-run -a -s '-screen 0 1600x1000x24' python3 test-windows-wine.py
python3 tools/package-release.py linux-x64
python3 tools/package-release.py win-x64
```

为了修改并重新链接 LGPL 控件库，解开 `artifacts/relink-sources/AtomUI-*.tar.gz`，修改其中源码，再指定一个独立输出目录：

```sh
python3 tools/publish-gui.py linux-x64 \
  --atomui-source /absolute/path/to/modified/AtomUI \
  --output artifacts/my-modified-build
```

Windows 对应使用 `win-x64`，同时满足其 SDK 前提。程序会复制修改后的库及应用源码到隔离构建目录，不覆盖修改源。已实际加入一条 AtomUI 诊断输出、重新编译静态 AOT 程序，并通过真实 GUI 测试确认修改生效。此诊断程序与正式发布版分开存放。
