# Linux 发布用原生库

`sources.json` 固定 fontconfig、FreeType、HarfBuzz、libpng、zlib-ng、bzip2、Brotli、Expat、SQLite、ICU、OpenSSL、GCC 与 glibc 2.31 开发头文件的来源和 SHA-256。`prepare.py` 将归档缓存于 `artifacts/linux-native/sources/`，构建输出默认位于 `artifacts/linux-native/glibc231/`，从不安装主机库或字体配置。

若原来的 `artifacts/glibc231/` 中全部 481 个已审核文件与 `verified-cache.json` 匹配，可直接复制归档、头文件与 ICU 数据；编译器封装总是按当前工作区重建。没有旧实验目录时，使用这里的源码构建配方。后续复用会校验全部输出哈希与配方指纹；缓存损坏或配方改变时必须选择新的输出目录，避免混用结果。

字体依赖使用独立 `wuwa_fc_` 符号命名空间，避免错误绑定 Skia 内嵌 FreeType。ICU 使用与 Skia 相同的 Zig/libc++；异常展开使用针对 glibc 2.31 头文件构建的 GCC unwinder。运行时 ICU/OpenSSL/SQLite 无系统回退。glibc 和显示/图形栈仍动态使用宿主系统。

`LinuxNative.targets` 仅在正式脚本启用 `WuwaCompactLinux=true` 时生效。RuntimeCompiler 显式依赖 AotAnywhere 的 PointLinkerToZig，随后为 .NET 原生 PAL 编译设置 `zig cc` 封装，最终可执行文件仍交给 AotAnywhere 的链接阶段。

原生库的许可位于 `licenses/linux-native/`；正式重新链接材料同时包含固定源码归档。编译工具与当前实际验收边界见 [RELEASE.md](../../docs/RELEASE.md)。

已实际执行 `python3 build/LinuxNative/prepare.py --from-source --output artifacts/linux-native/glibc231-clean-ar`，不复用旧归档，15 份固定输入生成 481 个校验文件、18 份静态归档；ICU 全部 906 locale 差分与 6,153,051 字节 Brotli 数据验证通过。日志为 `artifacts/linux-native/clean-build-ar.log`，缓存 manifest 记录每项输出哈希。
