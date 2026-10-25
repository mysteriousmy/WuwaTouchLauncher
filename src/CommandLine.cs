using System.Text;

internal static class CommandLine
{
    // A deterministic argv grammar, not a shell: no expansion, redirection or command substitution.
    // Single/double quotes group spaces; backslash only escapes quotes, backslashes or whitespace.
    // Consequently an unquoted Windows path C:\Games\Client.exe retains its backslashes.
    public static string[] Split(string text)
    {
        var result = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        bool started = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\0' || c is '\r' or '\n') throw new ArgumentException("启动参数不能包含空字符或换行；环境变量请使用环境变量输入框。");
            if (c == '\\' && quote != '\'' && i + 1 < text.Length &&
                (text[i + 1] is '\\' or '"' || (quote == '\0' && (text[i + 1] == '\'' || char.IsWhiteSpace(text[i + 1])))))
            { token.Append(text[++i]); started = true; continue; }
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else token.Append(c);
                started = true;
            }
            else if (c is '\'' or '"') { quote = c; started = true; }
            else if (char.IsWhiteSpace(c))
            {
                if (started) { result.Add(token.ToString()); token.Clear(); started = false; }
            }
            else { token.Append(c); started = true; }
        }
        if (quote != '\0') throw new ArgumentException("启动参数有未闭合的引号。");
        if (started) result.Add(token.ToString());
        return result.ToArray();
    }

    public static string Join(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));
    public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static void ValidateEnvironment(string key, string value)
    {
        if (key.Length == 0 || !(char.IsAsciiLetter(key[0]) || key[0] == '_') ||
            key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')) || value.IndexOfAny(['\0', '\n', '\r']) >= 0)
            throw new ArgumentException($"环境变量格式无效：{key}。每行使用 NAME=值。");
    }

    public static Dictionary<string, string> ParseEnvironment(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            int at = line.IndexOf('=');
            if (at <= 0) throw new ArgumentException($"环境变量缺少等号：{line}");
            string key = line[..at].Trim(), value = line[(at + 1)..];
            ValidateEnvironment(key, value);
            result[key] = value;
        }
        return result;
    }
}

internal readonly record struct Hotkey(uint Modifiers, uint Key)
{
    public static Hotkey Parse(string text)
    {
        uint modifiers = 0, key = 0;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= 2; break;
                case "alt": modifiers |= 1; break;
                case "shift": modifiers |= 4; break;
                case "win": modifiers |= 8; break;
                default:
                    if (key != 0) throw new ArgumentException("快捷键只能包含一个主键。");
                    if (part.StartsWith('F') || part.StartsWith('f'))
                    {
                        if (int.TryParse(part[1..], out int number) && number is >= 1 and <= 24)
                            key = (uint)(111 + number);
                    }
                    if (key == 0 && part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]))
                        key = char.ToUpperInvariant(part[0]);
                    if (key == 0) throw new ArgumentException("快捷键主键支持 F1–F24、A–Z、0–9，例如 Ctrl+Alt+F10。");
                    break;
            }
        }
        if (key == 0 || modifiers == 0) throw new ArgumentException("快捷键需含 Ctrl / Alt / Shift / Win 和一个主键。");
        return new Hotkey(modifiers, key);
    }
}
