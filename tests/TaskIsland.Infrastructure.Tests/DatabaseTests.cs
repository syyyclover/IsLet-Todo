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
    [Fact] public async Task GenericSettingsAreParameterizedUpdatedDeletedAndPersisted()
    {
        const string key = "theme'; DROP TABLE Tasks;--";
        await using (var store = new SqliteTaskStore(root))
        {
            await store.InitializeAsync();
            await store.WriteAsync(tx => { tx.WriteSetting(key, "dark"); tx.WriteSetting("temporary", "x"); tx.WriteSetting("temporary", null); return 0; });
            await store.WriteAsync(tx => { tx.WriteSetting(key, "light"); return 0; });
        }
        await using var reopened = new SqliteTaskStore(root); await reopened.InitializeAsync();
        Assert.Equal("light", await reopened.ReadAsync(tx => tx.ReadSetting(key)));
        Assert.Null(await reopened.ReadAsync(tx => tx.ReadSetting("temporary")));
        Assert.Empty(await reopened.ReadAsync(tx => tx.ListActive()));
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
    [Fact] public async Task LegacySnakeCaseSchemaMigratesWithoutLosingTasksOrRelatedRecords()
    {
        Directory.CreateDirectory(root);
        var id = Guid.NewGuid();
        using (var db = Open()) Execute(db, $"""
            CREATE TABLE tasks(id TEXT PRIMARY KEY,title TEXT NOT NULL,created_at_utc INTEGER NOT NULL,updated_at_utc INTEGER NOT NULL,due_at_utc INTEGER,archive_at_utc INTEGER NOT NULL,archived_at_utc INTEGER,archive_reason INTEGER,reminder_revision INTEGER NOT NULL);
            CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT);
            CREATE TABLE reminder_deliveries(task_id TEXT NOT NULL,reminder_revision INTEGER NOT NULL,status INTEGER NOT NULL,attempt_count INTEGER NOT NULL,next_attempt_at_utc INTEGER,last_error TEXT,PRIMARY KEY(task_id,reminder_revision));
            CREATE TABLE attachments(id TEXT PRIMARY KEY,task_id TEXT REFERENCES tasks(id),stored_relative_path TEXT);
            CREATE TABLE pending_file_cleanup(id INTEGER PRIMARY KEY,stored_relative_path TEXT);
            INSERT INTO tasks VALUES('{id}','保留旧日程',1000,1000,NULL,9999999999999,NULL,NULL,3);
            INSERT INTO settings VALUES('PinnedTaskId','{id}');
            INSERT INTO reminder_deliveries VALUES('{id}',3,1,1,NULL,NULL);
            INSERT INTO attachments VALUES('attachment','{id}','copies/preserved.txt');
            INSERT INTO pending_file_cleanup VALUES(1,'copies/queued.txt');
            PRAGMA user_version=1;
            """);
        await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
        var task = Assert.Single(await store.ReadAsync(tx => tx.ListActive()));
        Assert.Equal(id, task.Id); Assert.Equal("保留旧日程", task.Title); Assert.Equal(3, task.ReminderRevision);
        Assert.Equal(id, await store.ReadAsync(tx => tx.GetPinnedTaskId()));
        Assert.Equal(TaskIsland.Application.ReminderStatus.Submitted, (await store.ReadAsync(tx => tx.FindReminder(id, 3)))!.Status);
        await store.WriteAsync(tx => { tx.Save(task with { Title = "可继续编辑" }); return 0; });
        using var current = Open();
        Assert.Equal(1L, Scalar(current, "SELECT COUNT(*) FROM attachments"));
        Assert.Equal(1L, Scalar(current, "SELECT COUNT(*) FROM pending_file_cleanup"));
        Assert.Equal(2L, Scalar(current, "PRAGMA user_version"));
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(root, "backups"), "*.db"));
        using var saved = new SqliteConnection($"Data Source={backup};Pooling=False"); saved.Open();
        Assert.Equal(1L, Scalar(saved, "PRAGMA user_version"));
        Assert.Equal("保留旧日程", Scalar(saved, "SELECT title FROM tasks"));
    }
    [Fact] public async Task LegacyNotNullSettingsAllowsCompletingAndRestoringOnlyPinnedTask()
    {
        Directory.CreateDirectory(root);
        var id = Guid.NewGuid();
        using (var db = Open()) Execute(db, $"""
            CREATE TABLE tasks(id TEXT PRIMARY KEY,title TEXT NOT NULL,created_at_utc INTEGER NOT NULL,updated_at_utc INTEGER NOT NULL,due_at_utc INTEGER,archive_at_utc INTEGER NOT NULL,archived_at_utc INTEGER,archive_reason INTEGER,reminder_revision INTEGER NOT NULL);
            CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
            CREATE TABLE reminder_deliveries(task_id TEXT NOT NULL,reminder_revision INTEGER NOT NULL,status INTEGER NOT NULL,attempt_count INTEGER NOT NULL,next_attempt_at_utc INTEGER,last_error TEXT,PRIMARY KEY(task_id,reminder_revision));
            INSERT INTO tasks VALUES('{id}','旧版唯一置顶任务',1000,1000,NULL,9999999999999,NULL,NULL,1);
            INSERT INTO settings VALUES('PinnedTaskId','{id}');
            INSERT INTO settings VALUES('OtherSetting','preserved');
            PRAGMA user_version=1;
            """);
        await using (var store = new SqliteTaskStore(root))
        {
            await store.InitializeAsync();
            var service = new TaskIsland.Application.TaskService(store, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));
            await service.ArchiveTaskAsync(id, ArchiveReason.Completed);
            Assert.Empty((await service.ListActiveAsync()).Tasks);
            Assert.Null((await service.ListActiveAsync()).PinnedTaskId);
            Assert.Equal(ArchiveReason.Completed, Assert.Single(await service.ListArchivedAsync()).ArchiveReason);
            await service.RestoreTaskAsync(id);
            Assert.Equal(id, Assert.Single((await service.ListActiveAsync()).Tasks).Id);
            Assert.Null((await service.ListActiveAsync()).PinnedTaskId);
            await service.SetPinnedTaskAsync(id);
            await service.SetPinnedTaskAsync(null);
            await service.SetPinnedTaskAsync(null);
        }
        await using var reopened = new SqliteTaskStore(root);
        await reopened.InitializeAsync();
        Assert.Equal(id, Assert.Single(await reopened.ReadAsync(tx => tx.ListActive())).Id);
        Assert.Null(await reopened.ReadAsync(tx => tx.GetPinnedTaskId()));
        using var current = Open();
        Assert.Equal("preserved", Scalar(current, "SELECT value FROM settings WHERE key='OtherSetting'"));
        Assert.Equal(0L, Scalar(current, "SELECT COUNT(*) FROM settings WHERE key='PinnedTaskId'"));
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
