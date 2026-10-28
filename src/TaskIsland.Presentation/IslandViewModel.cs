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
    [ObservableProperty] public partial bool HasMoreArchives { get; set; }
    [ObservableProperty] public partial string EditorTimeDescription { get; set; } = "未设置结束时间";
    public ObservableCollection<TaskRowViewModel> Tasks { get; } = [];
    public ObservableCollection<TaskRowViewModel> ArchivedTasks { get; } = [];
    public async Task AttachAsync(TaskService taskService)
    {
        if (service is not null) service.Changed -= OnChanged;
        service = taskService; service.Changed += OnChanged;
        await ReloadAsync();
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
            Tasks.Clear(); foreach (var item in overview.Active.Tasks) Tasks.Add(new(item, item.Id == overview.Active.PinnedTaskId));
            ArchivedTasks.Clear(); foreach (var item in overview.Archived) ArchivedTasks.Add(new(item, false));
            HasMoreArchives = overview.Archived.Count == 50;
            FeaturedTitle = Tasks.FirstOrDefault()?.Summary ?? "暂无日程";
        }
        catch (Exception ex) when (!disposed) { ShowFailure(ex); }
    }
    private void OnChanged()
    {
        if (disposed) return;
        Interlocked.Exchange(ref refreshPending, 1);
        if (IsBusy) return;
        if (context is not null) context.Post(state => { if (!disposed && !IsBusy) _ = ReloadAsync(); }, null);
        else _ = ReloadAsync();
    }
    [RelayCommand] private void Add()
    {
        if (service is null || IsBusy) return;
        if (editing) { NavigationRequested?.Invoke(IslandPage.Editor); return; }
        editingItem = null; editing = true; originalTitle = ""; EditorTitle = "";
        EditorTimeDescription = "未设置结束时间 · 创建后保留 24 小时";
        ConfirmDiscard = false; IsError = false; Status = "输入日程内容";
        NavigationRequested?.Invoke(IslandPage.Editor);
    }
    [RelayCommand] private void Edit(TaskRowViewModel row)
    {
        if (service is null || IsBusy) return;
        editingItem = row.Item; editing = true; originalTitle = row.Title; EditorTitle = row.Title;
        EditorTimeDescription = row.Item.DueAtUtc is { } due ? $"结束时间：{due.ToLocalTime():yyyy-MM-dd HH:mm}" : "未设置结束时间";
        ConfirmDiscard = false; IsError = false; Status = "编辑日程";
        NavigationRequested?.Invoke(IslandPage.Editor);
    }
    [RelayCommand] private Task SaveAsync() => RunAsync(async () =>
    {
        if (!editing) return;
        var submittedTitle = EditorTitle;
        var saved = editingItem is null ? await service!.CreateTaskAsync(submittedTitle, null)
            : await service!.UpdateTaskAsync(editingItem.Id, submittedTitle, editingItem.DueAtUtc);
        editingItem = saved; originalTitle = saved.Title;
        if (EditorTitle != submittedTitle)
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
        if (EditorTitle != originalTitle) { discardDestination = destination; ConfirmDiscard = true; return false; }
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
    { await service!.RenewArchiveAsync(row.Item.Id); await ReloadAsync(); Status = "已按当前时间重新计算保留期限"; });
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
