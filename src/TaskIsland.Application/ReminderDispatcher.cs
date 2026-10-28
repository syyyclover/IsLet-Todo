using TaskIsland.Domain;

namespace TaskIsland.Application;

// Completion means the platform accepted submission, not that the user saw it.
public interface IReminderSender
{
    Task SendAsync(IReadOnlyList<TaskItem> tasks, CancellationToken ct = default);
}

public sealed class ReminderDispatcher(TaskService service, ITaskStore store, IReminderSender sender, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task DispatchAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await service.ReconcileTimeAsync(ct).ConfigureAwait(false);
            var candidates = await store.ReadAsync(tx => tx.ListActive().Where(item =>
                Eligible(item, tx.FindReminder(item.Id, item.ReminderRevision), clock.GetUtcNow()))
                .OrderBy(item => item.Id).ToArray(), ct).ConfigureAwait(false);
            if (candidates.Length == 0) return;
            var revisions = candidates.ToDictionary(item => item.Id, item => item.ReminderRevision);
            // Recheck current state and content immediately before submission. Attempts are
            // only consumed after a definite sender outcome; cancellation cannot burn a retry.
            var batch = await store.ReadAsync(tx => candidates.Select(item => tx.Find(item.Id))
                .Where(current => current is not null && revisions[current.Id] == current.ReminderRevision &&
                    Eligible(current, tx.FindReminder(current.Id, current.ReminderRevision), clock.GetUtcNow()))
                .Select(current => current!).ToArray(), ct).ConfigureAwait(false);
            if (batch.Length == 0) return;
            bool accepted;
            try { await sender.SendAsync(batch, ct).ConfigureAwait(false); accepted = true; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { accepted = false; }
            await store.WriteAsync(tx =>
            {
                foreach (var item in batch)
                {
                    var current = tx.Find(item.Id);
                    var delivery = tx.FindReminder(item.Id, item.ReminderRevision);
                    if (current is null || current.ReminderRevision != item.ReminderRevision || current.ArchivedAtUtc is not null ||
                        delivery is null || delivery.Status is ReminderStatus.Skipped or ReminderStatus.Submitted) continue;
                    var attempts = delivery.Attempts + 1;
                    tx.SaveReminder(delivery with { Status = accepted ? ReminderStatus.Submitted : ReminderStatus.Failed, Attempts = attempts,
                        NextAttemptAtUtc = accepted || attempts >= 3 ? null : clock.GetUtcNow().AddSeconds(attempts == 1 ? 10 : 60),
                        LastError = accepted ? null : "notification_submission_failed" });
                }
                return 0;
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task<DateTimeOffset?> GetNextEventAtAsync(CancellationToken ct = default)
    {
        var next = await service.GetNextEventAtAsync(ct).ConfigureAwait(false);
        var retry = await store.ReadAsync(tx => tx.ListActive()
            .Select(item => tx.FindReminder(item.Id, item.ReminderRevision))
            .Where(delivery => delivery is { Attempts: < 3, Status: ReminderStatus.Pending or ReminderStatus.Failed })
            .Select(delivery => delivery!.NextAttemptAtUtc ?? clock.GetUtcNow()).Cast<DateTimeOffset?>().Min(), ct).ConfigureAwait(false);
        return next is null ? retry : retry is null ? next : next < retry ? next : retry;
    }

    private static bool Eligible(TaskItem item, ReminderDelivery? delivery, DateTimeOffset now) =>
        item.ArchivedAtUtc is null && item.ArchiveAtUtc > now && item.DueAtUtc <= now &&
        delivery is { Attempts: < 3, Status: ReminderStatus.Pending or ReminderStatus.Failed } &&
        (delivery.NextAttemptAtUtc is null || delivery.NextAttemptAtUtc <= now);
}
