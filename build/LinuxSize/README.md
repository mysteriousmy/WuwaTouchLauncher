# Linux 单文件体积优化

入口是 `./scripts/release-linux.sh`（构建并打包）或 `./scripts/publish-compact-linux.sh`（仅构建）。正式发布固定启用裁剪、ICU Brotli 压缩和 UPX 5.2.1 `--best`，不提供静默跳过阶段的开关。隔离构建位于 `artifacts/linux-release/`，最终输出在 `artifacts/publish/linux-x64/`，不会覆盖旧版本归档。准确体积和运行结果见 [BINARY_SIZE.md](../../docs/BINARY_SIZE.md)。

## 前提

- `build/LinuxNative/` 保存原生源码版本/哈希、构建配方和 glibc 2.31 链接配置。脚本首先验证缓存；若所有旧实验归档与固定哈希一致可复用，否则从固定源码构建，不再复制旧实验工程。工具条件见 [RELEASE.md](../../docs/RELEASE.md)。
- 保留项目本地 .NET SDK、锁定 NuGet 包、本地 Avalonia 后端，以及实验所用 Zig 0.17.0 / ICU 78.3 / Brotli 1.2.0。Python 构建环境需要 `brotli==1.2.0`；运行产物不需要 Python。
- 只适用于目前验证过的 Linux x64；其它 RID 的正式打包入口在验证完成前拒绝发布。P0 实验脚本仍独立保留。

## 做法与边界

1. NativeAOT 使用 `OptimizationPreference=Size`，继续运行 AOT/trimming 分析。
2. ICU 删除本应用不调用的单位格式化、转写、拼写数字和原生代码页转换数据。保留全部 locale、排序、日历、时区、显示名称、Unicode 与断词数据。代码页转换仍由 .NET `Encoding.CodePages` 提供。输入数据 SHA-256 固定，版本变化必须重新验证过滤规则。
3. `icu_compare.c` 禁止文件数据回退，在全部 906 个 locale 上对照日期、四类数字格式、显示名称、排序键与搜索结果，包括 ICU 状态码。它是代表性差分验证，不声称覆盖 ICU 的所有 API。
4. Brotli 数据压缩复用已有静态字体链中的同版本解码器。`icu_loader.c` 在 `.NET GlobalizationNative_LoadICU` 前以 `pthread_once` 解压并注册数据，缓冲区由 ICU 持有直到进程结束。大小/解码/注册失败会返回初始化失败，不回退到系统 ICU。未引入运行时文件解包。
5. 固定 Zig 驱动在本次实验中丢弃了可执行文件的 `--version-script`，导致大量原生符号被导出并阻止无用段回收。`zig_link.py` 先让原驱动解析自己的 glibc sysroot，再使用**同一个 Zig 自带的 LLD**重放解析结果，补上 NativeAOT 导出限制和可选包装入口。未改用宿主链接器。临时初次链接不执行；只有最终链接成功才替换目标。

链接封装校验 Zig 版本、唯一导出脚本、解析后的目标文件和延迟对象；不匹配就失败。最终命令保留为原生输出旁的 `.link.json`。由于这是固定版本的命令行适配，升级 Zig/AotAnywhere/.NET 时必须重新核验，不能直接扩大版本条件。符号剥离仍交给 AotAnywhere。

保留全部业务功能、图标、字体、歌词渲染和既有验证入口；没有采用 invariant globalization、整程序集保留或关闭全部警告。压缩版本需要约 23 MB 的常驻解压缓冲区；这是用户选择的发布策略；其它组合只保留历史实验记录。

UPX 固定归档哈希；每次执行 `--best`、`-t`、解压逐字节校验，随后直接运行压缩产物的核心/存储/ICU/TLS/适配器检查。任何阶段失败不产生新发布输出，打包脚本拒绝缺失策略、源码指纹或文件哈希不匹配的产物。
