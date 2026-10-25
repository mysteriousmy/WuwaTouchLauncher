# 帧率插件来源

`ww_ulk_plugin_base.dll` 来自用户提供并要求复用功能的 `鸣潮触控启动.exe`，本地提取后未修改。

- 原 EXE SHA-256：`244d76167242bfc704f2feb123baf36961628f04b4e964222785bcf3693eaa31`
- 资源名：`WwRes.ww_ulk_plugin_base.dll`
- DLL 大小：34304 字节
- DLL SHA-256：`ee48443ed86f65260364ecd36b65bd5a6bb2e4c5dd074e26983d5259847c630b`
- 命名管道：`55984705-F24C-45C2-B2B7-27F047B43A56`
- 消息：`0,0,45.0,0,0,<FPS>`，其中 FPS 为 120、240 或 420。

上游项目：<https://github.com/30launchers/WutheringWaves-FPS-unlocker>。

2026-10-25 根据用户提供的上游地址核对，项目采用 MIT 许可证，版权所有者为 `Copyright (c) 2026 30launchers`。完整许可见 [FPS-Unlocker-MIT.txt](FPS-Unlocker-MIT.txt)。

- 固定源码提交：`5aa7ecb0fb659b04deedbce43102240c0b4d17bd`。
- 对应子项目：`ww_plugin_base`；源码快照保存在仓库 `vendor/fps-unlocker/ww_plugin_base`。
- 上游源码的命名管道和 FPS 请求格式与现有插件协议一致。发布时仍使用上述本地提取的原样 DLL；未声称它与该提交重编译结果逐字节一致。
- 帧率插件保留自己的 MIT 版权声明；启动器自身代码另见根目录 LICENSE。

默认关闭，仅在触控模式明确选择解锁目标后提取并加载。辅助程序只向自己新建的游戏子进程加载该 DLL；发送请求时核对管道服务端 PID。加载失败时终止尚未恢复主线程的自建子进程并报告失败，不接管已经运行的游戏。请求成功发送不等于已测得实际帧率。
