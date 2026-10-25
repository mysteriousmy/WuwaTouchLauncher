# 可选帧率解锁

界面位置：**触控画质 → 显示与帧率 → 解锁帧率**。可选关闭（默认）、120、240、420 帧。仅用于下次触控启动；普通模式忽略此设置。

```sh
wuwa-touch-launcher launch --mode touch --touch-setting unlock-fps=240
# 保存后由触控快捷方式使用
wuwa-touch-launcher save --touch-setting unlock-fps=240
# 关闭，下一次启动不再加载插件
wuwa-touch-launcher save --touch-setting unlock-fps=keep
```

复现了用户原程序的三部分行为：

1. 临时将 `CustomFrameRate` 设为 `3`、`PcVsync` 设为 `0`。旧版存在 `GameQualitySetting` 时，同时修改其中 `KeyCustomFrameRate` 和 `KeyPcVsync`；新版缺少这个旧项时仍可使用。
2. 创建挂起的游戏子进程，加载原样的 `ww_ulk_plugin_base.dll`，成功后恢复游戏主线程。
3. 核对命名管道所属的游戏 PID，再发送 `0,0,45.0,0,0,<目标帧率>`。

启用后，`t.MaxFPS`、`FrameRateLimit` 和垂直同步配置以解锁目标为准，避免其他保存的上限与之冲突。数据库及 INI 使用恢复日志；正常退出或下次启动时恢复，保留游戏内后来修改的值。插件与游戏进程一起结束，不修改游戏 EXE。切换或关闭鼠标过滤不会卸载帧率插件。

DLL 加载失败会取消此次仍处于挂起状态的新游戏启动并报告 `fps-error`。管道请求超时或服务端 PID 不匹配时显示请求失败，不向其他进程发送目标值。状态中的 `requested` 仅表示字节已发送，不是实际帧率测量。

升级后目录中可能残留不支持 `--fps-target` 的旧 `touch-bridge.exe`，旧助手会在游戏创建前以代码 `2` 退出。启动器现已固定调用内置且按内容哈希提取的助手，避免同目录旧文件覆盖新协议。

这个 DLL 来自用户给出的样本，已校验原始 SHA-256；依赖 `MSVCP140.dll`、`VCRUNTIME140.dll`、`VCRUNTIME140_1.dll` 及 UCRT，应由已有游戏/Proton 环境提供。上游为 [30launchers/WutheringWaves-FPS-unlocker](https://github.com/30launchers/WutheringWaves-FPS-unlocker)，采用 MIT 许可证，版权属于 30launchers。许可随包提供为 `licenses/FPS-Unlocker-MIT.txt`，对应源码快照在 `vendor/fps-unlocker/`。原 DLL 来源和哈希见 `licenses/FPS-PLUGIN-PROVENANCE.md`。

测试使用隔离 Wine 前缀中的自建子进程，验证实际 DLL 加载、原管道协议、关闭路径和失败路径；另用临时 SQLite 数据库验证新旧格式、退出恢复及异常恢复。2026-10-20 在本机 DW-Proton 下临时启用 120 帧后，真实游戏成功进入触控大世界，现场叠加层显示 120.0 FPS。此记录仅覆盖该机器、游戏版本和场景；240/420 帧及其他环境仍未实测，功能保留实验性标记。
