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
    [Fact] public async Task EditingAnotherRowDoesNotOverwriteBackgroundDraft()
    {
        model.AddCommand.Execute(null); model.EditorTitle = "已有日程";
        await model.SaveCommand.ExecuteAsync(null);
        model.AddCommand.Execute(null); model.EditorTitle = "后台草稿";
        model.EditCommand.Execute(model.Tasks[0]);
        Assert.Equal("后台草稿", model.EditorTitle);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, model.Tasks.Count);
        Assert.Contains(model.Tasks, row => row.Title == "已有日程");
        Assert.Contains(model.Tasks, row => row.Title == "后台草稿");
    }
    [Fact] public async Task AddIsUnavailableUntilStorageHasLoaded()
    {
        using var cold = new IslandViewModel();
        Assert.False(cold.AddCommand.CanExecute(null));
        await cold.AttachAsync(new TaskService(store, TimeProvider.System));
        Assert.True(cold.AddCommand.CanExecute(null));
        cold.IsBusy = true; Assert.True(cold.AddCommand.CanExecute(null));
    }
    [Fact] public async Task OptionalCapabilityErrorDoesNotBlockLoadedTasks()
    {
        using var cold = new IslandViewModel { IsError = true, Status = "全屏检测不可用" };
        await cold.AttachAsync(new TaskService(store, TimeProvider.System));
        Assert.True(cold.IsReady); Assert.True(cold.AddCommand.CanExecute(null));
    }
    [Fact] public async Task SaveFailureKeepsInputAndEditorState()
    {
        model.AddCommand.Execute(null); model.EditorTitle = " ";
        await model.SaveCommand.ExecuteAsync(null);
        Assert.Equal(" ", model.EditorTitle); Assert.Empty(model.Tasks); Assert.True(model.IsError);
        Assert.Equal("请输入日程内容。", model.ErrorMessage);
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
    [Theory] [InlineData("renew")] [InlineData("complete")] [InlineData("archive")]
    public void TaskOperationsKeepAddButtonStableAndQueueRequestedEditor(string operation)
    {
        var paused = new PausedStore(store); var service = new TaskService(paused, TimeProvider.System);
        IslandMotionTests.RunSta(window =>
        {
            FinishOnDispatcher(service.CreateTaskAsync("入口保持稳定", null));
            FinishOnDispatcher(window.AttachTasksAsync(service)); window.OpenTasks(false); IslandMotionTests.Pump(300);
            var vm = (IslandViewModel)window.DataContext; var row = Assert.Single(vm.Tasks);
            var add = (System.Windows.Controls.Button)window.FindName("AddTaskButton");
            var changes = 0; var resets = 0;
            add.IsEnabledChanged += (_, _) => changes++;
            vm.Tasks.CollectionChanged += (_, e) => { if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++; };
            paused.PauseWrites = true;
            var task = operation switch { "renew" => vm.RenewCommand.ExecuteAsync(row), "complete" => vm.CompleteCommand.ExecuteAsync(row), _ => vm.ArchiveCommand.ExecuteAsync(row) };
            FinishOnDispatcher(paused.Entered.Task);
            Assert.True(add.IsEnabled); vm.AddCommand.Execute(null);
            paused.Release.SetResult(); FinishOnDispatcher(task);
            Assert.Equal(0, changes); Assert.Equal(0, resets);
            Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)window.FindName("EditorPanel")).Visibility);
            Assert.False(vm.IsError);
        });
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
    [Fact] public void ExpandedEmptyIslandExposesStyledAddButtonAndOpensEditor()
    {
        IslandMotionTests.RunSta(window =>
        {
            FinishOnDispatcher(window.AttachTasksAsync(new TaskService(store, TimeProvider.System)));
            window.OpenTasks(false); IslandMotionTests.Pump(300);
            var add = Assert.IsType<System.Windows.Controls.Button>(window.FindName("AddTaskButton"));
            Assert.True(add.IsVisible); Assert.True(add.IsEnabled);
            Assert.True(add.ActualWidth >= 300); Assert.True(add.ActualHeight >= 32);
            Assert.Equal("添加日程", System.Windows.Automation.AutomationProperties.GetName(add));
            Assert.NotNull(add.Template);
            var empty = (System.Windows.FrameworkElement)window.FindName("EmptyTasksPanel");
            Assert.Equal(System.Windows.Visibility.Visible, empty.Visibility);
            var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(add);
            ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
            IslandMotionTests.Pump(40);
            Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)window.FindName("EditorPanel")).Visibility);
            CaptureWindow(window, "editor");
            var vm = (IslandViewModel)window.DataContext;
            vm.CancelEditCommand.Execute(null); IslandMotionTests.Pump(40);
            CaptureWindow(window, "empty");
            vm.AddCommand.Execute(null); vm.EditorTitle = "整理今天的工作清单";
            FinishOnDispatcher(vm.SaveCommand.ExecuteAsync(null)); IslandMotionTests.Pump(40);
            CaptureWindow(window, "tasks");
            window.OpenSettings(); CaptureWindow(window, "settings");
            window.OpenArchive(); CaptureWindow(window, "archive");
        });
    }
    private static void CaptureWindow(IslandWindow window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("TASKISLAND_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        IslandMotionTests.Pump(250);
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(360, 295, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
    [Fact] public void EditorTextAndTimePickerFitInsideIsland()
    {
        IslandMotionTests.RunSta(window =>
        {
            FinishOnDispatcher(window.AttachTasksAsync(new TaskService(store, TimeProvider.System)));
            var vm = (IslandViewModel)window.DataContext;
            vm.AddCommand.Execute(null); vm.EditorTitle = "明天下午整理工作清单 👩‍💻";
            IslandMotionTests.Pump(300); window.UpdateLayout();
            var input = (System.Windows.Controls.TextBox)window.FindName("TitleInput");
            var host = (System.Windows.FrameworkElement)input.Template.FindName("PART_ContentHost", input);
            Assert.True(host.ActualHeight >= input.FontSize * 1.25, $"Text host clipped: {host.ActualHeight}");
            CaptureWindow(window, "editor-filled");
            vm.EditorTime.OpenCommand.Execute(null); window.UpdateLayout();
            Assert.False(input.IsVisible);
            CaptureWindow(window, "time-wheel");
            vm.EditorTime.ConfirmCommand.Execute(null); window.UpdateLayout(); Assert.True(input.IsVisible);
        });
    }
    private static void FinishOnDispatcher(Task task)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(5)) IslandMotionTests.Pump(10);
        Assert.True(task.IsCompletedSuccessfully, task.Exception?.ToString() ?? "UI operation timed out");
    }
}
