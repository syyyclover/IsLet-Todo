using TaskIsland.Application;
using TaskIsland.Infrastructure;
using TaskIsland.Presentation;
using System.IO;
using Xunit;

public class EditorTimeWorkflowTests
{
    [Fact] public async Task ConfirmedTimePersistsAndChangingOnlyTimeRequiresDiscardConfirmation()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskIsland.UI.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new SqliteTaskStore(root); await store.InitializeAsync();
            using var model = new IslandViewModel(); await model.AttachAsync(new TaskService(store, TimeProvider.System));
            model.AddCommand.Execute(null); model.EditorTitle = "设置时间";
            model.EditorTime.OpenCommand.Execute(null);
            model.EditorTime.Date = DateTime.Today.AddDays(2); model.EditorTime.Hour = 12; model.EditorTime.Minute = 34;
            model.EditorTime.ConfirmCommand.Execute(null);
            var due = model.EditorTime.ConfirmedDueAt; Assert.NotNull(due);
            await model.SaveCommand.ExecuteAsync(null);
            Assert.Equal(due, Assert.Single(model.Tasks).Item.DueAtUtc);
            model.EditCommand.Execute(model.Tasks[0]); model.EditorTime.RemoveCommand.Execute(null);
            Assert.False(model.TryLeaveEditor(IslandPage.Tasks)); Assert.True(model.ConfirmDiscard);
            model.KeepEditingCommand.Execute(null); await model.SaveCommand.ExecuteAsync(null);
            Assert.Null(Assert.Single(model.Tasks).Item.DueAtUtc);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
