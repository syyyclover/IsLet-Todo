using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using TaskIsland.Application;
using TaskIsland.Domain;
using TaskIsland.Infrastructure;
using Xunit;

public sealed class ArchiveAttachmentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TaskIsland.ArchiveAttachments.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-10-29T00:00:00Z"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory]
    [InlineData(ArchiveReason.Completed)] [InlineData(ArchiveReason.Manual)] [InlineData(ArchiveReason.Automatic)]
    public async Task ArchiveRemovesRecordsQueuesCopiesAndRestoreDoesNotRestoreAttachments(ArchiveReason reason)
    {
        await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
        var tasks = new TaskService(store, clock); var task = await tasks.CreateTaskAsync("task", null);
        var source = Path.Combine(root, "source.txt"); await File.WriteAllTextAsync(source, "source preserved");
        var attachments = new AttachmentService(store, new ManagedAttachmentFiles(root), clock);
        var item = Assert.Single(await attachments.AddAsync(task.Id, new[] { source })).Attachment!;
        using (var occupied = new FileStream(attachments.GetManagedPath(item), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            if (reason == ArchiveReason.Automatic) { clock.Advance(TimeSpan.FromHours(25)); await tasks.ReconcileTimeAsync(); }
            else await tasks.ArchiveTaskAsync(task.Id, reason);
            Assert.Equal(reason, Assert.Single(await tasks.ListArchivedAsync()).ArchiveReason);
            Assert.Empty(await attachments.ListAsync(task.Id));
            Assert.Single(await store.ReadAsync(tx => tx.ListPendingCleanup()));
            Assert.False(await attachments.RetryCleanupAsync());
            Assert.True(File.Exists(attachments.GetManagedPath(item)));
            await tasks.RestoreTaskAsync(task.Id);
            Assert.Empty(await attachments.ListAsync(task.Id));
        }
        Assert.True(await attachments.RetryCleanupAsync());
        Assert.False(File.Exists(attachments.GetManagedPath(item)));
        Assert.Equal("source preserved", await File.ReadAllTextAsync(source));
    }

    [Fact] public async Task ReconcilePreservesHistoricalArchivedAttachmentsAndDefersLiveCopies()
    {
        await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
        var gate = new TaskMutationGate(); var tasks = new TaskService(store, clock, gate);
        var historical = await tasks.CreateTaskAsync("historical", null);
        var copying = await tasks.CreateTaskAsync("copying", null);
        await tasks.ArchiveTaskAsync(historical.Id, ArchiveReason.Manual);
        await store.WriteAsync(tx =>
        {
            tx.AddAttachment(new(Guid.NewGuid(), historical.Id, "old", "old.txt", 1, "file", clock.GetUtcNow()));
            tx.AddAttachment(new(Guid.NewGuid(), copying.Id, "current", "current.txt", 1, "file", clock.GetUtcNow())); return 0;
        });
        clock.Advance(TimeSpan.FromHours(25));
        using (gate.Acquire(copying.Id))
        {
            await tasks.ReconcileTimeAsync();
            Assert.Single((await tasks.ListActiveAsync()).Tasks);
            await Assert.ThrowsAsync<InvalidOperationException>(() => tasks.ArchiveTaskAsync(copying.Id, ArchiveReason.Completed));
            Assert.Single(await store.ReadAsync(tx => tx.ListAttachments(copying.Id)));
        }
        await tasks.ReconcileTimeAsync();
        Assert.Empty(await store.ReadAsync(tx => tx.ListAttachments(copying.Id)));
        Assert.Single(await store.ReadAsync(tx => tx.ListAttachments(historical.Id)));
        Assert.Equal("current.txt", Assert.Single(await store.ReadAsync(tx => tx.ListPendingCleanup())).StoredRelativePath);
    }
    [Fact] public async Task CleanupQueueFailureRollsBackArchiveAndAttachmentRemoval()
    {
        await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
        var tasks = new TaskService(store, clock); var task = await tasks.CreateTaskAsync("rollback", null);
        var item = new AttachmentItem(Guid.NewGuid(), task.Id, "name", "attachments/copy.txt", 1, "file", clock.GetUtcNow());
        await store.WriteAsync(tx => { tx.AddAttachment(item); return 0; });
        using (var db = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
        {
            db.Open(); using var command = db.CreateCommand(); command.CommandText = "CREATE TRIGGER fail_cleanup BEFORE INSERT ON pending_file_cleanup BEGIN SELECT RAISE(ABORT,'test queue failure'); END"; command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<SqliteException>(() => tasks.ArchiveTaskAsync(task.Id, ArchiveReason.Completed));
        Assert.Single((await tasks.ListActiveAsync()).Tasks);
        Assert.Equal(item, Assert.Single(await store.ReadAsync(tx => tx.ListAttachments(task.Id))));
        Assert.Empty(await store.ReadAsync(tx => tx.ListPendingCleanup()));
    }
}
