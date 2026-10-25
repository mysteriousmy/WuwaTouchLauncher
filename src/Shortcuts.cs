using System.Diagnostics;
using System.Text;

internal static class Shortcuts
{
    public static string[] CreateBoth(string configPath, string? directory = null)
    {
        directory ??= OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(AppPaths.Home, ".local/share"), "applications");
        Directory.CreateDirectory(directory);
        var paths = new List<string>();
        foreach (string mode in new[] { "normal", "touch" })
        {
            string name = mode == "touch" ? "鸣潮 · 触控模式" : "鸣潮 · 普通模式";
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定启动器可执行文件。");
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请使用已发布的 NativeAOT 程序创建快捷方式。");
            string[] arguments = ["launch", "--config", Path.GetFullPath(configPath), "--mode", mode, "--notify"];
            if (OperatingSystem.IsWindows())
            {
                string path = Path.Combine(directory, name + ".lnk");
                var info = new ProcessStartInfo(AppPaths.Bridge) { UseShellExecute = false, CreateNoWindow = true };
                foreach (string arg in new[] { "--create-shortcut", path, executable, WindowsArguments(arguments), Path.GetDirectoryName(executable)! })
                    info.ArgumentList.Add(arg);
                using var child = Process.Start(info) ?? throw new IOException("无法创建 Windows 快捷方式。");
                if (!child.WaitForExit(10000) || child.ExitCode != 0) throw new IOException("Windows Shell 未能保存快捷方式。");
                paths.Add(path);
            }
            else
            {
                string path = Path.Combine(directory, $"wuwa-{mode}.desktop");
                string exec = string.Join(' ', new[] { executable }.Concat(arguments).Select(DesktopArgument));
                string contents = $"""
                    [Desktop Entry]
                    Version=1.0
                    Type=Application
                    Name={name}
                    Comment=使用已保存配置直接启动，不打开配置界面
                    Exec={exec}
                    Icon=wuwa-touch-launcher
                    Terminal=false
                    Categories=Game;
                    StartupNotify=false

                    """;
                File.WriteAllText(path, contents);
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                paths.Add(path);
            }
        }
        return paths.ToArray();
    }

    internal static string DesktopArgument(string value)
    {
        if (value.IndexOfAny(['\n', '\r', '\0']) >= 0) throw new ArgumentException("快捷方式路径不能包含换行或空字符。");
        // Desktop-entry string unescaping precedes Exec token unquoting.
        string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$");
        return "\"" + escaped.Replace("\\", "\\\\").Replace("%", "%%") + "\"";
    }

    internal static string WindowsArguments(IEnumerable<string> args)
    {
        var result = new List<string>();
        foreach (string arg in args)
        {
            var value = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                value.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                value.Append(c); slashes = 0;
            }
            value.Append('\\', slashes * 2).Append('"');
            result.Add(value.ToString());
        }
        return string.Join(' ', result);
    }
}
