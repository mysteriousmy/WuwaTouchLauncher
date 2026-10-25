using System.Text.Json;
using System.Text.RegularExpressions;
#if WINDOWS
using Microsoft.Win32;
#endif

internal sealed record InstalledGame(string Name, string Executable, string Source);

/// <summary>Reads installation metadata only; never walks arbitrary disks or modifies a launcher.</summary>
internal static partial class WindowsDiscovery
{
    private const int MaximumMetadataBytes = 1024 * 1024;
    private const string GameExecutableName = "Client-Win64-Shipping.exe";

    public static string? FindGameExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Path.GetFullPath(path);
            if (File.Exists(path) && Path.GetFileName(path).Equals(GameExecutableName, StringComparison.OrdinalIgnoreCase))
                return path;
            string root = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
            foreach (string relative in new[]
            {
                Path.Combine("Client", "Binaries", "Win64", GameExecutableName),
                Path.Combine("Binaries", "Win64", GameExecutableName),
                Path.Combine("Wuthering Waves Game", "Client", "Binaries", "Win64", GameExecutableName)
            })
            {
                string candidate = Path.Combine(root, relative);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception ex) when (IsMetadataError(ex)) { }
        return null;
    }

    public static void Scan(DiscoveryResult result)
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows()) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var registry = RegistryKey.OpenBaseKey(hive, view);
                using (var uninstall = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    foreach (string name in uninstall?.GetSubKeyNames().Take(4096) ?? [])
                    {
                        try
                        {
                            using var entry = uninstall!.OpenSubKey(name);
                            if (entry is null || !IsWuwa(name + " " + ReadValue(entry, "DisplayName"))) continue;
                            foreach (string field in new[] { "InstallPath", "InstallLocation", "LauncherPath", "DisplayIcon", "UninstallString" })
                                AddRegisteredPath(ReadValue(entry, field), "官方 / 安装记录", result, seen, roots);
                        }
                        catch (Exception ex) when (IsMetadataError(ex)) { }
                    }
                }
                using (var launcher = registry.OpenSubKey(@"SOFTWARE\kurogame\KRLauncher"))
                {
                    foreach (string name in launcher?.GetSubKeyNames().Take(128) ?? [])
                    {
                        if (!name.Contains("G152", StringComparison.OrdinalIgnoreCase) &&
                            !name.Contains("Aki", StringComparison.OrdinalIgnoreCase) && !IsWuwa(name)) continue;
                        using var entry = launcher!.OpenSubKey(name);
                        if (entry is not null)
                            AddRegisteredPath(ReadValue(entry, "SingleLauncherInstallPath"), "库洛启动器", result, seen, roots);
                    }
                }
                using var steam = registry.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (steam is not null)
                    foreach (string field in new[] { "SteamPath", "InstallPath" })
                        if (ReadValue(steam, field) is { Length: > 0 } path && Directory.Exists(path))
                            steamRoots.Add(path);
            }
            catch (Exception ex) when (IsMetadataError(ex))
            { result.Messages.Add($"部分安装记录无法读取（{hive}/{view}）。"); }
        }
        string standardSteam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(standardSteam)) steamRoots.Add(standardSteam);
        foreach (string root in steamRoots.ToArray())
        {
            string? text = ReadSmallFile(Path.Combine(root, "steamapps", "libraryfolders.vdf"));
            if (text is null) continue;
            foreach (Match match in VdfPairs().Matches(text))
                if (match.Groups[1].Value == "path") steamRoots.Add(UnescapeVdf(match.Groups[2].Value));
        }
        foreach (string root in steamRoots.Take(64))
        {
            foreach (string file in Files(Path.Combine(root, "steamapps"), "appmanifest_*.acf", 2048))
            {
                string? text = ReadSmallFile(file);
                if (text is null) continue;
                var fields = VdfPairs().Matches(text).Cast<Match>()
                    .GroupBy(m => m.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => UnescapeVdf(g.First().Groups[2].Value), StringComparer.OrdinalIgnoreCase);
                if (!IsWuwa(fields.GetValueOrDefault("name", ""))) continue;
                string directory = fields.GetValueOrDefault("installdir", "");
                if (directory.Length == 0 || Path.IsPathRooted(directory) ||
                    directory.Split('/', '\\').Any(part => part == "..")) continue;
                AddRegisteredPath(Path.Combine(root, "steamapps", "common", directory), "Steam", result, seen, roots);
            }
        }
        string epic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        foreach (string file in Files(epic, "*.item", 2048))
        {
            try
            {
                string? text = ReadSmallFile(file);
                if (text is null) continue;
                using var manifest = JsonDocument.Parse(text, new() { MaxDepth = 16 });
                if (IsWuwa(JsonString(manifest.RootElement, "DisplayName")))
                    AddRegisteredPath(JsonString(manifest.RootElement, "InstallLocation"), "Epic Games", result, seen, roots);
            }
            catch (Exception ex) when (IsMetadataError(ex)) { }
        }
        result.InstalledGames = result.InstalledGames.OrderBy(x => x.Source)
            .ThenBy(x => x.Executable, StringComparer.OrdinalIgnoreCase).ToList();
#endif
    }

#if WINDOWS
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string ReadValue(RegistryKey key, string name) => key.GetValue(name) as string ?? "";
#endif

    private static void AddRegisteredPath(string value, string source, DiscoveryResult result,
        HashSet<string> seen, HashSet<string> inspectedRoots)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            string path = Environment.ExpandEnvironmentVariables(value.Trim());
            if (path.StartsWith('"'))
            {
                int end = path.IndexOf('"', 1);
                if (end < 0) return;
                path = path[1..end];
            }
            else if (path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) is var end && end >= 0)
                path = path[..(end + 4)];
            if (!Path.IsPathFullyQualified(path)) return;
            string root = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
            foreach (string directory in new[] { root, Path.GetDirectoryName(root) ?? root })
            {
                if (!inspectedRoots.Add(directory)) continue;
                AddGame(directory, "鸣潮", source, result, seen);
                string? cached = ReadSmallFile(Path.Combine(directory, "kr_game_cache", "kr_game_temp.bin"));
                if (cached is null) continue;
                try
                {
                    byte[] data = Convert.FromBase64String(cached.Trim());
                    for (int i = 0; i < data.Length; i++) data[i] ^= 0x63;
                    using var document = JsonDocument.Parse(data, new() { MaxDepth = 16 });
                    AddGame(JsonString(document.RootElement, "installDirPath"), "鸣潮", source, result, seen);
                    string resources = JsonString(document.RootElement, "resources");
                    if (resources.Length == 0) continue;
                    using var entries = JsonDocument.Parse(resources, new() { MaxDepth = 8 });
                    if (entries.RootElement.ValueKind != JsonValueKind.Array) continue;
                    foreach (var entry in entries.RootElement.EnumerateArray().Take(32))
                    {
                        string tier = JsonString(entry, "bundleName");
                        AddGame(JsonString(entry, "installDirPath"), tier is "HD" or "UHD" or "SD" ? $"鸣潮 · {tier}" : "鸣潮",
                            source, result, seen);
                    }
                }
                catch (Exception ex) when (IsMetadataError(ex)) { }
            }
        }
        catch (Exception ex) when (IsMetadataError(ex)) { }
    }

    private static void AddGame(string path, string name, string source, DiscoveryResult result, HashSet<string> seen)
    {
        if (!Path.IsPathFullyQualified(path)) return;
        if (FindGameExecutable(path) is { } executable && seen.Add(executable))
            result.InstalledGames.Add(new(name, executable, source));
    }

    private static string JsonString(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string? ReadSmallFile(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumMetadataBytes) return null;
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (IsMetadataError(ex)) { return null; }
    }

    private static string[] Files(string path, string pattern, int limit)
    {
        try { return Directory.Exists(path) ? Directory.EnumerateFiles(path, pattern).Take(limit).ToArray() : []; }
        catch (Exception ex) when (IsMetadataError(ex)) { return []; }
    }

    private static bool IsWuwa(string name) => name.Contains("Wuthering Waves", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("鸣潮", StringComparison.Ordinal) || name.Contains("鳴潮", StringComparison.Ordinal);
    private static string UnescapeVdf(string value) => value.Replace("\\\\", "\\").Replace("\\\"", "\"");
    private static bool IsMetadataError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or ArgumentException or JsonException or FormatException or System.Security.SecurityException;

    [GeneratedRegex("\"([^\"]+)\"\\s+\"((?:\\\\.|[^\"\\\\])*)\"")]
    private static partial Regex VdfPairs();
}
