using System.Text.Json;

// A launch profile only owns the keys it changes. Game writes to other keys
// during the session survive restoration.
internal sealed class IniOverrideScope : IDisposable
{
    private readonly string journalPath;
    private IniOverrideScope(string path) => journalPath = path;

    public static IniOverrideScope Apply(IniOverride[] overrides, string journalPath)
    {
        Recover(journalPath);
        var changes = new List<IniChange>();
        var contents = new Dictionary<string, string>();
        foreach (var item in overrides)
        {
            if (!Path.IsPathFullyQualified(item.Path))
                throw new ArgumentException($"画质配置文件必须使用绝对路径：{item.Path}");
            if (item.Section.Length == 0 || item.Section.IndexOfAny(['[', ']', '\r', '\n']) >= 0 ||
                item.Values.Any(x => x.Key.Length == 0 || x.Key.IndexOfAny(['=', '\r', '\n']) >= 0 ||
                                     x.Value.IndexOfAny(['\r', '\n']) >= 0))
                throw new ArgumentException("画质覆盖包含无效的 INI 节、键或值。");
            string original = contents.TryGetValue(item.Path, out var previous)
                ? previous : File.Exists(item.Path) ? File.ReadAllText(item.Path) : "";
            changes.Add(new IniChange { Path = item.Path, Section = item.Section,
                Values = item.Values, OriginalText = original,
                Existed = File.Exists(item.Path) || contents.ContainsKey(item.Path) });
            contents[item.Path] = Edit(original, item.Section, item.Values
                .ToDictionary(x => x.Key, x => (string?)x.Value));
        }
        if (changes.Count == 0) return new IniOverrideScope(journalPath);
        WriteAtomic(journalPath, JsonSerializer.Serialize(changes.ToArray(), AppJsonContext.Default.IniChangeArray));
        try
        {
            foreach (var file in contents) WriteAtomic(file.Key, file.Value);
        }
        catch
        {
            Recover(journalPath);
            throw;
        }
        return new IniOverrideScope(journalPath);
    }

    public static void Recover(string journalPath)
    {
        if (!File.Exists(journalPath)) return;
        var changes = JsonSerializer.Deserialize(File.ReadAllText(journalPath), AppJsonContext.Default.IniChangeArray)
            ?? throw new InvalidOperationException("画质恢复记录为空。");
        foreach (var item in changes.Reverse())
        {
            if (!File.Exists(item.Path)) continue;
            string current = File.ReadAllText(item.Path);
            var restore = new Dictionary<string, string?>();
            foreach (var pair in item.Values)
            {
                // Respect a later edit by the game or the user.
                if (Get(current, item.Section, pair.Key) == pair.Value)
                    restore[pair.Key] = Get(item.OriginalText, item.Section, pair.Key);
            }
            string next = Edit(current, item.Section, restore);
            // Exact restore also retains original formatting when nothing else changed.
            string applied = Edit(item.OriginalText, item.Section, item.Values
                .ToDictionary(x => x.Key, x => (string?)x.Value));
            if (!item.Existed && current == applied) File.Delete(item.Path);
            else WriteAtomic(item.Path, current == applied ? item.OriginalText : next);
        }
        File.Delete(journalPath);
    }

    private static string? Get(string text, string wantedSection, string wantedKey)
    {
        string section = "";
        string? result = null;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) section = trimmed[1..^1];
            else if (section.Equals(wantedSection, StringComparison.OrdinalIgnoreCase))
            {
                int index = line.IndexOf('=');
                if (index > 0 && line[..index].Trim().Equals(wantedKey, StringComparison.OrdinalIgnoreCase))
                    result = line[(index + 1)..].Trim();
            }
        }
        return result;
    }

    private static string Edit(string text, string wantedSection, Dictionary<string, string?> values)
    {
        if (values.Count == 0) return text;
        var keys = new HashSet<string>(values.Keys, StringComparer.OrdinalIgnoreCase);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        string section = "";
        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) section = trimmed[1..^1];
            else if (section.Equals(wantedSection, StringComparison.OrdinalIgnoreCase))
            {
                int index = lines[i].IndexOf('=');
                if (index > 0 && keys.Contains(lines[i][..index].Trim())) lines.RemoveAt(i--);
            }
        }
        var additions = values.Where(x => x.Value is not null).Select(x => $"{x.Key}={x.Value}").ToArray();
        if (additions.Length != 0)
        {
            int index = lines.FindLastIndex(x => x.Trim().Equals($"[{wantedSection}]", StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                lines.Add($"[{wantedSection}]");
                index = lines.Count - 1;
            }
            lines.InsertRange(index + 1, additions);
        }
        return string.Join(newline, lines);
    }

    private static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".wuwa-touch-new";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, true);
    }

    public void Dispose() => Recover(journalPath);
}

internal class IniOverride
{
    public string Path { get; set; } = "";
    public string Section { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = [];
}

internal sealed class IniChange : IniOverride
{
    public string OriginalText { get; set; } = "";
    public bool Existed { get; set; } = true;
}
