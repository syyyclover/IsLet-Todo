namespace TaskIsland.Domain;

public enum ArchiveReason { Automatic, Manual, Completed }

public sealed record TaskItem(Guid Id, string Title, DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc, DateTimeOffset? DueAtUtc, DateTimeOffset ArchiveAtUtc,
    DateTimeOffset? ArchivedAtUtc, ArchiveReason? ArchiveReason, int ReminderRevision);
