using TaskIsland.Domain;
using TaskIsland.Presentation;
using Xunit;

public class HourglassCountdownTests
{
    [Fact]
    public void TooltipUsesConciseRemainingTime()
    {
        var now = DateTimeOffset.UtcNow;
        var row = new TaskRowViewModel(new TaskItem(Guid.NewGuid(), "任务", now, now, null, now.AddHours(23.5), null, null, 0), false);
        Assert.Equal("剩余23小时 · 点击续期", row.ArchiveTooltip);
    }

    [Theory]
    [InlineData(3600, "剩余1小时 · 点击续期")]
    [InlineData(3599, "剩余60分钟 · 点击续期")]
    [InlineData(1, "剩余1分钟 · 点击续期")]
    [InlineData(0, "待归档 · 点击续期")]
    [InlineData(-60, "待归档 · 点击续期")]
    public void CountdownUsesSuppliedClockAndHandlesExpiry(int seconds, string expected)
    {
        var now = DateTimeOffset.Parse("2026-10-28T10:00:00Z");
        Assert.Equal(expected, TaskRowViewModel.FormatArchiveTooltip(now.AddSeconds(seconds), now));
    }
}
