namespace TaskIsland.Domain;

public sealed record AttachmentItem(Guid Id, Guid TaskId, string OriginalName, string StoredRelativePath,
    long SizeBytes, string Kind, DateTimeOffset AddedAtUtc);
