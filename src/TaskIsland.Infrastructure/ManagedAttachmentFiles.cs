using TaskIsland.Application;

namespace TaskIsland.Infrastructure;

public interface IAttachmentStreamCopier
{
    Task CopyAsync(Stream source, Stream destination, IProgress<long>? progress, CancellationToken ct);
}

/// <summary>Only this component touches managed attachment copies; source files are read-only.</summary>
public sealed class ManagedAttachmentFiles : IAttachmentFiles
{
    private const long MaximumBytes = 100L * 1024 * 1024;
    private readonly string root;
    private readonly IAttachmentStreamCopier copier;
    public ManagedAttachmentFiles(string dataRoot, IAttachmentStreamCopier? copier = null)
    {
        root = Path.GetFullPath(Path.Combine(dataRoot, "attachments"));
        this.copier = copier ?? new StreamCopier();
    }
    public AttachmentFileInfo Inspect(string sourcePath)
    {
        if (!Path.IsPathFullyQualified(sourcePath) || sourcePath.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidOperationException("仅支持本地文件。");
        var path = Path.GetFullPath(sourcePath);
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network) throw new InvalidOperationException("仅支持本地文件。");
        RejectLinks(path);
        var file = new FileInfo(path);
        if (!file.Exists || (file.Attributes & FileAttributes.Directory) != 0) throw new InvalidOperationException("仅支持本地文件，不支持目录。");
        if (file.Length > MaximumBytes) throw new InvalidOperationException("单个附件不能超过 100 MiB。");
        var extension = file.Extension.ToLowerInvariant();
        var kind = extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" or ".gif" ? "image"
            : extension is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" ? "video" : "file";
        return new(file.Name, file.Length, kind);
    }
    public string CreateRelativePath(Guid taskId, Guid attachmentId, string originalName)
    {
        var extension = Path.GetExtension(originalName);
        if (extension.Length > 20 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.')) extension = "";
        return Path.Combine(taskId.ToString("N"), attachmentId.ToString("N") + extension);
    }
    public Task<long> CopyAsync(string sourcePath, string relativePath, IProgress<long>? progress, CancellationToken ct) => Task.Run(async () =>
    {
        Inspect(sourcePath);
        var final = GetManagedPath(relativePath);
        var temporary = final + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        RejectLinks(final);
        var ownsTemporary = false; long copiedBytes = 0;
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (source.Length > MaximumBytes) throw new InvalidOperationException("单个附件不能超过 100 MiB。");
                await using var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                ownsTemporary = true;
                await copier.CopyAsync(source, destination, progress, ct).ConfigureAwait(false);
                if (destination.Length > MaximumBytes || destination.Length != source.Length) throw new IOException("附件复制不完整。");
                await destination.FlushAsync(ct).ConfigureAwait(false);
                copiedBytes = destination.Length;
            }
            ct.ThrowIfCancellationRequested();
            RejectLinks(final);
            File.Move(temporary, final, false);
            ownsTemporary = false;
            return copiedBytes;
        }
        finally
        {
            if (ownsTemporary)
            {
                // A failed cleanup is surfaced; the caller persists a retry for this exact managed path.
                RejectLinks(temporary);
                File.Delete(temporary);
            }
        }
    }, ct);
    public Task<bool> DeleteAsync(string relativePath, CancellationToken ct) => Task.Run(() =>
    {
        var path = GetManagedPath(relativePath);
        try { RejectLinks(path); File.Delete(path); return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (FileNotFoundException) { return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }, ct);
    public string GetManagedPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
            throw new InvalidOperationException("附件路径无效。");
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("附件路径超出管理目录。");
        RejectLinks(path);
        return path;
    }
    private static void RejectLinks(string path)
    {
        for (var cursor = path; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
        {
            try
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("不支持符号链接或重解析路径。");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private sealed class StreamCopier : IAttachmentStreamCopier
    {
        public async Task CopyAsync(Stream source, Stream destination, IProgress<long>? progress, CancellationToken ct)
        {
            var buffer = new byte[81920]; long count = 0; int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                count += read;
                if (count > MaximumBytes) throw new InvalidOperationException("单个附件不能超过 100 MiB。");
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                progress?.Report(count);
            }
        }
    }
}
