# Linux → Windows 单文件 NativeAOT

本目录提供 MSVC ABI 链接检查和 UPX PE 验证，入口为 `tools/publish-gui.py win-x64`。AotAnywhere 1.1.0 负责跨平台 ILC，LLVM `lld-link` 使用 Microsoft SDK/CRT 和 StaticLink 的 Windows 原生归档；不混入 ELF 或 Linux 链接参数。

需要 LLVM（clang、lld-link、llvm-ar、llvm-rc、llvm-readobj）、PowerShell Core、Python 3，以及 .NET SDK 10.0.402。固定 SDK 布局由 xwin 0.10.0 生成：

```sh
/path/to/xwin --manifest build/WindowsNative/vs-channel.json \
  --cache-dir artifacts/p0/xwin-cache --arch x86_64 \
  splat --output artifacts/p0/windows-sdk
```

首次获取 SDK 时由 xwin 展示 Microsoft 许可条款。固定清单对应 Visual Studio 17.14.42、SDK 10.0.26100、CRT 14.44.17.14；清单与全部 SDK 库的内容哈希在 `sources.json`。SDK 是构建前提，不随应用或源码包分发。已有匹配布局也可通过 `--sdk-root /path/to/sdk` 使用。

```sh
bash build.sh --native-only
python3 tools/prepare-static-feed.py
python3 tools/publish-gui.py win-x64
```

输出为 `artifacts/publish/win-x64/wuwa-touch-launcher.exe`；`build-windows-cross.sh` 另负责复制到 `dist/win-x64/` 并制作正式 ZIP。隔离源码、未压缩程序、PDB、binlog、命令、COFF 输入哈希及 PE 导入记录保存在 `artifacts/publish-builds/win-x64-*/`。

`link.py` 拒绝 Linux/Unix 库参数、非 AMD64 COFF 输入和无法解析的库。`static-longjmp.c` 为静态 PNG 对象提供指向真实 MSVC `longjmp` 的导入地址槽；它不替代函数实现。`/nodefaultlib:vcruntime.lib` 避免额外的 VC 可再发行 DLL 依赖。

UPX 5.2.1 的 PE 重建不是逐字节还原。`upx.py` 验证解压后的节布局、除已解析元数据/填充外的全部字节、导入与 IAT 映射、全部资源内容和 CET 元数据。原始 EXE 和 PDB 保留以便调试。

Windows 成品的 CLI、注册表与商店安装发现、SQLite 恢复、快捷方式和 GUI 已通过 Wine 测试。Wine 使用单独前缀和 NLS 测试设置；真实 Windows 验收尚未执行。相关限制和命令见 `docs/NATIVEAOT.md`。
