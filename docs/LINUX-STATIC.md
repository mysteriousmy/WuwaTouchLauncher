# Linux 静态命令行版

0.5 的 Avalonia 图形版采用 glibc 2.31 目标和原生库静态链接，详见 [当前 GUI 发布方案](NATIVEAOT.md)。本文的 MewUI / DirectPInvoke 实验仅保留为 0.4 历史记录。

`wuwa-touch-cli` 是以 musl 静态链接的 NativeAOT 命令行程序。它与图形版
`wuwa-touch-launcher` 使用同一套配置、参数解析、图形覆盖和退出恢复逻辑。
**此构建不含 GUI**，不带参数显示帮助；`gui` 会明确提示使用图形版。
它是独立的可选下载，不包含在默认 GUI 安装包中。图形版主程序本身也支持命令行，无需依赖这个额外文件。

```sh
./wuwa-touch-cli --help
./wuwa-touch-cli launch --mode touch
./wuwa-touch-cli launch --mode normal
./wuwa-touch-cli preview --mode touch
./wuwa-touch-cli save --mode touch --touch-setting shadow=3
```

默认读取 `${XDG_CONFIG_HOME:-~/.config}/wuwa-touch-launcher/profiles.json`，
也可以用 `--config FILE`。触控助手已内置，需要时释放到本程序状态目录。
`shortcuts` 命令创建指向此静态 CLI 的普通 / 触控快速启动菜单。

“零共享库依赖”指这个 Linux CLI 自身：libc、.NET 原生运行时和 SQLite
均链接进程序，不需要目标系统提供 libc、libstdc++、libsqlite3 或 .NET。
实际运行 Windows 游戏仍需用户安装游戏、Wine / Proton 及其所需的图形驱动。
通知和文件管理器等可选桌面功能也依赖系统对应程序。

## 构建

需要 Docker；构建 Windows 辅助程序还使用主机 Clang、LLD 和 Wine 开发头文件。

```sh
bash build-linux-static.sh
python3 test-static.py
WUWA_TEST_LAUNCHER="$PWD/dist/linux-static-cli/wuwa-touch-cli" python3 test-product.py
bash package-linux-static.sh
```

脚本固定 .NET 10 Alpine SDK 镜像摘要，在独立容器中构建 `linux-musl-x64`，
不会把 musl 或其他开发包安装到宿主机。核心选项：

- `PublishAot=true`、`StaticExecutable=true`、`InvariantGlobalization=true`
- `StaticOpenSslLinking=true`
- `DirectPInvoke`：`libc`、`libsqlite3.so.0`
- `NativeLibrary`：Alpine 的 `/usr/lib/libsqlite3.a`
- `EnableGui=false`：移除 Avalonia / AtomUI 引用及界面代码，保留相同的命令行功能

产物位于 `dist/linux-static-cli/`。调试符号不随发布包分发。

## 验证范围

`readelf` 检查没有 `PT_INTERP` 和 `DT_NEEDED`；此外，`test-static.py`
在 `FROM scratch`、无网络、只读根文件系统、普通用户身份的容器里实际运行：

1. 帮助和参数说明；
2. 保存、读取同一份配置，并生成启动预览；
3. 使用静态测试运行器模拟游戏，验证 SQLite 设置确实在运行时变更、退出后恢复；
4. 无参数和 `gui` 的提示行为。

空容器测试比单看 `ldd` 更严格：没有共享库导入项不代表程序不会在运行时
尝试 `dlopen`。下方 0.4 的旧 GUI 实验展示了这个区别。

## glibc 下单独启用 DirectPInvoke

`DirectPInvoke` 不要求 musl。下面的配置同样可以用于 `linux-x64`（glibc）：

```xml
<ItemGroup>
  <DirectPInvoke Include="libc" />
</ItemGroup>
```

它让匹配的 P/Invoke 生成直接调用；当库仍采用动态链接时，符号由系统动态
加载器绑定。要将原生库静态链接进程序，还需要对应的静态库及链接设置。
单独添加这一项不会消除 libc、X11 或 OpenGL 的共享库依赖。

2026-10-20 在 0.4.0 源码副本上进行了对照构建：两者均为 `linux-x64`，
未启用 `StaticExecutable`，唯一选项差异是 `DirectPInvoke Include="libc"`。
编译器响应文件确认增加了 `--directpinvoke:libc`，ELF 动态符号表新增
`getuid` 和 `setlocale` 的直接导入。两版均能显示完整中文 GUI。

| 项目 | 默认 glibc 构建 | 增加 DirectPInvoke |
|---|---:|---:|
| 可执行文件大小 | 10,275,336 字节 | 10,274,888 字节 |
| 平均 RSS | 189.50 MiB | 189.55 MiB |
| 平均 PSS | 110.29 MiB | 110.44 MiB |

内存数据来自同一私有 Xvfb 显示环境、同一配置和 1040×850 窗口，交替启动
各三次，每次空闲五秒后读取 `/proc/PID/smaps`。软件渲染环境的绝对值不能
与此前真实桌面的测量直接比较；这次对照没有显示出明显的内存收益。
最初一次未完整结束的真实桌面测量已排除。

两版的 `DT_NEEDED` 均为 `libm.so.6`、`libc.so.6` 和
`ld-linux-x86-64.so.2`，仍有 `/lib64/ld-linux-x86-64.so.2` 解释器。
这些只包含启动时依赖；GUI 运行时还会加载图形相关共享库。
因此本次实验验证了 glibc 下直接导入可用，但没有得到零依赖 GUI。
实验未替换已安装版本，也未启动游戏。

原始数据和实验副本保存在
[`artifacts/glibc-direct-20261020/`](../artifacts/glibc-direct-20261020/validation.json)。

## GUI 静态链接实验

```sh
bash build-linux-static.sh --gui-experiment
DISPLAY=:0 ./artifacts/static-gui-experiment/wuwa-touch-launcher gui
```

完整 MewUI 版本可以生成没有 ELF 共享库导入项的静态可执行文件，但当前
MewUI 0.22.1 的 Linux 后端仍通过 P/Invoke 加载 X11、字体库和 OpenGL。
实际运行会在 `libX11.so.6` 报 `Dynamic loading not supported`。
所以该实验产物**不能当作可用的零依赖 GUI 发布**。

要让当前 GUI 彻底静态运行，还需要构建并直接链接图形后端的完整原生依赖链，
处理 OpenGL 驱动加载方式和字体发现；仅增加 `StaticExecutable` 不够。
本版本保留经验证的 X11 / MewVG 图形包，并提供独立可用的静态 CLI。

参考：[hez2010 的静态链接文章](https://www.cnblogs.com/hez2010/p/18813775/dotnet-nativeaot-distroless-statically-linked-app)、
[.NET NativeAOT 原生互操作](https://learn.microsoft.com/dotnet/core/deploying/native-aot/interop)、
[MewUI Linux GL 入口](https://github.com/aprillz/MewUI/blob/v0.22.1/src/MewUI/Shared/Native/Linux/X11/LibGL.cs)。
