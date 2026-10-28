using TaskIsland.Application;
using TaskIsland.Infrastructure;
using Xunit;

public class SettingsServiceTests
{
    [Fact] public async Task SettingsAreStoredTogetherAndSurviveRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "TaskIsland.Settings.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using (var store = new SqliteTaskStore(path))
            {
                await store.InitializeAsync(); var service = new SettingsService(store);
                Assert.Equal(new(IslandAnchor.Center, true), await service.LoadAsync());
                await service.SaveAsync(new(IslandAnchor.Right, false));
            }
            await using var reopened = new SqliteTaskStore(path); await reopened.InitializeAsync();
            Assert.Equal(new(IslandAnchor.Right, false), await new SettingsService(reopened).LoadAsync());
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
