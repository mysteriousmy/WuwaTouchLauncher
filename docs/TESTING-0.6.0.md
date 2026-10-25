# 0.6.0 发布验证

验证日期：2026-10-25。测试仅使用隔离的配置、数据库、伪运行器和自建 Win32 子进程；本轮没有启动鸣潮，没有新增实际游戏或 Windows 真机验收。Windows 正式产物在 Wine 中验证。

## 成品与测试范围

| 测试入口 | 用例数 | 结果与范围 |
|---|---:|---|
| `test-product.py` | 61 | 通过；Linux NativeAOT CLI、配置、恢复与内存请求 |
| `test-gui.py` | 1 | 通过；Linux X11 实际呈现的 AtomUI 控件与截图 |
| `test-windows-wine.py` | 18 | 通过；Windows NativeAOT、GUI、发现、快捷方式与会话 |
| `test-memory-wine.py` | 19 | 通过；18 项完整回归，新增的零初值边界用例另行通过 |
| `test-fps-wine.py` | 4 | 通过；包含原始 FPS DLL 在隔离子进程中的加载与原始管道协议 |
| `test-lifecycle.py` | 11 | 通过；会话退出、锁释放、配置恢复 |
| `test-ui-fonts.py` | 1 | 通过；UI 字体子集覆盖中文源码文案 |

GUI 用例内部还检查触摸、鼠标、键盘、三种模式切换、54 项内存选择、资源缺失提示、主题、保存和关闭流程。显示模式与输出分辨率始终没有内存选择框。

## 本次需求与直接证据

以下名称可在仓库中的测试入口直接检索，并可使用 Python unittest 的 `类名.方法名` 参数单独运行。

| 需求 | 直接验证用例 / GUI 检查 |
|---|---|
| 默认混合、默认全部配置，普通模式不应用 | `ProductContracts.test_memory_mixed_defaults_to_file_persists_and_excludes_normal_mode` |
| 逐项选择，切换模式保留勾选 | `ProductContracts.test_memory_mixed_selects_only_checked_items_and_retains_selections`；`memory-radio-exclusive-and-selections-retained` |
| 旧布尔配置迁移，显式新模式优先 | `ProductContracts.test_memory_legacy_boolean_migration_and_explicit_mode_precedence` |
| 54 项都能提出请求或明确报告跳过，沿用不注入 | `ProductContracts.test_all_54_memory_choices_produce_a_request_or_explicit_skip`；`memory-all-54-options-except-display-and-resolution` |
| FSR / XeSS 场景恢复后重新开启插帧 | `MemoryBridgeContracts.test_fsr_and_xess_frame_generation_recover_from_pause_and_engine_reset` |
| 三种插帧的回写恢复状态互不干扰 | `MemoryBridgeContracts.test_frame_generation_algorithms_have_independent_reset_state` |
| 混合模式不与显式 FPS 插件目标冲突 | `ProductContracts.test_mixed_memory_does_not_fight_unchecked_fps_unlock_plugin` |
| 未知映射不伪造成功，也不阻止已知映射 | `MemoryBridgeContracts.test_unavailable_requests_are_reported_without_blocking_known_values` |
| 布尔引用只写单字节，保护相邻内存 | `MemoryBridgeContracts.test_boolean_reference_writes_only_one_byte_per_copy` |
| 质量组、颜色、插帧倍率的类型与零初值边界 | `MemoryBridgeContracts.test_experimental_scalability_colors_and_multipliers_accept_zero_initialization` |
| 保留原始帧率插件及通信行为 | `FpsBridgeContracts.test_original_supplied_plugin_loads_in_isolated_child`；`FpsBridgeContracts.test_original_pipe_protocol_reaches_only_new_child` |

## 构建与打包检查

- Linux x64、Windows x64 均完成正式 NativeAOT 构建，构建器的 AOT / 裁剪 / 互操作警告检查通过。
- 已核对发布二进制使用的源码快照和当前源码一致；UPX 完整性、解压后代码与加载元数据校验通过。
- 从仅含公开文件的干净检出中恢复固定构建材料成功；Win32 助手从源码重新编译与随源码提供的版本字节一致。
- 发布包保留项目 MIT、原 FPS 插件 MIT 和各项第三方许可证；包内 `FILES.sha256` 覆盖实际文件。
- GitHub Release 仅上传 Linux x64、Windows x64 两个运行包。GitHub 自动生成的源码归档另计。

## 证据边界

发布材料整理后，重新生成了后端包，并重新编译 ICU、OpenSSL 和两个平台的正式 NativeAOT 程序。对公开源码及嵌套依赖归档共 13,568 个文件成员进行了引用扫描，同时检查了两个平台压缩前后的可执行文件。

Linux 仍以 glibc 2.31 为目标，最终 ELF 最高导入版本为 GLIBC_2.29。重建后的压缩程序已在 Ubuntu 20.04 / glibc 2.31 容器通过 `python3 test-gui.py` 的完整界面检查；Windows 原生开发配置使用官方图形包的编译检查也通过（0 警告、0 错误）。

这些测试验证配置与内存写入协议，不证明全部渲染效果、硬件能力或实际插帧倍率。54 个可选图形项目中，41 项已有数值映射，13 项会明确跳过并继续采用配置预置。新增映射尤其可能需要引擎回调或资源重建。可用性与不支持列表见 [内存模式说明](MEMORY-GRAPHICS.md)。

既有游戏实验单独记录在 [历史图形实验](GRAPHICS-EXPERIMENT-20261025.md)，不能视为本版本新增功能的游戏验收。云端 CI 状态以 GitHub Actions 的实际运行结果为准，本记录描述本地发布前验证。
