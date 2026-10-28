using Microsoft.Data.Sqlite;
using TaskIsland.Application;
using TaskIsland.Domain;

namespace TaskIsland.Infrastructure;

internal sealed class SqliteTaskSession(SqliteConnection db, SqliteTransaction tx) : ITaskWriter
{
    public string? ReadSetting(string key)
    {
        using var command = Command("SELECT Value FROM Settings WHERE Key=$key", ("$key", key));
        return command.ExecuteScalar() as string;
    }
    public void WriteSetting(string key, string? value)
    {
        using var command = value is null
            ? Command("DELETE FROM Settings WHERE Key=$key", ("$key", key))
            : Command("INSERT INTO Settings(Key,Value) VALUES($key,$value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value", ("$key", key), ("$value", value));
        command.ExecuteNonQuery();
    }
    public TaskItem? Find(Guid id) => QueryTasks("SELECT * FROM Tasks WHERE Id=$id", ("$id", id.ToString())).SingleOrDefault();
    public IReadOnlyList<TaskItem> ListActive() => QueryTasks("SELECT * FROM Tasks WHERE ArchivedAtUtc IS NULL");
    public IReadOnlyList<TaskItem> ListArchived(int skip, int take)
        => QueryTasks("SELECT * FROM Tasks WHERE ArchivedAtUtc IS NOT NULL ORDER BY ArchivedAtUtc DESC, Id LIMIT $take OFFSET $skip", ("$take", take), ("$skip", skip));
    public Guid? GetPinnedTaskId()
    {
        using var command = Command("SELECT Value FROM Settings WHERE Key='PinnedTaskId'");
        return Guid.TryParse(command.ExecuteScalar() as string, out var id) ? id : null;
    }
    public void SetPinnedTaskId(Guid? id)
    {
        if (id is null)
        {
            // Absence represents no pin, including legacy Settings tables with NOT NULL values.
            using var clear = Command("DELETE FROM Settings WHERE Key='PinnedTaskId'");
            clear.ExecuteNonQuery();
            return;
        }
        using var command = Command("INSERT INTO Settings(Key,Value) VALUES('PinnedTaskId',$id) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value", ("$id", id?.ToString()));
        command.ExecuteNonQuery();
    }
    public void Save(TaskItem item)
    {
        using var command = Command("""
            INSERT INTO Tasks(Id,Title,CreatedAtUtc,UpdatedAtUtc,DueAtUtc,ArchiveAtUtc,ArchivedAtUtc,ArchiveReason,ReminderRevision)
            VALUES($id,$title,$created,$updated,$due,$archive,$archived,$reason,$revision)
            ON CONFLICT(Id) DO UPDATE SET Title=excluded.Title, UpdatedAtUtc=excluded.UpdatedAtUtc,
              DueAtUtc=excluded.DueAtUtc, ArchiveAtUtc=excluded.ArchiveAtUtc, ArchivedAtUtc=excluded.ArchivedAtUtc,
              ArchiveReason=excluded.ArchiveReason, ReminderRevision=excluded.ReminderRevision
            """, ("$id", item.Id.ToString()), ("$title", item.Title), ("$created", Millis(item.CreatedAtUtc)),
            ("$updated", Millis(item.UpdatedAtUtc)), ("$due", Millis(item.DueAtUtc)), ("$archive", Millis(item.ArchiveAtUtc)),
            ("$archived", Millis(item.ArchivedAtUtc)), ("$reason", item.ArchiveReason is { } reason ? (int)reason : null), ("$revision", item.ReminderRevision));
        command.ExecuteNonQuery();
    }
    public ReminderDelivery? FindReminder(Guid taskId, int revision)
    {
        using var command = Command("SELECT Status,Attempts,NextAttemptAtUtc,LastError FROM ReminderDeliveries WHERE TaskId=$id AND Revision=$revision", ("$id", taskId.ToString()), ("$revision", revision));
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(taskId, revision, (ReminderStatus)reader.GetInt32(0), reader.GetInt32(1), Instant(reader, 2), reader.IsDBNull(3) ? null : reader.GetString(3)) : null;
    }
    public void SaveReminder(ReminderDelivery delivery)
    {
        using var command = Command("""
            INSERT INTO ReminderDeliveries(TaskId,Revision,Status,Attempts,NextAttemptAtUtc,LastError)
            VALUES($id,$revision,$status,$attempts,$next,$error)
            ON CONFLICT(TaskId,Revision) DO UPDATE SET Status=excluded.Status,Attempts=excluded.Attempts,
              NextAttemptAtUtc=excluded.NextAttemptAtUtc,LastError=excluded.LastError
            """, ("$id", delivery.TaskId.ToString()), ("$revision", delivery.Revision), ("$status", (int)delivery.Status),
            ("$attempts", delivery.Attempts), ("$next", Millis(delivery.NextAttemptAtUtc)), ("$error", delivery.LastError));
        command.ExecuteNonQuery();
    }
    public void SkipUnsubmittedReminders(Guid taskId)
    {
        using var command = Command("UPDATE ReminderDeliveries SET Status=$skipped,NextAttemptAtUtc=NULL WHERE TaskId=$id AND Status IN ($pending,$failed)",
            ("$id", taskId.ToString()), ("$skipped", (int)ReminderStatus.Skipped), ("$pending", (int)ReminderStatus.Pending), ("$failed", (int)ReminderStatus.Failed));
        command.ExecuteNonQuery();
    }
    public void Delete(Guid id)
    {
        EnsureNoAttachments(id);
        using var command = Command("DELETE FROM Tasks WHERE Id=$id", ("$id", id.ToString()));
        command.ExecuteNonQuery();
    }
    public void DeleteAllArchived()
    {
        EnsureNoAttachments(null);
        using var command = Command("DELETE FROM Tasks WHERE ArchivedAtUtc IS NOT NULL");
        command.ExecuteNonQuery();
    }

    private void EnsureNoAttachments(Guid? taskId)
    {
        // Migrated databases can retain legacy attachment records. Until M4 coordinates
        // transactions with its cleanup queue, neither cascade nor orphan those copies.
        using var table = Command("SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='attachments' COLLATE NOCASE)");
        if (Convert.ToInt64(table.ExecuteScalar()) == 0) return;
        using var attached = taskId is { } id
            ? Command("SELECT EXISTS(SELECT 1 FROM attachments WHERE task_id=$id)", ("$id", id.ToString()))
            : Command("SELECT EXISTS(SELECT 1 FROM attachments AS a JOIN Tasks AS t ON a.task_id=t.Id WHERE t.ArchivedAtUtc IS NOT NULL)");
        if (Convert.ToInt64(attached.ExecuteScalar()) != 0)
            throw new InvalidOperationException("此日程含附件，暂时无法彻底删除");
    }

    private List<TaskItem> QueryTasks(string sql, params (string Key, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters); using var reader = command.ExecuteReader();
        var rows = new List<TaskItem>();
        while (reader.Read()) rows.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), Instant(reader, 2)!.Value,
            Instant(reader, 3)!.Value, Instant(reader, 4), Instant(reader, 5)!.Value, Instant(reader, 6),
            reader.IsDBNull(7) ? null : (ArchiveReason)reader.GetInt32(7), reader.GetInt32(8)));
        return rows;
    }
    private SqliteCommand Command(string sql, params (string Key, object? Value)[] parameters)
    {
        var command = db.CreateCommand(); command.Transaction = tx; command.CommandText = sql;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }
    private static long? Millis(DateTimeOffset? value) => value?.ToUnixTimeMilliseconds();
    private static DateTimeOffset? Instant(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(ordinal));
}
