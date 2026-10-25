using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static partial class ProductApp
{
    public static int Run(string[] args)
    {
        string command = args.FirstOrDefault() ?? "gui";
        bool gui = args.Length == 0 || args[0] == "gui";
        string? sessionResult = null;
        if (OperatingSystem.IsWindows() && !gui) AttachParentConsole();
        try
        {
#if HEADLESS
            if (args.Length == 0) { PrintHelp(); return 0; }
#endif
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("此版本支持 Windows 和 Linux。");
            if (args.Contains("--help") || args.Contains("-h") || command == "help") { PrintHelp(); return 0; }
            var list = args.Length == 0 ? new List<string>() : args.Skip(1).ToList();
            string configPath = AppPaths.DefaultConfig;
            int configIndex = list.IndexOf("--config");
            if (configIndex >= 0)
            {
                if (configIndex + 1 >= list.Count) throw new ArgumentException("--config 需要文件路径。");
                configPath = Path.GetFullPath(AppPaths.Expand(list[configIndex + 1]));
                list.RemoveRange(configIndex, 2);
            }
            int resultIndex = list.IndexOf("--session-result");
            if (resultIndex >= 0)
            {
                if (!OperatingSystem.IsWindows() || command != "launch" || resultIndex + 1 >= list.Count)
                    throw new ArgumentException("启动结果参数仅用于 Windows 游戏会话。");
                sessionResult = WindowsElevation.ResultPath(list[resultIndex + 1]);
                list.RemoveRange(resultIndex, 2);
            }
            if (command == "wrap") return Launcher.Run(args); // Original Lutris wrapper contract.
            if (command == "legacy") return Launcher.Run(args[1..]);
            if (command is "status" or "stop" or "toggle")
            {
                if (list.Any(x => x != "--notify")) throw new ArgumentException("此命令不接受配置覆盖参数。");
                return command switch { "status" => Launcher.ShowStatus(), "stop" => Launcher.ReleaseMouse(), _ => Launcher.ToggleMouse() };
            }
            if (command == "scan")
            {
                if (list.Count != 0) throw new ArgumentException("scan 不接受配置覆盖参数。");
                Console.WriteLine(JsonSerializer.Serialize(Discovery.Scan(), ProductJson.Default.DiscoveryResult));
                return 0;
            }
            ProductConfig config = ConfigStore.Load(configPath);
#if !HEADLESS
            if (command == "ui-smoke")
            {
                if (list.Count != 2 || list[0] != "--output")
                    throw new ArgumentException("ui-smoke 需要 --output <目录>；测试只修改该目录中的配置副本。");
                string output = Path.GetFullPath(list[1]);
                Directory.CreateDirectory(output);
                string copy = Path.Combine(output, "profiles-test.json");
                if (string.Equals(copy, configPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new ArgumentException("测试输出不能覆盖输入配置，请选择其他目录。");
                ConfigStore.Save(copy, config);
                return LauncherWindow.Run(copy, config, output);
            }
#endif
            var options = ParseOverrides(config, list.ToArray());
            if (gui) return LauncherWindow.Run(configPath, config);
            switch (command)
            {
                case "config":
                    Console.WriteLine(JsonSerializer.Serialize(config, ProductJson.Default.ProductConfig)); return 0;
                case "save":
                    ConfigStore.Save(configPath, config);
                    Console.WriteLine($"已保存：{configPath}"); return 0;
                case "shortcuts":
                    // Install/upgrade refreshes shortcuts without asking to edit
                    // an existing profile. Persist only explicit overrides or a
                    // new profile, otherwise preserve its original bytes.
                    if (list.Count != 0 || !File.Exists(configPath)) ConfigStore.Save(configPath, config);
                    foreach (string path in Shortcuts.CreateBoth(configPath, options.ShortcutDirectory)) Console.WriteLine(path);
                    return 0;
                case "launch": case "preview": case "doctor":
                    var plan = PlanBuilder.Build(config, desktopResolution: options.DesktopResolution);
                    if (options.DryRun || command == "preview")
                    {
                        Console.WriteLine(JsonSerializer.Serialize(plan, ProductJson.Default.LaunchPlan));
                        return 0;
                    }
                    PlanBuilder.ValidateFiles(plan);
                    if (command == "doctor")
                    {
                        Console.WriteLine($"检查通过：{plan.Source}\n游戏：{plan.GameExecutable}\n模式：{plan.Mode}");
                        foreach (string note in plan.Notes) Console.WriteLine(note);
                        return 0;
                    }
                    // Elevate the session before any temporary game settings are changed.
                    // The configuration UI itself remains an ordinary desktop process.
                    return WindowsElevation.IsRequired
                        ? WindowsElevation.Run(configPath, list)
                        : Launcher.LaunchProduct(plan, options.Notify);
                default: throw new ArgumentException($"未知命令：{command}。使用 --help 查看帮助。");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException
            or InvalidOperationException or System.ComponentModel.Win32Exception or DllNotFoundException
            or EntryPointNotFoundException or PlatformNotSupportedException or ExternalException)
        {
            WindowsElevation.WriteResult(sessionResult, ex.Message);
            Console.Error.WriteLine($"鸣潮启动器：{ex.Message}");
            try
            {
                Directory.CreateDirectory(AppPaths.StateDirectory);
                File.AppendAllText(Path.Combine(AppPaths.StateDirectory, "launcher.log"), $"{DateTimeOffset.Now:O} {ex.Message}\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Launcher.ReportError(ex.Message, args.Contains("--notify"));
            if (gui) LauncherWindow.ShowStartupError(ex.Message);
            return 1;
        }
    }

    internal sealed record CliOptions(bool DryRun, bool Notify, string? ShortcutDirectory,
        DisplayResolution? DesktopResolution);
    internal static CliOptions ParseOverrides(ProductConfig c, string[] args)
    {
        bool dryRun = false, notify = false;
        string? shortcuts = null;
        DisplayResolution? desktopResolution = null;
        // Choose the destination profile first, regardless of the position of --mode.
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--mode")
            {
                if (++i >= args.Length) throw new ArgumentException("--mode 需要 normal 或 touch。");
                c.Mode = args[i];
            }
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (option == "--dry-run") { dryRun = true; continue; }
            if (option == "--notify") { notify = true; continue; }
            if (option == "--clear-env") { c.Linux.Environment.Clear(); continue; }
            if (++i >= args.Length) throw new ArgumentException($"{option} 缺少值。");
            string value = args[i];
            bool Boolean() => value.ToLowerInvariant() switch
            {
                "true" or "1" or "on" => true, "false" or "0" or "off" => false,
                _ => throw new ArgumentException($"{option} 需要 true 或 false。")
            };
            switch (option)
            {
                case "--mode": break;
                case "--game": c.GameExecutable = AppPaths.Expand(value); break;
                case "--resources": c.Selected.Resources = value; break;
                case "--dx": c.Selected.DirectX = value; break;
                case "--quality": c.Selected.Quality = value; break;
                case "--skip-splash": c.Selected.SkipSplash = Boolean(); break;
                case "--args": c.Selected.CustomArguments = value; break;
                case "--integration": c.Linux.Integration = value; break;
                case "--lutris-config": c.Linux.LutrisConfig = AppPaths.Expand(value); break;
                case "--runner": c.Linux.Runner = AppPaths.Expand(value); break;
                case "--runner-kind": c.Linux.RunnerKind = value; break;
                case "--prefix": c.Linux.Prefix = AppPaths.Expand(value); break;
                case "--umu": c.Linux.Umu = AppPaths.Expand(value); break;
                case "--wrapper": c.Linux.Wrapper = value; break;
                case "--inherit-lutris-wrapper": c.Linux.InheritLutrisWrapper = Boolean(); break;
                case "--env":
                    foreach (var pair in CommandLine.ParseEnvironment(value)) c.Linux.Environment[pair.Key] = pair.Value;
                    break;
                case "--mouse-filter": c.TouchOnly.MouseFilter = Boolean(); break;
                case "--adaptive-fullscreen": c.TouchOnly.AdaptiveFullscreen = Boolean(); break;
                // A live GUI snapshot, forwarded unchanged through the session/elevation path.
                // Never persisted in profiles.json; every GUI launch reads the screen again.
                case "--desktop-resolution": desktopResolution = DisplayResolution.Parse(value); break;
                case "--memory-injection": c.TouchOnly.MemoryInjection = Boolean(); break;
                case "--memory-mode": c.TouchOnly.MemoryMode = value; break;
                case "--memory-setting":
                    int memoryIndex = value.IndexOf('=');
                    if (memoryIndex <= 0) throw new ArgumentException("--memory-setting 需要 key=true|false。");
                    string memoryKey = value[..memoryIndex];
                    value = value[(memoryIndex + 1)..];
                    c.TouchOnly.MemoryOptions[memoryKey] = Boolean();
                    break;
                case "--hotkey-enabled": c.TouchOnly.HotkeyEnabled = Boolean(); break;
                case "--hotkey": c.TouchOnly.Hotkey = value; break;
                case "--touch-setting":
                    int index = value.IndexOf('=');
                    if (index <= 0) throw new ArgumentException("--touch-setting 需要 key=value。");
                    c.TouchOnly.Values[value[..index]] = value[(index + 1)..];
                    break;
                case "--shortcut-dir": shortcuts = Path.GetFullPath(AppPaths.Expand(value)); break;
                default: throw new ArgumentException($"未知选项：{option}");
            }
        }
        SettingsCatalog.Validate(c);
        return new CliOptions(dryRun, notify, shortcuts, desktopResolution);
    }

    private static void PrintHelp()
    {
#if HEADLESS
        Console.WriteLine("鸣潮启动器 · 静态命令行版 / NativeAOT · Linux\n");
        Console.WriteLine("此构建不含 GUI；与 wuwa-touch-launcher 共用 profiles.json。\n");
#else
        Console.WriteLine("鸣潮启动器 0.6 · Avalonia / AtomUI / NativeAOT · Windows / Linux\n");
        Console.WriteLine("不带参数 / gui             打开中文配置界面");
#endif
        Console.WriteLine("""
            launch                    无界面启动；默认使用 GUI 上次保存的配置
            preview / launch --dry-run 显示实际命令、环境和临时覆盖，不启动或修改游戏
            save                      将参数覆盖保存到配置
            config                    显示配置
            scan                      扫描 Windows 游戏安装或 Linux Lutris / Proton / Wine
            doctor                    检查所选配置和资源路径
            shortcuts                 创建普通、触控两个无界面快速启动快捷方式
            status / stop / toggle    查看状态 / 关闭鼠标过滤 / 切换鼠标过滤

            通用选项：
              --config FILE           配置文件（默认 profiles.json）
              --mode normal|touch     普通 / 触控；只选本次操作的模式
              --game PATH             游戏 EXE 或安装目录
              --resources keep|sd|hd|uhd  资源画质；需先在官方启动器下载
              --quality keep|1|2|3|4  游戏预设：沿用 / 低 / 中 / 高 / 最高
              --dx auto|dx11|dx12     DirectX 模式
              --skip-splash true|false 跳过开头动画
              --args "参数 ..."        自定义游戏参数；带空格的路径请加引号

            Linux 选项（Windows 不使用）：
              --integration auto|lutris|manual
              --lutris-config FILE    指定 Lutris 条目
              --runner PATH           Proton 目录 / wine 可执行文件
              --runner-kind auto|proton|wine
              --prefix DIR            Wine 前缀 / Proton 兼容数据目录
              --umu PATH              UMU 程序（留空自动查找）
              --wrapper "命令 参数"    如 gamemoderun；不经过 shell
              --inherit-lutris-wrapper true|false  兼容旧配置；Lutris 模式始终沿用其前置命令
              --env NAME=VALUE        可重复，覆盖环境变量
              --clear-env             清除本程序保存的环境变量覆盖

            仅触控模式：
              --adaptive-fullscreen true|false  按当前主屏幕分辨率启动独占全屏，默认关闭
              --mouse-filter true|false
              --hotkey-enabled true|false
              --hotkey Ctrl+Alt+F10
              --touch-setting KEY=VALUE  可重复；keep 表示沿用游戏设置
              --memory-mode file|memory|mixed  配置／内存／混合模式，默认 mixed
              --memory-setting KEY=true|false  混合模式逐项启用内存注入，默认全部 false
              --memory-injection true|false  旧参数，等价于 memory／file 模式
            """);
        foreach (var option in SettingsCatalog.TouchOptions)
            Console.WriteLine($"  {option.Key}: {string.Join(" | ", option.Choices.Select(x => x.Value))}  {option.Label}");
        Console.WriteLine("""

            快捷方式：--shortcut-dir DIR（默认 Linux 应用菜单 / Windows 桌面）
            launch 可用 --notify。命令行覆盖默认只影响本次运行，save 才会写入配置。
            普通模式不会应用“仅触控模式”中的覆盖，也不会安装鼠标 Hook。
            启动预览含环境变量；对外分享前请移除个人信息。
            """);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "AttachConsole")]
    private static partial int AttachConsole(uint process);
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint owner, string message, string title, uint flags);
    internal static void ShowWindowsError(string message) => MessageBox(0, message, "鸣潮启动器", 0x10);
    private static void AttachParentConsole()
    {
        AttachConsole(uint.MaxValue);
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
    }
}
