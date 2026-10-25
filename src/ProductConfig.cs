using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class ProductConfig
{
    public int Version { get; set; } = 2;
    public string Mode { get; set; } = "normal";
    public string GameExecutable { get; set; } = "";
    public AppearanceSettings Appearance { get; set; } = new();
    public LinuxSettings Linux { get; set; } = new();
    public ModeSettings Normal { get; set; } = new() { Resources = "hd", DirectX = "auto" };
    public ModeSettings Touch { get; set; } = new();
    public TouchSettings TouchOnly { get; set; } = new();
    [JsonIgnore] public ModeSettings Selected => Mode == "touch" ? Touch : Normal;
}

internal sealed class AppearanceSettings
{
    public string Theme { get; set; } = "system";
    public int BackgroundOpacity { get; set; } = 85;
    // Keep the default portable: resolve it on the machine that opens the UI.
    public string WindowsEffect { get; set; } = "auto";
}

internal sealed class ModeSettings
{
    public string Resources { get; set; } = "hd";
    public string DirectX { get; set; } = "dx12";
    public bool SkipSplash { get; set; } = true;
    public string Quality { get; set; } = "keep";
    public string CustomArguments { get; set; } = "";
}

internal sealed class LinuxSettings
{
    public string Integration { get; set; } = "auto";
    public string LutrisConfig { get; set; } = "";
    public string Runner { get; set; } = "";
    public string RunnerKind { get; set; } = "auto";
    public string Prefix { get; set; } = "";
    public string Umu { get; set; } = "";
    public string Wrapper { get; set; } = "";
    public bool InheritLutrisWrapper { get; set; } = true;
    public Dictionary<string, string> Environment { get; set; } = [];
}

internal sealed class TouchSettings
{
    public bool AdaptiveFullscreen { get; set; }
    public string MemoryMode { get; set; } = "mixed";
    public Dictionary<string, bool> MemoryOptions { get; set; } = [];
    // Read compatibility is handled by ConfigStore so JSON property order cannot
    // override an explicit new mode. This alias also keeps the old CLI working.
    [JsonIgnore] public bool MemoryInjection
    {
        get => MemoryMode == "memory";
        set => MemoryMode = value ? "memory" : "file";
    }
    public bool UsesMemory(string key) => MemoryGraphicsCatalog.Supports(key) &&
        (MemoryMode == "memory" || MemoryMode == "mixed" && MemoryOptions.GetValueOrDefault(key));
    public bool MouseFilter { get; set; } = true;
    public bool HotkeyEnabled { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+F10";
    public Dictionary<string, string> Values { get; set; } = [];
}

internal static class AppPaths
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string ConfigDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WuwaTouchLauncher")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Home, ".config"), "wuwa-touch-launcher");
    public static string DefaultConfig => Path.Combine(ConfigDirectory, "profiles.json");
    public static string StateDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(ConfigDirectory, "state")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME") ?? Path.Combine(Home, ".local", "state"), "wuwa-touch-launcher");
    public static string Bridge => EmbeddedBridge.Path;
    public static string Expand(string value) => value.StartsWith("~/", StringComparison.Ordinal)
        ? Path.Combine(Home, value[2..]) : value;
}

internal static class ConfigStore
{
    public static ProductConfig Load(string path)
    {
        if (!File.Exists(path)) return ImportLegacy(path);
        string json = File.ReadAllText(path);
        var result = JsonSerializer.Deserialize(json, ProductJson.Default.ProductConfig)
            ?? throw new ArgumentException("配置文件为空。");
        using (var document = JsonDocument.Parse(json))
        {
            var touch = document.RootElement.EnumerateObject().FirstOrDefault(x =>
                x.Name.Equals("TouchOnly", StringComparison.OrdinalIgnoreCase)).Value;
            if (touch.ValueKind == JsonValueKind.Object && !touch.EnumerateObject().Any(x =>
                x.Name.Equals("MemoryMode", StringComparison.OrdinalIgnoreCase)))
            {
                var legacy = touch.EnumerateObject().FirstOrDefault(x =>
                    x.Name.Equals("MemoryInjection", StringComparison.OrdinalIgnoreCase)).Value;
                result.TouchOnly.MemoryMode = legacy.ValueKind == JsonValueKind.True ? "memory" : "mixed";
            }
        }
        if (result.Version != 2) throw new ArgumentException($"不支持的配置版本：{result.Version}；未修改原文件。");
        SettingsCatalog.Validate(result);
        return result;
    }

    public static void Save(string path, ProductConfig config)
    {
        SettingsCatalog.Validate(config);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".profiles-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(config, ProductJson.Default.ProductConfig));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    public static ProductConfig Clone(ProductConfig source) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(source, ProductJson.Default.ProductConfig),
            ProductJson.Default.ProductConfig)!;

    private static ProductConfig ImportLegacy(string requestedPath)
    {
        var result = new ProductConfig();
        // Custom config locations are isolated; tests and portable profiles never import this machine.
        if (Path.GetFullPath(requestedPath) != Path.GetFullPath(AppPaths.DefaultConfig)) return result;
        string path = Path.Combine(AppPaths.ConfigDirectory, "config.json");
        if (!File.Exists(path)) return result;
        var old = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.LauncherConfig);
        if (old is null) return result;
        result.GameExecutable = old.GameExecutable;
        result.Mode = "touch";
        result.Linux.Prefix = old.Environment.GetValueOrDefault("WINEPREFIX", "");
        result.Linux.Runner = old.Environment.GetValueOrDefault("PROTONPATH", "");
        result.Linux.Umu = old.RunnerCommand.LastOrDefault() ?? "";
        result.Linux.Wrapper = CommandLine.Join(old.RunnerCommand.SkipLast(1));
        result.Linux.Environment = old.Environment
            .Where(x => x.Key is not ("WINEPREFIX" or "PROTONPATH" or "STEAM_COMPAT_DATA_PATH" or "STEAM_COMPAT_INSTALL_PATH"))
            .ToDictionary(x => x.Key, x => x.Value);
        string? resource = old.GameArguments.FirstOrDefault(x => x.StartsWith("-krqlv=", StringComparison.OrdinalIgnoreCase));
        string tier = resource?["-krqlv=".Length..].ToLowerInvariant() ?? "hd";
        result.Touch.Resources = tier is "sd" or "hd" or "uhd" ? tier : "hd";
        // Engine settings that were actually trialled can be migrated. OptiScaler INI entries are excluded.
        foreach (var item in old.IniOverrides.Where(x => Path.GetFileName(x.Path) == "Engine.ini"))
        {
            void Copy(string ini, string key)
            { if (item.Values.TryGetValue(ini, out var value)) result.TouchOnly.Values[key] = value; }
            Copy("t.MaxFPS", "fps");
            Copy("r.VSync", "vsync");
            Copy("r.MaxAnisotropy", "anisotropy");
            Copy("r.Kuro.GlobalLightQuality", "lighting");
        }
        return result;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true,
    RespectNullableAnnotations = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ProductConfig))]
[JsonSerializable(typeof(LaunchPlan))]
[JsonSerializable(typeof(DiscoveryResult))]
[JsonSerializable(typeof(GraphicsDatabaseChange))]
[JsonSerializable(typeof(FpsDatabaseChange))]
internal partial class ProductJson : JsonSerializerContext;
