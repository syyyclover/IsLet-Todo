using Microsoft.Extensions.Time.Testing;
using TaskIsland.Application;
using TaskIsland.Domain;
using TaskIsland.Infrastructure;
using Xunit;

public sealed class ReminderDispatcherTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TaskIsland.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 28, 2, 0, 0, TimeSpan.Zero));
    private SqliteTaskStore store = null!;
    private TaskService service = null!;
    public async Task InitializeAsync() { store = new(root); await store.InitializeAsync(); service = new(store, clock); }
    public async Task DisposeAsync() { await store.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact] public async Task RecoveryCombinesDueTasksAndNeverResubmitsAcceptedRevision()
    {
        var a = await service.CreateTaskAsync("a", clock.GetUtcNow().AddHours(1));
        await service.CreateTaskAsync("b", clock.GetUtcNow().AddHours(1));
        var expired = await service.CreateTaskAsync("expired", null);
        // Due tasks archive 24 hours after due; recover just before that boundary.
        clock.Advance(TimeSpan.FromHours(25).Subtract(TimeSpan.FromMinutes(1)));
        var sender = new Sender(); var dispatcher = new ReminderDispatcher(service, store, sender, clock);
        await Task.WhenAll(dispatcher.DispatchAsync(), dispatcher.DispatchAsync());
        Assert.Equal(2, Assert.Single(sender.Batches).Count);
        Assert.DoesNotContain(sender.Batches[0], item => item.Id == expired.Id);
        Assert.Equal(ReminderStatus.Submitted, (await store.ReadAsync(tx => tx.FindReminder(a.Id, 1)))!.Status);
    }

    [Fact] public async Task FailuresRetryAfterTenAndSixtySecondsThenStop()
    {
        var task = await service.CreateTaskAsync("retry", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(1));
        var sender = new Sender { Failure = true }; var dispatcher = new ReminderDispatcher(service, store, sender, clock);
        await dispatcher.DispatchAsync();
        Assert.Equal(clock.GetUtcNow().AddSeconds(10), await dispatcher.GetNextEventAtAsync());
        await dispatcher.DispatchAsync(); Assert.Single(sender.Batches);
        clock.Advance(TimeSpan.FromSeconds(10)); await dispatcher.DispatchAsync();
        Assert.Equal(clock.GetUtcNow().AddSeconds(60), await dispatcher.GetNextEventAtAsync());
        clock.Advance(TimeSpan.FromSeconds(60)); await dispatcher.DispatchAsync();
        clock.Advance(TimeSpan.FromHours(1)); await dispatcher.DispatchAsync();
        Assert.Equal(3, sender.Batches.Count);
        var delivery = (await store.ReadAsync(tx => tx.FindReminder(task.Id, 1)))!;
        Assert.Equal(ReminderStatus.Failed, delivery.Status); Assert.Equal(3, delivery.Attempts);
        Assert.Equal("notification_submission_failed", delivery.LastError);
    }

    [Fact] public async Task EditDuringSubmissionDoesNotResurrectOldReminder()
    {
        var task = await service.CreateTaskAsync("edit", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(1));
        var sender = new Sender { OnSend = () => service.UpdateTaskAsync(task.Id, "changed", null) };
        await new ReminderDispatcher(service, store, sender, clock).DispatchAsync();
        Assert.Equal(ReminderStatus.Skipped, (await store.ReadAsync(tx => tx.FindReminder(task.Id, 1)))!.Status);
    }
    [Fact] public async Task ArchivedTaskIsNotRetriedAndRetryStateSurvivesDispatcherRestart()
    {
        var a = await service.CreateTaskAsync("retry", clock.GetUtcNow().AddHours(1));
        var b = await service.CreateTaskAsync("archive", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(1));
        await new ReminderDispatcher(service, store, new Sender { Failure = true }, clock).DispatchAsync();
        await service.ArchiveTaskAsync(b.Id, ArchiveReason.Completed);
        clock.Advance(TimeSpan.FromSeconds(10));
        var accepted = new Sender();
        await new ReminderDispatcher(service, store, accepted, clock).DispatchAsync();
        Assert.Equal(a.Id, Assert.Single(Assert.Single(accepted.Batches)).Id);
        Assert.Equal(ReminderStatus.Skipped, (await store.ReadAsync(tx => tx.FindReminder(b.Id, 1)))!.Status);
        Assert.Equal(ReminderStatus.Submitted, (await store.ReadAsync(tx => tx.FindReminder(a.Id, 1)))!.Status);
    }

    [Fact] public async Task CallerCancellationDoesNotConsumeRetryBudget()
    {
        var task = await service.CreateTaskAsync("cancel", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(1));
        using var cancel = new CancellationTokenSource();
        var sender = new Sender { OnSend = () => { cancel.Cancel(); return Task.FromCanceled(cancel.Token); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReminderDispatcher(service, store, sender, clock).DispatchAsync(cancel.Token));
        Assert.Equal(0, (await store.ReadAsync(tx => tx.FindReminder(task.Id, 1)))!.Attempts);
        var accepted = new Sender();
        await new ReminderDispatcher(service, store, accepted, clock).DispatchAsync();
        Assert.Single(accepted.Batches);
    }
    [Fact] public async Task SchedulerUsesRetryDeadlineBeforeSixtySecondCalibration()
    {
        await service.CreateTaskAsync("timer", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(1));
        var sender = new Sender { Failure = true };
        var dispatcher = new ReminderDispatcher(service, store, sender, clock);
        await using var scheduler = new TaskIsland.Application.TaskScheduler(service, clock, dispatcher);
        var cycles = System.Threading.Channels.Channel.CreateUnbounded<bool>();
        scheduler.CycleCompleted += () => cycles.Writer.TryWrite(true);
        scheduler.Start();
        await cycles.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        // Reconcile emits Changed and may enqueue an immediate second cycle.
        clock.Advance(TimeSpan.FromSeconds(10));
        while (sender.Batches.Count < 2)
            await cycles.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, sender.Batches.Count);
    }
    [Fact] public async Task CancellationBeforeFinalSubmissionPreservesRemainingAttempt()
    {
        var task = await service.CreateTaskAsync("last attempt", clock.GetUtcNow().AddHours(1));
        clock.Advance(TimeSpan.FromHours(1)); await service.ReconcileTimeAsync();
        await store.WriteAsync(tx => { tx.SaveReminder(new(task.Id, 1, ReminderStatus.Failed, 2, clock.GetUtcNow(), "notification_submission_failed")); return 0; });
        using var cancel = new CancellationTokenSource();
        var sender = new Sender();
        var interruptedStore = new CancelBeforeSecondRead(store, cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReminderDispatcher(service, interruptedStore, sender, clock).DispatchAsync(cancel.Token));
        Assert.Empty(sender.Batches);
        Assert.Equal(2, (await store.ReadAsync(tx => tx.FindReminder(task.Id, 1)))!.Attempts);
        await new ReminderDispatcher(service, store, sender, clock).DispatchAsync();
        Assert.Single(sender.Batches);
        Assert.Equal(ReminderStatus.Submitted, (await store.ReadAsync(tx => tx.FindReminder(task.Id, 1)))!.Status);
    }
    private sealed class CancelBeforeSecondRead(ITaskStore inner, CancellationTokenSource cancel) : ITaskStore
    {
        private int reads;
        public Task<T> ReadAsync<T>(Func<ITaskReader, T> read, CancellationToken ct = default)
        {
            if (++reads == 2) cancel.Cancel();
            return inner.ReadAsync(read, ct);
        }
        public Task<T> WriteAsync<T>(Func<ITaskWriter, T> write, CancellationToken ct = default) => inner.WriteAsync(write, ct);
    }
    private sealed class Sender : IReminderSender
    {
        public List<IReadOnlyList<TaskItem>> Batches { get; } = [];
        public bool Failure { get; init; }
        public Func<Task>? OnSend { get; init; }
        public async Task SendAsync(IReadOnlyList<TaskItem> tasks, CancellationToken ct = default)
        { Batches.Add(tasks); if (OnSend is not null) await OnSend(); if (Failure) throw new InvalidOperationException("sensitive body must not persist"); }
    }
}
