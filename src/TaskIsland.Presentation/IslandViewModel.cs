using CommunityToolkit.Mvvm.ComponentModel;
using TaskIsland.Application;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace TaskIsland.Presentation;

public partial class IslandViewModel : ObservableObject, IDisposable
{
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private TaskService? service;
    private TaskIsland.Domain.TaskItem? editingItem;
    private string originalTitle = "";
    private bool editing, disposed;
    private int reloadGeneration;
    private int refreshPending;
    private bool addAfterBusy;
    private IslandPage discardDestination = IslandPage.Tasks;
    public event Action<IslandPage>? NavigationRequested;
    [ObservableProperty] public partial string Status { get; set; } = "正在准备本地日程…";
    [ObservableProperty] public partial IslandAnchor Anchor { get; set; } = IslandAnchor.Center;
    [ObservableProperty] public partial bool AnimationsEnabled { get; set; } = true;
    [ObservableProperty] public partial string EditorTitle { get; set; } = "";
    [ObservableProperty] public partial string FeaturedTitle { get; set; } = "暂无日程";
    [ObservableProperty] public partial bool ConfirmDiscard { get; set; }
    [ObservableProperty] public partial bool IsError { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AddCommand))] public partial bool IsReady { get; set; }
    [ObservableProperty] public partial bool HasMoreArchives { get; set; }
    [ObservableProperty] public partial string EditorTimeDescription { get; set; } = "未设置结束时间";
    public EditorTimeModel EditorTime { get; } = new();
    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];
    public ObservableCollection<TaskRowViewModel> ArchivedTasks { get; } = [];
    public async Task AttachAsync(TaskService taskService)
    {
        if (service is not null) service.Changed -= OnChanged;
        service = taskService; service.Changed += OnChanged;
        IsReady = false; IsError = false;
        await ReloadAsync();
        IsReady = !IsError;
        if (!IsError) Status = Tasks.Count == 0 ? "添加第一条日程吧" : $"{Tasks.Count} 条活跃日程";
    }
    public async Task ReloadAsync()
    {
        if (service is null || disposed) return;
        Interlocked.Exchange(ref refreshPending, 0);
        var generation = ++reloadGeneration;
        try
        {
            var overview = await service.ReadOverviewAsync();
            if (disposed || generation != reloadGeneration) return;
            SyncRows(Tasks, overview.Active.Tasks.Select(item => new TaskRowViewModel(item, item.Id == overview.Active.PinnedTaskId)).ToArray());
            SyncRows(ArchivedTasks, overview.Archived.Select(item => new TaskRowViewModel(item, false)).ToArray());
            HasMoreArchives = overview.Archived.Count == 50;
            FeaturedTitle = Tasks.FirstOrDefault()?.Summary ?? "暂无日程";
        }
        catch (Exception ex) when (!disposed) { ShowFailure(ex); }
    }
    private static void SyncRows(ObservableCollection<TaskRowViewModel> rows, IReadOnlyList<TaskRowViewModel> desired)
    {
        var ids = desired.Select(x => x.Item.Id).ToHashSet();
        for (var i = rows.Count - 1; i >= 0; i--) if (!ids.Contains(rows[i].Item.Id)) rows.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            var index = -1;
            for (var j = i; j < rows.Count; j++) if (rows[j].Item.Id == desired[i].Item.Id) { index = j; break; }
            if (index < 0) rows.Insert(i, desired[i]);
            else { if (index != i) rows.Move(index, i); if (rows[i] != desired[i]) rows[i] = desired[i]; }
        }
    }
    private void OnChanged()
    {
        if (disposed) return;
        Interlocked.Exchange(ref refreshPending, 1);
        if (IsBusy) return;
        if (context is not null) context.Post(state => { if (!disposed && !IsBusy) _ = ReloadAsync(); }, null);
        else _ = ReloadAsync();
    }
    private bool CanAdd() => IsReady;
    [RelayCommand(CanExecute = nameof(CanAdd))] private void Add()
    {
        if (service is null) return;
        if (IsBusy) { addAfterBusy = true; return; }
        if (editing) { NavigationRequested?.Invoke(IslandPage.Editor); return; }
        editingItem = null; editing = true; originalTitle = ""; EditorTitle = "";
        EditorTime.Reset(null);
        ConfirmDiscard = false; IsError = false; Status = "输入日程内容";
        NavigationRequested?.Invoke(IslandPage.Editor);
    }
    [RelayCommand] private void Edit(TaskRowViewModel row)
    {
        if (service is null || IsBusy) return;
        editingItem = row.Item; editing = true; originalTitle = row.Title; EditorTitle = row.Title;
        EditorTime.Reset(row.Item.DueAtUtc);
        ConfirmDiscard = false; IsError = false; Status = "编辑日程";
        NavigationRequested?.Invoke(IslandPage.Editor);
    }
    [RelayCommand] private Task SaveAsync() => RunAsync(async () =>
    {
        if (!editing) return;
        if (!EditorTime.ValidateForSave()) { Status = EditorTime.Error; IsError = true; return; }
        var submittedDue = EditorTime.ConfirmedDueAt;
        var submittedTitle = EditorTitle;
        var saved = editingItem is null ? await service!.CreateTaskAsync(submittedTitle, submittedDue)
            : await service!.UpdateTaskAsync(editingItem.Id, submittedTitle, submittedDue);
        editingItem = saved; originalTitle = saved.Title;
        if (EditorTitle != submittedTitle || EditorTime.ConfirmedDueAt != submittedDue)
        {
            await ReloadAsync(); Status = "先前内容已保存，当前修改尚未保存";
            return;
        }
        editing = false; ConfirmDiscard = false; EditorTitle = "";
        await ReloadAsync(); Status = "已保存";
        NavigationRequested?.Invoke(IslandPage.Tasks);
    });
    public bool TryLeaveEditor(IslandPage destination)
    {
        if (!editing || destination == IslandPage.Editor) return true;
        if (EditorTitle != originalTitle || EditorTime.HasChanges) { discardDestination = destination; ConfirmDiscard = true; return false; }
        editing = false; ConfirmDiscard = false; return true;
    }
    [RelayCommand] private void CancelEdit()
    { if (!IsBusy && TryLeaveEditor(IslandPage.Tasks)) NavigationRequested?.Invoke(IslandPage.Tasks); }
    [RelayCommand] private void KeepEditing() { ConfirmDiscard = false; }
    [RelayCommand] private void DiscardChanges()
    {
        if (IsBusy) return;
        editing = false; ConfirmDiscard = false; EditorTitle = "";
        NavigationRequested?.Invoke(discardDestination);
    }
    [RelayCommand] private Task CompleteAsync(TaskRowViewModel row) => RunAsync(async () =>
    { await service!.ArchiveTaskAsync(row.Item.Id, TaskIsland.Domain.ArchiveReason.Completed); await ReloadAsync(); Status = "已完成并归档"; });
    [RelayCommand] private Task ArchiveAsync(TaskRowViewModel row) => RunAsync(async () =>
    { await service!.ArchiveTaskAsync(row.Item.Id, TaskIsland.Domain.ArchiveReason.Manual); await ReloadAsync(); Status = "已归档"; });
    [RelayCommand] private Task RestoreAsync(TaskRowViewModel row) => RunAsync(async () =>
    { await service!.RestoreTaskAsync(row.Item.Id); await ReloadAsync(); Status = "已恢复"; });
    [RelayCommand] private Task PinAsync(TaskRowViewModel row) => RunAsync(async () =>
    { await service!.SetPinnedTaskAsync(row.IsPinned ? null : row.Item.Id); await ReloadAsync(); Status = row.IsPinned ? "已取消置顶" : "已置顶"; });
    [RelayCommand] private Task RenewAsync(TaskRowViewModel row) => RunAsync(async () =>
    { await service!.RenewArchiveAsync(row.Item.Id); await ReloadAsync(); Status = "已延长保留时间"; });
    [RelayCommand] private Task LoadMoreArchivesAsync() => RunAsync(async () =>
    {
        var page = await service!.ListArchivedAsync(ArchivedTasks.Count);
        foreach (var item in page) ArchivedTasks.Add(new(item, false));
        HasMoreArchives = page.Count == 50;
    });
    private async Task RunAsync(Func<Task> action)
    {
        if (service is null || IsBusy || disposed) return;
        IsBusy = true; IsError = false;
        try { await action(); }
        catch (Exception ex) { ShowFailure(ex); }
        finally
        {
            IsBusy = false;
            if (Interlocked.Exchange(ref refreshPending, 0) != 0) await ReloadAsync();
            if (addAfterBusy && !disposed) { addAfterBusy = false; Add(); }
        }
    }
    private void ShowFailure(Exception ex)
    {
        IsError = true;
        Status = ex is ArgumentException or InvalidOperationException ? ex.Message.Split('\r', '\n')[0]
            : "操作失败，未保存的输入已保留。请重试或检查本地存储。";
    }
    public void Dispose() { disposed = true; if (service is not null) service.Changed -= OnChanged; }
}
