using TaskIsland.Application;
using TaskIsland.Infrastructure;
using TaskIsland.Presentation;
using Xunit;
using System.IO;

public class TaskWorkflowTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TaskIsland.UI.Tests", Guid.NewGuid().ToString("N"));
    private SqliteTaskStore store = null!;
    private IslandViewModel model = null!;
    public async Task InitializeAsync()
    { store = new(root); await store.InitializeAsync(); model = new(); await model.AttachAsync(new TaskService(store, TimeProvider.System)); }
    public async Task DisposeAsync() { model?.Dispose(); await store.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact] public async Task AddCompleteAndRestorePersistThroughCommands()
    {
        model.AddCommand.Execute(null); model.EditorTitle = "准备会议";
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal("准备会议", Assert.Single(model.Tasks).Title);
        Assert.Equal("准备会议", model.FeaturedTitle);
        await model.CompleteCommand.ExecuteAsync(model.Tasks[0]);
        Assert.Empty(model.Tasks); Assert.Single(model.ArchivedTasks);
        await model.RestoreCommand.ExecuteAsync(model.ArchivedTasks[0]);
        Assert.Single(model.Tasks); Assert.Empty(model.ArchivedTasks);
    }
    [Fact] public async Task SaveFailureKeepsInputAndEditorState()
    {
        model.AddCommand.Execute(null); model.EditorTitle = " ";
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(" ", model.EditorTitle); Assert.Empty(model.Tasks); Assert.True(model.IsError);
        model.EditorTitle = "修正后保存"; await model.SaveCommand.ExecuteAsync(null);
        Assert.Single(model.Tasks);
    }
    [Fact] public async Task CancelChangedEditorRequiresInlineConfirmation()
    {
        model.AddCommand.Execute(null); model.EditorTitle = "草稿";
        model.CancelEditCommand.Execute(null); Assert.True(model.ConfirmDiscard);
        model.KeepEditingCommand.Execute(null); Assert.Equal("草稿", model.EditorTitle); Assert.False(model.ConfirmDiscard);
        model.CancelEditCommand.Execute(null); model.DiscardChangesCommand.Execute(null);
        Assert.Empty(model.Tasks); Assert.False(model.ConfirmDiscard);
        await Task.CompletedTask;
    }
    [Fact] public async Task EditingTitleUpdatesFeaturedSummaryWithoutLosingTaskIdentity()
    {
        model.AddCommand.Execute(null); model.EditorTitle = "旧标题"; await model.SaveCommand.ExecuteAsync(null);
        var id = model.Tasks[0].Item.Id;
        model.EditCommand.Execute(model.Tasks[0]); model.EditorTitle = "新标题"; await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(id, Assert.Single(model.Tasks).Item.Id); Assert.Equal("新标题", model.FeaturedTitle);
    }
    [Fact] public void RepeatedAddDoesNotDiscardCurrentDraft()
    {
        model.AddCommand.Execute(null); model.EditorTitle = "不能丢的草稿";
        model.AddCommand.Execute(null);
        Assert.Equal("不能丢的草稿", model.EditorTitle);
    }
    [Fact] public async Task NewInputDuringPendingSaveRemainsUnsavedUntilNextSave()
    {
        var paused = new PausedStore(store) { PauseWrites = true };
        using var vm = new IslandViewModel(); await vm.AttachAsync(new TaskService(paused, TimeProvider.System));
        vm.AddCommand.Execute(null); vm.EditorTitle = "先前内容";
        var saving = vm.SaveCommand.ExecuteAsync(null); await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.EditorTitle = "稍后新增的内容"; paused.Release.SetResult(); await saving;
        Assert.Equal("稍后新增的内容", vm.EditorTitle);
        Assert.Equal("先前内容", Assert.Single(vm.Tasks).Title);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("稍后新增的内容", Assert.Single(vm.Tasks).Title);
    }
    [Fact] public async Task ChangeDuringArchivePagingRefreshesActiveListWhenBusyEnds()
    {
        var paused = new PausedStore(store); var service = new TaskService(paused, TimeProvider.System);
        var task = await service.CreateTaskAsync("调度器将归档", null);
        using var vm = new IslandViewModel(); await vm.AttachAsync(service);
        paused.PauseReads = true;
        var paging = vm.LoadMoreArchivesCommand.ExecuteAsync(null); await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.ArchiveTaskAsync(task.Id, TaskIsland.Domain.ArchiveReason.Automatic);
        paused.Release.SetResult(); await paging;
        Assert.Empty(vm.Tasks); Assert.Equal("暂无日程", vm.FeaturedTitle); Assert.Single(vm.ArchivedTasks);
    }
    private sealed class PausedStore(ITaskStore inner) : ITaskStore
    {
        public bool PauseReads { get; set; }
        public bool PauseWrites { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T> ReadAsync<T>(Func<ITaskReader, T> read, CancellationToken ct = default)
        {
            var result = await inner.ReadAsync(read, ct);
            if (PauseReads) { PauseReads = false; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return result;
        }
        public async Task<T> WriteAsync<T>(Func<ITaskWriter, T> write, CancellationToken ct = default)
        {
            if (PauseWrites) { PauseWrites = false; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return await inner.WriteAsync(write, ct);
        }
    }
    [Fact] public void RenderedIslandCanSaveAndProtectDirtyEditor()
    {
        var service = new TaskService(store, TimeProvider.System);
        IslandMotionTests.RunSta(window =>
        {
            FinishOnDispatcher(window.AttachTasksAsync(service));
            var vm = (IslandViewModel)window.DataContext;
            vm.AddCommand.Execute(null);
            Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)window.FindName("EditorPanel")).Visibility);
            vm.EditorTitle = "窗口流程";
            window.OpenSettings(); Assert.True(vm.ConfirmDiscard);
            vm.KeepEditingCommand.Execute(null);
            FinishOnDispatcher(vm.SaveCommand.ExecuteAsync(null));
            Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)window.FindName("TasksPanel")).Visibility);
            Assert.Equal("窗口流程", Assert.Single(vm.Tasks).Title);
        });
    }
    private static void FinishOnDispatcher(Task task)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(5)) IslandMotionTests.Pump(10);
        Assert.True(task.IsCompletedSuccessfully, task.Exception?.ToString() ?? "UI operation timed out");
    }
}
