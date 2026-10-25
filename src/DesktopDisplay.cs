using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

internal readonly record struct DisplayResolution(int Width, int Height)
{
    internal void Validate()
    {
        if (Width is < 1 or > 65535 || Height is < 1 or > 65535)
            throw new ArgumentException("无法使用此屏幕分辨率，请确认显示器已连接并开启。");
    }

    internal static DisplayResolution Parse(string value)
    {
        string[] parts = value.Split('x');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height))
            throw new ArgumentException("屏幕分辨率需要使用 宽x高 格式。");
        var result = new DisplayResolution(width, height);
        result.Validate();
        return result;
    }

    public override string ToString() => FormattableString.Invariant($"{Width}x{Height}");
}

internal static partial class DesktopDisplay
{
    internal static DisplayResolution ReadPrimary()
    {
        if (OperatingSystem.IsWindows())
        {
            // DEVMODEW is caller-owned, contains no pointers and has no driver-extra data.
            // EnumDisplaySettingsW returns physical pixels, independent of DPI awareness.
            var mode = new DisplayMode { Size = 220 };
            if (EnumDisplaySettingsW(0, uint.MaxValue, ref mode) == 0 ||
                mode.Width is < 1 or > 65535 || mode.Height is < 1 or > 65535)
                throw new InvalidOperationException("无法读取主屏幕分辨率，请确认显示器已连接并开启。");
            var size = new DisplayResolution((int)mode.Width, (int)mode.Height);
            size.Validate();
            return size;
        }

        // A GUI launch supplies Avalonia's physical screen bounds. Quick-launch/CLI
        // sessions can query the same X11/XWayland display without starting a GUI.
        string? xrandr = Discovery.FindOnPath("xrandr");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) || xrandr is null)
            throw new InvalidOperationException("无法读取主屏幕分辨率。请从图形界面启动；Linux 快捷启动需要可用的 DISPLAY 和 xrandr。");
        var info = new ProcessStartInfo(xrandr)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("--current");
        info.Environment["LC_ALL"] = "C";
        using var process = Process.Start(info) ?? throw new IOException("无法查询屏幕分辨率。");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new InvalidOperationException("读取主屏幕分辨率超时，请检查当前显示会话。");
        }
        string text = output.GetAwaiter().GetResult();
        _ = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("无法读取主屏幕分辨率，请检查当前显示会话。");
        Match[] screens = ConnectedScreen().Matches(text).Cast<Match>().ToArray();
        Match? screen = screens.FirstOrDefault(x => x.Groups["primary"].Success) ?? screens.FirstOrDefault();
        if (screen is null)
            throw new InvalidOperationException("未找到正在显示的屏幕，无法启用自适应全屏。");
        return DisplayResolution.Parse(screen.Groups["width"].Value + "x" + screen.Groups["height"].Value);
    }

    [GeneratedRegex(@"^\S+ connected(?<primary> primary)? (?<width>\d+)x(?<height>\d+)[+-]\d+[+-]\d+(?:\s|$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ConnectedScreen();

    // Layout checked against the Windows SDK DEVMODEW declaration (wingdi.h).
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    private struct DisplayMode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(172)] public uint Width;
        [FieldOffset(176)] public uint Height;
    }

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW")]
    private static partial int EnumDisplaySettingsW(nint deviceName, uint modeNumber, ref DisplayMode mode);
}
