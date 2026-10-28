using Microsoft.Data.Sqlite;
using TaskIsland.Domain;
using TaskIsland.Infrastructure;
using Xunit;

public sealed class DatabaseTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TaskIsland.Tests", Guid.NewGuid().ToString("N"), "中文 路径 #");
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact] public async Task TasksPinAndReminderSurviveRestart()
    {
        var task = TaskRules.Create(Guid.NewGuid(), "重启恢复", Now.AddHours(2), Now);
        await using (var store = new SqliteTaskStore(root))
        {
            await store.InitializeAsync();
            await store.WriteAsync(tx => { tx.Save(task); tx.SetPinnedTaskId(task.Id); tx.SaveReminder(new(task.Id, 1, TaskIsland.Application.ReminderStatus.Pending, 0, Now, null)); return 0; });
        }
        await using var reopened = new SqliteTaskStore(root);
        await reopened.InitializeAsync();
        Assert.Equal(task, await reopened.ReadAsync(tx => tx.Find(task.Id)));
        Assert.Equal(task.Id, await reopened.ReadAsync(tx => tx.GetPinnedTaskId()));
        Assert.NotNull(await reopened.ReadAsync(tx => tx.FindReminder(task.Id, 1)));
    }
    [Fact] public async Task FailedTransactionRollsBackAllChanges()
    {
        await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
        var task = TaskRules.Create(Guid.NewGuid(), "不能半完成", null, Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync<int>(tx => { tx.Save(task); tx.SetPinnedTaskId(task.Id); throw new InvalidOperationException("rollback"); }));
        Assert.Empty(await store.ReadAsync(tx => tx.ListActive()));
        Assert.Null(await store.ReadAsync(tx => tx.GetPinnedTaskId()));
    }
    [Fact] public async Task ConcurrentTransactionsDoNotLoseWrites()
    {
        await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(index => store.WriteAsync(tx => { tx.Save(TaskRules.Create(Guid.NewGuid(), $"任务{index}", null, Now)); return 0; })));
        Assert.Equal(30, (await store.ReadAsync(tx => tx.ListActive())).Count);
    }
    [Fact] public async Task WritesExecuteOffCallerThread()
    {
        await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
        var result = new TaskCompletionSource<(int Caller, int Worker)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(async () =>
        {
            try { var caller = Environment.CurrentManagedThreadId; var worker = await store.WriteAsync(_ => Environment.CurrentManagedThreadId); result.SetResult((caller, worker)); }
            catch (Exception ex) { result.SetException(ex); }
        });
        thread.Start();
        var ids = await result.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(ids.Caller, ids.Worker);
    }
    [Fact] public async Task FutureSchemaIsNotOverwritten()
    {
        Directory.CreateDirectory(root);
        using (var db = Open()) { Execute(db, "CREATE TABLE Evidence(Value TEXT); INSERT INTO Evidence VALUES ('preserve'); PRAGMA user_version=999;"); }
        var before = File.ReadAllBytes(Path.Combine(root, "data.db"));
        await using var store = new SqliteTaskStore(root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Equal(before, File.ReadAllBytes(store.DatabasePath));
    }
    [Fact] public async Task CorruptionDoesNotClearDatabase()
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "data.db");
        var bytes = System.Text.Encoding.UTF8.GetBytes("not a database; preserve this evidence"); File.WriteAllBytes(path, bytes);
        await using var store = new SqliteTaskStore(root);
        await Assert.ThrowsAsync<SqliteException>(() => store.InitializeAsync());
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task MigrationBacksUpAndFailureKeepsVersionOne(bool causeFailure)
    {
        Directory.CreateDirectory(root);
        using (var db = Open())
        {
            Execute(db, "CREATE TABLE Tasks(Id TEXT PRIMARY KEY, Title TEXT NOT NULL, CreatedAtUtc INTEGER NOT NULL, UpdatedAtUtc INTEGER NOT NULL, DueAtUtc INTEGER, ArchiveAtUtc INTEGER NOT NULL, ArchivedAtUtc INTEGER, ArchiveReason INTEGER, ReminderRevision INTEGER NOT NULL); CREATE TABLE Settings(Key TEXT PRIMARY KEY, Value TEXT); PRAGMA user_version=1;");
            if (causeFailure) Execute(db, "CREATE TABLE ReminderDeliveries(Unexpected TEXT);");
        }
        await using var store = new SqliteTaskStore(root);
        if (causeFailure) await Assert.ThrowsAsync<SqliteException>(() => store.InitializeAsync()); else await store.InitializeAsync();
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(root, "backups"), "*.db"));
        using var saved = new SqliteConnection($"Data Source={backup};Pooling=False"); saved.Open();
        Assert.Equal(1L, Scalar(saved, "PRAGMA user_version"));
        using var current = Open(); Assert.Equal(causeFailure ? 1L : 2L, Scalar(current, "PRAGMA user_version"));
    }
    [Fact] public async Task DatabaseUsesWalAndEnforcesForeignKeys()
    {
        await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
        using var db = Open(); Assert.Equal("wal", Scalar(db, "PRAGMA journal_mode"));
        await Assert.ThrowsAsync<SqliteException>(() => store.WriteAsync(tx => { tx.SaveReminder(new(Guid.NewGuid(), 1, TaskIsland.Application.ReminderStatus.Pending, 0, Now, null)); return 0; }));
    }
    [Fact] public async Task DisposalDrainsAcceptedWrites()
    {
        var store = new SqliteTaskStore(root); await store.InitializeAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = store.WriteAsync(tx => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(5)); tx.Save(TaskRules.Create(Guid.NewGuid(), "先", null, Now)); return 0; });
        await entered.Task;
        var second = store.WriteAsync(tx => { tx.Save(TaskRules.Create(Guid.NewGuid(), "后", null, Now)); return 0; });
        var disposal = store.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        release.Set(); await Task.WhenAll(first, second, disposal);
        await using var reopened = new SqliteTaskStore(root); await reopened.InitializeAsync();
        Assert.Equal(2, (await reopened.ReadAsync(tx => tx.ListActive())).Count);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ReadAsync(tx => tx.ListActive()));
    }
    private SqliteConnection Open() { var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "data.db"), Pooling = false }.ToString()); db.Open(); return db; }
    private static void Execute(SqliteConnection db, string sql) { using var command = db.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private static object? Scalar(SqliteConnection db, string sql) { using var command = db.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
}
