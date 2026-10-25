using System.Security.Cryptography;

/// <summary>Materializes the Windows helper only when a launch plan needs it.</summary>
internal static class EmbeddedBridge
{
    private static readonly Lazy<string> EmbeddedPath = new(() => Extract("Wuwa.TouchBridge", "touch-bridge.exe"));

    private static readonly Lazy<string> FpsPlugin = new(() => Extract("Wuwa.FpsUnlockPlugin", "ww_ulk_plugin_base.dll"));
    public static string FpsPluginPath => FpsPlugin.Value;

    // The helper and launcher share a command/status protocol. An old helper
    // left beside an upgraded executable must never shadow the bundled version.
    public static string Path => EmbeddedPath.Value;

    private static string Extract(string resource, string filename)
    {
        using var stream = typeof(EmbeddedBridge).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("启动器缺少内置辅助组件，请重新构建或安装完整发布版。");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] data = memory.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        string directory = System.IO.Path.Combine(AppPaths.StateDirectory, "runtime", hash);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string destination = System.IO.Path.Combine(directory, filename);
        if (File.Exists(destination) && File.ReadAllBytes(destination).AsSpan().SequenceEqual(data))
            return destination;
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { File.Delete(temporary); }
        return destination;
    }
}
