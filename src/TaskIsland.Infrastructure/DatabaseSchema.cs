using Microsoft.Data.Sqlite;

namespace TaskIsland.Infrastructure;

internal static class DatabaseSchema
{
    internal const int CurrentVersion = 2;
    public static void Initialize(SqliteConnection db, string backupDirectory)
    {
        var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"));
        if (version > CurrentVersion) throw new InvalidOperationException("数据库来自较新版本，已停止写入；请使用兼容版本打开。");
        if (version == 0 && Convert.ToInt64(Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")) != 0)
            throw new InvalidOperationException("数据库版本不明，保留原文件并停止初始化。");
        if (version is > 0 and < CurrentVersion)
        {
            Directory.CreateDirectory(backupDirectory);
            var path = Path.Combine(backupDirectory, $"before-v{CurrentVersion}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.db");
            using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            backup.Open();
            db.BackupDatabase(backup);
        }
        if (version < CurrentVersion)
        {
            using var tx = db.BeginTransaction();
            if (version == 0) Execute(db, """
                CREATE TABLE Tasks(
                  Id TEXT PRIMARY KEY, Title TEXT NOT NULL, CreatedAtUtc INTEGER NOT NULL,
                  UpdatedAtUtc INTEGER NOT NULL, DueAtUtc INTEGER, ArchiveAtUtc INTEGER NOT NULL,
                  ArchivedAtUtc INTEGER, ArchiveReason INTEGER, ReminderRevision INTEGER NOT NULL);
                CREATE TABLE Settings(Key TEXT PRIMARY KEY, Value TEXT);
                """, tx);
            Execute(db, """
                CREATE TABLE ReminderDeliveries(
                  TaskId TEXT NOT NULL REFERENCES Tasks(Id) ON DELETE CASCADE,
                  Revision INTEGER NOT NULL, Status INTEGER NOT NULL, Attempts INTEGER NOT NULL,
                  NextAttemptAtUtc INTEGER, LastError TEXT, PRIMARY KEY(TaskId, Revision));
                CREATE INDEX IX_Tasks_ActiveArchive ON Tasks(ArchiveAtUtc) WHERE ArchivedAtUtc IS NULL;
                CREATE INDEX IX_Tasks_Archived ON Tasks(ArchivedAtUtc DESC, Id) WHERE ArchivedAtUtc IS NOT NULL;
                PRAGMA user_version=2;
                """, tx);
            tx.Commit();
        }
        Execute(db, "PRAGMA journal_mode=WAL;");
    }
    private static object? Scalar(SqliteConnection db, string sql) { using var command = db.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
    private static void Execute(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    { using var command = db.CreateCommand(); command.Transaction = tx; command.CommandText = sql; command.ExecuteNonQuery(); }
}
