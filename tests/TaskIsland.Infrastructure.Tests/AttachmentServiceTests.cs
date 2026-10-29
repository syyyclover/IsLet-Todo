using Microsoft.Data.Sqlite;
using TaskIsland.Application;
using TaskIsland.Domain;
using TaskIsland.Infrastructure;
using Xunit;

public sealed class AttachmentServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TaskIsland.ServiceCopy.Tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T00:00:00Z");
    [Fact] public async Task BatchKeepsSuccessAndReportsFailureWithoutSourceChanges()
    {
        await using var store = await Store(); var task = await Seed(store);
        var source = await Source(); var service = new AttachmentService(store, new ManagedAttachmentFiles(root), new FixedClock());
        var results = await service.AddAsync(task.Id, new[] { source, Path.Combine(root, "missing.txt"), source });
        Assert.True(results[0].Success); Assert.False(results[1].Success); Assert.True(results[2].Success);
        var items = await service.ListAsync(task.Id); Assert.Equal(2, items.Count);
        Assert.NotEqual(items[0].StoredRelativePath, items[1].StoredRelativePath);
        Assert.All(items, item => { Assert.Equal(Now, item.AddedAtUtc); Assert.Equal(9, item.SizeBytes); });
        Assert.Empty(await store.ReadAsync(tx => tx.ListPendingCleanup()));
        Assert.Equal("preserved", await File.ReadAllTextAsync(source));
    }
    [Fact] public async Task RegistrationFailureRollsBackAndCompensatesCopies()
    {
        await using var store = await Store(); var task = await Seed(store); var source = await Source();
        var service = new AttachmentService(new FailRegistration(store, task.Id), new ManagedAttachmentFiles(root), new FixedClock());
        var result = Assert.Single(await service.AddAsync(task.Id, new[] { source })); Assert.False(result.Success);
        Assert.Empty(await service.ListAsync(task.Id));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "attachments"), "*", SearchOption.AllDirectories));
        Assert.Empty(await store.ReadAsync(tx => tx.ListPendingCleanup()));
        Assert.Equal("preserved", await File.ReadAllTextAsync(source));
    }
    [Fact] public async Task OccupiedCopyStaysPendingUntilActuallyDeleted()
    {
        await using var store = await Store(); var task = await Seed(store); var source = await Source();
        var service = new AttachmentService(store, new ManagedAttachmentFiles(root), new FixedClock());
        var item = Assert.Single(await service.AddAsync(task.Id, new[] { source })).Attachment!;
        using (var occupied = new FileStream(service.GetManagedPath(item), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(await service.RemoveAsync(task.Id, item.Id));
            Assert.Single(await store.ReadAsync(tx => tx.ListPendingCleanup()));
            Assert.True(File.Exists(service.GetManagedPath(item)));
        }
        Assert.True(await service.RetryCleanupAsync());
        Assert.False(File.Exists(service.GetManagedPath(item)));
        Assert.Equal("preserved", await File.ReadAllTextAsync(source));
    }
    [Fact] public async Task CopyCancellationReleasesBusyLeaseAndIdleWaiter()
    {
        await using var store = await Store(); var task = await Seed(store); var source = await Source();
        var copier = new WaitingCopier(); var gate = new TaskMutationGate();
        var service = new AttachmentService(store, new ManagedAttachmentFiles(root, copier), new FixedClock(), gate);
        var work = service.AddAsync(task.Id, new[] { source });
        await copier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.IsCopying); Assert.True(gate.IsBusy(task.Id));
        var idle = service.WaitForIdleAsync(); Assert.False(idle.IsCompleted);
        service.CancelAll(); Assert.False(Assert.Single(await work).Success);
        await idle.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(gate.IsBusy(task.Id)); Assert.False(service.IsCopying);
        Assert.Empty(await service.ListAsync(task.Id)); Assert.Empty(await store.ReadAsync(tx => tx.ListPendingCleanup()));
        Assert.Equal("preserved", await File.ReadAllTextAsync(source));
    }
    [Fact] public async Task CapacityAndSizeLimitsRejectWithoutCopying()
    {
        await using var store = await Store(); var task = await Seed(store); var source = await Source();
        var service = new AttachmentService(store, new ManagedAttachmentFiles(root), new FixedClock());
        await store.WriteAsync(tx => { for (var i = 0; i < 20; i++) tx.AddAttachment(new(Guid.NewGuid(), task.Id, "file", $"{i}.txt", 1, "file", Now)); return 0; });
        Assert.False(Assert.Single(await service.AddAsync(task.Id, new[] { source })).Success);
        Assert.Equal(20, (await service.ListAsync(task.Id)).Count);
        var large = Path.Combine(root, "large.bin"); using (var file = File.Create(large)) file.SetLength(100L * 1024 * 1024 + 1);
        Assert.False(Assert.Single(await service.AddAsync(task.Id, new[] { large })).Success);
        Assert.False(Directory.Exists(Path.Combine(root, "attachments")));
    }
    private async Task<SqliteTaskStore> Store() { var store = new SqliteTaskStore(root); await store.InitializeAsync(); return store; }
    private static Task<TaskItem> Seed(ITaskStore store) => store.WriteAsync(tx => { var item = TaskRules.Create(Guid.NewGuid(), "任务", null, Now); tx.Save(item); return item; });
    private async Task<string> Source() { var path = Path.Combine(root, "source.txt"); await File.WriteAllTextAsync(path, "preserved"); return path; }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FailRegistration(ITaskStore inner, Guid taskId) : ITaskStore
    {
        public Task<T> ReadAsync<T>(Func<ITaskReader, T> read, CancellationToken ct = default) => inner.ReadAsync(read, ct);
        public Task<T> WriteAsync<T>(Func<ITaskWriter, T> write, CancellationToken ct = default) => inner.WriteAsync(tx =>
        { var result = write(tx); if (tx.ListAttachments(taskId).Count != 0) throw new IOException("simulated commit failure"); return result; }, ct);
    }
    private sealed class WaitingCopier : IAttachmentStreamCopier
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task CopyAsync(Stream source, Stream destination, IProgress<long>? progress, CancellationToken ct)
        { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TaskIsland.ServiceCopy.Tests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
