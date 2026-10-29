using TaskIsland.Domain;

namespace TaskIsland.Presentation;

public sealed record TaskRowViewModel(TaskItem Item, bool IsPinned)
{
    public int AttachmentCount { get; init; }
    public string Title => Item.Title;
    public string Summary => TaskText.Summarize(Item.Title);
    public string Detail => Item.ArchivedAtUtc is { } archived
        ? $"{archived.ToLocalTime():MM-dd HH:mm} · {Item.ArchiveReason switch { ArchiveReason.Completed => "已完成", ArchiveReason.Manual => "手动归档", _ => "自动归档" }}"
        : Item.DueAtUtc is { } due ? $"{(due <= DateTimeOffset.UtcNow ? "已到期 · " : "")}{due.ToLocalTime():MM-dd HH:mm}" : "未设置结束时间";
    public string ArchiveTooltip => FormatArchiveTooltip(Item.ArchiveAtUtc, DateTimeOffset.UtcNow);
    public static string FormatArchiveTooltip(DateTimeOffset archiveAtUtc, DateTimeOffset now)
    {
        var remaining = archiveAtUtc - now;
        var text = remaining <= TimeSpan.Zero ? "待归档"
            : remaining.TotalHours >= 1 ? $"剩余{(int)remaining.TotalHours}小时"
            : $"剩余{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}分钟";
        return $"{text} · 点击续期";
    }
    public string PinTooltip => IsPinned ? "取消置顶" : "置顶";
    public string PinGlyph => IsPinned ? "●" : "↑";
}
