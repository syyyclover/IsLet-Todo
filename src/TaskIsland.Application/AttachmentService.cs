using TaskIsland.Domain;

namespace TaskIsland.Application;

public sealed class AttachmentService(ITaskStore store, IAttachmentFiles files, TimeProvider clock, TaskMutationGate? gate = null)
{
    private readonly TaskMutationGate mutations = gate ?? new();
    private readonly object sync = new();
    private readonly HashSet<CancellationTokenSource> copies = [];
    private readonly HashSet<string> copyingPaths = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource idle = CompletedSource();
    private readonly SemaphoreSlim cleanupGate = new(1, 1);
    public event Action? Changed;
    public event Action<AttachmentProgress>? ProgressChanged;
    public bool IsCopying => ActiveCopyCount != 0;
    public int ActiveCopyCount { get { lock (sync) return copies.Count; } }
    public Task WaitForIdleAsync() { lock (sync) return idle.Task; }
    public void CancelAll()
    {
        CancellationTokenSource[] snapshot;
        lock (sync) snapshot = copies.ToArray();
        foreach (var copy in snapshot) { try { copy.Cancel(); } catch (ObjectDisposedException) { } }
    }
    public Task<IReadOnlyList<AttachmentItem>> ListAsync(Guid taskId, CancellationToken ct = default) => store.ReadAsync(tx => tx.ListAttachments(taskId), ct);
    public string GetManagedPath(AttachmentItem attachment) => files.GetManagedPath(attachment.StoredRelativePath);

    public async Task<IReadOnlyList<AttachmentAddResult>> AddAsync(Guid taskId, IReadOnlyList<string> sourcePaths, CancellationToken ct = default)
    {
        using var lease = mutations.Acquire(taskId);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (sync) { if (copies.Count == 0) idle = new(TaskCreationOptions.RunContinuationsAsynchronously); copies.Add(cancellation); }
        var results = new List<AttachmentAddResult>();
        try
        {
            Changed?.Invoke();
            for (var index = 0; index < sourcePaths.Count; index++)
            {
                var source = sourcePaths[index]; var name = SafeName(source); string? relative = null;
                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var info = await Task.Run(() => files.Inspect(source), cancellation.Token).ConfigureAwait(false);
                    if (info.SizeBytes > 100L * 1024 * 1024) throw new InvalidOperationException("单个附件不能超过 100 MiB。");
                    name = info.OriginalName;
                    var id = Guid.NewGuid(); relative = files.CreateRelativePath(taskId, id, name);
                    lock (sync) { copyingPaths.Add(relative); copyingPaths.Add(relative + ".partial"); }
                    // Persist cleanup intent before touching copies; a crash never leaves an untracked file.
                    await store.WriteAsync(tx =>
                    {
                        RequireActiveAndCapacity(tx, taskId);
                        tx.EnqueueCleanup(relative); tx.EnqueueCleanup(relative + ".partial"); return 0;
                    }, cancellation.Token).ConfigureAwait(false);
                    var fileIndex = index + 1;
                    var progress = new InlineProgress(bytes => ProgressChanged?.Invoke(new(taskId, fileIndex, sourcePaths.Count, bytes, info.SizeBytes)));
                    var copiedBytes = await files.CopyAsync(source, relative, progress, cancellation.Token).ConfigureAwait(false);
                    var attachment = new AttachmentItem(id, taskId, name, relative, copiedBytes, info.Kind, clock.GetUtcNow());
                    await store.WriteAsync(tx =>
                    {
                        RequireActiveAndCapacity(tx, taskId);
                        tx.AddAttachment(attachment);
                        foreach (var pending in tx.ListPendingCleanup().Where(x => x.StoredRelativePath == relative || x.StoredRelativePath == relative + ".partial"))
                            tx.CompleteCleanup(pending.Id);
                        return 0;
                    }, cancellation.Token).ConfigureAwait(false);
                    results.Add(new(name, attachment, null));
                }
                catch (Exception exception)
                {
                    results.Add(new(name, null, Message(exception)));
                }
                finally
                {
                    if (relative is not null) lock (sync) { copyingPaths.Remove(relative); copyingPaths.Remove(relative + ".partial"); }
                }
                // Failed registrations and interrupted copies remain reserved until deletion actually succeeds.
                try { await RetryCleanupAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { /* Durable cleanup intent remains for the next attempt. */ }
            }
            return results;
        }
        finally
        {
            lease.Dispose();
            lock (sync) { copies.Remove(cancellation); if (copies.Count == 0) idle.TrySetResult(); }
            Changed?.Invoke();
        }
    }

    public async Task<bool> RemoveAsync(Guid taskId, Guid attachmentId, CancellationToken ct = default)
    {
        using var lease = mutations.Acquire(taskId);
        await store.WriteAsync(tx =>
        {
            if (!tx.ListAttachments(taskId).Any(x => x.Id == attachmentId)) throw new InvalidOperationException("附件不存在。");
            tx.RemoveAttachment(attachmentId); return 0;
        }, ct).ConfigureAwait(false);
        var complete = await RetryCleanupAsync(ct).ConfigureAwait(false);
        Changed?.Invoke(); return complete;
    }
    public async Task<bool> RetryCleanupAsync(CancellationToken ct = default)
    {
        await cleanupGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pending = await store.ReadAsync(tx => tx.ListPendingCleanup(), ct).ConfigureAwait(false);
            foreach (var item in pending)
            {
                lock (sync) if (copyingPaths.Contains(item.StoredRelativePath)) continue;
                bool removed;
                try { removed = await files.DeleteAsync(item.StoredRelativePath, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { removed = false; }
                if (removed) await store.WriteAsync(tx => { tx.CompleteCleanup(item.Id); return 0; }, ct).ConfigureAwait(false);
            }
            return await store.ReadAsync(tx => tx.ListPendingCleanup().Count == 0, ct).ConfigureAwait(false);
        }
        finally { cleanupGate.Release(); }
    }
    private static void RequireActiveAndCapacity(ITaskReader tx, Guid id)
    {
        var task = tx.Find(id) ?? throw new InvalidOperationException("日程不存在。");
        if (task.ArchivedAtUtc is not null) throw new InvalidOperationException("已归档日程不能添加附件。");
        if (tx.ListAttachments(id).Count >= 20) throw new InvalidOperationException("每个日程最多保留 20 个附件。");
    }
    private static string Message(Exception ex) => ex switch
    {
        OperationCanceledException => "复制已取消。",
        InvalidOperationException => ex.Message,
        FileNotFoundException => "文件不存在。",
        UnauthorizedAccessException => "无法读取文件或清理副本。",
        _ => "附件复制或保存失败，请重试。"
    };
    private static string SafeName(string path) { try { return Path.GetFileName(path); } catch { return "文件"; } }
    private static TaskCompletionSource CompletedSource() { var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); source.SetResult(); return source; }
    private sealed class InlineProgress(Action<long> report) : IProgress<long> { public void Report(long value) => report(value); }
}
