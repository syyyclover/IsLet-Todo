using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TaskIsland.Presentation;

public partial class IslandViewModel
{
    private Guid? deleteTaskId;
    private bool deleteAll;
    [ObservableProperty] public partial bool ConfirmDelete { get; set; }
    [ObservableProperty] public partial string DeletePrompt { get; set; } = "";

    [RelayCommand] private void RequestDelete(TaskRowViewModel row)
    {
        if (IsBusy || service is null || row.Item.ArchivedAtUtc is null) return;
        deleteTaskId = row.Item.Id; deleteAll = false;
        DeletePrompt = "彻底删除此归档日程？此操作无法撤销。";
        IsError = false; ConfirmDelete = true;
    }
    [RelayCommand] private void RequestDeleteAll()
    {
        if (IsBusy || service is null || ArchivedTasks.Count == 0) return;
        deleteTaskId = null; deleteAll = true;
        DeletePrompt = "彻底删除全部归档日程？活跃日程将保留，此操作无法撤销。";
        IsError = false; ConfirmDelete = true;
    }
    [RelayCommand] private void CancelDelete()
    {
        if (IsBusy) return;
        ConfirmDelete = false; deleteTaskId = null; deleteAll = false;
    }
    [RelayCommand] private Task ConfirmDeleteAsync() => RunAsync(async () =>
    {
        if (!ConfirmDelete) return;
        if (deleteAll) await service!.DeleteAllArchivedTasksAsync();
        else if (deleteTaskId is { } id) await service!.DeleteArchivedTaskAsync(id);
        else return;
        await FeedbackAsync(deleteTaskId, OperationCue.Delete);
        await ReloadAsync();
        ConfirmDelete = false; deleteTaskId = null; deleteAll = false;
        Status = "已删除归档日程";
    });
}
