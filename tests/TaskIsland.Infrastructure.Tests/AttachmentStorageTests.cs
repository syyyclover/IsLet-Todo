using Microsoft.Data.Sqlite;
using TaskIsland.Domain;
using TaskIsland.Infrastructure;
using Xunit;

public sealed class AttachmentStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TaskIsland.Tests", Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(1000);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    [Fact] public async Task RemovalQueuesCopyAtomicallyAndRollbackPreservesRecords()
    {
        await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
        var task = TaskRules.Create(Guid.NewGuid(), "task", null, now);
        var attachment = new AttachmentItem(Guid.NewGuid(), task.Id, "source.txt", "copies/copy.txt", 3, "file", now);
        await store.WriteAsync(tx => { tx.Save(task); tx.AddAttachment(attachment); return 0; });
        Assert.Equal(attachment, Assert.Single(await store.ReadAsync(tx => tx.ListAttachments(task.Id))));
        Assert.Equal(1, (await store.ReadAsync(tx => tx.AttachmentCounts()))[task.Id]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync<int>(tx => { tx.RemoveAttachment(attachment.Id); throw new InvalidOperationException(); }));
        Assert.Single(await store.ReadAsync(tx => tx.ListAttachments(task.Id)));
        Assert.Empty(await store.ReadAsync(tx => tx.ListPendingCleanup()));
        await store.WriteAsync(tx => { tx.Delete(task.Id); return 0; });
        Assert.Empty(await store.ReadAsync(tx => tx.ListAttachments(task.Id)));
        var cleanup = Assert.Single(await store.ReadAsync(tx => tx.ListPendingCleanup()));
        Assert.Equal(attachment.StoredRelativePath, cleanup.StoredRelativePath);
        await store.WriteAsync(tx => { tx.CompleteCleanup(cleanup.Id); return 0; });
        Assert.Empty(await store.ReadAsync(tx => tx.ListPendingCleanup()));
    }
    [Fact] public async Task VersionTwoMigrationFailureRollsBackAndPreservesBackup()
    {
        Directory.CreateDirectory(root);
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "data.db")};Pooling=False"))
        {
            db.Open(); using var command = db.CreateCommand();
            command.CommandText = "CREATE TABLE attachments(id TEXT PRIMARY KEY,evidence TEXT); INSERT INTO attachments VALUES('keep','preserved'); PRAGMA user_version=2;";
            command.ExecuteNonQuery();
        }
        await using var store = new SqliteTaskStore(root);
        await Assert.ThrowsAsync<SqliteException>(() => store.InitializeAsync());
        using var current = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"); current.Open();
        using var query = current.CreateCommand(); query.CommandText = "PRAGMA user_version"; Assert.Equal(2L, query.ExecuteScalar());
        query.CommandText = "SELECT evidence FROM attachments"; Assert.Equal("preserved", query.ExecuteScalar());
        query.CommandText = "SELECT COUNT(*) FROM pragma_table_info('attachments') WHERE name='original_name'"; Assert.Equal(0L, query.ExecuteScalar());
        Assert.Single(Directory.GetFiles(Path.Combine(root, "backups"), "*.db"));
    }
    [Fact] public async Task VersionTwoLegacyAttachmentsAndQueueArePreservedAndBackedUp()
    {
        await using (var store = new SqliteTaskStore(root)) await store.InitializeAsync();
        // Emulate v2's minimal legacy auxiliary tables while keeping the known task schema.
        var taskId = Guid.NewGuid(); var attachmentId = Guid.NewGuid();
        using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "data.db")};Pooling=False"))
        {
            db.Open(); using var command = db.CreateCommand(); command.CommandText = $"""
                DROP TABLE attachments; DROP TABLE pending_file_cleanup;
                CREATE TABLE attachments(id TEXT PRIMARY KEY,task_id TEXT REFERENCES Tasks(Id),stored_relative_path TEXT);
                CREATE TABLE pending_file_cleanup(id INTEGER PRIMARY KEY,stored_relative_path TEXT);
                INSERT INTO Tasks VALUES('{taskId}','legacy',1000,1000,NULL,9999999999999,NULL,NULL,1);
                INSERT INTO attachments VALUES('{attachmentId}','{taskId}','copies/legacy.txt');
                INSERT INTO pending_file_cleanup VALUES(7,'copies/pending.txt'); PRAGMA user_version=2;
                """; command.ExecuteNonQuery();
        }
        await using var reopened = new SqliteTaskStore(root); await reopened.InitializeAsync();
        Assert.Equal("copies/legacy.txt", Assert.Single(await reopened.ReadAsync(tx => tx.ListAttachments(taskId))).StoredRelativePath);
        Assert.Equal(7, Assert.Single(await reopened.ReadAsync(tx => tx.ListPendingCleanup())).Id);
        Assert.Single(Directory.GetFiles(Path.Combine(root, "backups"), "*.db"));
        await reopened.WriteAsync(tx => { tx.Delete(taskId); return 0; });
        Assert.Equal(2, (await reopened.ReadAsync(tx => tx.ListPendingCleanup())).Count);
    }
}
