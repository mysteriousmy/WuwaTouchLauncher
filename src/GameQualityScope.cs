using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

internal sealed class GameQualityScope : IDisposable
{
    private readonly string journal;
    private GameQualityScope(string path) => journal = path;

    public static GameQualityScope Apply(string database, int? quality, string journal, bool activatePreset = false)
    {
        Recover(journal);
        if (quality is null) return new GameQualityScope(journal);
        if (quality is < 1 or > 4 || !Path.IsPathFullyQualified(database) || !File.Exists(database))
            throw new ArgumentException("GameImageQuality 必须为 1–4，且 GameSettingsDatabase 必须存在。");
        using var db = QualityDatabase.Open(database);
        db.Execute("BEGIN IMMEDIATE");
        try
        {
            var change = new QualityChange
            {
                Database = database, Original = db.Read(),
                Applied = quality.Value.ToString(CultureInfo.InvariantCulture)
            };
            if (activatePreset)
            {
                change.OriginalPresetFlag = db.ReadSetting("IsUsingQualityPreset")
                    ?? throw new InvalidOperationException("游戏数据库没有 IsUsingQualityPreset；请先选“沿用游戏设置”启动游戏。");
                change.AppliedPresetFlag = change.OriginalPresetFlag switch
                {
                    "\"___0B___\"" or "\"___1B___\"" => "\"___1B___\"",
                    "false" or "true" => "true",
                    _ => throw new InvalidOperationException("无法识别此版本的画质预设标记，未猜测或修改其值。")
                };
            }
            string temporary = journal + ".new";
            File.WriteAllText(temporary, JsonSerializer.Serialize(change, AppJsonContext.Default.QualityChange));
            File.Move(temporary, journal, true);
            db.Replace(change.Original, change.Applied);
            if (change.AppliedPresetFlag is not null)
                db.ReplaceSetting("IsUsingQualityPreset", change.OriginalPresetFlag!, change.AppliedPresetFlag);
            db.Execute("COMMIT");
        }
        catch
        {
            db.Dispose(); // Closing rolls back any uncommitted transaction.
            Recover(journal);
            throw;
        }
        return new GameQualityScope(journal);
    }

    private static void Recover(string journal)
    {
        if (!File.Exists(journal)) return;
        var change = JsonSerializer.Deserialize(File.ReadAllText(journal), AppJsonContext.Default.QualityChange)
            ?? throw new InvalidOperationException("画质预设恢复记录为空。");
        using var db = QualityDatabase.Open(change.Database);
        // Preserve a later choice made in the game. The preset value and activation flag
        // form one setting; restoring just one could undo the user's new choice.
        db.Execute("BEGIN IMMEDIATE");
        if (db.Read() == change.Applied &&
            (change.AppliedPresetFlag is null || db.ReadSetting("IsUsingQualityPreset") == change.AppliedPresetFlag))
        {
            db.Replace(change.Applied, change.Original);
            if (change.AppliedPresetFlag is not null)
                db.ReplaceSetting("IsUsingQualityPreset", change.AppliedPresetFlag, change.OriginalPresetFlag!);
        }
        db.Execute("COMMIT");
        File.Delete(journal);
    }

    public void Dispose() => Recover(journal);
}

internal sealed class QualityChange
{
    public string Database { get; set; } = "";
    public string Original { get; set; } = "";
    public string Applied { get; set; } = "";
    public string? OriginalPresetFlag { get; set; }
    public string? AppliedPresetFlag { get; set; }
}

internal sealed class QualityDatabase : SafeHandleZeroOrMinusOneIsInvalid
{
    private QualityDatabase(nint pointer) : base(true) => SetHandle(pointer);
    protected override bool ReleaseHandle() => Sqlite.Close(handle) == 0;

    public static QualityDatabase Open(string path)
    {
        int result = Sqlite.Open(path, out nint pointer, 2, 0); // Read/write; never create a database.
        var db = new QualityDatabase(pointer);
        try
        {
            db.Check(result);
            db.Check(Sqlite.BusyTimeout(db, 5000));
            return db;
        }
        catch { db.Dispose(); throw; }
    }

    private void Check(int result)
    {
        if (result != 0)
            throw new IOException($"SQLite 画质设置失败（{result}）：{Marshal.PtrToStringUTF8(Sqlite.Error(this))}");
    }

    private QualityStatement Prepare(string sql)
    {
        int result = Sqlite.Prepare(this, sql, -1, out nint pointer, 0);
        var statement = new QualityStatement(pointer);
        try { Check(result); return statement; }
        catch { statement.Dispose(); throw; }
    }

    public string Read()
    {
        return ReadSetting("ImageQuality")
            ?? throw new InvalidOperationException("游戏数据库没有 ImageQuality；未创建或猜测记录。");
    }

    public string? ReadSetting(string key)
    {
        using var statement = Prepare("SELECT value FROM LocalStorage WHERE key=?1");
        Check(Sqlite.BindText(statement, 1, key, -1, -1));
        int result = Sqlite.Step(statement);
        if (result == 101) return null;
        if (result != 100) Check(result);
        return Marshal.PtrToStringUTF8(Sqlite.ColumnText(statement, 0))
            ?? throw new InvalidOperationException("ImageQuality 值为空。");
    }

    public void Replace(string expected, string replacement)
        => ReplaceSetting("ImageQuality", expected, replacement);

    public void ReplaceSetting(string key, string expected, string replacement)
    {
        using var statement = Prepare("UPDATE LocalStorage SET value=?1 WHERE key=?3 AND value=?2");
        Check(Sqlite.BindText(statement, 1, replacement, -1, -1)); // SQLITE_TRANSIENT copies UTF-8 text.
        Check(Sqlite.BindText(statement, 2, expected, -1, -1));
        Check(Sqlite.BindText(statement, 3, key, -1, -1));
        int result = Sqlite.Step(statement);
        if (result != 101) Check(result);
    }

    public void Execute(string sql)
    {
        using var statement = Prepare(sql);
        int result = Sqlite.Step(statement);
        if (result != 101) Check(result);
    }
}

internal sealed class QualityStatement : SafeHandleZeroOrMinusOneIsInvalid
{
    public QualityStatement(nint pointer) : base(true) => SetHandle(pointer);
    protected override bool ReleaseHandle() => Sqlite.Finalize(handle) == 0;
}

internal static partial class Sqlite
{
#if WINDOWS
    private const string Library = "winsqlite3.dll";
#else
    private const string Library = "libsqlite3.so.0";
#endif
    [LibraryImport(Library, EntryPoint = "sqlite3_open_v2", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Open(string path, out nint database, int flags, nint vfs);
    [LibraryImport(Library, EntryPoint = "sqlite3_close_v2")]
    internal static partial int Close(nint database);
    [LibraryImport(Library, EntryPoint = "sqlite3_busy_timeout")]
    internal static partial int BusyTimeout(QualityDatabase database, int milliseconds);
    [LibraryImport(Library, EntryPoint = "sqlite3_errmsg")]
    internal static partial nint Error(QualityDatabase database);
    [LibraryImport(Library, EntryPoint = "sqlite3_prepare_v2", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Prepare(QualityDatabase database, string sql, int bytes, out nint statement, nint tail);
    [LibraryImport(Library, EntryPoint = "sqlite3_bind_text", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int BindText(QualityStatement statement, int index, string text, int bytes, nint destructor);
    [LibraryImport(Library, EntryPoint = "sqlite3_step")]
    internal static partial int Step(QualityStatement statement);
    [LibraryImport(Library, EntryPoint = "sqlite3_column_text")]
    internal static partial nint ColumnText(QualityStatement statement, int column);
    [LibraryImport(Library, EntryPoint = "sqlite3_finalize")]
    internal static partial int Finalize(nint statement);
}
