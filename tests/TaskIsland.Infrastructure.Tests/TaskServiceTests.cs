using Microsoft.Extensions.Time.Testing;
using TaskIsland.Application;
using TaskIsland.Domain;
using TaskIsland.Infrastructure;
using Xunit;

public class TaskServiceTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TaskIsland.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 28, 2, 0, 0, TimeSpan.Zero));
    private SqliteTaskStore store = null!;
    private TaskService service = null!;
    public async Task InitializeAsync() { store = new(root); await store.InitializeAsync(); service = new(store, clock); }
    public async Task DisposeAsync() { await store.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact] public async Task PinReplacementArchiveAndRestoreAreAtomic()
    {
        var a = await service.CreateTaskAsync("甲", null); var b = await service.CreateTaskAsync("乙", null);
        await service.SetPinnedTaskAsync(a.Id); await service.SetPinnedTaskAsync(b.Id);
        Assert.Equal(b.Id, (await service.ListActiveAsync()).Tasks[0].Id);
        await service.ArchiveTaskAsync(b.Id, ArchiveReason.Completed);
        Assert.Null((await service.ListActiveAsync()).PinnedTaskId);
        await service.RestoreTaskAsync(b.Id);
        Assert.Null((await service.ListActiveAsync()).PinnedTaskId);
        Assert.Equal(2, (await service.ListActiveAsync()).Tasks.Single(x => x.Id == b.Id).ReminderRevision);
    }
    [Fact] public async Task DueCreatesExactlyOnePendingReminderPerRevision()
    {
        var task = await service.CreateTaskAsync("到期", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(2)); await service.ReconcileTimeAsync(); await service.ReconcileTimeAsync();
        Assert.Equal(ReminderStatus.Pending, (await store.ReadAsync(tx => tx.FindReminder(task.Id, 1)))!.Status);
        task = await service.UpdateTaskAsync(task.Id, task.Title, clock.GetUtcNow().AddHours(1));
        Assert.Equal(ReminderStatus.Skipped, (await store.ReadAsync(tx => tx.FindReminder(task.Id, 1)))!.Status);
        clock.Advance(TimeSpan.FromHours(1)); await service.ReconcileTimeAsync();
        Assert.Equal(ReminderStatus.Pending, (await store.ReadAsync(tx => tx.FindReminder(task.Id, 2)))!.Status);
    }
    [Fact] public async Task StartupArchivesOldTaskWithoutQueuingOldNotification()
    {
        var item = await service.CreateTaskAsync("关机期间过期", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(30)); await service.ReconcileTimeAsync();
        Assert.Empty((await service.ListActiveAsync()).Tasks);
        Assert.Single(await service.ListArchivedAsync());
        Assert.Null(await store.ReadAsync(tx => tx.FindReminder(item.Id, 1)));
    }
    [Fact] public async Task CompletedTaskSkipsPendingReminderAndCanBeRestoredWithoutDue()
    {
        var task = await service.CreateTaskAsync("完成", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(2)); await service.ReconcileTimeAsync();
        await service.ArchiveTaskAsync(task.Id, ArchiveReason.Completed);
        Assert.Equal(ReminderStatus.Skipped, (await store.ReadAsync(tx => tx.FindReminder(task.Id, 1)))!.Status);
        await service.RestoreTaskAsync(task.Id);
        var restored = Assert.Single((await service.ListActiveAsync()).Tasks);
        Assert.Null(restored.DueAtUtc);
        Assert.Equal(clock.GetUtcNow().AddHours(24), restored.ArchiveAtUtc);
    }
    [Fact] public async Task FailedWriteDoesNotPublishChangeOrReplaceTitle()
    {
        var task = await service.CreateTaskAsync("保留", null); var changes = 0; service.Changed += () => changes++;
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateTaskAsync(task.Id, " ", null));
        Assert.Equal("保留", Assert.Single((await service.ListActiveAsync()).Tasks).Title);
        Assert.Equal(0, changes);
    }
    [Fact] public async Task RenewKeepsRevisionAndChangingTimeInvalidatesIt()
    {
        var task = await service.CreateTaskAsync("续期", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(2)); await service.RenewArchiveAsync(task.Id);
        Assert.Equal(1, Assert.Single((await service.ListActiveAsync()).Tasks).ReminderRevision);
        task = await service.UpdateTaskAsync(task.Id, task.Title, null);
        Assert.Equal(2, task.ReminderRevision); Assert.Equal(clock.GetUtcNow().AddHours(24), task.ArchiveAtUtc);
    }
    [Fact] public async Task ClockMovingBackDoesNotDuplicateReminder()
    {
        var task = await service.CreateTaskAsync("时钟", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(2)); await service.ReconcileTimeAsync();
        var before = await store.ReadAsync(tx => tx.FindReminder(task.Id, 1));
        var earlierClock = new FakeTimeProvider(clock.GetUtcNow().AddHours(-2));
        var serviceAfterClockChange = new TaskService(store, earlierClock);
        await serviceAfterClockChange.ReconcileTimeAsync();
        earlierClock.Advance(TimeSpan.FromHours(2)); await serviceAfterClockChange.ReconcileTimeAsync();
        Assert.Equal(before, await store.ReadAsync(tx => tx.FindReminder(task.Id, 1)));
    }
    [Fact] public async Task IdleReconcileDoesNotOpenWriteTransaction()
    {
        var counted = new CountedStore(store); var idleService = new TaskService(counted, clock);
        await idleService.ReconcileTimeAsync();
        Assert.Equal(0, counted.Writes);
    }
    [Fact] public async Task OnlyArchivedTasksMayBeDeletedAndArchivePagesAreBounded()
    {
        var task = await service.CreateTaskAsync("删除保护", null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteArchivedTaskAsync(task.Id));
        await service.ArchiveTaskAsync(task.Id, ArchiveReason.Manual);
        Assert.Single(await service.ListArchivedAsync(0, 1)); Assert.Empty(await service.ListArchivedAsync(1, 1));
        await service.DeleteArchivedTaskAsync(task.Id); Assert.Empty(await service.ListArchivedAsync());
    }
    [Fact] public async Task NextEventChangesWithRenewalAndArchive()
    {
        var now = clock.GetUtcNow(); var task = await service.CreateTaskAsync("调度", now.AddHours(1));
        Assert.Equal(now.AddHours(1), await service.GetNextEventAtAsync());
        clock.Advance(TimeSpan.FromHours(2)); await service.ReconcileTimeAsync(); await service.RenewArchiveAsync(task.Id);
        Assert.Equal(now.AddHours(26), await service.GetNextEventAtAsync());
        await service.ArchiveTaskAsync(task.Id, ArchiveReason.Manual); Assert.Null(await service.GetNextEventAtAsync());
    }
    private sealed class CountedStore(ITaskStore inner) : ITaskStore
    {
        public int Writes { get; private set; }
        public Task<T> ReadAsync<T>(Func<ITaskReader,T> read, CancellationToken ct = default) => inner.ReadAsync(read, ct);
        public Task<T> WriteAsync<T>(Func<ITaskWriter,T> write, CancellationToken ct = default) { Writes++; return inner.WriteAsync(write, ct); }
    }
}
