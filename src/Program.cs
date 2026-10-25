using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => ProductApp.Run(args);
}

internal static class Launcher
{
    private static readonly string UserHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(UserHome, ".config"),
        "wuwa-touch-launcher", "config.json");
    private static readonly string StateDirectory = AppPaths.StateDirectory;
    private static readonly string StatusPath = Path.Combine(StateDirectory, "status.json");
    private static readonly string StopPath = Path.Combine(StateDirectory, "stop");
    private static bool desktopNotifications;

    internal static int LaunchProduct(LaunchPlan plan, bool notify)
    {
        desktopNotifications = notify;
        PlanBuilder.ValidateFiles(plan);
        Directory.CreateDirectory(StateDirectory);
        using var launchLock = AcquireLaunchLock();
        if (IsLive(ReadStatus())) throw new InvalidOperationException("已有游戏会话正在运行，请先退出游戏。");
        // Fine-setting recovery must precede preset recovery: it restores the
        // preset activation flag that the older scope compares.
        FpsUnlockScope.Recover(Path.Combine(StateDirectory, "fps-database-restore.json"));
        GameGraphicsScope.Recover(Path.Combine(StateDirectory, "graphics-database-restore.json"));
        using var graphics = IniOverrideScope.Apply(plan.IniOverrides, Path.Combine(StateDirectory, "graphics-restore.json"));
        using var quality = GameQualityScope.Apply(plan.Database, plan.ImageQuality, Path.Combine(StateDirectory, "quality-restore.json"), activatePreset: true);
        using var details = GameGraphicsScope.Apply(plan.Database, plan.DatabaseOverrides, Path.Combine(StateDirectory, "graphics-database-restore.json"));
        using var fps = FpsUnlockScope.Apply(plan.Database, plan.FpsUnlockTarget, Path.Combine(StateDirectory, "fps-database-restore.json"));
        File.Delete(StatusPath);
        File.Delete(StopPath);
        var startInfo = new ProcessStartInfo(plan.Command[0])
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(plan.GameExecutable)!,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string value in plan.Command.Skip(1)) startInfo.ArgumentList.Add(value);
        foreach (var pair in plan.Environment) startInfo.Environment[pair.Key] = pair.Value;
        using var child = new Process { StartInfo = startInfo };
        child.OutputDataReceived += (_, e) => RelayRunnerOutput(e.Data);
        child.ErrorDataReceived += (_, e) => RelayRunnerOutput(e.Data);
        if (!child.Start()) throw new InvalidOperationException("无法启动游戏运行器。");
        child.BeginOutputReadLine();
        child.BeginErrorReadLine();
        Log($"started mode={plan.Mode}, runner pid={child.Id}");
        using var sigterm = OperatingSystem.IsLinux()
            ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; RequestStop(); }) : null;
        using var sigint = OperatingSystem.IsLinux()
            ? PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; RequestStop(); }) : null;
        // The helper tracks the game lifetime even with mouse filtering disabled.
        // The GUI can close while this process keeps the temporary settings journal alive.
        var result = MonitorSession(child);
        Log($"session finished; mode={plan.Mode}, bridge={result.Phase}, exit={result.ExitCode}");
        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.Win32Error == 740
                ? "游戏需要管理员权限（Win32 740）。请允许 Windows 的管理员授权后重试。"
                : $"游戏启动失败：{result.Phase}（Win32 {result.Win32Error}）。详见状态与启动记录。");
        return result.ExitCode;
    }

    internal static BridgeStatus? CurrentStatus() => ReadStatus();
    internal static bool SessionIsLive() => IsLive(ReadStatus());
    internal static int ShowStatus() => Status();
    internal static int ReleaseMouse() => Stop();
    internal static void ReportError(string message, bool notify)
    {
        desktopNotifications = notify;
        Notify(message, true);
    }
    internal static int ToggleMouse()
    {
        if (!IsLive(ReadStatus())) throw new InvalidOperationException("尚未运行触控游戏会话。");
        File.WriteAllText(StopPath, "toggle\n");
        return 0;
    }

    public static int Run(string[] args)
    {
        desktopNotifications = args.Contains("--notify");
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("此程序是 Linux 启动器，请在 Linux 主机上运行。");
            return 2;
        }
        try
        {
            Directory.CreateDirectory(StateDirectory);
            File.SetUnixFileMode(StateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string command = args.FirstOrDefault() ?? "help";
            switch (command)
            {
                case "help": case "--help": case "-h":
                    Console.WriteLine("""
                        鸣潮触控启动器 · Linux Native AOT + Wine
                        launch [--dry-run] [--notify] 使用已配置的 Proton/UMU 启动
                        wrap -- COMMAND...  接管 Lutris 提供的完整启动命令
                        status              查看过滤状态和输入计数
                        stop                恢复鼠标，不关闭游戏
                        doctor              检查本机路径与配置

                        Wine 紧急恢复：Ctrl+Alt+F10（需 status 显示热键注册成功）。
                        配置：~/.config/wuwa-touch-launcher/config.json
                        """);
                    return 0;
                case "status": return Status();
                case "stop": return Stop();
                case "doctor": return Doctor(ReadConfig());
                case "launch":
                    if (args.Skip(1).Any(x => x is not ("--dry-run" or "--notify")))
                        throw new ArgumentException("未知 launch 参数。");
                    var config = ReadConfig();
                    return Launch(config, [.. config.RunnerCommand, config.GameExecutable, .. config.GameArguments],
                        args.Contains("--dry-run"));
                case "wrap":
                    if (args.Length < 4 || args[1] != "--") throw new ArgumentException("用法：wrap -- COMMAND...");
                    return Launch(ReadConfig(), args[2..], false);
                default: throw new ArgumentException($"未知命令：{command}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception
                                   or DllNotFoundException or EntryPointNotFoundException)
        {
            Console.Error.WriteLine($"启动器：{ex.Message}");
            Log(ex.Message);
            Notify(ex.Message, true);
            return 1;
        }
    }

    private static LauncherConfig ReadConfig() =>
        JsonSerializer.Deserialize(File.ReadAllText(ConfigPath), AppJsonContext.Default.LauncherConfig)
        ?? throw new ArgumentException("配置文件为空。");

    private static void Validate(LauncherConfig config)
    {
        if (!Path.IsPathFullyQualified(config.GameExecutable) || !File.Exists(config.GameExecutable))
            throw new ArgumentException($"找不到游戏：{config.GameExecutable}");
        if (!Path.IsPathFullyQualified(config.BridgeExecutable) || !File.Exists(config.BridgeExecutable))
            throw new ArgumentException($"找不到 Wine 辅助程序：{config.BridgeExecutable}");
        if (config.RunnerCommand.Length == 0 || !File.Exists(config.RunnerCommand[0]))
            throw new ArgumentException("RunnerCommand 必须以存在的可执行文件绝对路径开头。");
        if (!config.Environment.TryGetValue("WINEPREFIX", out var prefix) || !Directory.Exists(prefix))
            throw new ArgumentException("WINEPREFIX 不存在。");
        if (!config.Environment.TryGetValue("PROTONPATH", out var proton) || !File.Exists(Path.Combine(proton, "proton")))
            throw new ArgumentException("PROTONPATH 中找不到 proton。");
        foreach (var resource in config.RequiredResourceFiles)
        {
            var file = new FileInfo(resource.Key);
            if (!Path.IsPathFullyQualified(resource.Key) || resource.Value <= 0 ||
                !file.Exists || file.Length != resource.Value)
                throw new ArgumentException($"画质基础资源缺失或尚未下载完整：{resource.Key}。请先完成资源安装。");
        }
    }

    private static int Doctor(LauncherConfig config)
    {
        Validate(config);
        Console.WriteLine($"游戏：{config.GameExecutable}");
        Console.WriteLine($"Proton：{config.Environment["PROTONPATH"]}");
        Console.WriteLine($"Wine prefix：{config.Environment["WINEPREFIX"]}");
        Console.WriteLine($"辅助程序：{config.BridgeExecutable}");
        Console.WriteLine($"桌面会话：{Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "未知"}；DISPLAY={Environment.GetEnvironmentVariable("DISPLAY") ?? "未设置"}");
        Console.WriteLine("路径检查通过。实际 Hook 和触控效果以运行状态及游戏表现为准。");
        return 0;
    }

    private static int Launch(LauncherConfig config, string[] command, bool dryRun)
    {
        Validate(config);
        int gameIndex = Array.FindIndex(command, x => x == config.GameExecutable);
        if (gameIndex < 1) throw new ArgumentException("启动命令中找不到配置中的游戏绝对路径，未启动任何程序。");
        // Linux argv remains an array. The bridge separately quotes Windows argv.
        List<string> rewritten = [.. command[..gameIndex], config.BridgeExecutable,
            "--status", WindowsPath(StatusPath), "--stop", WindowsPath(StopPath),
            "--game", WindowsPath(config.GameExecutable), "--", .. command[(gameIndex + 1)..]];
        if (dryRun)
        {
            Console.WriteLine(JsonSerializer.Serialize(rewritten.ToArray(), AppJsonContext.Default.StringArray));
            return 0;
        }
        using var launchLock = AcquireLaunchLock();
        if (IsLive(ReadStatus()))
            throw new InvalidOperationException("已有辅助程序正在运行；可用 status 查看，或用 stop 释放过滤。");
        using var graphics = IniOverrideScope.Apply(config.IniOverrides,
            Path.Combine(StateDirectory, "graphics-restore.json"));
        using var quality = GameQualityScope.Apply(config.GameSettingsDatabase, config.GameImageQuality,
            Path.Combine(StateDirectory, "quality-restore.json"));
        File.Delete(StatusPath);
        File.Delete(StopPath);
        var startInfo = new ProcessStartInfo(rewritten[0])
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(config.GameExecutable)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string value in rewritten.Skip(1)) startInfo.ArgumentList.Add(value);
        foreach (var variable in config.Environment) startInfo.Environment[variable.Key] = variable.Value;
        // Keep UMU's usual Steam compatibility ID based on the game, not the bridge.
        startInfo.Environment["STEAM_COMPAT_INSTALL_PATH"] = Path.GetDirectoryName(config.GameExecutable)!;
        using var child = new Process { StartInfo = startInfo };
        child.OutputDataReceived += (_, e) => RelayRunnerOutput(e.Data);
        child.ErrorDataReceived += (_, e) => RelayRunnerOutput(e.Data);
        if (!child.Start()) throw new InvalidOperationException("无法启动 UMU。");
        child.BeginOutputReadLine();
        child.BeginErrorReadLine();
        Log($"started runner pid={child.Id}");
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true;
            RequestStop();
        });
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
        {
            ctx.Cancel = true;
            RequestStop();
        });
        Console.WriteLine("已启动。恢复鼠标：wuwa-touch-launcher stop；Wine 内热键 Ctrl+Alt+F10。");
        Notify("正在启动鸣潮；恢复鼠标可使用菜单中的 Restore Mouse。");
        var result = MonitorSession(child);
        Log($"session finished; runner={(child.HasExited ? child.ExitCode.ToString() : "still-running")}, bridge={result.Phase}");
        Console.WriteLine($"辅助程序：{result.Phase}；拦截鼠标消息 {result.BlockedMouse} 条。");
        if (result.ExitCode != 0) Notify($"启动失败：{result.Phase}（Win32 {result.Win32Error}）", true);
        return result.ExitCode;
    }

    private static FileStream AcquireLaunchLock()
    {
        try
        {
            return new FileStream(Path.Combine(StateDirectory, "launcher.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("启动器已在运行或正在启动游戏，请查看 View Touch Filter Status。", ex);
        }
    }

    private static BridgeStatus MonitorSession(Process runner)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var status = ReadStatus();
            // UMU can outlive our game because Proton helpers (e.g. Xalia) remain
            // alive. The bridge owns the game lifecycle, so its terminal state
            // releases the launcher lock even when an outer process still waits.
            if (status is { Filtering: false } && status.Phase is
                "game-exited" or "launch-error" or "fps-error" or "hook-error" or "timer-error" or "already-running")
                return status;
            if (IsLive(status))
            {
                Thread.Sleep(250);
                continue;
            }
            if (runner.HasExited)
                throw new InvalidOperationException($"游戏会话辅助程序已退出（{runner.ExitCode}），未收到有效运行状态。请查看启动记录。");
            if (status is not null &&
                DateTime.UtcNow - File.GetLastWriteTimeUtc(StatusPath) > TimeSpan.FromSeconds(15))
                throw new InvalidOperationException("触控辅助程序心跳已中断，请查看过滤状态后重新启动。");
            if (status is null && elapsed.Elapsed > TimeSpan.FromSeconds(120))
                throw new InvalidOperationException("等待游戏会话辅助程序启动超时，请查看启动记录。");
            // Never use parameterless WaitForExit here: it also waits for EOF on
            // redirected streams, which detached Wine children may keep open.
            Thread.Sleep(250);
        }
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(StateDirectory, "launcher.log"), $"{DateTimeOffset.Now:O} {message}\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Notify(string message, bool error = false)
    {
        if (!desktopNotifications || !OperatingSystem.IsLinux()) return;
        try
        {
            var info = new ProcessStartInfo("notify-send") { UseShellExecute = false };
            foreach (string argument in new[] { "--app-name=Wuthering Waves Touch Launcher",
                         error ? "--urgency=critical" : "--urgency=normal",
                         "Wuthering Waves Touch Launcher", message })
                info.ArgumentList.Add(argument);
            using var notification = Process.Start(info);
            notification?.WaitForExit(1500);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log($"Notification unavailable: {ex.Message}");
        }
    }

    private static void RelayRunnerOutput(string? line)
    {
        if (line is null) return;
        // Game stdout can contain login/session data. Show only known runner diagnostics.
        if (line.StartsWith("ERROR:", StringComparison.Ordinal) ||
            line.StartsWith("WARNING:", StringComparison.Ordinal) ||
            line.StartsWith("INFO:", StringComparison.Ordinal) ||
            line.StartsWith("Proton:", StringComparison.Ordinal))
            Console.Error.WriteLine(line);
    }

    private static string WindowsPath(string path) =>
        "Z:" + Path.GetFullPath(path).Replace('/', '\\');

    private static BridgeStatus? ReadStatus()
    {
        try
        {
            // The native helper publishes heartbeats by atomic replacement. On
            // Windows, readers must share DELETE or they block MoveFileEx.
            using var stream = new FileStream(StatusPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize(stream, AppJsonContext.Default.BridgeStatus);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private static bool IsLive(BridgeStatus? status) =>
        status is not null && (status.Phase == "active" || status.Phase == "released") &&
        DateTime.UtcNow - File.GetLastWriteTimeUtc(StatusPath) < TimeSpan.FromSeconds(5);

    private static int Status()
    {
        var status = ReadStatus();
        if (status is null) { Console.WriteLine("尚无运行记录。"); return 0; }
        bool live = IsLive(status);
        Console.WriteLine($"状态：{status.Phase}；{(live ? "心跳正常" : "未运行或状态已过期")}");
        Console.WriteLine($"鼠标过滤：{(live && status.Filtering ? "启用" : "未启用/未确认")}；目标进程：{status.TargetCount}");
        Console.WriteLine($"拦截鼠标：{status.BlockedMouse}；放行鼠标：{status.PassedMouse}；配置窗口：{status.ConfiguredWindows}");
        Console.WriteLine($"过滤开关快捷键：{(live && status.EmergencyHotkey ? "已注册" : "未注册")}；Win32 错误：{status.Win32Error}");
        return status.ExitCode == 0 ? 0 : 1;
    }

    private static void RequestStop()
    {
        try { File.WriteAllText(StopPath, "release\n"); }
        catch (IOException ex) { Console.Error.WriteLine($"无法写入停止请求：{ex.Message}"); }
    }

    private static int Stop()
    {
        var before = ReadStatus();
        if (!IsLive(before)) { Console.WriteLine("没有检测到正在运行的过滤辅助程序。"); return 0; }
        RequestStop();
        for (int i = 0; i < 20; i++)
        {
            Thread.Sleep(200);
            var status = ReadStatus();
            if (status is not null && !status.Filtering && status.Phase is "released" or "game-exited")
            {
                Console.WriteLine("鼠标过滤已停止，窗口属性已恢复；游戏继续运行。");
                Notify("鼠标过滤已停止，游戏继续运行。");
                return 0;
            }
        }
        Console.Error.WriteLine("已发送停止请求，但尚未收到恢复确认；请查看 status。");
        return 1;
    }
}

internal sealed class LauncherConfig
{
    public string GameExecutable { get; set; } = "";
    public string BridgeExecutable { get; set; } = "";
    public string[] RunnerCommand { get; set; } = [];
    public string[] GameArguments { get; set; } = [];
    public Dictionary<string, string> Environment { get; set; } = [];
    public IniOverride[] IniOverrides { get; set; } = [];
    public string GameSettingsDatabase { get; set; } = "";
    public int? GameImageQuality { get; set; }
    public Dictionary<string, long> RequiredResourceFiles { get; set; } = [];
}

internal sealed class BridgeStatus
{
    public string MemoryGraphicsState { get; set; } = "disabled";
    public int MemoryGraphicsCount { get; set; }
    public int MemoryGraphicsApplied { get; set; }
    public int MemoryGraphicsSkipped { get; set; }
    public uint MemoryGraphicsWrites { get; set; }
    public uint MemoryGraphicsError { get; set; }
    public string MemoryFrameGenerationState { get; set; } = "not-requested";
    public int FpsUnlockTarget { get; set; }
    public string FpsUnlockState { get; set; } = "disabled";
    public uint FpsUnlockError { get; set; }
    public string Phase { get; set; } = "";
    public bool Filtering { get; set; }
    public int TargetCount { get; set; }
    public uint BlockedMouse { get; set; }
    public uint PassedMouse { get; set; }
    public uint ConfiguredWindows { get; set; }
    public bool EmergencyHotkey { get; set; }
    public uint Win32Error { get; set; }
    public int ExitCode { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(LauncherConfig))]
[JsonSerializable(typeof(BridgeStatus))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(IniChange[]))]
[JsonSerializable(typeof(QualityChange))]
internal partial class AppJsonContext : JsonSerializerContext;
