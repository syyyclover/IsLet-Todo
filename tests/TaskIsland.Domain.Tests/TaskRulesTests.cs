using TaskIsland.Domain;
using Xunit;

public class TaskRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 2, 0, 0, TimeSpan.Zero);
    private static TaskItem Create(DateTimeOffset? due = null) => TaskRules.Create(Guid.NewGuid(), "日程", due, Now);

    [Fact] public void UntimedCreationRetainsFor24Hours()
    {
        var task = Create();
        Assert.Equal(Now.AddHours(24), task.ArchiveAtUtc);
        Assert.Null(task.DueAtUtc);
        Assert.Equal(1, task.ReminderRevision);
    }
    [Fact] public void TimedCreationRetainsUntil24HoursAfterDue()
        => Assert.Equal(Now.AddHours(32), Create(Now.AddHours(8)).ArchiveAtUtc);
    [Fact] public void CreatingPastDueTaskIsRejected()
        => Assert.Throws<ArgumentException>(() => Create(Now));
    [Fact] public void RenewBeforeDueDoesNotShortenOrCreateReminder()
    {
        var item = Create(Now.AddHours(8));
        var renewed = TaskRules.Renew(item, Now.AddHours(1));
        Assert.Equal(item.ArchiveAtUtc, renewed.ArchiveAtUtc);
        Assert.Equal(item.ReminderRevision, renewed.ReminderRevision);
    }
    [Fact] public void RepeatedRenewalsResetRatherThanAccumulate()
    {
        var item = Create(Now.AddHours(1));
        item = TaskRules.Renew(item, Now.AddHours(3));
        item = TaskRules.Renew(item, Now.AddHours(4));
        Assert.Equal(Now.AddHours(28), item.ArchiveAtUtc);
        Assert.Equal(Now.AddHours(1), item.DueAtUtc);
    }
    [Fact] public void UntimedRenewStartsFresh24HourWindow()
        => Assert.Equal(Now.AddHours(29), TaskRules.Renew(Create(), Now.AddHours(5)).ArchiveAtUtc);
    [Fact] public void TitleEditAfterDueKeepsTimesAndRevision()
    {
        var item = Create(Now.AddHours(1));
        var edited = TaskRules.Update(item, "改字", item.DueAtUtc, Now.AddHours(2));
        Assert.Equal(item.ArchiveAtUtc, edited.ArchiveAtUtc);
        Assert.Equal(item.ReminderRevision, edited.ReminderRevision);
    }
    [Fact] public void ChangingDueReplacesRenewalAndReminderRevision()
    {
        var item = TaskRules.Renew(Create(Now.AddHours(1)), Now.AddHours(3));
        item = TaskRules.Update(item, item.Title, Now.AddHours(8), Now.AddHours(4));
        Assert.Equal(Now.AddHours(32), item.ArchiveAtUtc);
        Assert.Equal(2, item.ReminderRevision);
    }
    [Fact] public void RemovingDueRetainsFromOperationTime()
    {
        var item = TaskRules.Update(Create(Now.AddHours(8)), "日程", null, Now.AddHours(3));
        Assert.Null(item.DueAtUtc);
        Assert.Equal(Now.AddHours(27), item.ArchiveAtUtc);
        Assert.Equal(2, item.ReminderRevision);
    }
    [Theory]
    [InlineData(ArchiveReason.Completed)] [InlineData(ArchiveReason.Manual)] [InlineData(ArchiveReason.Automatic)]
    public void ArchivePreservesDueAndRecordsReason(ArchiveReason reason)
    {
        var item = TaskRules.Archive(Create(Now.AddHours(8)), reason, Now.AddHours(1));
        Assert.Equal(Now.AddHours(1), item.ArchivedAtUtc);
        Assert.Equal(reason, item.ArchiveReason);
        Assert.Equal(Now.AddHours(8), item.DueAtUtc);
    }
    [Theory] [InlineData(2, true)] [InlineData(9, false)]
    public void RestoreKeepsOnlyFutureDue(int hours, bool retainsDue)
    {
        var item = TaskRules.Archive(Create(Now.AddHours(8)), ArchiveReason.Completed, Now.AddHours(1));
        item = TaskRules.Restore(item, Now.AddHours(hours));
        Assert.Null(item.ArchivedAtUtc);
        Assert.Null(item.ArchiveReason);
        Assert.Equal(retainsDue, item.DueAtUtc.HasValue);
        Assert.Equal(Now.AddHours(retainsDue ? 32 : 33), item.ArchiveAtUtc);
        Assert.Equal(2, item.ReminderRevision);
    }
    [Fact] public void SortingUsesPinDueCreatedAndId()
    {
        var untimed = Create();
        var later = Create(Now.AddHours(4));
        var earlier = Create(Now.AddHours(1));
        Assert.Equal(new[] { untimed.Id, earlier.Id, later.Id }, TaskRules.Sort([later, untimed, earlier], untimed.Id).Select(x => x.Id));
        Assert.Equal(new[] { earlier.Id, later.Id, untimed.Id }, TaskRules.Sort([later, untimed, earlier], null).Select(x => x.Id));
        var first = earlier with { Id = Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var second = first with { Id = Guid.Parse("00000000-0000-0000-0000-000000000002") };
        Assert.Equal(new[] { first.Id, second.Id }, TaskRules.Sort([second, first], null).Select(x => x.Id));
    }
    [Fact] public void TitleNormalizesLineBreaksAndWhitespace()
        => Assert.Equal("一 二 三", TaskText.Normalize("  一\r\n二\n三  "));
    [Theory] [InlineData("")] [InlineData(" \r\n ")]
    public void EmptyTitleRejected(string title) => Assert.Throws<ArgumentException>(() => TaskText.Normalize(title));
    [Fact] public void LengthUsesUnicodeTextElements()
    {
        var emoji = "👩‍👩‍👧‍👦";
        Assert.Equal(string.Concat(Enumerable.Repeat(emoji, 200)), TaskText.Normalize(string.Concat(Enumerable.Repeat(emoji, 200))));
        Assert.Throws<ArgumentException>(() => TaskText.Normalize(new string('中', 201)));
        Assert.Equal("12345678" + emoji + "…", TaskText.Summarize("12345678" + emoji + "后续"));
        Assert.Equal("12345678e\u0301…", TaskText.Summarize("12345678e\u0301后续"));
    }
}
