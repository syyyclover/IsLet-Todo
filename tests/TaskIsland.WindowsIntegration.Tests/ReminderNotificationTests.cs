using TaskIsland.Application;
using TaskIsland.Domain;
using TaskIsland.WindowsIntegration;
using Xunit;

public class ReminderNotificationTests
{
    private static TaskItem Item(Guid id, int revision = 1) => new(id, "完整正文 < & 中文 😀", DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch, new DateTimeOffset(2026, 10, 28, 9, 5, 0, TimeSpan.Zero),
        DateTimeOffset.UnixEpoch, null, null, revision);
    [Fact] public void IndividualPreservesFullTextAndRoutesToTask()
    {
        var item = Item(Guid.NewGuid()); var data = ReminderNotification.Create([item], TimeZoneInfo.Utc);
        Assert.Equal(item.Title, data.Title); Assert.Contains("2026-10-28 09:05", data.Body);
        Assert.Equal(item.Id, ActivationRequest.Parse(data.Argument).TaskId);
        Assert.InRange(data.Tag.Length, 1, 64); Assert.InRange(data.Group.Length, 1, 16);
        Assert.Equal(data.Tag, ReminderNotification.Create([item]).Tag);
        Assert.NotEqual(data.Tag, ReminderNotification.Create([item with { ReminderRevision = 2 }]).Tag);
    }
    [Fact] public void SummaryIdentityIgnoresOrderButIncludesEveryRevision()
    {
        var a = Item(Guid.NewGuid()); var b = Item(Guid.NewGuid());
        var data = ReminderNotification.Create([a, b]);
        Assert.Equal(data.Tag, ReminderNotification.Create([b, a]).Tag);
        Assert.NotEqual(data.Tag, ReminderNotification.Create([a, b with { ReminderRevision = 2 }]).Tag);
        Assert.Contains("2", data.Title); Assert.Null(ActivationRequest.Parse(data.Argument).TaskId);
        Assert.InRange(data.Tag.Length, 1, 64);
    }
    [Fact] public void EmptyBatchIsRejected() => Assert.Throws<ArgumentException>(() => ReminderNotification.Create([]));
    [Fact] public async Task UnregisteredSenderFailsWithoutDisplayingNotification()
    {
        using var sender = new NotificationService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync([Item(Guid.NewGuid())], default));
        Assert.NotNull(sender.Error);
    }
}
