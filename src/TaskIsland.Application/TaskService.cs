using TaskIsland.Domain;

namespace TaskIsland.Application;

public sealed class TaskService(ITaskStore store, TimeProvider clock, TaskMutationGate? mutationGate = null)
{
    public event Action? Changed;
    private DateTimeOffset Now => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
    public Task<TaskItem> CreateTaskAsync(string title, DateTimeOffset? dueAt, CancellationToken ct = default) => MutateAsync(tx =>
    { var task = TaskRules.Create(Guid.NewGuid(), title, dueAt, Now); tx.Save(task); return task; }, ct);
    public Task<TaskItem> UpdateTaskAsync(Guid id, string title, DateTimeOffset? dueAt, CancellationToken ct = default) => MutateAsync(tx =>
    {
        var original = RequireTask(tx, id);
        if (original.DueAtUtc != dueAt) EnsureNotCopying(id);
        var task = TaskRules.Update(original, title, dueAt, Now);
        if (original.ReminderRevision != task.ReminderRevision) tx.SkipUnsubmittedReminders(id);
        tx.Save(task); return task;
    }, ct);
    public Task<ActiveTaskList> ListActiveAsync(CancellationToken ct = default) => store.ReadAsync(tx =>
    { var pin = tx.GetPinnedTaskId(); return new ActiveTaskList(TaskRules.Sort(tx.ListActive(), pin), pin); }, ct);
    public Task<TaskOverview> ReadOverviewAsync(CancellationToken ct = default) => store.ReadAsync(tx =>
    { var pin = tx.GetPinnedTaskId(); return new TaskOverview(new(TaskRules.Sort(tx.ListActive(), pin), pin), tx.ListArchived(0, 50), tx.AttachmentCounts()); }, ct);
    public Task<IReadOnlyList<TaskItem>> ListArchivedAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        if (skip < 0 || take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(take));
        return store.ReadAsync(tx => tx.ListArchived(skip, take), ct);
    }
    public Task RenewArchiveAsync(Guid id, CancellationToken ct = default) => MutateAsync(tx =>
    { tx.Save(TaskRules.Renew(RequireTask(tx, id), Now)); return 0; }, ct);
    public Task SetPinnedTaskAsync(Guid? id, CancellationToken ct = default) => MutateAsync(tx =>
    {
        if (id is { } value && RequireTask(tx, value).ArchivedAtUtc is not null) throw new InvalidOperationException("不能置顶已归档日程。");
        tx.SetPinnedTaskId(id); return 0;
    }, ct);
    public Task ArchiveTaskAsync(Guid id, ArchiveReason reason, CancellationToken ct = default) => MutateAsync(tx =>
    { EnsureNotCopying(id); Archive(tx, RequireTask(tx, id), reason, Now); return 0; }, ct);
    public Task RestoreTaskAsync(Guid id, CancellationToken ct = default) => MutateAsync(tx =>
    {
        tx.Save(TaskRules.Restore(RequireTask(tx, id), Now)); tx.SkipUnsubmittedReminders(id);
        if (tx.GetPinnedTaskId() == id) tx.SetPinnedTaskId(null);
        return 0;
    }, ct);
    public Task DeleteArchivedTaskAsync(Guid id, CancellationToken ct = default) => MutateAsync(tx =>
    {
        EnsureNotCopying(id);
        if (RequireTask(tx, id).ArchivedAtUtc is null) throw new InvalidOperationException("只能彻底删除已归档日程。");
        if (tx.GetPinnedTaskId() == id) tx.SetPinnedTaskId(null);
        tx.Delete(id); return 0;
    }, ct);
    public Task DeleteAllArchivedTasksAsync(CancellationToken ct = default) => MutateAsync(tx =>
    { tx.DeleteAllArchived(); return 0; }, ct);

    public async Task ReconcileTimeAsync(CancellationToken ct = default)
    {
        var now = Now;
        var candidates = await store.ReadAsync(tx => tx.ListActive().Where(item => item.ArchiveAtUtc <= now ||
            (item.DueAtUtc <= now && tx.FindReminder(item.Id, item.ReminderRevision) is null)).Select(x => x.Id).ToArray(), ct).ConfigureAwait(false);
        if (candidates.Length == 0) return;
        var changed = await store.WriteAsync(tx =>
        {
            var actualNow = Now; var any = false;
            foreach (var id in candidates)
            {
                var item = tx.Find(id);
                if (item is null || item.ArchivedAtUtc is not null || mutationGate?.IsBusy(id) == true) continue;
                if (item.ArchiveAtUtc <= actualNow) { Archive(tx, item, ArchiveReason.Automatic, actualNow); any = true; }
                else if (item.DueAtUtc <= actualNow && tx.FindReminder(id, item.ReminderRevision) is null)
                { tx.SaveReminder(new(id, item.ReminderRevision, ReminderStatus.Pending, 0, item.DueAtUtc, null)); any = true; }
            }
            return any;
        }, ct).ConfigureAwait(false);
        if (changed) Changed?.Invoke();
    }
    public Task<DateTimeOffset?> GetNextEventAtAsync(CancellationToken ct = default)
    {
        var now = Now;
        return store.ReadAsync(tx => tx.ListActive().Where(item => mutationGate?.IsBusy(item.Id) != true).SelectMany(item => item.DueAtUtc > now
            ? new[] { item.ArchiveAtUtc, item.DueAtUtc.Value } : new[] { item.ArchiveAtUtc }).Select(x => (DateTimeOffset?)x).Min(), ct);
    }
    private void EnsureNotCopying(Guid id)
    { if (mutationGate?.IsBusy(id) == true) throw new InvalidOperationException("附件正在保存，请稍后再试。"); }
    private async Task<T> MutateAsync<T>(Func<ITaskWriter, T> action, CancellationToken ct)
    {
        var result = await store.WriteAsync(action, ct).ConfigureAwait(false);
        Changed?.Invoke();
        return result;
    }
    private static TaskItem RequireTask(ITaskReader tx, Guid id) => tx.Find(id) ?? throw new InvalidOperationException("日程不存在，可能已被删除。");
    private static void Archive(ITaskWriter tx, TaskItem item, ArchiveReason reason, DateTimeOffset now)
    {
        tx.Save(TaskRules.Archive(item, reason, now)); tx.SkipUnsubmittedReminders(item.Id);
        if (tx.GetPinnedTaskId() == item.Id) tx.SetPinnedTaskId(null);
    }
}
