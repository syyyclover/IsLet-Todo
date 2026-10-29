namespace TaskIsland.Application;

/// <summary>Shared, non-waiting task mutation leases; busy copies never cause scheduler spin.</summary>
public sealed class TaskMutationGate
{
    private readonly object sync = new();
    private readonly HashSet<Guid> busy = [];
    public bool IsBusy(Guid id) { lock (sync) return busy.Contains(id); }
    public IDisposable Acquire(Guid id) => TryAcquire(id, out var lease) ? lease! : throw new InvalidOperationException("附件正在复制，请稍后再操作。");
    public bool TryAcquire(Guid id, out IDisposable? lease)
    {
        lock (sync)
        {
            if (!busy.Add(id)) { lease = null; return false; }
            lease = new Lease(this, id); return true;
        }
    }
    private sealed class Lease(TaskMutationGate owner, Guid id) : IDisposable
    {
        private TaskMutationGate? current = owner;
        public void Dispose()
        {
            var gate = Interlocked.Exchange(ref current, null);
            if (gate is not null) lock (gate.sync) gate.busy.Remove(id);
        }
    }
}
