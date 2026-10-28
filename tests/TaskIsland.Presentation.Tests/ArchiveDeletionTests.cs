using TaskIsland.Application;
using TaskIsland.Domain;
using TaskIsland.Infrastructure;
using TaskIsland.Presentation;
using System.IO;
using Xunit;

public class ArchiveDeletionTests
{
    [Fact] public async Task CancelPreservesArchiveAndConfirmDeletesIt()
    {
        await WithModel(async (model, service) =>
        {
            var item = await service.CreateTaskAsync("归档任务", null);
            await service.ArchiveTaskAsync(item.Id, ArchiveReason.Manual); await model.ReloadAsync();
            model.RequestDeleteCommand.Execute(model.ArchivedTasks[0]); Assert.True(model.ConfirmDelete);
            model.CancelDeleteCommand.Execute(null); Assert.False(model.ConfirmDelete); Assert.Single(model.ArchivedTasks);
            model.RequestDeleteCommand.Execute(model.ArchivedTasks[0]); await model.ConfirmDeleteCommand.ExecuteAsync(null);
            Assert.False(model.ConfirmDelete); Assert.Empty(model.ArchivedTasks);
        });
    }
    [Fact] public async Task BulkConfirmationDeletesOnlyArchivesAndRejectsActiveRow()
    {
        await WithModel(async (model, service) =>
        {
            await service.CreateTaskAsync("活跃任务", null);
            var item = await service.CreateTaskAsync("归档任务", null);
            await service.ArchiveTaskAsync(item.Id, ArchiveReason.Manual); await model.ReloadAsync();
            model.RequestDeleteCommand.Execute(model.Tasks[0]); Assert.False(model.ConfirmDelete);
            model.RequestDeleteAllCommand.Execute(null); Assert.True(model.ConfirmDelete);
            Assert.Single(model.ArchivedTasks); await model.ConfirmDeleteCommand.ExecuteAsync(null);
            Assert.Empty(model.ArchivedTasks); Assert.Single(model.Tasks);
        });
    }
    [Fact] public async Task FailedDeletePreservesConfirmationAndActiveTask()
    {
        await WithModel(async (model, service) =>
        {
            var item = await service.CreateTaskAsync("将恢复的日程", null);
            await service.ArchiveTaskAsync(item.Id, ArchiveReason.Manual); await model.ReloadAsync();
            model.RequestDeleteCommand.Execute(model.ArchivedTasks[0]);
            await service.RestoreTaskAsync(item.Id);
            await model.ConfirmDeleteCommand.ExecuteAsync(null);
            Assert.True(model.ConfirmDelete); Assert.True(model.IsError);
            await model.ReloadAsync(); Assert.Single(model.Tasks);
        });
    }
    private static async Task WithModel(Func<IslandViewModel, TaskService, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskIsland.UI.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
            var service = new TaskService(store, TimeProvider.System);
            using var model = new IslandViewModel(); await model.AttachAsync(service); await action(model, service);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
