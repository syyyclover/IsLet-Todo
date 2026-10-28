namespace TaskIsland.Domain;

public static class TaskRules
{
    public static TaskItem Create(Guid id, string title, DateTimeOffset? due, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new ArgumentException("任务 ID 不能为空。", nameof(id));
        ValidateDue(due, now);
        return new(id, TaskText.Normalize(title), now.ToUniversalTime(), now.ToUniversalTime(),
            due?.ToUniversalTime(), (due ?? now).AddHours(24).ToUniversalTime(), null, null, 1);
    }
    public static TaskItem Update(TaskItem item, string title, DateTimeOffset? due, DateTimeOffset now)
    {
        RequireActive(item);
        var changed = item.DueAtUtc != due;
        if (changed) ValidateDue(due, now);
        return item with
        {
            Title = TaskText.Normalize(title), UpdatedAtUtc = now.ToUniversalTime(), DueAtUtc = due?.ToUniversalTime(),
            ArchiveAtUtc = changed ? (due ?? now).AddHours(24).ToUniversalTime() : item.ArchiveAtUtc,
            ReminderRevision = changed ? checked(item.ReminderRevision + 1) : item.ReminderRevision
        };
    }
    public static TaskItem Renew(TaskItem item, DateTimeOffset now)
    {
        RequireActive(item);
        var deadline = now.AddHours(24);
        if (item.DueAtUtc is { } due && due.AddHours(24) > deadline) deadline = due.AddHours(24);
        return item with { ArchiveAtUtc = deadline.ToUniversalTime(), UpdatedAtUtc = now.ToUniversalTime() };
    }
    public static TaskItem Archive(TaskItem item, ArchiveReason reason, DateTimeOffset now)
    {
        RequireActive(item);
        if (!Enum.IsDefined(reason)) throw new ArgumentException("归档原因无效。", nameof(reason));
        return item with { ArchivedAtUtc = now.ToUniversalTime(), ArchiveReason = reason, UpdatedAtUtc = now.ToUniversalTime() };
    }
    public static TaskItem Restore(TaskItem item, DateTimeOffset now)
    {
        if (item.ArchivedAtUtc is null) throw new InvalidOperationException("只能恢复已归档日程。");
        var due = item.DueAtUtc > now ? item.DueAtUtc : null;
        return item with { DueAtUtc = due, ArchiveAtUtc = (due ?? now).AddHours(24).ToUniversalTime(),
            ArchivedAtUtc = null, ArchiveReason = null, ReminderRevision = checked(item.ReminderRevision + 1), UpdatedAtUtc = now.ToUniversalTime() };
    }
    public static IReadOnlyList<TaskItem> Sort(IEnumerable<TaskItem> items, Guid? pinned)
        => items.Where(x => x.ArchivedAtUtc is null).OrderBy(x => x.Id == pinned ? 0 : 1)
            .ThenBy(x => x.DueAtUtc.HasValue ? 0 : 1).ThenBy(x => x.DueAtUtc)
            .ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToArray();

    private static void RequireActive(TaskItem item)
    {
        if (item.ArchivedAtUtc is not null) throw new InvalidOperationException("该日程已归档，请先恢复。");
    }
    private static void ValidateDue(DateTimeOffset? due, DateTimeOffset now)
    {
        if (due <= now) throw new ArgumentException("结束时间必须在未来。", nameof(due));
    }
}
