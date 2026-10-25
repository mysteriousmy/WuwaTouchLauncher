using System.Text;
using System.Text.Json;
using System.Globalization;

// The original launcher used an optional legacy JSON row plus two scalar rows.
// Current game versions retain the scalar rows but may omit GameQualitySetting.
internal sealed class FpsUnlockScope : IDisposable
{
    private readonly string journal;
    private FpsUnlockScope(string path) => journal = path;

    public static FpsUnlockScope Apply(string database, int target, string journal)
    {
        Recover(journal);
        if (target == 0) return new(journal);
        if (target is not (120 or 240 or 420)) throw new ArgumentException("无效的解锁帧率。");
        using var db = QualityDatabase.Open(database);
        db.Execute("BEGIN IMMEDIATE");
        try
        {
            var change = new FpsDatabaseChange { Database = database };
            foreach (var (key, value) in new[] { ("CustomFrameRate", "3"), ("PcVsync", "0") })
            {
                string original = db.ReadSetting(key)
                    ?? throw new InvalidOperationException($"游戏数据库缺少 {key}，无法应用帧率解锁配置。请先在游戏中保存图形设置。");
                change.Values.Add(new() { Key = key, Original = original, Applied = value });
            }
            if (db.ReadSetting("GameQualitySetting") is { } legacy)
                change.Values.Add(new() { Key = "GameQualitySetting", Original = legacy, Applied = Rewrite(legacy, target) });
            File.WriteAllText(journal + ".new", JsonSerializer.Serialize(change, ProductJson.Default.FpsDatabaseChange));
            File.Move(journal + ".new", journal, true);
            foreach (var value in change.Values) db.ReplaceSetting(value.Key, value.Original, value.Applied);
            db.Execute("COMMIT");
        }
        catch
        {
            db.Dispose();
            Recover(journal);
            throw;
        }
        return new(journal);
    }

    private static string Rewrite(string value, int? target = null, string? original = null, string? applied = null)
    {
        using var document = JsonDocument.Parse(value);
        using var old = original is null ? null : JsonDocument.Parse(original);
        using var requested = applied is null ? null : JsonDocument.Parse(applied);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("无法识别旧版画质配置，未修改帧率数据库。");
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name is "KeyCustomFrameRate" or "KeyPcVsync")
                {
                    if (target.HasValue)
                    {
                        writer.WriteNumber(property.Name, property.Name == "KeyCustomFrameRate" ? target.Value : 0);
                        continue;
                    }
                    if (old!.RootElement.TryGetProperty(property.Name, out var before) &&
                        requested!.RootElement.TryGetProperty(property.Name, out var after) &&
                        MatchesAppliedInteger(property.Value, after))
                    {
                        writer.WritePropertyName(property.Name); before.WriteTo(writer);
                        continue;
                    }
                }
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static bool MatchesAppliedInteger(JsonElement current, JsonElement applied)
    {
        // These two fields were written as integers. Avoid a recursive JSON
        // comparison (and its execution-stack probe on musl's small stacks).
        // Normalize the decimal representation exactly, without rounding away
        // a later edit such as 420.0000000000000000000000000001 or 1e-100.
        if (current.ValueKind != JsonValueKind.Number || applied.ValueKind != JsonValueKind.Number ||
            !applied.TryGetInt32(out int expected)) return false;
        if (current.TryGetInt32(out int integer)) return integer == expected;
        string number = current.GetRawText();
        int exponentAt = number.IndexOfAny(['e', 'E']);
        string mantissa = exponentAt < 0 ? number : number[..exponentAt];
        bool negative = mantissa[0] == '-';
        if (negative) mantissa = mantissa[1..];
        int point = mantissa.IndexOf('.');
        int fraction = point < 0 ? 0 : mantissa.Length - point - 1;
        string digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return expected == 0;
        if (negative || expected <= 0) return false;
        int exponent = 0;
        if (exponentAt >= 0 && !int.TryParse(number.AsSpan(exponentAt + 1),
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return false;
        string significant = digits.TrimEnd('0');
        string wanted = expected.ToString(CultureInfo.InvariantCulture);
        string wantedSignificant = wanted.TrimEnd('0');
        return significant == wantedSignificant &&
            (long)exponent - fraction + digits.Length - significant.Length == wanted.Length - wantedSignificant.Length;
    }

    public static void Recover(string journal)
    {
        if (!File.Exists(journal)) return;
        var change = JsonSerializer.Deserialize(File.ReadAllText(journal), ProductJson.Default.FpsDatabaseChange)
            ?? throw new InvalidOperationException("帧率恢复记录为空。");
        using var db = QualityDatabase.Open(change.Database);
        db.Execute("BEGIN IMMEDIATE");
        foreach (var value in change.Values)
        {
            string? current = db.ReadSetting(value.Key);
            if (current == value.Applied) db.ReplaceSetting(value.Key, current, value.Original);
            else if (value.Key == "GameQualitySetting" && current is not null)
            {
                // Keep unrelated in-game edits, and keep a frame-rate choice changed in game.
                string restored = Rewrite(current, original: value.Original, applied: value.Applied);
                db.ReplaceSetting(value.Key, current, restored);
            }
        }
        db.Execute("COMMIT");
        File.Delete(journal);
    }

    public void Dispose() => Recover(journal);
}

internal sealed class FpsDatabaseChange
{
    public string Database { get; set; } = "";
    public List<GraphicsDatabaseValue> Values { get; set; } = [];
}
