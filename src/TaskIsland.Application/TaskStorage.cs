using TaskIsland.Domain;

namespace TaskIsland.Application;

public enum ReminderStatus { Pending, Submitted, Failed, Skipped }
public sealed record ReminderDelivery(Guid TaskId, int Revision, ReminderStatus Status, int Attempts,
    DateTimeOffset? NextAttemptAtUtc, string? LastError);
public sealed record ActiveTaskList(IReadOnlyList<TaskItem> Tasks, Guid? PinnedTaskId);
public sealed record TaskOverview(ActiveTaskList Active, IReadOnlyList<TaskItem> Archived, IReadOnlyDictionary<Guid, int>? AttachmentCounts = null);
public sealed record PendingFileCleanup(long Id, string StoredRelativePath);

public interface ITaskReader
{
    IReadOnlyList<AttachmentItem> ListAttachments(Guid taskId);
    IReadOnlyList<PendingFileCleanup> ListPendingCleanup();
    IReadOnlyDictionary<Guid, int> AttachmentCounts();
    string? ReadSetting(string key);
    TaskItem? Find(Guid id);
    IReadOnlyList<TaskItem> ListActive();
    IReadOnlyList<TaskItem> ListArchived(int skip, int take);
    Guid? GetPinnedTaskId();
    ReminderDelivery? FindReminder(Guid taskId, int revision);
}
public interface ITaskWriter : ITaskReader
{
    void AddAttachment(AttachmentItem item);
    void RemoveAttachment(Guid id);
    void CompleteCleanup(long id);
    void EnqueueCleanup(string storedRelativePath);
    void WriteSetting(string key, string? value);
    void Save(TaskItem item);
    void SetPinnedTaskId(Guid? id);
    void SaveReminder(ReminderDelivery delivery);
    void SkipUnsubmittedReminders(Guid taskId);
    void Delete(Guid id);
    void DeleteAllArchived();
}
public interface ITaskStore
{
    Task<T> ReadAsync<T>(Func<ITaskReader, T> read, CancellationToken ct = default);
    Task<T> WriteAsync<T>(Func<ITaskWriter, T> write, CancellationToken ct = default);
}
