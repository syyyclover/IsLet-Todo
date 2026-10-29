using TaskIsland.Domain;
namespace TaskIsland.Application;

public sealed record AttachmentFileInfo(string OriginalName, long SizeBytes, string Kind);
public sealed record AttachmentProgress(Guid TaskId, int FileIndex, int FileCount, long BytesCopied, long TotalBytes);
public sealed record AttachmentAddResult(string OriginalName, AttachmentItem? Attachment, string? Error)
{
    public bool Success => Attachment is not null;
}
public interface IAttachmentFiles
{
    AttachmentFileInfo Inspect(string sourcePath);
    string CreateRelativePath(Guid taskId, Guid attachmentId, string originalName);
    Task<long> CopyAsync(string sourcePath, string relativePath, IProgress<long>? progress, CancellationToken ct);
    Task<bool> DeleteAsync(string relativePath, CancellationToken ct);
    string GetManagedPath(string relativePath);
}
