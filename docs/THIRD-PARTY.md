# 第三方代码和重新链接材料

本程序使用 Avalonia（MIT）、AtomUI 6.2.7（LGPL v3）、StaticLink.Avalonia、Skia / ANGLE、HarfBuzz、Fontconfig、FreeType、SQLite、ICU、OpenSSL、Brotli、Noto CJK、Inter 等组件。每个还原包的版本、NuGet 内容哈希、上游仓库及许可证文件记录于 `licenses/third-party.json`；静态原生库的源码和构建配方见 `build/LinuxNative/sources.json`。

可选帧率插件来自 [30launchers/WutheringWaves-FPS-unlocker](https://github.com/30launchers/WutheringWaves-FPS-unlocker)，MIT 许可。发布包保留上游版权与完整许可，来源和 SHA-256 位于 `licenses/FPS-PLUGIN-PROVENANCE.md`；对应子项目的源码快照在 `vendor/fps-unlocker/`。

完整许可文本、原生第三方通知和字体许可位于 `licenses/`。字体子集已按 OFL 要求重命名并保留版权信息。界面自定义线形图标由本项目绘制；AtomUI 内部图标的相应通知仍随包提供。

AtomUI 的完整对应源码固定在提交 `4021b4ed7ab49e12fa047fa8ad8cc42870937222`，归档和 SHA-256 见 `licenses/source-archives.json`。正式程序使用未修改的 AtomUI 6.2.7；验证重新链接能力的诊断修改只存在于 `artifacts/`。

公开仓库中的 `vendor/build-inputs/` 包含完整 AtomUI、Avalonia、StaticLink 和后端相关源码归档，以及实际使用的 StaticLink 包和补丁后端包。大于 40 MiB 的原件分块存储，`manifest.json` 记录每块与重组后的 SHA-256；`python3 tools/bootstrap-build-inputs.py` 校验并恢复到构建目录。此路径不需要 GitHub 登录或已过期的 CI 下载权限。

与二进制对应的 Git tag 包含应用代码、字体、原生助手、构建配方、依赖锁、补丁、许可和上述重新链接材料。Linux 原生依赖源码按 `build/LinuxNative/sources.json` 的固定地址和校验和下载。编译器与 Microsoft SDK 是外部构建前提；SDK 不随包分发。完整步骤见 [NativeAOT 发布说明](NATIVEAOT.md)。GitHub 自动生成的 tag Source code 归档也包含这些源码材料；本次 Release 手动上传的运行包仅为 Linux x64 与 Windows x64。

允许为了修改这些 LGPL 组件、重新组合/重新链接本应用及调试相关修改而使用、修改和重建所提供的应用材料；不限制为调试这些修改而进行的逆向工程。各第三方组件继续适用其原有许可。

构建基础设施中保留的 MIT 版权通知见 `licenses/Build-Infrastructure-MIT.txt`。
