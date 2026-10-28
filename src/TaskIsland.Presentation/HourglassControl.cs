using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TaskIsland.Presentation;

/// <summary>A small countdown icon; all clocks stop when its task list leaves the screen.</summary>
public sealed class HourglassControl : Control
{
    public static readonly DependencyProperty ArchiveAtUtcProperty = DependencyProperty.Register(nameof(ArchiveAtUtc), typeof(DateTimeOffset), typeof(HourglassControl), new FrameworkPropertyMetadata(DateTimeOffset.MinValue, Changed));
    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(HourglassControl), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty AnimationsEnabledProperty = DependencyProperty.Register(nameof(AnimationsEnabled), typeof(bool), typeof(HourglassControl), new PropertyMetadata(true, Changed));
    private static readonly DependencyPropertyKey CountdownTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CountdownText), typeof(string), typeof(HourglassControl), new PropertyMetadata("点击续期"));
    public static readonly DependencyProperty CountdownTextProperty = CountdownTextPropertyKey.DependencyProperty;
    private static readonly Geometry Outline = Geometry.Parse("M3,1 L12,1 M3,14 L12,14 M4,1 L4,4 11,11 11,14 M11,1 L11,4 4,11 4,14");
    private readonly DispatcherTimer refreshTimer;
    private readonly DispatcherTimer flipTimer;
    private readonly RotateTransform rotation = new();
    private readonly ScaleTransform scale = new();
    private double remaining;

    public DateTimeOffset ArchiveAtUtc { get => (DateTimeOffset)GetValue(ArchiveAtUtcProperty); set => SetValue(ArchiveAtUtcProperty, value); }
    public bool IsRunning { get => (bool)GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public bool AnimationsEnabled { get => (bool)GetValue(AnimationsEnabledProperty); set => SetValue(AnimationsEnabledProperty, value); }
    public string CountdownText => (string)GetValue(CountdownTextProperty);
    public TimeProvider Clock { get; set; } = TimeProvider.System;
    internal bool IsCountdownActive => refreshTimer.IsEnabled;
    internal bool IsFlipActive => flipTimer.IsEnabled;

    static HourglassControl() => Outline.Freeze();

    public HourglassControl()
    {
        Width = Height = 16;
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        RenderTransformOrigin = new Point(.5, .5);
        var transforms = new TransformGroup();
        transforms.Children.Add(scale);
        transforms.Children.Add(rotation);
        RenderTransform = transforms;
        refreshTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => RefreshCountdown(), Dispatcher);
        flipTimer = new DispatcherTimer(TimeSpan.FromSeconds(3.4), DispatcherPriority.Background, (_, _) => Flip(), Dispatcher);
        refreshTimer.Stop();
        flipTimer.Stop();
        Loaded += (_, _) => UpdateActivity();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => UpdateActivity();
        IsEnabledChanged += (_, _) => UpdateActivity();
    }

    private static void Changed(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        var control = (HourglassControl)source;
        control.RefreshCountdown();
        control.UpdateActivity();
    }

    private bool Active => IsLoaded && IsVisible && IsEnabled && IsRunning;
    private bool MotionAllowed => Active && AnimationsEnabled && SystemParameters.ClientAreaAnimation;

    private void UpdateActivity()
    {
        if (!Active) { Stop(); return; }
        RefreshCountdown();
        refreshTimer.Start();
        if (MotionAllowed) flipTimer.Start();
        else { flipTimer.Stop(); ClearMotion(); }
    }

    private void Stop()
    {
        refreshTimer.Stop();
        flipTimer.Stop();
        ClearMotion();
    }

    private void ClearMotion()
    {
        rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    private void RefreshCountdown()
    {
        var now = Clock.GetUtcNow();
        SetValue(CountdownTextPropertyKey, TaskRowViewModel.FormatArchiveTooltip(ArchiveAtUtc, now));
        remaining = Math.Clamp((ArchiveAtUtc - now).TotalHours / 24, 0, 1);
        InvalidateVisual();
    }

    private void Flip()
    {
        if (!MotionAllowed) { UpdateActivity(); return; }
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 180, TimeSpan.FromMilliseconds(620))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop
        });
    }

    /// <summary>Call only after the renewal write succeeds.</summary>
    public void PulseRenewal()
    {
        RefreshCountdown();
        if (!MotionAllowed) return;
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 180, TimeSpan.FromMilliseconds(480))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop
        });
        var pulse = new DoubleAnimation(1, .82, TimeSpan.FromMilliseconds(170)) { AutoReverse = true, FillBehavior = FillBehavior.Stop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var ink = Foreground ?? Brushes.LightSteelBlue;
        drawing.PushTransform(new TranslateTransform((ActualWidth - 10.5) / 2, (ActualHeight - 10.5) / 2));
        drawing.PushTransform(new ScaleTransform(.7, .7));
        var pen = new Pen(ink, 1.35) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawing.DrawGeometry(null, pen, Outline);
        // Two sand banks communicate the remaining portion of the 24-hour retention window.
        var upper = Math.Max(.3, remaining * 2.6);
        var lower = Math.Max(.3, (1 - remaining) * 2.6);
        drawing.DrawLine(new Pen(ink, upper), new Point(5.5, 3.3), new Point(9.5, 3.3));
        drawing.DrawLine(new Pen(ink, lower), new Point(5.5, 12), new Point(9.5, 12));
        drawing.DrawLine(new Pen(ink, .7), new Point(7.5, 7), new Point(7.5, 9));
        drawing.Pop();
        drawing.Pop();
    }
}
