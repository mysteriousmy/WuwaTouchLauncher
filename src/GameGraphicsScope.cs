using System.Text.Json;

// Only known, existing graphics rows are changed. A database upgrade that removes
// a key fails before any UPDATE; it must never silently invent a new schema.
internal sealed class GameGraphicsScope : IDisposable
{
    private readonly string journal;
    private GameGraphicsScope(string journalPath) => journal = journalPath;

    public static GameGraphicsScope Apply(string database, Dictionary<string, string> values, string journal)
    {
        Recover(journal);
        if (values.Count == 0) return new(journal);
        using var db = QualityDatabase.Open(database);
        db.Execute("BEGIN IMMEDIATE");
        try
        {
            var change = new GraphicsDatabaseChange { Database = database };
            foreach (var pair in values)
            {
                string original = db.ReadSetting(pair.Key)
                    ?? throw new InvalidOperationException($"此游戏版本没有图形设置 {pair.Key}；请将对应选项设为“沿用游戏设置”。未写入任何图形数据库项。");
                change.Values.Add(new() { Key = pair.Key, Original = original, Applied = pair.Value });
            }
            string flag = db.ReadSetting("IsUsingQualityPreset")
                ?? throw new InvalidOperationException("游戏数据库缺少自定义画质标记，请先在游戏中保存一次图形设置。");
            string custom = flag switch
            {
                "\"___0B___\"" or "\"___1B___\"" => "\"___0B___\"",
                "true" or "false" => "false",
                _ => throw new InvalidOperationException("无法识别此版本的画质预设标记，未修改数据库。")
            };
            change.Values.Add(new() { Key = "IsUsingQualityPreset", Original = flag, Applied = custom });
            string temporary = journal + ".new";
            File.WriteAllText(temporary, JsonSerializer.Serialize(change, ProductJson.Default.GraphicsDatabaseChange));
            File.Move(temporary, journal, true);
            foreach (var value in change.Values) db.ReplaceSetting(value.Key, value.Original, value.Applied);
            db.Execute("COMMIT");
        }
        catch
        {
            db.Dispose(); // SQLite rolls back an uncommitted transaction on close.
            Recover(journal);
            throw;
        }
        return new(journal);
    }

    public static void Recover(string journal)
    {
        if (!File.Exists(journal)) return;
        var change = JsonSerializer.Deserialize(File.ReadAllText(journal), ProductJson.Default.GraphicsDatabaseChange)
            ?? throw new InvalidOperationException("图形数据库恢复记录为空。");
        using var db = QualityDatabase.Open(change.Database);
        db.Execute("BEGIN IMMEDIATE");
        // If the user selected a preset during the game, all its fine settings belong
        // to that choice. Otherwise restore each unchanged row independently.
        var flag = change.Values.Single(x => x.Key == "IsUsingQualityPreset");
        if (db.ReadSetting(flag.Key) == flag.Applied)
        {
            bool changedInGame = change.Values.Where(x => x.Key != flag.Key)
                .Any(x => db.ReadSetting(x.Key) != x.Applied);
            foreach (var value in change.Values.Where(x => x.Key != flag.Key))
                db.ReplaceSetting(value.Key, value.Applied, value.Original);
            if (!changedInGame) db.ReplaceSetting(flag.Key, flag.Applied, flag.Original);
        }
        db.Execute("COMMIT");
        File.Delete(journal);
    }

    public void Dispose() => Recover(journal);
}

internal sealed class GraphicsDatabaseChange
{
    public string Database { get; set; } = "";
    public List<GraphicsDatabaseValue> Values { get; set; } = [];
}

internal sealed class GraphicsDatabaseValue
{
    public string Key { get; set; } = "";
    public string Original { get; set; } = "";
    public string Applied { get; set; } = "";
}
