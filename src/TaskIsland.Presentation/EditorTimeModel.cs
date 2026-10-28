using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TaskIsland.Presentation;

public partial class EditorTimeModel(TimeProvider? clock = null, TimeZoneInfo? zone = null) : ObservableObject
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly TimeZoneInfo zone = zone ?? TimeZoneInfo.Local;
    private DateTimeOffset? original;
    [ObservableProperty] public partial DateTime Date { get; set; }
    [ObservableProperty] public partial int Hour { get; set; }
    [ObservableProperty] public partial int Minute { get; set; }
    [ObservableProperty] public partial bool IsPickerOpen { get; set; }
    [ObservableProperty] public partial DateTimeOffset? ConfirmedDueAt { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";
    public bool HasChanges => ConfirmedDueAt != original || (IsPickerOpen && DraftDiffers());
    public string Description => ConfirmedDueAt is { } due ? $"结束于 {TimeZoneInfo.ConvertTime(due, zone):MM月dd日 HH:mm}" : "未设置结束时间";
    public string DateLabel => Date.ToString("MM月dd日");
    public string HourLabel => Hour.ToString("00");
    public string MinuteLabel => Minute.ToString("00");
    public string Preview
    {
        get
        {
            var local = SelectedLocal();
            var suffix = zone.IsAmbiguousTime(local) ? $" UTC{OffsetText(zone.GetAmbiguousTimeOffsets(local).Max())}" : "";
            return $"{local:yyyy年MM月dd日 HH:mm}{suffix}";
        }
    }
    private static string OffsetText(TimeSpan offset) => $"{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
    partial void OnDateChanged(DateTime value) => NotifyDraft();
    partial void OnHourChanged(int value) => NotifyDraft();
    partial void OnMinuteChanged(int value) => NotifyDraft();
    partial void OnConfirmedDueAtChanged(DateTimeOffset? value) { OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(HasChanges)); }
    private void NotifyDraft() { OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(DateLabel)); OnPropertyChanged(nameof(HourLabel)); OnPropertyChanged(nameof(MinuteLabel)); OnPropertyChanged(nameof(HasChanges)); Error = ""; }
    public void Reset(DateTimeOffset? due) { original = due; ConfirmedDueAt = due; IsPickerOpen = false; Error = ""; }
    [RelayCommand] private void Open()
    {
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime;
        var value = ConfirmedDueAt is { } due ? TimeZoneInfo.ConvertTime(due, zone).DateTime
            : now.Date.AddMinutes((Math.Floor(now.TimeOfDay.TotalMinutes / 30) + 1) * 30);
        Date = value.Date; Hour = value.Hour; Minute = value.Minute; Error = ""; IsPickerOpen = true;
    }
    [RelayCommand] private void Cancel() { IsPickerOpen = false; Error = ""; }
    [RelayCommand] private void Remove() { ConfirmedDueAt = null; IsPickerOpen = false; Error = ""; }
    [RelayCommand] private void Confirm()
    {
        if (!TryResolve(out var due)) return;
        ConfirmedDueAt = due; IsPickerOpen = false;
    }
    public bool ValidateForSave()
    {
        if (IsPickerOpen) { Error = "请先确认或取消时间选择"; return false; }
        if (ConfirmedDueAt == original || ConfirmedDueAt is null) return true;
        if (ConfirmedDueAt <= clock.GetUtcNow()) { Error = "时间已过去，请重新选择"; return false; }
        return true;
    }
    private DateTime SelectedLocal() => DateTime.SpecifyKind(Date.Date.AddHours(Math.Clamp(Hour, 0, 23)).AddMinutes(Math.Clamp(Minute, 0, 59)), DateTimeKind.Unspecified);
    private bool DraftDiffers() => ConfirmedDueAt is null || SelectedLocal() != TimeZoneInfo.ConvertTime(ConfirmedDueAt.Value, zone).DateTime;
    private bool TryResolve(out DateTimeOffset due)
    {
        due = default;
        var local = SelectedLocal(); var now = clock.GetUtcNow(); var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        if (Date.Date < today || Date.Date > today.AddDays(365)) { Error = "请选择今天至 365 天内的日期"; return false; }
        if (zone.IsInvalidTime(local)) { Error = "此时间因夏令时不存在，请重新选择"; return false; }
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        due = new DateTimeOffset(local, offset).ToUniversalTime();
        if (due <= now) { Error = "请选择下一个整分钟之后的时间"; return false; }
        Error = ""; return true;
    }
    public void Step(string column, int delta)
    {
        if (column == "hour") Hour = (Hour + delta % 24 + 24) % 24;
        else if (column == "minute") Minute = (Minute + delta % 60 + 60) % 60;
        else
        {
            var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).Date;
            Date = new DateTime(Math.Clamp(Date.Date.AddDays(delta).Ticks, today.Ticks, today.AddDays(365).Ticks));
        }
    }
}
