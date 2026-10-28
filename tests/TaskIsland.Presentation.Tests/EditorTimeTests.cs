using TaskIsland.Presentation;
using Xunit;

public class EditorTimeTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }
    [Fact] public void SelectionCommitsOnlyOnConfirmAndCancelRestores()
    {
        var clock = new Clock(new(2026, 10, 28, 10, 1, 30, TimeSpan.Zero));
        var model = new EditorTimeModel(clock, TimeZoneInfo.Utc);
        model.Reset(null); model.OpenCommand.Execute(null);
        Assert.Equal(30, model.Minute); Assert.Null(model.ConfirmedDueAt);
        model.Minute = 42; model.CancelCommand.Execute(null);
        Assert.Null(model.ConfirmedDueAt);
        model.OpenCommand.Execute(null); model.ConfirmCommand.Execute(null);
        Assert.Equal(30, model.ConfirmedDueAt!.Value.Minute); Assert.True(model.HasChanges);
    }
    [Fact] public void ConfirmationRechecksClockAndMaximumDate()
    {
        var clock = new Clock(new(2026, 10, 28, 10, 1, 30, TimeSpan.Zero));
        var model = new EditorTimeModel(clock, TimeZoneInfo.Utc);
        model.Reset(null); model.OpenCommand.Execute(null);
        clock.Now = clock.Now.AddHours(1); model.ConfirmCommand.Execute(null);
        Assert.Null(model.ConfirmedDueAt); Assert.NotEmpty(model.Error);
        model.Date = clock.Now.Date.AddDays(366); model.ConfirmCommand.Execute(null);
        Assert.Null(model.ConfirmedDueAt);
    }
    [Fact] public void UnchangedExpiredValueCanBeKeptButChangedValueMustBeFuture()
    {
        var clock = new Clock(new(2026, 10, 28, 10, 1, 30, TimeSpan.Zero));
        var expired = clock.Now.AddHours(-1);
        var model = new EditorTimeModel(clock, TimeZoneInfo.Utc); model.Reset(expired);
        Assert.True(model.ValidateForSave()); Assert.False(model.HasChanges);
        model.RemoveCommand.Execute(null); Assert.True(model.HasChanges);
    }
    [Fact] public void DstGapRejectedAndOverlapUsesEarlierInstant()
    {
        var clock = new Clock(new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var model = new EditorTimeModel(clock, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
        model.Reset(null); model.OpenCommand.Execute(null);
        model.Date = new(2026, 3, 8); model.Hour = 2; model.Minute = 30;
        model.ConfirmCommand.Execute(null); Assert.Null(model.ConfirmedDueAt);
        model.Date = new(2026, 11, 1); model.Hour = 1; model.Minute = 30;
        model.ConfirmCommand.Execute(null);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), model.ConfirmedDueAt);
        Assert.Contains("-04:00", model.Preview);
    }
}
