using Microsoft.Extensions.Time.Testing;
using TaskIsland.Application;
using TaskIsland.Infrastructure;
using Xunit;

public class SchedulerTests
{
    [Fact] public async Task FakeTimeDrivesDueAndArchiveWithoutWaitingForRealHours()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskIsland.Tests", Guid.NewGuid().ToString("N"));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero));
        await using var store = new SqliteTaskStore(root);
        try
        {
            await store.InitializeAsync(); var service = new TaskService(store, clock);
            var task = await service.CreateTaskAsync("定时", clock.GetUtcNow().AddHours(1));
            await using var scheduler = new TaskIsland.Application.TaskScheduler(service, clock);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            scheduler.CycleCompleted += () => ready.TrySetResult();
            scheduler.Start(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromHours(1));
            await Eventually(async () => await store.ReadAsync(tx => tx.FindReminder(task.Id, 1)) is not null);
            clock.Advance(TimeSpan.FromHours(24));
            await Eventually(async () => (await service.ListActiveAsync()).Tasks.Count == 0);
            Assert.Single(await service.ListArchivedAsync());
        }
        finally { await store.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static async Task Eventually(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition()) await Task.Delay(10, timeout.Token);
    }
}
