# 窗口外观

在“设置 → 窗口外观”中修改，即时预览，点击“保存配置”后持久化。取消关闭提示会保留当前编辑；不保存退出则下次沿用已保存的值。旧版配置没有 `Appearance` 时自动使用默认值，不改动原文件。

| 设置 | 行为 |
|---|---|
| 随系统变化 | 初次打开读取系统主题，并响应后续系统颜色变化 |
| 日间 / 暗夜 | 固定亮色或深色；系统改变主题不覆盖手动选择 |
| 背景不透明度 | 30–100%，默认 85%；只影响背景，文字与控件保持不透明 |
| 亚克力 | 仅 Windows 显示；使用系统材质时由 Windows 管理透明度 |
| 云母（Mica） | 仅 Windows 显示；Windows 11 的默认材质；选择后禁用透明度滑杆并保留原值 |
| 无特效 | 不请求背景模糊或云母；仍可调整普通透明背景的不透明度 |

Windows 默认在 Windows 11 选择云母、Windows 10 选择亚克力。图形后端优先使用 ANGLE / Direct3D 11，窗口材质通过 Avalonia 的 `TransparencyLevelHint` 请求，由 `ActualTransparencyLevel` 反映框架选择的材质。应用不再通过 P/Invoke 设置 DWM 背景或扩展窗框。云母与亚克力生效时内容底色透明，滑杆暂时禁用，选择“无特效”后恢复原值。显卡或系统不支持时按框架能力回退；系统关闭透明效果或使用省电模式仍可能影响视觉效果。Linux 不显示 Windows 特效菜单，普通透明背景由桌面合成器提供。

Mica 是系统绘制的壁纸材质，本身不是透视后方窗口的透明背景，其透明程度不由应用滑杆控制。[Microsoft Mica 说明](https://learn.microsoft.com/en-us/windows/apps/design/style/mica)。

主窗口、启动预览和确认弹窗共用 `AppearanceWindow`。Windows 标题栏通过 AtomUI 的 `NotifyConfigureTitleBar` 使用与内容区相同的背景画刷；窗口本身保持透明，外层内容不再覆盖一层不透明背景。浅色和深色使用同步的 AtomUI 主题与自定义调色板，保留当前页、焦点和未保存的编辑。

Linux 的 AtomUI 客户端窗框（CSD）还有一个 `PART_WindowFrame` 底层。应用样式将它设为透明，避免它用不透明主题色盖住滑杆控制的背景；标题栏和内容区仍各自使用同一个带透明度的颜色。Linux 界面固定使用软件渲染；Windows 启用 GPU 加速，并保留软件回退。

Windows 优先使用 Segoe UI 与 Microsoft YaHei UI / Microsoft YaHei 字体，并在透明窗口中使用灰度抗锯齿以避免彩色字边。其他平台继续使用原有 Inter 与内嵌中文字体；内嵌字体已补齐当前界面文字。

配置文件中对应：

```json
{
  "Appearance": {
    "Theme": "system",
    "BackgroundOpacity": 85,
    "WindowsEffect": "auto"
  }
}
```

`Theme` 可为 `system` / `light` / `dark`；`WindowsEffect` 可为 `auto` / `acrylic` / `mica` / `none`。`auto` 在当前机器解析默认材质，因此携带配置换系统后仍采用该系统的默认选择。窗口外观与普通 / 触控游戏设置独立；完全静态 CLI 也能读取并保留该部分。

## Windows 内存

沿用参考工程的原生 `K32EmptyWorkingSet` API，在启动初始化完成后及窗口最小化时延迟 500 毫秒合并请求。调用只针对启动器自身的当前进程伪句柄，无需关闭句柄；窗口关闭会停止计时器。没有定时清理循环，不修改工作集上限，也不操作游戏进程。

该 API 移除工作集中的可回收驻留页，降低的指标是物理驻留内存；它不释放应用仍持有的对象，也不保证降低私有提交内存。后续操作重新访问这些页时，驻留内存可能增长。[Microsoft EmptyWorkingSet 说明](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-emptyworkingset)。

窗口回归检查使用实际 NativeAOT 程序，覆盖绘图上下文、主题切换、背景透明度、标题栏一致性、材质及滑杆恢复、保存配置和内存 API 生命周期。Windows 还检查普通、最大化、还原时三颗标题栏按钮不重叠，以及中文与拉丁字符的字体匹配。原生 Windows 测试要求实际 GPU 上下文，Wine 可验证软件回退；结果不替代其他 Windows 版本、系统透明效果关闭或特殊显示环境的实测。
