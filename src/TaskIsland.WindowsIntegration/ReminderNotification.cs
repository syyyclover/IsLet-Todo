using System.Security.Cryptography;
using System.Text;
using TaskIsland.Domain;

namespace TaskIsland.WindowsIntegration;

/// <summary>Pure notification content and identity; never submits to Windows.</summary>
public sealed record ReminderNotification(string Tag, string Group, string Argument, string Title, string Body)
{
    public static ReminderNotification Create(IReadOnlyList<TaskItem> tasks, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (tasks.Count == 0) throw new ArgumentException("提醒任务不能为空", nameof(tasks));
        zone ??= TimeZoneInfo.Local;
        var identities = tasks.Select(t => $"{t.Id:N}:{t.ReminderRevision}").Order(StringComparer.Ordinal);
        // 32 hexadecimal characters fit the Windows 11 64-character tag limit.
        var tag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(";", identities))))[..32];
        if (tasks.Count == 1)
        {
            var item = tasks[0];
            var time = item.DueAtUtc is { } due ? TimeZoneInfo.ConvertTime(due, zone).ToString("yyyy-MM-dd HH:mm") : "";
            return new(tag, "TaskIslandDue", $"task={item.Id:D}", item.Title, $"日程已到期 · {time}");
        }
        return new(tag, "TaskIslandDue", "show", $"{tasks.Count} 条日程已到期", "点击查看日程列表");
    }
}
