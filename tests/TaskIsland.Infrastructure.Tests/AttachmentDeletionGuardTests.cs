using Microsoft.Data.Sqlite;
using TaskIsland.Domain;
using TaskIsland.Infrastructure;
using Xunit;

public sealed class AttachmentDeletionGuardTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TaskIsland.AttachmentDeletion.Tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-28T00:00:00Z");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachedArchivedTaskRefusesDeletionAndRollsBackEntireTransaction(bool bulk)
    {
        await using var store = new SqliteTaskStore(root);
        await store.InitializeAsync();
        var attached = Archived("含旧附件");
        var other = Archived("无附件");
        await store.WriteAsync(tx => { tx.Save(attached); tx.Save(other); return 0; });
        var source = Path.Combine(root, "source.txt");
        var copy = Path.Combine(root, "saved-copy.txt");
        await File.WriteAllTextAsync(source, "source evidence");
        await File.WriteAllTextAsync(copy, "copy evidence");
        using (var db = Open(store.DatabasePath))
        {
            Execute(db, "CREATE TABLE attachments(id TEXT PRIMARY KEY,task_id TEXT REFERENCES Tasks(Id) ON DELETE CASCADE,stored_relative_path TEXT);");
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO attachments VALUES('attachment',$id,'saved-copy.txt')";
            insert.Parameters.AddWithValue("$id", attached.Id.ToString());
            insert.ExecuteNonQuery();
        }
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(tx =>
        {
            tx.WriteSetting("rollback-evidence", "must not survive");
            if (bulk) tx.DeleteAllArchived(); else tx.Delete(attached.Id);
            return 0;
        }));
        Assert.Equal("此日程含附件，暂时无法彻底删除", exception.Message);
        Assert.NotNull(await store.ReadAsync(tx => tx.Find(attached.Id)));
        Assert.NotNull(await store.ReadAsync(tx => tx.Find(other.Id)));
        Assert.Null(await store.ReadAsync(tx => tx.ReadSetting("rollback-evidence")));
        using (var db = Open(store.DatabasePath))
        {
            using var query = db.CreateCommand(); query.CommandText = "SELECT COUNT(*) FROM attachments";
            Assert.Equal(1L, query.ExecuteScalar());
        }
        Assert.Equal("source evidence", await File.ReadAllTextAsync(source));
        Assert.Equal("copy evidence", await File.ReadAllTextAsync(copy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TasksWithoutAttachmentTableCanStillBeDeleted(bool bulk)
    {
        await using var store = new SqliteTaskStore(root);
        await store.InitializeAsync();
        var task = Archived("无附件");
        await store.WriteAsync(tx => { tx.Save(task); return 0; });
        await store.WriteAsync(tx => { if (bulk) tx.DeleteAllArchived(); else tx.Delete(task.Id); return 0; });
        Assert.Null(await store.ReadAsync(tx => tx.Find(task.Id)));
    }

    [Fact]
    public async Task AttachmentsOnActiveTasksDoNotBlockDeletingOtherArchivedTasks()
    {
        await using var store = new SqliteTaskStore(root);
        await store.InitializeAsync();
        var active = TaskRules.Create(Guid.NewGuid(), "活跃含附件", null, Now);
        var archived = Archived("归档无附件");
        await store.WriteAsync(tx => { tx.Save(active); tx.Save(archived); return 0; });
        using (var db = Open(store.DatabasePath))
        {
            Execute(db, "CREATE TABLE attachments(id TEXT PRIMARY KEY,task_id TEXT REFERENCES Tasks(Id),stored_relative_path TEXT);");
            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO attachments VALUES('active-attachment',$id,'active.txt')";
            insert.Parameters.AddWithValue("$id", active.Id.ToString());
            insert.ExecuteNonQuery();
        }
        await store.WriteAsync(tx => { tx.DeleteAllArchived(); return 0; });
        Assert.NotNull(await store.ReadAsync(tx => tx.Find(active.Id)));
        Assert.Null(await store.ReadAsync(tx => tx.Find(archived.Id)));
    }

    private static TaskItem Archived(string title) => TaskRules.Create(Guid.NewGuid(), title, null, Now) with
        { ArchivedAtUtc = Now, ArchiveReason = ArchiveReason.Manual };
    private static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString());
        db.Open(); return db;
    }
    private static void Execute(SqliteConnection db, string sql) { using var command = db.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TaskIsland.AttachmentDeletion.Tests")) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(root);
        if (!resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test cleanup path.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
