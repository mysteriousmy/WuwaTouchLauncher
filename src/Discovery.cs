using System.Text.RegularExpressions;
#if !WINDOWS
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
#endif

internal sealed record RunnerCandidate(string Name, string Path, string Kind);
internal sealed class LutrisGame
{
    public string Name { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string RunnerVersion { get; set; } = "";
    public string RunnerPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Wrapper { get; set; } = "";
    public Dictionary<string, string> Environment { get; set; } = [];
}
internal sealed class DiscoveryResult
{
    public string Platform { get; set; } = OperatingSystem.IsWindows() ? "windows" : "linux";
    public List<RunnerCandidate> Runners { get; set; } = [];
    public List<LutrisGame> LutrisGames { get; set; } = [];
    public List<InstalledGame> InstalledGames { get; set; } = [];
    public string Umu { get; set; } = "";
    public List<string> Messages { get; set; } = [];
}

internal static partial class Discovery
{
    public static DiscoveryResult Scan()
    {
        var result = new DiscoveryResult();
        if (OperatingSystem.IsWindows()) { WindowsDiscovery.Scan(result); return result; }
        var roots = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.Combine(AppPaths.Home, ".local/share/Steam"),
            Path.Combine(AppPaths.Home, ".steam/steam"),
            Path.Combine(AppPaths.Home, ".var/app/com.valvesoftware.Steam/data/Steam")
        };
        foreach (string root in roots.ToArray())
        {
            string libraries = Path.Combine(root, "steamapps/libraryfolders.vdf");
            if (!File.Exists(libraries)) continue;
            foreach (Match match in SteamLibraryPath().Matches(File.ReadAllText(libraries)))
                roots.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            ScanRunnerRoot(Path.Combine(root, "compatibilitytools.d"), 2, result, seen);
            ScanRunnerRoot(Path.Combine(root, "steamapps/common"), 2, result, seen);
        }
        foreach (string root in new[]
        {
            "~/.local/share/lutris/runners/wine", "~/.var/app/net.lutris.Lutris/data/lutris/runners/wine",
            "~/.config/heroic/tools/proton", "~/.config/heroic/tools/wine",
            "~/.var/app/com.heroicgameslauncher.hgl/config/heroic/tools/proton",
            "~/.var/app/com.heroicgameslauncher.hgl/config/heroic/tools/wine",
            "~/.local/share/umu/compatibilitytools", "/opt", "/usr/local/share/Steam/compatibilitytools.d"
        }) ScanRunnerRoot(AppPaths.Expand(root), 2, result, seen);
        foreach (string name in new[] { "wine", "wine64" })
        {
            string? path = FindOnPath(name);
            if (path is not null && seen.Add(path))
                result.Runners.Add(new RunnerCandidate($"系统 {name}", path, "wine"));
        }
        result.Runners = result.Runners.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        result.Umu = FindOnPath("umu-run") ?? new[]
        {
            "~/.local/share/lutris/runtime/umu/umu-run",
            "~/.var/app/net.lutris.Lutris/data/lutris/runtime/umu/umu-run"
        }.Select(AppPaths.Expand).FirstOrDefault(File.Exists) ?? "";
#if !WINDOWS
        foreach (string root in LutrisRoots())
        {
            string directory = Path.Combine(root, "games");
            if (!Directory.Exists(directory)) continue;
            try
            {
                foreach (string path in Directory.EnumerateFiles(directory, "*.yml").Take(1000))
                {
                    try
                    {
                        var game = ReadLutris(path);
                        if (IsWuwa(game)) result.LutrisGames.Add(game);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlException or ArgumentException)
                    { result.Messages.Add($"无法读取 {Path.GetFileName(path)}：{ex.Message}"); }
                }
            }
            catch (UnauthorizedAccessException) { result.Messages.Add($"无法读取目录：{directory}"); }
        }
#endif
        return result;
    }

    private static bool IsWuwa(LutrisGame game) =>
        game.Name.Contains("wuthering", StringComparison.OrdinalIgnoreCase) ||
        game.Name.Contains("wuwa", StringComparison.OrdinalIgnoreCase) || game.Name.Contains("鸣潮", StringComparison.Ordinal) ||
        Path.GetFileName(game.Executable).Equals("Client-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> LutrisRoots()
    {
        yield return Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(AppPaths.Home, ".local/share"), "lutris");
        yield return Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(AppPaths.Home, ".config"), "lutris");
        yield return AppPaths.Expand("~/.var/app/net.lutris.Lutris/data/lutris");
        yield return AppPaths.Expand("~/.var/app/net.lutris.Lutris/config/lutris");
    }

    private static void ScanRunnerRoot(string root, int depth, DiscoveryResult result, HashSet<string> seen)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(root).Take(500))
            {
                if (root == "/opt" && !Path.GetFileName(directory).Contains("wine", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileName(directory).Contains("proton", StringComparison.OrdinalIgnoreCase)) continue;
                string full = CanonicalDirectory(directory);
                string proton = Path.Combine(full, "proton");
                string wine = Path.Combine(full, "bin/wine");
                if (File.Exists(proton))
                {
                    if (seen.Add(full + "|" + Path.GetFileName(directory))) result.Runners.Add(new RunnerCandidate(Path.GetFileName(directory), full, "proton"));
                }
                else if (File.Exists(wine))
                {
                    if (seen.Add(wine)) result.Runners.Add(new RunnerCandidate(Path.GetFileName(directory), wine, "wine"));
                }
                else if (depth > 1) ScanRunnerRoot(full, depth - 1, result, seen);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { result.Messages.Add($"扫描运行器时跳过：{root}（{ex.Message}）"); }
    }

    private static string CanonicalDirectory(string path)
    {
        string current = Path.GetPathRoot(Path.GetFullPath(path))!;
        foreach (string part in Path.GetFullPath(path)[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            current = new DirectoryInfo(current).ResolveLinkTarget(true)?.FullName ?? current;
        }
        return current;
    }

    public static string? FindOnPath(string name)
    {
        if (Path.IsPathFullyQualified(name)) return File.Exists(name) ? name : null;
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(x => x.Length > 0).Select(x => Path.Combine(x, name)).FirstOrDefault(File.Exists);
    }

    public static LutrisGame ReadLutris(string path)
    {
#if WINDOWS
        throw new PlatformNotSupportedException("Windows 不使用 Lutris。");
#else
        if (!File.Exists(path)) throw new ArgumentException($"找不到 Lutris 配置：{path}");
        // Lutris cascades global -> runner -> game. Parsing only YAML events avoids reflection.
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string root = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
        foreach (string file in new[] { Path.Combine(root, "system.yml"), Path.Combine(root, "runners/wine.yml"), path })
        {
            if (!File.Exists(file)) continue;
            foreach (var pair in ReadYamlScalars(file)) map[pair.Key] = pair.Value;
        }
        var game = new LutrisGame
        {
            Name = Path.GetFileNameWithoutExtension(path), ConfigPath = path,
            Executable = AppPaths.Expand(map.GetValueOrDefault("game.exe", "")),
            Prefix = AppPaths.Expand(map.GetValueOrDefault("game.prefix", "")),
            RunnerVersion = map.GetValueOrDefault("wine.version", ""),
            RunnerPath = AppPaths.Expand(map.GetValueOrDefault("wine.custom_wine_path", "")),
            Arguments = map.GetValueOrDefault("game.args", ""),
            Wrapper = map.GetValueOrDefault("system.prefix_command", "")
        };
        foreach (var item in map.Where(x => x.Key.StartsWith("system.env.", StringComparison.Ordinal)))
            game.Environment[item.Key["system.env.".Length..]] = item.Value;
        foreach (var item in new[] { ("wine.esync", "WINEESYNC"), ("wine.fsync", "WINEFSYNC") })
            if (map.TryGetValue(item.Item1, out string? value))
                game.Environment.TryAdd(item.Item2, value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "1" : "0");
        var overrides = map.Where(x => x.Key.StartsWith("wine.overrides.", StringComparison.Ordinal))
            .Select(x => x.Key["wine.overrides.".Length..] + "=" + x.Value);
        string dlls = string.Join(';', overrides);
        if (dlls.Length > 0) game.Environment.TryAdd("WINEDLLOVERRIDES", dlls);
        return game;
#endif
    }

#if !WINDOWS
    internal static Dictionary<string, string> ReadYamlScalars(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new ArgumentException("Lutris 配置超过 2MB。");
        using var reader = File.OpenText(path);
        var parser = new Parser(reader);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        ReadMap(parser, "", result, 0);
        parser.Consume<DocumentEnd>();
        parser.Consume<StreamEnd>();
        return result;
    }

    private static void ReadMap(IParser parser, string prefix, Dictionary<string, string> values, int depth)
    {
        if (depth > 16) throw new ArgumentException("Lutris YAML 嵌套过深。");
        parser.Consume<MappingStart>();
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            string key = parser.Consume<Scalar>().Value;
            string name = prefix + key;
            if (parser.TryConsume<Scalar>(out var scalar)) values[name] = scalar.Value;
            else if (parser.Accept<MappingStart>(out _)) ReadMap(parser, name + ".", values, depth + 1);
            else if (parser.Accept<AnchorAlias>(out _))
                throw new ArgumentException("此 Lutris 配置使用 YAML 引用，请在启动设置中改用手动运行器。");
            else parser.SkipThisAndNestedEvents();
        }
    }
#endif

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex SteamLibraryPath();
}
