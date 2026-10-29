using TaskIsland.Application;
using TaskIsland.Domain;

namespace TaskIsland.Infrastructure;

internal sealed partial class SqliteTaskSession
{
    private long CleanupNow => (timeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds();
    public IReadOnlyList<AttachmentItem> ListAttachments(Guid taskId)
    {
        using var command = Command("SELECT id,task_id,original_name,stored_relative_path,size_bytes,kind,added_at_utc FROM attachments WHERE task_id=$task ORDER BY added_at_utc,id", ("$task", taskId.ToString()));
        using var reader = command.ExecuteReader(); var items = new List<AttachmentItem>();
        while (reader.Read()) items.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetString(5), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6))));
        return items;
    }
    public IReadOnlyDictionary<Guid, int> AttachmentCounts()
    {
        using var command = Command("SELECT task_id,COUNT(*) FROM attachments GROUP BY task_id");
        using var reader = command.ExecuteReader(); var counts = new Dictionary<Guid, int>();
        while (reader.Read()) counts.Add(Guid.Parse(reader.GetString(0)), reader.GetInt32(1));
        return counts;
    }
    public IReadOnlyList<PendingFileCleanup> ListPendingCleanup()
    {
        using var command = Command("SELECT id,stored_relative_path FROM pending_file_cleanup ORDER BY id");
        using var reader = command.ExecuteReader(); var items = new List<PendingFileCleanup>();
        while (reader.Read()) items.Add(new(reader.GetInt64(0), reader.GetString(1)));
        return items;
    }
    public void AddAttachment(AttachmentItem item)
    {
        using var command = Command("INSERT INTO attachments(id,task_id,original_name,stored_relative_path,size_bytes,kind,added_at_utc) VALUES($id,$task,$name,$path,$size,$kind,$added)",
            ("$id", item.Id.ToString()), ("$task", item.TaskId.ToString()), ("$name", item.OriginalName), ("$path", item.StoredRelativePath), ("$size", item.SizeBytes), ("$kind", item.Kind), ("$added", item.AddedAtUtc.ToUnixTimeMilliseconds()));
        command.ExecuteNonQuery();
    }
    public void RemoveAttachment(Guid id)
    {
        using var queue = Command("INSERT INTO pending_file_cleanup(stored_relative_path,created_at_utc) SELECT stored_relative_path,$cleanupNow FROM attachments WHERE id=$id", ("$id", id.ToString()), ("$cleanupNow", CleanupNow)); queue.ExecuteNonQuery();
        using var remove = Command("DELETE FROM attachments WHERE id=$id", ("$id", id.ToString())); remove.ExecuteNonQuery();
    }
    public void CompleteCleanup(long id) { using var command = Command("DELETE FROM pending_file_cleanup WHERE id=$id", ("$id", id)); command.ExecuteNonQuery(); }
    public void EnqueueCleanup(string storedRelativePath) { using var command = Command("INSERT INTO pending_file_cleanup(stored_relative_path,created_at_utc) VALUES($path,$cleanupNow)", ("$path", storedRelativePath), ("$cleanupNow", CleanupNow)); command.ExecuteNonQuery(); }
    private void QueueTaskCopies(string condition, params (string Key, object? Value)[] parameters)
    {
        using var queue = Command($"INSERT INTO pending_file_cleanup(stored_relative_path,created_at_utc) SELECT stored_relative_path,$cleanupNow FROM attachments WHERE {condition}", parameters.Concat(new[] { ("$cleanupNow", (object?)CleanupNow) }).ToArray()); queue.ExecuteNonQuery();
        using var remove = Command($"DELETE FROM attachments WHERE {condition}", parameters); remove.ExecuteNonQuery();
    }
}
