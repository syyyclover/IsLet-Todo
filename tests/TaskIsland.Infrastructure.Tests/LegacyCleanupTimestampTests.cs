using Microsoft.Data.Sqlite;
using TaskIsland.Infrastructure;
using Xunit;

public class LegacyCleanupTimestampTests
{
    [Fact] public async Task LegacyRequiredCleanupTimestampStillAcceptsNewReservations()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskIsland.LegacyQueue.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using (var first = new SqliteTaskStore(root)) await first.InitializeAsync();
            using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "data.db")};Pooling=False"))
            {
                db.Open(); using var command = db.CreateCommand();
                command.CommandText = "DROP TABLE pending_file_cleanup; CREATE TABLE pending_file_cleanup(id INTEGER PRIMARY KEY AUTOINCREMENT,stored_relative_path TEXT NOT NULL,created_at_utc INTEGER NOT NULL); PRAGMA user_version=2;";
                command.ExecuteNonQuery();
            }
            await using var reopened = new SqliteTaskStore(root); await reopened.InitializeAsync();
            await reopened.WriteAsync(tx => { tx.EnqueueCleanup("copy.partial"); return 0; });
            Assert.Single(await reopened.ReadAsync(tx => tx.ListPendingCleanup()));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
