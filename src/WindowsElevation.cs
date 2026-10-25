using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

internal static class WindowsElevation
{
    internal static bool IsRequired
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    internal static string ResultPath(string token)
    {
        if (!Guid.TryParseExact(token, "N", out var id))
            throw new ArgumentException("启动结果标识无效。");
        return Path.Combine(AppPaths.StateDirectory, $"session-result-{id:N}.txt");
    }

    internal static ProcessStartInfo StartInfo(string executable, string configPath,
        IEnumerable<string> overrides, string token)
    {
        _ = ResultPath(token);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Environment.CurrentDirectory
        };
        foreach (var arg in new[] { "launch", "--config", Path.GetFullPath(configPath) }.Concat(overrides)
                     .Concat(["--session-result", token]))
            info.ArgumentList.Add(arg);
        return info;
    }

    internal static int Run(string configPath, IEnumerable<string> overrides)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位启动器。");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请使用发布后的启动器请求管理员权限。");
        string token = Guid.NewGuid().ToString("N");
        string report = ResultPath(token);
        Directory.CreateDirectory(AppPaths.StateDirectory);
        using (new FileStream(report, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
        try
        {
            using var child = Process.Start(StartInfo(executable, configPath, overrides, token))
                ?? throw new InvalidOperationException("未能启动管理员权限的游戏会话。");
            child.WaitForExit();
            if (child.ExitCode == 0) return 0;
            string message = File.ReadAllText(report, Encoding.UTF8).Trim();
            throw new InvalidOperationException(message.Length > 0 ? message :
                $"管理员权限的游戏会话已退出（退出码 {child.ExitCode}）。请查看启动记录。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("已取消管理员授权，游戏未启动。再次启动时允许 Windows 授权即可。", ex);
        }
        finally
        {
            try { File.Delete(report); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void WriteResult(string? path, string message)
    {
        if (path is null) return;
        try { File.WriteAllText(path, message, new UTF8Encoding(false)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
