internal static class LauncherWindow
{
    public static int Run(string path, ProductConfig config) =>
        throw new PlatformNotSupportedException(
            "这是静态命令行版。使用 launch 读取 GUI 保存的配置启动游戏；打开配置界面请运行 wuwa-touch-launcher。");

    public static void ShowStartupError(string text) { } // ProductApp already prints the error.
}
