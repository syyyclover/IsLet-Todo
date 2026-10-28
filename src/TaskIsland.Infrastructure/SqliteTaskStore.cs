using TaskIsland.Application;
using Microsoft.Data.Sqlite;

namespace TaskIsland.Infrastructure;

public sealed class SqliteTaskStore(string directory) : ITaskStore, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;
    private int disposed;
    public string DatabasePath { get; } = Path.Combine(directory, "data.db");
    public Task InitializeAsync(CancellationToken ct = default) => SerializedAsync(() =>
    {
        if (initialized) return 0;
        Directory.CreateDirectory(directory);
        using var db = Open();
        DatabaseSchema.Initialize(db, Path.Combine(directory, "backups"));
        initialized = true;
        return 0;
    }, ct);

    public Task<T> ReadAsync<T>(Func<ITaskReader, T> read, CancellationToken ct = default) => SerializedAsync(() =>
    {
        EnsureInitialized();
        using var db = Open();
        using var tx = db.BeginTransaction();
        var result = read(new SqliteTaskSession(db, tx));
        tx.Commit();
        return result;
    }, ct);

    public Task<T> WriteAsync<T>(Func<ITaskWriter, T> write, CancellationToken ct = default) => SerializedAsync(() =>
    {
        EnsureInitialized();
        using var db = Open();
        using var tx = db.BeginTransaction();
        var result = write(new SqliteTaskSession(db, tx));
        ct.ThrowIfCancellationRequested();
        tx.Commit();
        return result;
    }, ct);

    private async Task<T> SerializedAsync<T>(Func<T> action, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(action, ct).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = DatabasePath, Pooling = false, ForeignKeys = true, DefaultTimeout = 5 }.ToString());
        try { db.Open(); return db; } catch { db.Dispose(); throw; }
    }
    private void EnsureInitialized()
    {
        if (!initialized) throw new InvalidOperationException("数据库尚未成功初始化。");
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await gate.WaitAsync().ConfigureAwait(false);
        // All accepted operations queued before disposal have drained. Keep the gate alive for their finally blocks.
        gate.Release();
    }
}
