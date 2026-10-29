using Microsoft.Data.Sqlite;

namespace TaskIsland.Infrastructure;

internal static class DatabaseSchema
{
    internal const int CurrentVersion = 3;
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
            var legacy = version == 1 && Convert.ToInt64(Scalar(db,
                "SELECT COUNT(*) FROM pragma_table_info('tasks') WHERE name='archive_at_utc'")) == 1;
            if (legacy)
            {
                // Known pre-M2 schema. Preserve records and related tables; all changes roll back together.
                foreach (var (oldName, newName) in new[] {
                    ("created_at_utc", "CreatedAtUtc"), ("updated_at_utc", "UpdatedAtUtc"),
                    ("due_at_utc", "DueAtUtc"), ("archive_at_utc", "ArchiveAtUtc"),
                    ("archived_at_utc", "ArchivedAtUtc"), ("archive_reason", "ArchiveReason"),
                    ("reminder_revision", "ReminderRevision") })
                    Execute(db, $"ALTER TABLE tasks RENAME COLUMN {oldName} TO {newName};", tx);
            }
            if (version == 0) Execute(db, """
                CREATE TABLE Tasks(
                  Id TEXT PRIMARY KEY, Title TEXT NOT NULL, CreatedAtUtc INTEGER NOT NULL,
                  UpdatedAtUtc INTEGER NOT NULL, DueAtUtc INTEGER, ArchiveAtUtc INTEGER NOT NULL,
                  ArchivedAtUtc INTEGER, ArchiveReason INTEGER, ReminderRevision INTEGER NOT NULL);
                CREATE TABLE Settings(Key TEXT PRIMARY KEY, Value TEXT);
                """, tx);
            if (version < 2) Execute(db, """
                CREATE TABLE ReminderDeliveries(
                  TaskId TEXT NOT NULL REFERENCES Tasks(Id) ON DELETE CASCADE,
                  Revision INTEGER NOT NULL, Status INTEGER NOT NULL, Attempts INTEGER NOT NULL,
                  NextAttemptAtUtc INTEGER, LastError TEXT, PRIMARY KEY(TaskId, Revision));
                CREATE INDEX IX_Tasks_ActiveArchive ON Tasks(ArchiveAtUtc) WHERE ArchivedAtUtc IS NULL;
                CREATE INDEX IX_Tasks_Archived ON Tasks(ArchivedAtUtc DESC, Id) WHERE ArchivedAtUtc IS NOT NULL;
                """, tx);
            if (legacy) Execute(db, """
                INSERT INTO ReminderDeliveries(TaskId,Revision,Status,Attempts,NextAttemptAtUtc,LastError)
                  SELECT task_id,reminder_revision,status,attempt_count,next_attempt_at_utc,last_error FROM reminder_deliveries;
                DROP TABLE reminder_deliveries;
                """, tx);
            Execute(db, """
                CREATE TABLE IF NOT EXISTS attachments(id TEXT PRIMARY KEY,task_id TEXT NOT NULL REFERENCES Tasks(Id) ON DELETE CASCADE,stored_relative_path TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS pending_file_cleanup(id INTEGER PRIMARY KEY,stored_relative_path TEXT NOT NULL,created_at_utc INTEGER NOT NULL DEFAULT 0);
                """, tx);
            foreach (var (name, definition) in new[] { ("original_name", "TEXT NOT NULL DEFAULT ''"), ("size_bytes", "INTEGER NOT NULL DEFAULT 0"),
                ("kind", "TEXT NOT NULL DEFAULT 'file'"), ("added_at_utc", "INTEGER NOT NULL DEFAULT 0") })
                if (Convert.ToInt64(Scalar(db, $"SELECT COUNT(*) FROM pragma_table_info('attachments') WHERE name='{name}'")) == 0)
                    Execute(db, $"ALTER TABLE attachments ADD COLUMN {name} {definition};", tx);
            if (Convert.ToInt64(Scalar(db, "SELECT COUNT(*) FROM pragma_table_info('pending_file_cleanup') WHERE name='created_at_utc'")) == 0)
                Execute(db, "ALTER TABLE pending_file_cleanup ADD COLUMN created_at_utc INTEGER NOT NULL DEFAULT 0;", tx);
            Execute(db, "CREATE INDEX IF NOT EXISTS IX_Attachments_Task ON attachments(task_id); PRAGMA user_version=3;", tx);
            tx.Commit();
        }
        Execute(db, "PRAGMA journal_mode=WAL;");
    }
    private static object? Scalar(SqliteConnection db, string sql) { using var command = db.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
    private static void Execute(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    { using var command = db.CreateCommand(); command.Transaction = tx; command.CommandText = sql; command.ExecuteNonQuery(); }
}
