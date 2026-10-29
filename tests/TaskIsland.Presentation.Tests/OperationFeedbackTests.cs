using TaskIsland.Application;
using System.IO;
using TaskIsland.Infrastructure;
using TaskIsland.Presentation;
using Xunit;

public class OperationFeedbackTests
{
    [Fact] public async Task FeedbackOnlyFollowsCommittedDataAndVisualFailureDoesNotFailSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TaskIsland.Feedback.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new SqliteTaskStore(directory); await store.InitializeAsync();
            var service = new TaskService(store, TimeProvider.System);
            using var vm = new IslandViewModel(); await vm.AttachAsync(service);
            var cues = new List<OperationCue>(); var committedBeforeComplete = false;
            vm.FeedbackRequested += async (id, cue) =>
            {
                cues.Add(cue);
                if (cue == OperationCue.Complete) committedBeforeComplete = (await service.ListActiveAsync()).Tasks.Count == 0;
                if (cue == OperationCue.Save) throw new InvalidOperationException("visual-only failure");
            };
            vm.AddCommand.Execute(null); vm.EditorTitle = " "; await vm.SaveCommand.ExecuteAsync(null);
            Assert.Empty(cues); Assert.True(vm.IsError);
            vm.EditorTitle = "已提交后才反馈"; await vm.SaveCommand.ExecuteAsync(null);
            Assert.False(vm.IsError); Assert.Single(vm.Tasks); Assert.Equal(OperationCue.Save, Assert.Single(cues));
            await vm.CompleteCommand.ExecuteAsync(vm.Tasks[0]);
            Assert.True(committedBeforeComplete); Assert.Empty(vm.Tasks); Assert.Single(vm.ArchivedTasks); Assert.False(vm.IsError);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
