using TaskIsland.Domain;

namespace TaskIsland.Presentation;

public sealed record TaskRowViewModel(TaskItem Item, bool IsPinned)
{
    public string Title => Item.Title;
    public string Summary => TaskText.Summarize(Item.Title);
    public string Detail => Item.ArchivedAtUtc is { } archived
        ? $"{archived.ToLocalTime():MM-dd HH:mm} · {Item.ArchiveReason switch { ArchiveReason.Completed => "已完成", ArchiveReason.Manual => "手动归档", _ => "自动归档" }}"
        : Item.DueAtUtc is { } due ? $"{(due <= DateTimeOffset.UtcNow ? "已到期 · " : "")}{due.ToLocalTime():MM-dd HH:mm}" : "未设置结束时间";
    public string ArchiveTooltip => $"保留至 {Item.ArchiveAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}，点击续期";
    public string PinTooltip => IsPinned ? "取消置顶" : "置顶";
    public string PinGlyph => IsPinned ? "●" : "↑";
}
